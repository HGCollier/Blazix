using System.Text;
using System.Text.RegularExpressions;
using Blazix.Registry;

namespace Blazix.Cli;

/// <summary>
/// The components' icons in the set blazix.json's <c>iconLibrary</c> names, like shadcn's transform-icons, but as
/// inline SVG rather than an icon package's markup, so copied components still need no icon package. The registry
/// writes each icon as an <c>&lt;IconPlaceholder&gt;</c> naming it in every set; <see cref="Transform"/> replaces
/// it with that set's <c>&lt;svg&gt;</c> and keeps its other attributes (<c>class</c>, <c>aria-hidden</c>,
/// <c>data-slot</c>, <c>@attributes</c>), so an icon marked <c>cn-rtl-flip</c> is still found by <see cref="Rtl"/>,
/// which runs after. The data is RegistryIcons.g.cs, which this project compiles in from Blazix.Registry/Icons.
/// </summary>
internal static partial class Icons
{
    /// <summary>The set a project without <c>iconLibrary</c> gets: the one the components were drawn with.</summary>
    public const string Default = "lucide";

    private const string Placeholder = "<IconPlaceholder";

    // The <svg> attributes each kind of icon gets, which a placeholder's own attributes override.
    private static readonly (string Name, string Value)[] StrokeDefaults =
    [
        ("xmlns", "http://www.w3.org/2000/svg"), ("width", "24"), ("height", "24"), ("viewBox", ""), ("fill", "none"),
        ("stroke", "currentColor"), ("stroke-width", "2"), ("stroke-linecap", "round"), ("stroke-linejoin", "round"),
    ];

    private static readonly (string Name, string Value)[] FillDefaults =
    [
        ("xmlns", "http://www.w3.org/2000/svg"), ("width", "24"), ("height", "24"), ("viewBox", ""), ("fill", "currentColor"),
    ];

    public static IEnumerable<string> Libraries => RegistryIcons.Sets.Keys;

    /// <summary>The set's key, checked: <c>lucide</c>, <c>tabler</c>, <c>hugeicons</c>, <c>phosphor</c>, <c>remixicon</c>, <c>fontawesome</c>.</summary>
    public static string Library(string? name)
    {
        var key = (name ?? Default).Trim().ToLowerInvariant();
        return RegistryIcons.Sets.ContainsKey(key)
            ? key
            : throw new CliException($"unknown icon library '{name}'. Available: {string.Join(", ", Libraries)}");
    }

    /// <summary>Replaces each IconPlaceholder in a .razor file with the library's inline SVG.</summary>
    public static string Transform(string fileName, string source, string library)
    {
        if (!fileName.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || !source.Contains(Placeholder, StringComparison.Ordinal))
        {
            return source;
        }
        var set = RegistryIcons.Sets[library];
        var result = new StringBuilder(source.Length + 1024);
        var at = 0;
        foreach (var tag in Tags(source, Placeholder))
        {
            var name = tag.Value(set.Parameter)
                ?? throw new CliException($"{fileName}: an IconPlaceholder names no {set.Title} icon");
            var icon = set.Icons.TryGetValue(name, out var found) ? found
                : throw new CliException($"{fileName}: no {set.Title} icon '{name}' in this CLI's icon data");
            var kept = tag.Attributes.Where(a => !IsLibraryParameter(a.Name)).ToList();
            result.Append(source, at, tag.Start - at).Append(Svg(set, icon, kept, Indent(source, tag.Start)));
            at = tag.End;
        }
        return result.Append(source, at, source.Length - at).ToString();
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

    /// <summary>
    /// Swaps the registry's icons in an already copied file from one set to another: an <c>&lt;svg&gt;</c> whose
    /// viewBox and markup are a registry icon of <paramref name="from"/> becomes that icon in <paramref name="to"/>,
    /// with its other attributes kept. Any other SVG (yours, or one you've edited) is left alone and counted.
    /// </summary>
    public static string Migrate(string source, string from, string to, out int swapped, out int skipped)
    {
        swapped = 0;
        skipped = 0;
        var fromSet = RegistryIcons.Sets[from];
        var toSet = RegistryIcons.Sets[to];
        var known = fromSet.Icons.GroupBy(kv => (kv.Value.ViewBox, Normalize(kv.Value.Body))).ToDictionary(g => g.Key, g => g.First().Key);
        var defaults = (fromSet.Icons.Values.FirstOrDefault()?.Fill == true ? FillDefaults : StrokeDefaults).ToList();

        var result = new StringBuilder(source.Length);
        var at = 0;
        foreach (var tag in Tags(source, "<svg"))
        {
            var close = source.IndexOf("</svg>", tag.End, StringComparison.Ordinal);
            if (tag.SelfClosing || close < 0)
            {
                continue;
            }
            var body = Normalize(source[tag.End..close]);
            if (!known.TryGetValue((tag.Value("viewBox") ?? "", body), out var name))
            {
                skipped++;
                continue;
            }
            var row = RegistryIcons.Rows.FirstOrDefault(r => r[from] == name);
            if (row is null || !toSet.Icons.TryGetValue(row[to], out var icon))
            {
                skipped++;
                continue;
            }
            // Keep what the copy adds to the set's own defaults: a class, aria-hidden, a size of its own, @attributes.
            var kept = tag.Attributes.Where(a => !defaults.Any(d => d.Name == a.Name && (d.Value.Length == 0 || d.Value == a.Value))).ToList();
            result.Append(source, at, tag.Start - at).Append(Svg(toSet, icon, kept, Indent(source, tag.Start)));
            at = close + "</svg>".Length;
            swapped++;
        }
        return result.Append(source, at, source.Length - at).ToString();
    }

    private static bool IsLibraryParameter(string name) =>
        RegistryIcons.Sets.Values.Any(s => s.Parameter == name);

    /// <summary>
    /// The icon as Razor markup: the set's defaults the attributes don't override, then the attributes as written
    /// (their line breaks re-indented under the <c>&lt;svg</c>), then the markup, one element a line.
    /// </summary>
    private static string Svg(RegistryIconSet set, RegistryIcon icon, List<TagAttribute> kept, string indent)
    {
        var svg = new StringBuilder("<svg");
        foreach (var (name, value) in icon.Fill ? FillDefaults : StrokeDefaults)
        {
            if (kept.All(a => !string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                svg.Append(' ').Append(name).Append("=\"").Append(name == "viewBox" ? icon.ViewBox : value).Append('"');
            }
        }
        foreach (var attribute in kept)
        {
            var leading = attribute.Leading.Contains('\n') ? "\n" + indent + "     " : " ";
            svg.Append(leading).Append(attribute.Text);
        }
        svg.Append('>');
        var inner = indent + "    ";
        if (set.Notice is not null)
        {
            // The set's license asks for it (Font Awesome's CC BY 4.0). Razor drops comments, so it costs nothing at run time.
            svg.Append('\n').Append(inner).Append("<!--! ").Append(Razor(set.Notice)).Append(" -->");
        }
        foreach (var element in Elements(icon.Body))
        {
            svg.Append('\n').Append(inner).Append(Razor(element));
        }
        return svg.Append('\n').Append(indent).Append("</svg>").ToString();
    }

    // '@' starts C# in Razor: Font Awesome's notice has "@fontawesome".
    private static string Razor(string markup) => markup.Replace("@", "@@", StringComparison.Ordinal);

    // The top-level elements of an icon's markup, which the generator writes with no whitespace between them.
    private static IEnumerable<string> Elements(string body)
    {
        var depth = 0;
        var start = 0;
        var i = 0;
        while ((i = body.IndexOf('<', i)) >= 0)
        {
            var end = body.IndexOf('>', i);
            var closing = body[i + 1] == '/';
            var selfClosing = body[end - 1] == '/';
            if (closing)
            {
                depth--;
            }
            else if (!selfClosing)
            {
                depth++;
            }
            if (depth == 0)
            {
                yield return body[start..(end + 1)];
                start = end + 1;
            }
            i = end + 1;
        }
    }

    /// <summary>Markup compared without comments and with whitespace between elements and before <c>/&gt;</c> ignored.</summary>
    private static string Normalize(string markup) =>
        SelfClosingSpace().Replace(BetweenElements().Replace(HtmlComment().Replace(markup, ""), "><"), "/>").Trim();

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

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@">\s+<", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BetweenElements();

    [GeneratedRegex(@"\s*/>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SelfClosingSpace();
}
