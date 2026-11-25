using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Blazix.Cli;

internal sealed partial class Project
{
    private static readonly string[] ImportsCandidates = ["Components/_Imports.razor", "_Imports.razor"];

    private Project(string path)
    {
        FilePath = path;
        Directory = Path.GetDirectoryName(path)!;
        Name = Path.GetFileNameWithoutExtension(path);
        var match = RootNamespaceElement.Match(File.ReadAllText(path));
        RootNamespace = match.Success ? match.Groups[1].Value : string.Join('.', Name.Split('.').Select(Identifier));
    }

    public string Directory { get; }
    public string Name { get; }
    public string RootNamespace { get; }
    private string FilePath { get; }

    /// <summary>A Blazor WebAssembly project: a standalone app, or the .Client of a Blazor Web App (Auto/WebAssembly).</summary>
    public bool IsWebAssembly => WebAssemblySdk.IsMatch(File.ReadAllText(FilePath));

    /// <summary>The Blazor WebAssembly project this host references, if exactly one (a Blazor Web App's .Client).</summary>
    public Project? WebAssemblyClient()
    {
        var clients = ProjectReferences().Where(File.Exists).Select(p => new Project(p)).Where(p => p.IsWebAssembly).ToList();
        return clients is [var single] ? single : null;
    }

    /// <summary>The project that references this one, looked for beside it (a .Client's server host).</summary>
    public Project? Host()
    {
        var parent = Path.GetDirectoryName(Directory);
        if (parent is null)
        {
            return null;
        }
        var candidates = System.IO.Directory.GetFiles(parent, "*.csproj")
            .Concat(System.IO.Directory.GetDirectories(parent).SelectMany(d => System.IO.Directory.GetFiles(d, "*.csproj")))
            .Where(p => !SamePath(p, FilePath))
            .Select(p => new Project(Path.GetFullPath(p)))
            .Where(p => p.ProjectReferences().Any(r => SamePath(r, FilePath)))
            .ToList();
        return candidates is [var single] ? single : null;
    }

    /// <summary>A path from this project's folder, with forward slashes (for CSS and MSBuild globs).</summary>
    public string RelativePathTo(string path) => Path.GetRelativePath(Directory, path).Replace('\\', '/');

    private IEnumerable<string> ProjectReferences() =>
        ProjectReferenceElement.Matches(File.ReadAllText(FilePath))
            .Select(m => Path.GetFullPath(Path.Combine(Directory, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Adds an MSBuild property to the project, unless one of that name is already set.
    /// </summary>
    public void AddProperty(string name, string value, string comment)
    {
        var xml = File.ReadAllText(FilePath);
        if (xml.Contains($"<{name}>", StringComparison.Ordinal))
        {
            return;
        }
        // Before the Tailwind.targets import, which reads it.
        var index = xml.IndexOf("<Import Project=\"Tailwind.targets\"", StringComparison.Ordinal);
        index = index >= 0 ? xml.LastIndexOf('\n', index) + 1 : xml.LastIndexOf("</Project>", StringComparison.Ordinal);
        xml = xml[..index] + $"  <PropertyGroup>\n    <!-- {comment} -->\n    <{name}>{value}</{name}>\n  </PropertyGroup>\n\n" + xml[index..];
        File.WriteAllText(FilePath, xml);
        Console.WriteLine($"  set {name} in {Path.GetFileName(FilePath)}");
    }

    public static Project Find(string? path)
    {
        path ??= System.IO.Directory.GetCurrentDirectory();
        if (System.IO.Directory.Exists(path))
        {
            path = ProjectFileIn(path);
        }
        return File.Exists(path) ? new Project(Path.GetFullPath(path)) : throw new CliException($"'{path}' not found");
    }

    private static string ProjectFileIn(string directory) => System.IO.Directory.GetFiles(directory, "*.csproj") switch
    {
        [var single] => single,
        [] => ProjectInSolutionFolder(directory)
              ?? throw new CliException("no .csproj in this folder. Run inside your Blazor project or pass --project."),
        _ => throw new CliException("several .csproj files here. Pass --project <path>."),
    };

    /// <summary>
    /// A solution folder with a project per subfolder, as <c>dotnet new blazor --interactivity Auto</c> makes it: the
    /// only project, or the one Blazor Web App host whose .Client is beside it (test projects don't count).
    /// </summary>
    private static string? ProjectInSolutionFolder(string directory)
    {
        var projects = System.IO.Directory.GetDirectories(directory)
            .SelectMany(d => System.IO.Directory.GetFiles(d, "*.csproj"))
            .Select(p => new Project(Path.GetFullPath(p)))
            .ToList();
        if (projects is [var only])
        {
            return only.FilePath;
        }
        var hosts = projects
            .Where(p => p.WebAssemblyClient() is { } client && projects.Any(other => SamePath(other.FilePath, client.FilePath)))
            .ToList();
        if (hosts is [var host])
        {
            Console.WriteLine($"Using {host.Name}, the Blazor Web App in this folder");
            return host.FilePath;
        }
        return projects.Count > 1
            ? throw new CliException($"several projects in this folder's subfolders ({string.Join(", ", projects.Select(p => p.Name))}). Pass --project <path>.")
            : null;
    }

    /// <summary>The project's Program.cs, if it has one.</summary>
    public string? ProgramFile => File.Exists(Full("Program.cs")) ? Full("Program.cs") : null;

    /// <summary>Whether the project's Program.cs mentions this text (a service it registers).</summary>
    public bool ProgramMentions(string text) =>
        ProgramFile is { } program && File.ReadAllText(program).Contains(text, StringComparison.Ordinal);

    // Mirrors how Razor turns folder names into namespace segments.
    public static string Identifier(string segment)
    {
        var cleaned = NonIdentifierChar.Replace(segment, "_");
        return char.IsDigit(cleaned[0]) ? "_" + cleaned : cleaned;
    }

    public bool Exists(string relative) => File.Exists(Full(relative));

    public string ReadText(string relative) => File.ReadAllText(Full(relative));

    public void WriteFile(string relative, string content, bool overwrite = true)
    {
        var full = Full(relative);
        if (File.Exists(full) && !overwrite)
        {
            Console.WriteLine($"  kept {relative} (already exists{(relative.EndsWith(".css") ? "" : "; --overwrite to replace, or dnx Blazix.Cli diff to compare")})");
            return;
        }
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        Console.WriteLine($"  wrote {relative}");
    }

    public bool MoveIfExists(string from, string to)
    {
        if (!File.Exists(Full(from)) || File.Exists(Full(to)))
        {
            return false;
        }
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Full(to))!);
        File.Move(Full(from), Full(to));
        Console.WriteLine($"  moved {from} -> {to} (now imported by Styles/app.css)");
        return true;
    }

    public void AddImport(string targetsFile)
    {
        var xml = File.ReadAllText(FilePath);
        if (xml.Contains($"\"{targetsFile}\"", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var index = xml.LastIndexOf("</Project>", StringComparison.Ordinal);
        xml = xml[..index] + $"  <Import Project=\"{targetsFile}\" />\n\n" + xml[index..];
        File.WriteAllText(FilePath, xml);
        Console.WriteLine($"  imported {targetsFile} in {Path.GetFileName(FilePath)}");
    }

    /// <summary>
    /// A C# global using (&lt;Using Include&gt;): its types work in pages' code (Side.Right...) but Razor
    /// doesn't match tags against it, so its components don't collide with the copied ones.
    /// </summary>
    public void AddGlobalUsing(string ns)
    {
        var xml = File.ReadAllText(FilePath);
        if (xml.Contains($"<Using Include=\"{ns}\"", StringComparison.Ordinal))
        {
            return;
        }
        var index = xml.LastIndexOf("</Project>", StringComparison.Ordinal);
        xml = xml[..index] + $"  <ItemGroup>\n    <Using Include=\"{ns}\" />\n  </ItemGroup>\n\n" + xml[index..];
        File.WriteAllText(FilePath, xml);
        Console.WriteLine($"  added a global using for {ns} to {Path.GetFileName(FilePath)}");
    }

    public void RegisterTailwindWatcher()
    {
        var program = Full("Program.cs");
        var source = File.Exists(program) ? File.ReadAllText(program) : "";
        if (source.Contains("AddTailwindWatcher", StringComparison.Ordinal))
        {
            return;
        }
        var match = BuilderAssignment.Match(source);
        if (!match.Success)
        {
            Console.WriteLine("  ! add 'builder.Services.AddTailwindWatcher();' to Program.cs yourself (couldn't find the builder)");
            return;
        }
        var insert = $"\n// Development only: recompiles Tailwind as you edit, so hot reload shows new classes.\n{match.Groups[1].Value}.Services.AddTailwindWatcher();\n";
        File.WriteAllText(program, source.Insert(match.Index + match.Length, insert));
        Console.WriteLine("  registered TailwindWatcher in Program.cs");
    }

    /// <summary>
    /// Links a stylesheet from wwwroot/index.html, a standalone WebAssembly app's page.
    /// A Blazor Web App's App.razor links app.css already.
    /// </summary>
    public void LinkStylesheetInIndexHtml(string href)
    {
        var index = Full("wwwroot/index.html");
        if (!File.Exists(index))
        {
            return;
        }
        var html = File.ReadAllText(index);
        if (html.Contains($"href=\"{href}\"", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head < 0)
        {
            Console.WriteLine($"  ! add <link rel=\"stylesheet\" href=\"{href}\" /> to wwwroot/index.html yourself (no </head> found)");
            return;
        }
        // Indented like the line before </head>.
        var lineStart = html.LastIndexOf('\n', head) + 1;
        var indent = html[lineStart..head];
        var newline = html.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        html = html[..head] + $"    <link rel=\"stylesheet\" href=\"{href}\" />{newline}{indent}" + html[head..];
        File.WriteAllText(index, html);
        Console.WriteLine($"  linked {href} in wwwroot/index.html");
    }

    public void AddRazorUsings(params string[] namespaces)
    {
        var imports = ImportsCandidates.Select(Full).FirstOrDefault(File.Exists) ?? Full(ImportsCandidates[0]);
        var text = File.Exists(imports) ? File.ReadAllText(imports) : "";
        var lines = text.Split('\n').Select(line => line.TrimEnd()).ToHashSet(StringComparer.Ordinal);
        var missing = namespaces.Where(n => !lines.Contains($"@using {n}")).ToList();
        if (missing.Count == 0)
        {
            return;
        }
        var prefix = text.Length == 0 || text.EndsWith('\n') ? "" : "\n";
        File.AppendAllText(imports, prefix + string.Concat(missing.Select(n => $"@using {n}\n")));
        Console.WriteLine($"  added @using {string.Join(", ", missing)} to {Path.GetRelativePath(Directory, imports)}");
    }

    public void EnsurePackage(string id, string version)
    {
        // A PackageReference specifically: init's <Using Include="Blazix.Primitives" /> names it too.
        var reference = new Regex($@"<PackageReference\s[^>]*Include\s*=\s*""{Regex.Escape(id)}""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (reference.IsMatch(File.ReadAllText(FilePath)))
        {
            return;
        }
        Console.WriteLine($"  adding package {id} {version}...");
        var manualStep = $"  ! couldn't add {id}; run: dotnet add package {id} --version {version}";
        if (FindDotnet() is not { } dotnet)
        {
            Console.WriteLine(manualStep);
            return;
        }

        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "add", FilePath, "package", id, "--version", version },
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            return;
        }
        Console.WriteLine(manualStep);
        Console.WriteLine(error.Result.Length > 0 ? error.Result : output.Result);
    }

    /// <summary>
    /// The dotnet executable by absolute path, so nothing earlier on PATH can stand in for it:
    /// DOTNET_HOST_PATH when set, otherwise the install whose runtime is running this tool
    /// (it lives in &lt;dotnet root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/).
    /// </summary>
    private static string? FindDotnet()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (File.Exists(hostPath))
        {
            return hostPath;
        }
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var dotnet = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(dotnet) ? dotnet : null;
    }

    private string Full(string relative) => Path.GetFullPath(Path.Combine(Directory, relative));

    [GeneratedRegex(@"<RootNamespace>\s*([^<\s]+)\s*</RootNamespace>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RootNamespaceElement { get; }

    [GeneratedRegex(@"[^\p{L}\p{Nd}_]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NonIdentifierChar { get; }

    [GeneratedRegex(@"<Project\s+Sdk\s*=\s*""Microsoft\.NET\.Sdk\.BlazorWebAssembly""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WebAssemblySdk { get; }

    [GeneratedRegex(@"<ProjectReference\s[^>]*Include\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectReferenceElement { get; }

    [GeneratedRegex(@"^.*\b(\w+)\s*=\s*WebApplication\.CreateBuilder\([^)]*\);[^\n]*\n", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuilderAssignment { get; }
}
