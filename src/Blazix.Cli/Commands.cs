using System.Text.Json;
using System.Text.Json.Nodes;

namespace Blazix.Cli;

internal static class Commands
{
    public const string Usage = """
        blazix — copy Blazix components into your Blazor project as source you own.

        Usage:
          blazix init [--project <path>] [--dir Components/UI] [--rtl] [--icon-library <name>] [--skip-package]
          blazix add <item...> | --all [--project <path>] [--overwrite] [--skip-package]
          blazix diff [item...] [--project <path>]
          blazix list [--project <path>]
          blazix migrate rtl [path] [--project <path>] [--yes]
          blazix migrate icons [path] [--from <name>] [--to <name>] [--yes] [--project <path>]

        --rtl sets "rtl": true in blazix.json: components are copied with logical classes
        (ms-2, start-0, text-start), so they lay out right to left under dir="rtl".

        --icon-library sets "iconLibrary" in blazix.json: the icon set the components draw their icons from. init and add
        install its package (Blazix.Icons.Lucide, .Tabler, ...): lucide (the default), tabler, hugeicons, phosphor,
        remixicon, fontawesome.

        --all adds every component, with the dependencies they need, as shadcn's add --all does.

        migrate icons moves the components' icons to another library, as shadcn's does. It asks for the libraries you don't
        give, and confirms before it writes (--yes skips that). It lists the icons with no equivalent in the new library and
        leaves them as they are. Copies from earlier versions draw their icons inline, as SVG: migrate icons leaves those as
        they are, and add --overwrite replaces a copy with one that uses <Icon>s.

        diff shows what add --overwrite would change before you run it. With no items, it says whether each component
        you've added still matches. With items, it shows the changes to their files: - lines are yours, + lines are blazix's.

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

        // Running init again keeps the record of what's installed, and the icon library unless one is given.
        var configPath = Path.Combine(project.Directory, ConfigFile);
        var existing = File.Exists(configPath) ? JsonNode.Parse(File.ReadAllText(configPath)) : null;
        var installed = existing?["installed"]?.DeepClone();
        var iconLibrary = Icons.Library(options.IconLibrary ?? (string?)existing?["iconLibrary"]);
        var config = new JsonObject
        {
            ["namespace"] = ns, ["componentsDir"] = dir, ["version"] = version, ["rtl"] = options.Rtl, ["iconLibrary"] = iconLibrary,
            ["installed"] = installed ?? new JsonObject(),
        };
        WriteConfig(configPath, config);
        Console.WriteLine($"  wrote {ConfigFile}");

        // The components draw their icons from the set's package: shadcn's init installs its icon library the same way.
        var package = Icons.Package(iconLibrary);
        if (!options.SkipPackage)
        {
            project.EnsurePackage(package, version);
        }

        // A blazix.json without iconLibrary is lucide, which is what earlier versions drew with.
        var previousLibrary = existing is null ? null : Icons.Library((string?)existing["iconLibrary"]);
        if (previousLibrary is not null && previousLibrary != iconLibrary)
        {
            var sources = ComponentSources(project, dir);
            if (sources.Any(source => Icons.UsesLibrary(source, previousLibrary)))
            {
                Console.WriteLine($"  Icons you've already added keep their {previousLibrary} icons. To switch them: dnx Blazix.Cli migrate icons --from {previousLibrary} --to {iconLibrary}");
            }
            if (sources.Any(source => source.Contains("<svg", StringComparison.Ordinal)))
            {
                Console.WriteLine("  Copies that draw inline SVG stay as they are: add --overwrite replaces one with <Icon>s.");
            }
        }

        var output = styles == project ? "wwwroot/app.css" : $"{styles.Name}'s wwwroot/app.css";
        var install = options.SkipPackage ? $" Add it with: dotnet add package {package}" : "";
        Console.WriteLine($"""

            Done. Next:
              dnx Blazix.Cli add dropdown-menu
              dotnet watch

            {output} is now generated on build; you can add it to .gitignore.
            The components draw their icons from {package} ({iconLibrary}).{install}
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
        if (options.All && options.Items.Count > 0)
        {
            throw new CliException("--all adds every component, so name none: run 'blazix add --all' alone");
        }
        if (options.Items.Count == 0 && !options.All)
        {
            throw new CliException("say what to add, e.g. 'blazix add dropdown-menu' (see 'blazix list'), or every component: 'blazix add --all'");
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
        var iconLibrary = Icons.Library((string?)config["iconLibrary"]);
        if (options.IconLibrary is { } requested && Icons.Library(requested) != iconLibrary)
        {
            throw new CliException($"this project's icons come from {iconLibrary} (blazix.json's iconLibrary), and add doesn't switch them. "
                + $"To switch to {requested}, run migrate icons --to {requested}: it swaps the icons of the components you've added and sets iconLibrary.");
        }

        // As shadcn's add --all: every component, by name, with the dependencies they need resolved in.
        var names = options.All ? Registry.All().Select(item => item.Name).Order().ToList() : options.Items;
        var plan = Registry.Resolve(names);
        var usesIcons = false;
        foreach (var item in plan)
        {
            foreach (var file in item.Files)
            {
                var content = Render(file, ns, iconLibrary, rtl, out var fileUsesIcons);
                usesIcons |= fileUsesIcons;
                project.WriteFile(TargetPath(project, dir, file), content, options.Overwrite);
            }
            installed[item.Name] = version;
        }

        if (!options.SkipPackage && plan.Any(i => i.UsesPrimitives))
        {
            project.EnsurePackage("Blazix.Primitives", version);
        }
        if (!options.SkipPackage && usesIcons)
        {
            project.EnsurePackage(Icons.Package(iconLibrary), version);
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
    /// shadcn's diff: what <c>add --overwrite</c> would change, before you run it. With no items, whether each component
    /// you've added still matches this version's copy. With items, the changes to their files, and to their dependencies'
    /// (which add writes too), as unified diffs: <c>-</c> lines are yours, <c>+</c> lines are the version's.
    /// </summary>
    public static int Diff(Options options, string version)
    {
        var project = ComponentsProject(options);
        var configPath = Path.Combine(project.Directory, ConfigFile);
        if (!File.Exists(configPath))
        {
            throw new CliException($"no {ConfigFile} found. Run 'blazix init' first.");
        }
        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        var upstream = new Upstream(project, (string)config["componentsDir"]!, (string)config["namespace"]!,
            Icons.Library((string?)config["iconLibrary"]), (bool?)config["rtl"] ?? false, version);

        if (options.Items.Count > 0)
        {
            DiffItems(upstream, options.Items);
        }
        else
        {
            DiffAdded(upstream, config["installed"] as JsonObject ?? []);
        }
        return 0;
    }

    /// <summary>The changes to the named items' files, and their dependencies', as add --overwrite would write them.</summary>
    private static void DiffItems(Upstream upstream, List<string> names)
    {
        var checks = Registry.Resolve(names).Select(item => CheckItem(upstream, item)).ToList();
        PrintDiffs(checks);
        if (checks.Any(check => check.Differs))
        {
            Console.WriteLine($"\n- lines are yours and + lines are blazix {upstream.Version}'s. To replace yours with blazix's, run:\n"
                + $"  dnx Blazix.Cli add {string.Join(' ', names)} --overwrite");
        }
        else if (checks.All(check => check.Matches))
        {
            Console.WriteLine($"Your copies of {string.Join(", ", names)} match blazix {upstream.Version}: nothing to overwrite.");
        }
    }

    /// <summary>Each component added, and whether it still matches this version's copy. Names this version doesn't have are listed too.</summary>
    private static void DiffAdded(Upstream upstream, JsonObject installed)
    {
        var registry = Registry.All();
        var added = registry.Where(item => installed.ContainsKey(item.Name)).Select(item => CheckItem(upstream, item)).ToList();
        var unknown = installed.Select(entry => entry.Key).Where(name => registry.All(item => item.Name != name)).ToList();
        if (added.Count == 0 && unknown.Count == 0)
        {
            Console.WriteLine("Nothing added yet. Add components with: dnx Blazix.Cli add <item>");
            return;
        }

        Console.WriteLine($"Your components, compared with blazix {upstream.Version}:\n");
        PrintSummary(added);
        foreach (var name in unknown)
        {
            Console.WriteLine($"  {name,-16} not in blazix {upstream.Version}, so it can't be compared");
        }
        if (added.Any(check => !check.Matches))
        {
            Console.WriteLine("\nSee a component's changes with: dnx Blazix.Cli diff <item>");
        }
        else if (added.Count > 0)
        {
            Console.WriteLine($"\nEverything you've added matches blazix {upstream.Version}: nothing to overwrite.");
        }
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

    /// <summary>
    /// shadcn's migrate icons: moves the components' icons from one icon library to another, then sets <c>iconLibrary</c> so
    /// later adds match. It asks for the libraries it isn't given, and confirms before it writes, as shadcn's does. Inline SVG
    /// icons, from copies made before icons came from packages, become <c>&lt;Icon&gt;</c>s. An icon with no equivalent in the
    /// new library is listed and left as it is. A path migrates just those files and leaves blazix.json alone.
    /// </summary>
    public static int MigrateIcons(Options options, string version)
    {
        var project = ComponentsProject(options);
        var configPath = Path.Combine(project.Directory, ConfigFile);
        if (!File.Exists(configPath))
        {
            throw new CliException($"no {ConfigFile} found. Run 'blazix init' first.");
        }
        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        var current = Icons.Library((string?)config["iconLibrary"]);

        var root = options.Items.Count > 0
            ? Path.GetFullPath(options.Items[0], project.Directory)
            : Path.Combine(project.Directory, (string)config["componentsDir"]!);
        if (!File.Exists(root) && !Directory.Exists(root))
        {
            throw new CliException($"not found: {root}");
        }
        string[] files = File.Exists(root) ? [root] : [.. Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories).Where(IsComponentSource)];

        // The libraries are asked for when they aren't given, as shadcn asks. The library to migrate from defaults to the current
        // one; the library to migrate to has no default, since the icons have to move somewhere.
        var from = options.From is { } given
            ? Icons.Library(given)
            : AskLibrary("Which icon library would you like to migrate from?", current) ?? current;
        var to = options.To is { } target
            ? Icons.Library(target)
            : AskLibrary("Which icon library would you like to migrate to?", null)
                ?? throw new CliException($"say which icon library to migrate to, e.g. --to tabler. Available: {string.Join(", ", Icons.Libraries)}");
        if (from == to)
        {
            throw new CliException("cannot migrate to the same icon library: choose a different one. To replace a copy that draws inline SVG, add --overwrite.");
        }

        var shown = options.Items.Count > 0
            ? options.Items[0]
            : Path.GetRelativePath(project.Directory, root).Replace(Path.DirectorySeparatorChar, '/');
        var question = $"We will migrate {files.Length} file(s) in {shown} from {from} to {to}.";
        if (!options.Yes && !Confirm($"{question} Continue?"))
        {
            Console.WriteLine("Migration cancelled.");
            return 0;
        }

        var skipped = new List<Icons.Skipped>();
        int swapped = 0, changed = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(project.Directory, file).Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                var source = File.ReadAllText(file);
                var migrated = Icons.Migrate(source, from, to, skipped, out var fileSwapped);
                swapped += fileSwapped;
                if (migrated != source)
                {
                    File.WriteAllText(file, migrated);
                    changed++;
                    Console.WriteLine($"  migrated {relative} ({fileSwapped} icon(s))");
                }
            }
            catch (CliException ex)
            {
                Console.WriteLine($"  left {relative} as it is: {ex.Message}");
            }
        }

        if (options.Items.Count == 0)
        {
            config["iconLibrary"] = to;
            WriteConfig(configPath, config);
        }
        if (!options.SkipPackage && swapped > 0)
        {
            project.EnsurePackage(Icons.Package(to), version);
        }

        Console.WriteLine();
        if (swapped == 0)
        {
            Console.WriteLine($"Nothing to migrate: no {from} icons in these files.");
        }
        else
        {
            Console.WriteLine($"Migrated {swapped} icon(s) in {changed} of {files.Length} file(s) from {from} to {to}.");
        }
        if (options.Items.Count == 0)
        {
            Console.WriteLine($"Set \"iconLibrary\": \"{to}\" in {ConfigFile}.");
        }

        // Listed by name, as shadcn lists them: the icons of the library migrated from that the new one has no equivalent for.
        var listed = skipped.DistinctBy(item => item.Icon).ToList();
        if (listed.Count > 0)
        {
            Console.WriteLine($"Skipped {listed.Count} icon(s). These were left untouched:");
            foreach (var item in listed)
            {
                Console.WriteLine($"  - {item.Icon}: {item.Reason}");
            }
        }
        return 0;
    }

    /// <summary>
    /// Asks for an icon library: the names are numbered, and a number or a name answers. Enter takes
    /// <paramref name="defaultLibrary"/>, or gives no answer when there is none. Null when there is no answer, including when
    /// the input has ended, so no answer is made up.
    /// </summary>
    private static string? AskLibrary(string question, string? defaultLibrary)
    {
        var names = Icons.Libraries.ToList();
        Console.WriteLine(question);
        for (var i = 0; i < names.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {names[i]}");
        }
        var prompt = defaultLibrary is null ? "Enter a number or a name: " : $"Enter a number or a name [{defaultLibrary}]: ";
        Console.Write(prompt);
        var answer = ReadAnswer();
        if (answer is null)
        {
            Console.WriteLine();
            return null;
        }
        if (answer.Length == 0)
        {
            return defaultLibrary;
        }
        return int.TryParse(answer, out var number) && number >= 1 && number <= names.Count
            ? names[number - 1]
            : Icons.Library(answer);
    }

    /// <summary>Asks a yes-or-no question, where Enter says yes. When the input has ended, the answer is no.</summary>
    private static bool Confirm(string question)
    {
        Console.Write($"{question} [Y/n] ");
        var answer = ReadAnswer()?.ToLowerInvariant();
        if (answer is null)
        {
            Console.WriteLine();
            return false;
        }
        return answer is "" or "y" or "yes";
    }

    /// <summary>
    /// The next line of input, trimmed, or null when the input has ended. Windows PowerShell writes a byte-order mark before
    /// the first line it pipes in, so that is dropped too.
    /// </summary>
    private static string? ReadAnswer() => Console.ReadLine()?.Trim().TrimStart('﻿').Trim();

    /// <summary>
    /// Copies the registry's components as <c>add</c> would, with the default icon library (Lucide, as <c>&lt;Icon&gt;</c>
    /// elements of Blazix.Icons.Lucide) and, when <paramref name="rtl"/>, <c>"rtl": true</c>, in another namespace. The docs
    /// build renders its RTL examples from the RTL copy, and its installation steps from the other: an IconPlaceholder
    /// there would show a placeholder that only <c>add</c> replaces. Fails the docs build on a bad IconPlaceholder.
    /// </summary>
    public static int RegistryCopy(string from, string to, string ns, bool rtl)
    {
        var sources = Directory.EnumerateFiles(from, "*.*", SearchOption.AllDirectories).Where(IsComponentSource).ToList();
        var problems = sources.SelectMany(f => Icons.Check(Path.GetRelativePath(from, f), File.ReadAllText(f))).ToList();
        if (problems.Count > 0)
        {
            throw new CliException("the components' icons:\n  " + string.Join("\n  ", problems));
        }
        foreach (var file in sources)
        {
            var relative = Path.GetRelativePath(from, file);
            var content = Icons.Transform(relative, Registry.RewriteComponentNamespace(File.ReadAllText(file), ns), Icons.Default, out _);
            if (rtl)
            {
                content = Rtl.Transform(relative, content);
            }
            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target) || File.ReadAllText(target) != content)
            {
                File.WriteAllText(target, content);
            }
        }
        return 0;
    }

    /// <summary>What add writes into a project: where each registry file goes, and how it's rendered there.</summary>
    private sealed record Upstream(Project Project, string Dir, string Namespace, string IconLibrary, bool Rtl, string Version);

    /// <summary>A registry item, and how each of its files compares with the project's copy.</summary>
    private sealed record ItemCheck(RegistryItem Item, List<FileCheck> Files)
    {
        public bool Matches => Files.All(file => file.Matches);
        public bool Differs => Files.Any(file => file.Differs);
    }

    /// <param name="Relative">The file's path in the project.</param>
    /// <param name="Changes">How the project's copy differs from upstream's; null when there is no copy.</param>
    private sealed record FileCheck(string Relative, LineDiff.Changes? Changes)
    {
        public bool Matches => Changes is { IsEmpty: true };
        public bool Differs => Changes is { IsEmpty: false };
    }

    private static ItemCheck CheckItem(Upstream upstream, RegistryItem item) =>
        new(item, item.Files.Select(file => CheckFile(upstream, file)).ToList());

    private static FileCheck CheckFile(Upstream upstream, string file)
    {
        var relative = TargetPath(upstream.Project, upstream.Dir, file);
        if (!upstream.Project.Exists(relative))
        {
            return new FileCheck(relative, null);
        }
        var content = Render(file, upstream.Namespace, upstream.IconLibrary, upstream.Rtl, out _);
        return new FileCheck(relative, LineDiff.Compare(upstream.Project.ReadText(relative), content,
            $"{relative} (yours)", $"{relative} (blazix {upstream.Version})"));
    }

    private static void PrintSummary(List<ItemCheck> checks)
    {
        foreach (var check in checks)
        {
            Console.WriteLine($"  {check.Item.Name,-16} {(check.Matches ? "up to date" : "changed")}");
            foreach (var file in check.Files.Where(f => !f.Matches))
            {
                var change = file.Changes is { } changes ? $"+{changes.Added} -{changes.Removed}" : "missing";
                Console.WriteLine($"    {file.Relative,-40} {change}");
            }
        }
    }

    private static void PrintDiffs(List<ItemCheck> checks)
    {
        foreach (var file in checks.SelectMany(check => check.Files))
        {
            if (file.Changes is null)
            {
                Console.WriteLine($"{file.Relative} isn't in your project: add would create it.");
            }
            else if (!file.Changes.IsEmpty)
            {
                foreach (var line in file.Changes.Unified)
                {
                    Console.WriteLine(line);
                }
            }
        }
    }

    /// <summary>The text of each .razor file in <paramref name="dir"/>, where the components are.</summary>
    private static List<string> ComponentSources(Project project, string dir)
    {
        var root = Path.Combine(project.Directory, dir);
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories).Select(file => File.ReadAllText(file)).ToList()
            : new List<string>();
    }

    /// <summary>
    /// The registry's file as add writes it: in the app's namespace, with its icons drawn from the set and, when
    /// <paramref name="rtl"/>, its classes mapped. <paramref name="usesIcons"/> says whether it had any icons.
    /// </summary>
    private static string Render(string file, string ns, string iconLibrary, bool rtl, out bool usesIcons)
    {
        var content = Icons.Transform(file, Registry.RewriteComponentNamespace(Registry.Read("Components/" + file), ns),
            iconLibrary, out usesIcons);
        return rtl ? Rtl.Transform(file, content) : content;
    }

    private static bool IsComponentSource(string file) =>
        Path.GetExtension(file) is ".razor" or ".cs" && !file.Replace(Path.DirectorySeparatorChar, '/').Split('/').Any(p => p is "bin" or "obj");

    private static void WriteConfig(string path, JsonObject config) =>
        File.WriteAllText(path, config.ToJsonString(ConfigJson) + "\n");
}
