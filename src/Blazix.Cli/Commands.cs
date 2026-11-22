using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blazix.Cli;

internal static class Commands
{
    public const string Usage = """
        blazix — copy Blazix components into your Blazor project as source you own.

        Usage:
          blazix init [--project <path>] [--dir Components/UI] [--rtl] [--skip-package]
          blazix add <item...> [--project <path>] [--overwrite] [--skip-package]
          blazix list [--project <path>]
          blazix migrate rtl [path] [--project <path>] [--yes]

        --rtl sets "rtl": true in blazix.json: components are copied with logical classes
        (ms-2, start-0, text-start), so they lay out right to left under dir="rtl".

        Run without installing:  dnx Blazix.Cli <command>
        """;

    private const string ConfigFile = "blazix.json";

    private static readonly JsonSerializerOptions ConfigJson = new() { WriteIndented = true };

    public static int Init(Options options, string version)
    {
        var target = Project.Find(options.Project);
        var (project, styles) = Layout(target);
        var dir = (options.Dir ?? "Components/UI").Replace('\\', '/').Trim('/');
        var ns = project.RootNamespace + "." + string.Join('.', dir.Split('/').Select(Project.Identifier));
        Console.WriteLine($"Setting up {project.Name} (namespace {ns})");
        if (styles != project)
        {
            Console.WriteLine($"  Tailwind runs in {styles.Name}, the host that serves {project.Name}'s pages");
        }

        // 1. Tailwind without Node: MSBuild targets + dev-time watcher. The watcher is a hosted service,
        //    so it needs a server; a standalone WebAssembly app recompiles the CSS on each build instead.
        styles.WriteFile("Tailwind.targets", Registry.Read("Setup/Tailwind.targets"));
        if (styles.IsWebAssembly)
        {
            Console.WriteLine("  skipped TailwindWatcher (no server here): the CSS is recompiled on each build");
        }
        else
        {
            styles.WriteFile("TailwindWatcher.cs", Registry.RewriteRootNamespace(Registry.Read("Setup/TailwindWatcher.cs"), styles.RootNamespace));
        }
        if (styles != project)
        {
            var client = styles.RelativePathTo(project.Directory);
            styles.AddProperty("TailwindSources", $"{client}/**/*.razor;{client}/Components/**/*.cs",
                $"{project.Name}'s files, which Styles/app.css scans with @source: editing them rebuilds the CSS.");
        }
        styles.AddImport("Tailwind.targets");
        if (!styles.IsWebAssembly)
        {
            styles.RegisterTailwindWatcher();
        }

        // 2. Styles: keep the existing wwwroot/app.css by moving it next to the Tailwind entry point.
        //    Vendor/ is third-party CSS (tw-animate-css and the variants), so it's
        //    replaced on every init; theme.css and app.css are the app's own and never overwritten.
        var siteCss = styles.MoveIfExists("wwwroot/app.css", "Styles/site.css");
        styles.WriteFile("Styles/Vendor/tw-animate.css", Registry.Read("Setup/Vendor/tw-animate.css"));
        styles.WriteFile("Styles/Vendor/variants.css", Registry.Read("Setup/Vendor/variants.css"));
        styles.WriteFile("Styles/theme.css", Registry.Read("Setup/theme.css"), overwrite: false);
        var clientSources = "";
        if (styles != project)
        {
            var client = "../" + styles.RelativePathTo(project.Directory);
            clientSources = $"""
                @source "{client}";
                @source not "{client}/bin";
                @source not "{client}/obj";

                """;
        }
        styles.WriteFile("Styles/app.css", $"""
            /* Compiled by Tailwind.targets into wwwroot/app.css. Edit this file, not the output. */
            @import "tailwindcss" source("..");
            @import "./Vendor/tw-animate.css";
            @import "./Vendor/variants.css";
            @import "./theme.css";
            {(siteCss || styles.Exists("Styles/site.css") ? "@import \"./site.css\";\n" : "")}{clientSources}@source not "../bin";
            @source not "../obj";

            """, overwrite: false);
        if (styles.IsWebAssembly)
        {
            styles.LinkStylesheetInIndexHtml("app.css");
        }

        // 3. Usings so pages can use the components without @using lines. The primitives are a C#
        // global using only: their enums (Side, Orientation...) work in pages, and Razor matches
        // tags against the copied components alone (DialogTitle is the styled one).
        project.AddRazorUsings(ns);
        project.AddGlobalUsing("Blazix.Primitives");
        if (styles != project)
        {
            // The host has App.razor, the layouts and any static pages, which can use the components too.
            styles.AddRazorUsings(ns);
            styles.AddGlobalUsing("Blazix.Primitives");
        }

        // 4. The headless primitives package, pinned to this CLI's version.
        if (!options.SkipPackage)
        {
            project.EnsurePackage("Blazix.Primitives", version);
        }

        // Running init again keeps the record of what's installed.
        var configPath = Path.Combine(project.Directory, ConfigFile);
        var installed = File.Exists(configPath) ? JsonNode.Parse(File.ReadAllText(configPath))?["installed"]?.DeepClone() : null;
        var config = new JsonObject { ["namespace"] = ns, ["componentsDir"] = dir, ["version"] = version, ["rtl"] = options.Rtl, ["installed"] = installed ?? new JsonObject() };
        WriteConfig(configPath, config);
        Console.WriteLine($"  wrote {ConfigFile}");

        var output = styles == project ? "wwwroot/app.css" : $"{styles.Name}'s wwwroot/app.css";
        Console.WriteLine($"""

            Done. Next:
              dnx Blazix.Cli add dropdown-menu
              dotnet watch

            {output} is now generated on build; you can add it to .gitignore.
            """);
        if (styles != project)
        {
            Console.WriteLine($"""

                The components are in {project.Name}, so pages on every render mode (Server, WebAssembly, Auto) can use them.
                Give the pages and components that use them a render mode, e.g. @rendermode InteractiveAuto:
                {styles.Name}'s layouts render statically unless the app is interactive everywhere.
                See https://blazix-ui.com/docs/installation#render-modes
                """);
        }
        return 0;
    }

    /// <summary>
    /// The projects whose Program.cs must register the services a component injects: the components project and,
    /// for a .Client, its host, which runs the same pages on the server (prerendering, Interactive Server and Auto).
    /// </summary>
    private static List<Project> ServiceProjects(Project project)
    {
        List<Project> projects = project.ProgramFile is null ? [] : [project];
        if (project.IsWebAssembly && project.Host() is { ProgramFile: not null } host)
        {
            projects.Add(host);
        }
        return projects;
    }

    /// <summary>
    /// Where the components go, and where Tailwind runs. A Blazor Web App with a .Client project keeps the
    /// components in the client, so pages on every render mode can use them, and Tailwind in the server host,
    /// which serves the CSS and can run the watcher. Either project can be the one init was pointed at.
    /// </summary>
    private static (Project Components, Project Styles) Layout(Project target)
    {
        if (target.IsWebAssembly)
        {
            return (target, target.Host() ?? target);
        }
        return target.WebAssemblyClient() is { } client ? (client, target) : (target, target);
    }

    /// <summary>The project with blazix.json: the one given, or the .Client of the host given.</summary>
    private static Project ComponentsProject(Options options)
    {
        var project = Project.Find(options.Project);
        if (!File.Exists(Path.Combine(project.Directory, ConfigFile))
            && project.WebAssemblyClient() is { } client
            && File.Exists(Path.Combine(client.Directory, ConfigFile)))
        {
            return client;
        }
        return project;
    }

    public static int Add(Options options, string version)
    {
        if (options.Items.Count == 0)
        {
            throw new CliException("say what to add, e.g. 'blazix add dropdown-menu' (see 'blazix list')");
        }

        var project = ComponentsProject(options);
        var configPath = Path.Combine(project.Directory, ConfigFile);
        if (!File.Exists(configPath))
        {
            throw new CliException($"no {ConfigFile} found. Run 'blazix init' first.");
        }

        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        var ns = (string)config["namespace"]!;
        var dir = (string)config["componentsDir"]!;
        var installed = config["installed"] as JsonObject ?? [];
        var rtl = (bool?)config["rtl"] ?? false;

        var plan = Registry.Resolve(options.Items);
        foreach (var item in plan)
        {
            foreach (var file in item.Files)
            {
                var content = Registry.RewriteComponentNamespace(Registry.Read("Components/" + file), ns);
                if (rtl)
                {
                    content = Rtl.Transform(file, content);
                }
                project.WriteFile(TargetPath(project, dir, file), content, options.Overwrite);
            }
            installed[item.Name] = version;
        }

        if (!options.SkipPackage && plan.Any(i => i.UsesPrimitives))
        {
            project.EnsurePackage("Blazix.Primitives", version);
        }

        config["installed"] = installed;
        WriteConfig(configPath, config);
        Console.WriteLine($"\nAdded {string.Join(", ", plan.Select(i => i.Name))}.");

        // Services the components inject. A .Client's pages also run on its host, so both register them.
        var programs = ServiceProjects(project);
        foreach (var service in plan.SelectMany(i => i.Services).Distinct())
        {
            var missing = programs.Where(p => !p.ProgramMentions(service)).Select(p => $"{p.Name}/Program.cs").ToList();
            if (missing.Count > 0)
            {
                Console.WriteLine($"Register {service} for each user: add builder.Services.AddScoped<{service}>(); to {string.Join(" and ", missing)}.");
            }
        }
        return 0;
    }

    /// <summary>
    /// Where a registry file ("Button/Button.razor") goes: its component's folder in componentsDir. A file that
    /// an earlier version copied straight into componentsDir stays there, so it isn't duplicated in a folder.
    /// </summary>
    private static string TargetPath(Project project, string dir, string file)
    {
        var flat = $"{dir}/{Path.GetFileName(file)}";
        return !project.Exists($"{dir}/{file}") && project.Exists(flat) ? flat : $"{dir}/{file}";
    }

    public static int List(Options options)
    {
        JsonObject? installed = null;
        try
        {
            var project = ComponentsProject(options);
            var configPath = Path.Combine(project.Directory, ConfigFile);
            if (File.Exists(configPath))
            {
                installed = JsonNode.Parse(File.ReadAllText(configPath))?["installed"] as JsonObject;
            }
        }
        catch (CliException)
        {
            // Listing works outside a project too.
        }

        foreach (var item in Registry.All())
        {
            var mark = installed?.ContainsKey(item.Name) == true ? "✓" : " ";
            Console.WriteLine($" {mark} {item.Name,-16} {item.Description}");
        }
        return 0;
    }

    /// <summary>
    /// shadcn's migrate rtl: maps the classes of components copied before <c>"rtl": true</c>, then sets it so later
    /// adds come mapped. Run it once: a second run adds the <c>rtl:</c> classes again.
    /// </summary>
    public static int MigrateRtl(Options options)
    {
        var project = ComponentsProject(options);
        var configPath = Path.Combine(project.Directory, ConfigFile);
        if (!File.Exists(configPath))
        {
            throw new CliException($"no {ConfigFile} found. Run 'blazix init' first.");
        }
        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        if ((bool?)config["rtl"] == true && options.Items.Count == 0 && !options.Yes)
        {
            throw new CliException($"{ConfigFile} already has \"rtl\": true, so your components came mapped. Pass a path to migrate other files, or --yes to map them again.");
        }

        var root = options.Items.Count > 0
            ? Path.GetFullPath(options.Items[0], project.Directory)
            : Path.Combine(project.Directory, (string)config["componentsDir"]!);
        if (!File.Exists(root) && !Directory.Exists(root))
        {
            throw new CliException($"not found: {root}");
        }
        string[] files = File.Exists(root) ? [root] : [.. Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(IsComponentSource)];

        var changed = 0;
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var mapped = Rtl.Transform(file, source);
            if (mapped != source)
            {
                File.WriteAllText(file, mapped);
                changed++;
                Console.WriteLine($"  migrated {Path.GetRelativePath(project.Directory, file)}");
            }
        }

        config["rtl"] = true;
        WriteConfig(configPath, config);
        Console.WriteLine($"\nMigrated {changed} of {files.Length} file(s) and set \"rtl\": true in {ConfigFile}.");
        var review = files.Select(Path.GetFileName).Where(f => Rtl.ManualReview.Contains(f)).ToList();
        if (review.Count > 0)
        {
            Console.WriteLine($"Check these by hand, see https://blazix-ui.com/docs/rtl: {string.Join(", ", review)}");
        }
        return 0;
    }

    /// <summary>Copies the registry's components mapped for RTL, in another namespace: the docs render their RTL examples from it.</summary>
    public static int RtlCopy(string from, string to, string ns)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*.*", SearchOption.AllDirectories).Where(IsComponentSource))
        {
            var relative = Path.GetRelativePath(from, file);
            var content = Rtl.Transform(relative, Registry.RewriteComponentNamespace(File.ReadAllText(file), ns));
            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target) || File.ReadAllText(target) != content)
            {
                File.WriteAllText(target, content);
            }
        }
        return 0;
    }

    private static bool IsComponentSource(string file) =>
        Path.GetExtension(file) is ".razor" or ".cs" && !file.Replace(Path.DirectorySeparatorChar, '/').Split('/').Any(p => p is "bin" or "obj");

    private static void WriteConfig(string path, JsonObject config) =>
        File.WriteAllText(path, config.ToJsonString(ConfigJson) + "\n");
}
