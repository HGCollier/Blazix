using System.Text;
using System.Text.RegularExpressions;
using Blazix.Registry;

namespace Blazix.Cli;

/// <summary>
/// The components' icons, as shadcn's transform-icons writes them. The registry names each icon in every set with an
/// <c>&lt;IconPlaceholder&gt;</c>; <see cref="Transform"/> replaces it with an <c>&lt;Icon&gt;</c> that draws the icon of the
/// set blazix.json's <c>iconLibrary</c> names from that set's package (the commands add the package), with the using
/// directives it needs. The data is RegistryIcons.g.cs, which this project compiles in from Blazix.Registry/Icons.
/// <see cref="Migrate"/> moves the <c>&lt;Icon&gt;</c>s of copied components to another set's, as shadcn's migrate icons does.
/// </summary>
internal static partial class Icons
{
    /// <summary>The set a project without <c>iconLibrary</c> gets: the one the components were drawn with.</summary>
    public const string Default = "lucide";

    private const string Placeholder = "<IconPlaceholder";
    private const string Component = "<Icon";

    public static IEnumerable<string> Libraries => RegistryIcons.Sets.Keys;

    /// <summary>The set's key, checked: <c>lucide</c>, <c>tabler</c>, <c>hugeicons</c>, <c>phosphor</c>, <c>remixicon</c>, <c>fontawesome</c>.</summary>
    public static string Library(string? name)
    {
        var key = (name ?? Default).Trim().ToLowerInvariant();
        return RegistryIcons.Sets.ContainsKey(key)
            ? key
            : throw new CliException($"unknown icon library '{name}'. Available: {string.Join(", ", Libraries)}");
    }

    /// <summary>Whether the markup draws an icon of the set from its package: an <c>&lt;Icon Data="LucideIcons.…"&gt;</c>, say.</summary>
    public static bool UsesLibrary(string source, string library) =>
        source.Contains("Data=\"" + RegistryIcons.Sets[library].ClassName + ".", StringComparison.Ordinal);

    /// <summary>The package a set's icons come from, which the commands add: <c>Blazix.Icons.Lucide</c> for lucide.</summary>
    public static string Package(string library) => "Blazix.Icons." + RegistryIcons.Sets[library].Parameter;

    /// <summary>
    /// Replaces each IconPlaceholder in a .razor file with an <c>&lt;Icon&gt;</c> of the library's icon, keeping its other
    /// attributes, and adds the using directives it needs. <paramref name="usesIcons"/> says whether there was one.
    /// </summary>
    public static string Transform(string fileName, string source, string library, out bool usesIcons)
    {
        usesIcons = false;
        if (!fileName.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || !source.Contains(Placeholder, StringComparison.Ordinal))
        {
            return source;
        }
        var set = RegistryIcons.Sets[library];
        var newLine = NewLine(source);
        var result = new StringBuilder(source.Length + 256);
        var at = 0;
        foreach (var tag in Tags(source, Placeholder))
        {
            var name = tag.Value(set.Parameter)
                ?? throw new CliException($"{fileName}: an IconPlaceholder names no {set.Title} icon");
            var icon = set.Icons.TryGetValue(name, out var found) ? found
                : throw new CliException($"{fileName}: no {set.Title} icon '{name}' in this CLI's icon data");
            var kept = tag.Attributes.Where(a => !IsLibraryParameter(a.Name)).ToList();
            result.Append(source, at, tag.Start - at).Append(IconElement(icon.Reference, kept, Indent(source, tag.Start), newLine));
            at = tag.End;
            usesIcons = true;
        }
        if (!usesIcons)
        {
            return source;
        }
        return AddUsings(result.Append(source, at, source.Length - at).ToString(), "Blazix.Icons", set.Namespace);
    }

    /// <summary>
    /// Problems with a file's placeholders, which the docs build fails on: each names an icon in every set that this
    /// CLI has data for, and is hidden from assistive tech, named, or given its attributes by <c>@attributes</c>.
    /// </summary>
    public static IEnumerable<string> Check(string fileName, string source)
    {
        foreach (var tag in Tags(source, Placeholder))
        {
            var line = source.AsSpan(0, tag.Start).Count('\n') + 1;
            foreach (var set in RegistryIcons.Sets.Values)
            {
                var name = tag.Value(set.Parameter);
                if (name is null)
                {
                    yield return $"{fileName}({line}): IconPlaceholder has no {set.Parameter}=\"...\"";
                }
                else if (!set.Icons.ContainsKey(name))
                {
                    yield return $"{fileName}({line}): no {set.Title} icon '{name}' in RegistryIcons.g.cs: regenerate it (the command is at its top)";
                }
            }
            var hidden = tag.Value("aria-hidden") == "true";
            var named = tag.Value("aria-label") is not null && tag.Value("role") is not null || tag.Value("aria-labelledby") is not null;
            if (!hidden && !named && tag.Value("@attributes") is null)
            {
                yield return $"{fileName}({line}): IconPlaceholder is neither aria-hidden=\"true\" nor named (role and aria-label, or aria-labelledby)";
            }
        }
    }

    /// <summary>An icon that <see cref="Migrate"/> left as it is, and why.</summary>
    public sealed record Skipped(string Icon, string Reason);

    /// <summary>
    /// Moves the <c>&lt;Icon Data="…"&gt;</c>s of the set <paramref name="from"/> in a copied file to <paramref name="to"/>'s: each
    /// becomes the icon of the same name in <paramref name="to"/>. An icon with no counterpart in <paramref name="to"/> is added
    /// to <paramref name="skipped"/> and left as it is. Icons of other sets, and your own, are not touched. Inline SVG is not
    /// migrated: a copy that draws inline is replaced by adding its component again with <c>--overwrite</c>.
    /// </summary>
    public static string Migrate(string source, string from, string to, ICollection<Skipped> skipped, out int swapped)
    {
        swapped = 0;
        if (from == to)
        {
            return source;
        }
        var fromSet = RegistryIcons.Sets[from];
        var toSet = RegistryIcons.Sets[to];
        var nameByReference = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, icon) in fromSet.Icons)
        {
            nameByReference.TryAdd(icon.Reference, name);
        }
        // The library's references begin with its class, so a reference to an icon the CLI has no data for is still named.
        var prefix = fromSet.ClassName + ".";

        var migrated = new StringBuilder(source.Length + 256);
        var at = 0;
        foreach (var tag in Tags(source, Component))
        {
            var data = tag.Value("Data");
            if (data is null || !data.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (!nameByReference.TryGetValue(data, out var name) || Counterpart(from, name, to) is not { } icon)
            {
                skipped.Add(new Skipped(data, $"no {toSet.Title} equivalent found"));
                continue;
            }
            var attributes = tag.Attributes
                .Select(a => a.Name == "Data" ? a with { Value = icon.Reference, Text = "Data=\"" + icon.Reference + "\"" } : a);
            var element = Component + string.Concat(attributes.Select(a => a.Leading + a.Text)) + (tag.SelfClosing ? " />" : ">");
            migrated.Append(source, at, tag.Start - at).Append(element);
            at = tag.End;
            swapped++;
        }
        if (swapped == 0)
        {
            return source;
        }
        migrated.Append(source, at, source.Length - at);
        return WithUsings(migrated.ToString(), from, to);
    }

    // Adds the usings the migrated icons need, then drops the set's using when the file no longer names any of its icons.
    private static string WithUsings(string source, string from, string to)
    {
        var result = AddUsings(source, "Blazix.Icons", RegistryIcons.Sets[to].Namespace);
        var set = RegistryIcons.Sets[from];
        return !result.Contains(set.ClassName + ".", StringComparison.Ordinal)
            ? RemoveUsing(result, set.Namespace)
            : result;
    }

    // The icon of the other set that the registry's row pairs with the given one, or null if none does.
    private static RegistryIcon? Counterpart(string from, string name, string to)
    {
        var row = RegistryIcons.Rows.FirstOrDefault(r => r.TryGetValue(from, out var n) && n == name);
        return row is not null && row.TryGetValue(to, out var other) && RegistryIcons.Sets[to].Icons.TryGetValue(other, out var icon)
            ? icon
            : null;
    }

    private static bool IsLibraryParameter(string name) =>
        RegistryIcons.Sets.Values.Any(s => s.Parameter == name);

    /// <summary>
    /// The <c>&lt;Icon&gt;</c> that draws an icon of a set's package, with the attributes the placeholder or copy had. Its
    /// attributes start after the tag name, and continuation lines line up under them.
    /// </summary>
    private static string IconElement(string reference, List<TagAttribute> attributes, string indent, string newLine)
    {
        var element = new StringBuilder(Component).Append(" Data=\"").Append(reference).Append('"');
        foreach (var attribute in attributes)
        {
            var leading = attribute.Leading.Contains('\n') ? newLine + indent + new string(' ', Component.Length + 1) : " ";
            element.Append(leading).Append(attribute.Text);
        }
        return element.Append(" />").ToString();
    }

    private static string NewLine(string source) => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Adds the using directives the file doesn't have yet, right after its <c>@namespace</c> line.</summary>
    private static string AddUsings(string source, params string[] namespaces)
    {
        var missing = namespaces.Where(ns => !HasUsing(source, ns)).Select(ns => "@using " + ns).ToList();
        if (missing.Count == 0)
        {
            return source;
        }
        var newLine = NewLine(source);
        var block = string.Join(newLine, missing);
        var directive = NamespaceDirective().Match(source);
        if (!directive.Success)
        {
            return block + newLine + source;
        }
        var end = directive.Index + directive.Length;
        return directive.Value.EndsWith('\n')
            ? source.Insert(end, block + newLine)
            : source.Insert(end, newLine + block);
    }

    private static bool HasUsing(string source, string ns) =>
        Regex.IsMatch(source, "^@using " + Regex.Escape(ns) + @"[ \t]*\r?$", RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    private static string RemoveUsing(string source, string ns) =>
        Regex.Replace(source, "^@using " + Regex.Escape(ns) + @"[ \t]*\r?\n", "", RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    private static string Indent(string source, int at)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        var indent = source.AsSpan(lineStart, at - lineStart);
        return indent.IsWhiteSpace() ? indent.ToString() : new string(' ', indent.Length);
    }

    /// <summary>
    /// The start tags named <paramref name="open"/> in Razor markup, with their attributes as written. An attribute's
    /// value may hold C#, quotes and all (<c>class="@Cn("size-4", Class)"</c>): inside parentheses, quotes are C#'s.
    /// </summary>
    private static IEnumerable<Tag> Tags(string source, string open)
    {
        var from = 0;
        while ((from = source.IndexOf(open, from, StringComparison.Ordinal)) >= 0)
        {
            var i = from + open.Length;
            if (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '-'))
            {
                from = i;
                continue;
            }
            var attributes = new List<TagAttribute>();
            var selfClosing = false;
            while (i < source.Length)
            {
                var spaceStart = i;
                while (i < source.Length && char.IsWhiteSpace(source[i]))
                {
                    i++;
                }
                if (i >= source.Length)
                {
                    break;
                }
                if (source[i] == '>')
                {
                    i++;
                    break;
                }
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '>')
                {
                    selfClosing = true;
                    i += 2;
                    break;
                }
                var nameStart = i;
                while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ('=' or '>') && !(source[i] == '/' && i + 1 < source.Length && source[i + 1] == '>'))
                {
                    i++;
                }
                var name = source[nameStart..i];
                string? value = null;
                if (i < source.Length && source[i] == '=' && i + 1 < source.Length && source[i + 1] == '"')
                {
                    var valueStart = i + 2;
                    i = ValueEnd(source, valueStart);
                    value = source[valueStart..i];
                    i++;
                }
                attributes.Add(new TagAttribute(source[spaceStart..nameStart], name, value, source[nameStart..i]));
            }
            yield return new Tag(from, i, attributes, selfClosing);
            from = i;
        }
    }

    // The closing quote of an attribute value: quotes inside parentheses (C# arguments) and inside C# strings don't count.
    private static int ValueEnd(string source, int i)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (; i < source.Length; i++)
        {
            var c = source[i];
            if (escaped)
            {
                escaped = false;
            }
            else if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
            }
            else if (c == '"')
            {
                if (depth == 0)
                {
                    return i;
                }
                inString = true;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
        }
        throw new CliException("an attribute's value has no closing quote");
    }

    private sealed record TagAttribute(string Leading, string Name, string? Value, string Text);

    private sealed record Tag(int Start, int End, List<TagAttribute> Attributes, bool SelfClosing)
    {
        public string? Value(string name) => Attributes.FirstOrDefault(attribute => attribute.Name == name) is { } found ? found.Value ?? "" : null;
    }

    [GeneratedRegex(@"^@namespace[^\r\n]*(\r?\n)?", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamespaceDirective();
}
