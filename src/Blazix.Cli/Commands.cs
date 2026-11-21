using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blazix.Cli;

internal static class Commands
{
    public const string Usage = """
        blazix — copy Blazix components into your Blazor project as source you own.

        Usage:
          blazix init [--project <path>] [--dir Components/UI] [--skip-package]
          blazix add <item...> [--project <path>] [--overwrite] [--skip-package]
          blazix list [--project <path>]

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

        // 4. The headless primitives package, pinned to this CLI's version.
        if (!options.SkipPackage)
        {
            project.EnsurePackage("Blazix.Primitives", version);
        }

        var config = new JsonObject { ["namespace"] = ns, ["componentsDir"] = dir, ["version"] = version, ["installed"] = new JsonObject() };
        WriteConfig(Path.Combine(project.Directory, ConfigFile), config);
        Console.WriteLine($"  wrote {ConfigFile}");

        var output = styles == project ? "wwwroot/app.css" : $"{styles.Name}'s wwwroot/app.css";
        Console.WriteLine($"""

            Done. Next:
              dnx Blazix.Cli add dropdown-menu
              dotnet watch

            {output} is now generated on build; you can add it to .gitignore.
            """);
        return 0;
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

        var plan = Registry.Resolve(options.Items);
        foreach (var item in plan)
        {
            foreach (var file in item.Files)
            {
                var content = Registry.RewriteComponentNamespace(Registry.Read("Components/" + file), ns);
                project.WriteFile($"{dir}/{file}", content, options.Overwrite);
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
        return 0;
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

    private static void WriteConfig(string path, JsonObject config) =>
        File.WriteAllText(path, config.ToJsonString(ConfigJson) + "\n");
}
