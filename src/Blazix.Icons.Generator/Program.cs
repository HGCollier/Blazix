using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Blazix.Icons.Generator;

// set <key> <cache dir> <out .cs>             every icon of a set, for its Blazix.Icons.* package
// registry <registry dir> <cache dir> <out>   the icons the registry's IconPlaceholders name, in every set
try
{
    switch (args)
    {
        case ["set", var key, var cache, var output]:
            var set = IconSets.Find(key);
            var (glyphs, license) = await Read(set, cache);
            WriteIfChanged(output, Writers.Set(set, glyphs));
            // The set's own license, which its package ships next to Blazix's.
            WriteIfChanged(Path.ChangeExtension(output, null) + ".LICENSE.txt", license);
            return 0;
        case ["registry", var registry, var cache, var output]:
            var (used, rows) = Placeholders.Scan(registry);
            var sets = new List<(IconSet, IReadOnlyList<Glyph>)>();
            foreach (var each in IconSets.All)
            {
                sets.Add((each, (await Read(each, cache)).Glyphs));
            }
            WriteIfChanged(output, Writers.Registry(sets, used, rows));
            return 0;
        default:
            await Console.Error.WriteLineAsync("usage: set <key> <cache dir> <out.cs> | registry <registry dir> <cache dir> <out.cs>");
            return 2;
    }
}
catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or IOException)
{
    await Console.Error.WriteLineAsync($"error: {ex.Message}");
    return 1;
}

static async Task<(IReadOnlyList<Glyph> Glyphs, string License)> Read(IconSet set, string cache)
{
    var tarball = await Fetch(set, cache);
    var glyphs = new List<Glyph>();
    string? license = null;
    await using var file = File.OpenRead(tarball);
    await using var gzip = new GZipStream(file, CompressionMode.Decompress);
    await using var tar = new TarReader(gzip);
    while (await tar.GetNextEntryAsync() is { } entry)
    {
        var isLicense = entry.Name is "package/LICENSE" or "package/LICENSE.md" or "package/LICENSE.txt" or "package/License";
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null || !(isLicense || set.Include(entry.Name)))
        {
            continue;
        }
        using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        if (isLicense)
        {
            license = content;
        }
        else if (set.Read(entry.Name, content) is { } glyph)
        {
            glyphs.Add(glyph);
        }
    }
    if (glyphs.Count == 0)
    {
        throw new InvalidDataException($"no icons found in {set.FileName}");
    }
    if (license is null)
    {
        throw new InvalidDataException($"no license file found in {set.FileName}");
    }
    return ([.. glyphs.OrderBy(g => g.Group, StringComparer.Ordinal).ThenBy(g => g.Name, StringComparer.Ordinal)], license);
}

// The set's tarball from the npm registry, cached, and checked against the pinned sha512 every time it's used.
static async Task<string> Fetch(IconSet set, string cache)
{
    Directory.CreateDirectory(cache);
    var path = Path.Combine(cache, set.FileName);
    if (File.Exists(path) && Matches(path, set.Integrity))
    {
        return path;
    }
    Console.WriteLine($"Downloading {set.TarballUrl}");
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Blazix.Icons.Generator");
    var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
    await using (var stream = await http.GetStreamAsync(set.TarballUrl))
    await using (var target = File.Create(partial))
    {
        await stream.CopyToAsync(target);
    }
    if (!Matches(partial, set.Integrity))
    {
        File.Delete(partial);
        throw new InvalidDataException($"{set.TarballUrl} doesn't match its pinned integrity {set.Integrity}");
    }
    File.Move(partial, path, overwrite: true);
    return path;
}

static bool Matches(string path, string integrity)
{
    using var stream = File.OpenRead(path);
    return integrity == "sha512-" + Convert.ToBase64String(SHA512.HashData(stream));
}

static void WriteIfChanged(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    if (!File.Exists(path) || File.ReadAllText(path) != content)
    {
        File.WriteAllText(path, content);
    }
}

namespace Blazix.Icons.Generator
{
    /// <summary>The icon names the registry's components give their IconPlaceholders: per set, and per placeholder (a row).</summary>
    internal static partial class Placeholders
    {
        public static (Dictionary<string, SortedSet<string>> Used, List<SortedDictionary<string, string>> Rows) Scan(string registry)
        {
            var used = IconSets.All.ToDictionary(s => s.Key, _ => new SortedSet<string>(StringComparer.Ordinal));
            var rows = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(registry, "Components"), "*.razor", SearchOption.AllDirectories))
            {
                foreach (Match tag in Tag().Matches(File.ReadAllText(file)))
                {
                    var attributes = Attribute().Matches(tag.Value).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
                    var row = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var set in IconSets.All)
                    {
                        if (!attributes.TryGetValue(set.Parameter, out var name))
                        {
                            throw new InvalidDataException($"{Path.GetFileName(file)}: an IconPlaceholder has no {set.Parameter} icon");
                        }
                        used[set.Key].Add(name);
                        row[set.Key] = name;
                    }
                    rows.TryAdd(string.Join('|', row.Values), row);
                }
            }
            return (used, [.. rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Value)]);
        }

        [GeneratedRegex(@"<IconPlaceholder\b[^>]*?/>", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Tag();

        [GeneratedRegex(@"\b(?<name>[A-Z]\w*)=""(?<value>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex Attribute();
    }

    internal static class Writers
    {
        // Members a static class can't take without hiding object's.
        private static readonly HashSet<string> Reserved = ["Equals", "GetHashCode", "GetType", "ToString", "ReferenceEquals", "MemberwiseClone", "Finalize"];

        /// <summary>
        /// A package's icons, one expression-bodied property each. A property has no backing field and the class
        /// no static constructor, so trimming (Blazor WebAssembly) keeps only the icons an app uses.
        /// </summary>
        public static string Set(IconSet set, IReadOnlyList<Glyph> glyphs)
        {
            var code = new StringBuilder();
            code.AppendLine("// <auto-generated/>")
                .AppendLine($"// {set.Title} {set.Version} ({set.Package} on npm, {set.License}), by src/Blazix.Icons.Generator. Don't edit.")
                .AppendLine("#nullable enable")
                .AppendLine()
                .AppendLine($"namespace {set.Namespace};")
                .AppendLine()
                .AppendLine($"/// <summary>{set.Title} {set.Version}: render one with <c>&lt;Icon Data=\"{set.ClassName}.…\" /&gt;</c>.</summary>")
                .AppendLine($"public static class {set.ClassName}")
                .AppendLine("{");
            foreach (var group in glyphs.GroupBy(g => g.Group))
            {
                var indent = group.Key.Length == 0 ? "    " : "        ";
                if (group.Key.Length > 0)
                {
                    code.AppendLine($"    /// <summary>{set.Title}'s {group.Key.ToLowerInvariant()} icons.</summary>")
                        .AppendLine($"    public static class {group.Key}")
                        .AppendLine("    {");
                }
                var seen = new Dictionary<string, Glyph>(StringComparer.Ordinal);
                foreach (var glyph in group)
                {
                    var member = Member(set, glyph);
                    if (!seen.TryAdd(member, glyph))
                    {
                        // Lucide keeps old names as aliases (arrow-down-01 for arrow-down-0-1): the same icon, so one property.
                        if (seen[member].Body == glyph.Body && seen[member].ViewBox == glyph.ViewBox)
                        {
                            continue;
                        }
                        throw new InvalidDataException($"{set.Title}: '{glyph.Name}' and '{seen[member].Name}' would both be {member}");
                    }
                    code.AppendLine($"{indent}/// <summary><c>{glyph.Name}</c></summary>")
                        .AppendLine($"{indent}public static IconData {member} => new(\"{glyph.ViewBox}\", {Literal(glyph.Body)}, IconStyle.{(glyph.Fill ? "Fill" : "Stroke")});");
                }
                if (group.Key.Length > 0)
                {
                    code.AppendLine("    }");
                }
            }
            return code.AppendLine("}").ToString();
        }

        /// <summary>
        /// An icon's member in its set's class: <c>chevron-down</c> → <c>ChevronDown</c>, with <c>Icon</c> after it where that
        /// name is taken. The packages and the registry's references (RegistryIcons.g.cs) name icons this way.
        /// </summary>
        public static string Member(IconSet set, Glyph glyph)
        {
            var member = IconSets.Pascal(glyph.Name);
            return Reserved.Contains(member) || member == set.ClassName || member == glyph.Group ? member + "Icon" : member;
        }

        /// <summary>An icon as a copied component names it: <c>LucideIcons.ChevronDown</c>, <c>FontAwesomeIcons.Solid.House</c>.</summary>
        public static string Reference(IconSet set, Glyph glyph) =>
            $"{set.ClassName}.{(glyph.Group.Length > 0 ? glyph.Group + "." : "")}{Member(set, glyph)}";

        /// <summary>
        /// The registry's icons in every set: the docs render the Lucide ones, and Blazix.Cli (which compiles this file
        /// in) names the configured set's in the components it copies.
        /// </summary>
        public static string Registry(IReadOnlyList<(IconSet Set, IReadOnlyList<Glyph> Glyphs)> sets, Dictionary<string, SortedSet<string>> used,
            List<SortedDictionary<string, string>> rows)
        {
            var code = new StringBuilder();
            code.AppendLine("// <auto-generated/>")
                .AppendLine("// The icons the components' IconPlaceholders name, in every set. Regenerate after changing a placeholder:")
                .AppendLine("//   dotnet run --project src/Blazix.Icons.Generator -- registry src/Blazix.Registry artifacts/icon-cache src/Blazix.Registry/Icons/RegistryIcons.g.cs")
                .AppendLine("#nullable enable")
                .AppendLine()
                .AppendLine("namespace Blazix.Registry;")
                .AppendLine()
                .AppendLine("internal static class RegistryIcons")
                .AppendLine("{")
                .AppendLine("    public static readonly IReadOnlyDictionary<string, RegistryIconSet> Sets = new Dictionary<string, RegistryIconSet>")
                .AppendLine("    {");
            foreach (var (set, glyphs) in sets)
            {
                var byName = glyphs.ToLookup(g => g.Group.Length == 0 ? g.Name : $"{g.Group.ToLowerInvariant()}/{g.Name}");
                code.AppendLine($"        [\"{set.Key}\"] = new(\"{set.Parameter}\", \"{set.Title}\", \"{set.ClassName}\", \"{set.Package} {set.Version}\", \"{set.License}\", \"{set.Namespace}\", new Dictionary<string, RegistryIcon>")
                    .AppendLine("        {");
                foreach (var name in used[set.Key])
                {
                    // Font Awesome's names are solid icons unless they say otherwise (regular/copy).
                    var key = set.Key == "fontawesome" && !name.Contains('/') ? "solid/" + name : name;
                    var glyph = byName[key].FirstOrDefault() ?? throw new InvalidDataException($"{set.Title} {set.Version} has no icon '{name}'");
                    code.AppendLine($"            [\"{name}\"] = new(\"{glyph.ViewBox}\", {Literal(glyph.Body)}, {(glyph.Fill ? "true" : "false")}, \"{Reference(set, glyph)}\"),");
                }
                code.AppendLine("        }),");
            }
            code.AppendLine("    };")
                .AppendLine()
                .AppendLine("    // Each placeholder's icon in every set, once: what `migrate icons` swaps an icon of one set for.")
                .AppendLine("    public static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Rows =")
                .AppendLine("    [");
            foreach (var row in rows)
            {
                code.AppendLine($"        new Dictionary<string, string> {{ {string.Join(", ", row.Select(kv => $"[\"{kv.Key}\"] = \"{kv.Value}\""))} }},");
            }
            code.AppendLine("    ];")
                .AppendLine("}")
                .AppendLine()
                .AppendLine("// Parameter: the IconPlaceholder parameter that names the set's icon. ClassName and Namespace: the set's class and namespace in its package.")
                .AppendLine("internal sealed record RegistryIconSet(string Parameter, string Title, string ClassName, string Source, string License, string Namespace, IReadOnlyDictionary<string, RegistryIcon> Icons);")
                .AppendLine()
                .AppendLine("// Reference: the icon as a copied component names it, in the set's package (LucideIcons.ChevronDown).")
                .AppendLine("internal sealed record RegistryIcon(string ViewBox, string Body, bool Fill, string Reference);");
            return code.ToString();
        }

        // A raw string literal: SVG markup is full of quotes and never starts or ends with one.
        private static string Literal(string value) =>
            value.Contains("\"\"\"", StringComparison.Ordinal) || value.StartsWith('"') || value.EndsWith('"')
                ? "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
                : "\"\"\"" + value + "\"\"\"";
    }
}
