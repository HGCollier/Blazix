using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Blazix.Icons.Generator;

/// <summary>One icon: its SVG's viewBox and inner markup, and whether it's drawn with fill or stroke.</summary>
/// <param name="Group">The nested class it's in (Font Awesome's Solid, Regular, Brands), or "" for the set's class.</param>
/// <param name="Name">The set's own name for it, as its file is named: <c>chevron-down</c>.</param>
internal sealed record Glyph(string Group, string Name, string ViewBox, string Body, bool Fill);

/// <summary>An icon set, read from its npm tarball.</summary>
/// <param name="Key">The name blazix.json's <c>iconLibrary</c> takes (shadcn's, where shadcn has the set).</param>
/// <param name="Parameter">The IconPlaceholder parameter that names the set's icon.</param>
internal sealed record IconSet(
    string Key, string Parameter, string Title, string ClassName, string Package, string Version, string Integrity,
    string License, Func<string, bool> Include, Func<string, string, Glyph?> Read)
{
    public string Namespace => "Blazix.Icons." + Parameter;

    public string TarballUrl => $"https://registry.npmjs.org/{Package}/-/{Package.Split('/')[^1]}-{Version}.tgz";

    public string FileName => $"{Package.TrimStart('@').Replace('/', '-')}-{Version}.tgz";
}

internal static partial class IconSets
{
    public static readonly IconSet[] All =
    [
        new("lucide", "Lucide", "Lucide", "LucideIcons", "lucide-static", "1.77.0",
            "sha512-v+BzpQrjlwDRWPDzzAu5IFhOkI6jVKb91fsjw1ZSS7Tsb2YvRX5olRN0DXGW99LF2wV4fycuTf5aPOMC62ZW0g==",
            "ISC",
            path => InFolder(path, "package/icons/") && path.EndsWith(".svg", StringComparison.Ordinal),
            (path, svg) => FromSvg("", Stem(path), svg, fill: false)),

        // Outline, as shadcn's tabler icons are, and filled ones as <name>-filled (Tabler's React names: IconCircleCheckFilled).
        new("tabler", "Tabler", "Tabler Icons", "TablerIcons", "@tabler/icons", "3.52.0",
            "sha512-dldKlJNfO/gZnGLcIY0X2EFFKIMGwL/rEvTqglvBgAPFiwj9pZOCN5V3Sf0Gyjq6foX4VQmLdOmsS4w5FdWKAw==",
            "MIT",
            path => (InFolder(path, "package/icons/outline/") || InFolder(path, "package/icons/filled/")) && path.EndsWith(".svg", StringComparison.Ordinal),
            (path, svg) => path.Contains("/filled/", StringComparison.Ordinal)
                ? FromSvg("", Stem(path) + "-filled", svg, fill: true)
                : FromSvg("", Stem(path), svg, fill: false)),

        // Strokes made 2 wide on every element, as shadcn's <HugeiconsIcon strokeWidth={2} /> draws them.
        new("hugeicons", "Hugeicons", "Hugeicons", "HugeiconsIcons", "@hugeicons/core-free-icons", "4.4.0",
            "sha512-7Mz8bmGoY6tZ6l3C2TXwNMLVSr0I8HPwjjFIERAuJFj3rWwLxdF2ZLkDqdrVuEE1dx5b/VXyIsP4WoSU6Q40KQ==",
            "MIT",
            path => HugeiconsFile().IsMatch(path),
            (path, js) => new Glyph("", Kebab(HugeiconsFile().Match(path).Groups["name"].Value), "0 0 24 24", HugeiconsBody(js), Fill: false)),

        // Every weight; regular has no suffix, the others are <name>-bold, -fill, -duotone, -light, -thin, as Phosphor names its files.
        new("phosphor", "Phosphor", "Phosphor Icons", "PhosphorIcons", "@phosphor-icons/core", "2.1.1",
            "sha512-v4ARvrip4qBCImOE5rmPUylOEK4iiED9ZyKjcvzuezqMaiRASCHKcRIuvvxL/twvLpkfnEODCOJp5dM4eZilxQ==",
            "MIT",
            path => InFolder(path, "package/assets/") && path.EndsWith(".svg", StringComparison.Ordinal),
            (path, svg) => FromSvg("", Stem(path), svg, fill: true)),

        // 4.8.0, the last release under Apache-2.0. From 4.9.0 Remix Icon has its own license (Remix Icon License v1.0),
        // which forbids distributing the icons as an icon library (sections 3.1 and 3.2), as a package of them would be.
        new("remixicon", "Remixicon", "Remix Icon", "RemixIcons", "remixicon", "4.8.0",
            "sha512-8qTM/bWkmsAWitvcL9XrVPVdqHRrdmnNp4zCFBdmIHBygxfHWwoK6NzitbiMyRzjcXKBMlS/ab5M65/ZlxJTVA==",
            "Apache-2.0",
            path => InFolder(path, "package/icons/") && path.EndsWith(".svg", StringComparison.Ordinal),
            (path, svg) => FromSvg("", Stem(path), svg, fill: true)),

        // svgs-full: every icon on the same square 640 grid, so it sizes like the other sets (svgs/ varies the width).
        new("fontawesome", "FontAwesome", "Font Awesome Free", "FontAwesomeIcons", "@fortawesome/fontawesome-free", "7.3.1",
            "sha512-wmglKKPDIkgV3aWlZzWECCPoGIkYCulzBwxG9+w7rc5BGapZ6cPMpoPOT8k36J0Ni7PPX6c/rsoMWfS4d1MUMg==",
            "CC-BY-4.0 (icons) AND MIT (code)",
            path => InFolder(path, "package/svgs-full/") && path.EndsWith(".svg", StringComparison.Ordinal),
            (path, svg) => FromSvg(Pascal(path.Split('/')[^2]), Stem(path), svg, fill: true)),
    ];

    public static IconSet Find(string key) =>
        All.FirstOrDefault(s => s.Key == key) ?? throw new InvalidOperationException($"unknown icon set '{key}'. Known: {string.Join(", ", All.Select(s => s.Key))}");

    /// <summary>A C# identifier for an icon name: <c>chevron-down</c> → <c>ChevronDown</c>, <c>2fa</c> → <c>Icon2fa</c>.</summary>
    public static string Pascal(string name)
    {
        var pascal = string.Concat(name.Split('-', '_', '.', ' ').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return char.IsLetter(pascal[0]) ? pascal : "Icon" + pascal;
    }

    private static bool InFolder(string path, string folder) =>
        path.StartsWith(folder, StringComparison.Ordinal);

    private static string Stem(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary><c>ArrowDown01</c> → <c>arrow-down-01</c>, as Hugeicons names its icons on its site.</summary>
    private static string Kebab(string pascal) =>
        KebabBoundary().Replace(pascal, "-").ToLowerInvariant();

    private static Glyph FromSvg(string group, string name, string svg, bool fill)
    {
        var root = XDocument.Parse(svg).Root ?? throw new InvalidDataException($"{name}: no <svg>");
        var viewBox = (string?)root.Attribute("viewBox") ?? throw new InvalidDataException($"{name}: no viewBox");
        var body = new StringBuilder();
        foreach (var element in root.Elements())
        {
            // Tabler's invisible 24×24 bounding box, which only helps editors.
            if ((string?)element.Attribute("stroke") == "none" && (string?)element.Attribute("d") == "M0 0h24v24H0z")
            {
                continue;
            }
            Write(element, body, keep: _ => true);
        }
        return new Glyph(group, name, viewBox, body.ToString(), fill);
    }

    private static void Write(XElement element, StringBuilder into, Func<XAttribute, bool> keep)
    {
        into.Append('<').Append(element.Name.LocalName);
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && keep(a)))
        {
            into.Append(' ').Append(attribute.Name.LocalName).Append("=\"").Append(Escape(attribute.Value)).Append('"');
        }
        if (!element.HasElements)
        {
            into.Append(" />");
            return;
        }
        into.Append('>');
        foreach (var child in element.Elements())
        {
            Write(child, into, keep);
        }
        into.Append("</").Append(element.Name.LocalName).Append('>');
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Hugeicons' JS arrays (<c>[["path", { d: "…", strokeWidth: "1.5", key: "0" }]]</c>) as SVG markup drawn the way
    /// <c>&lt;HugeiconsIcon strokeWidth={2} /&gt;</c> draws them: every element gets <c>stroke="currentColor"</c> and a
    /// 2 wide stroke (inherited from the root here), elements with an opacity come first, and each keeps its own caps
    /// and joins. The root's round caps and joins are other sets' defaults, so the ones an element leaves unset are written out.
    /// </summary>
    private static string HugeiconsBody(string js)
    {
        var elements = HugeiconsElement().Matches(js)
            .Select(m => (Tag: m.Groups["tag"].Value, Attributes: HugeiconsAttribute().Matches(m.Groups["attrs"].Value)
                .Select(a => (Name: Kebab(a.Groups["name"].Value), a.Groups["value"].Value))
                .Where(a => a.Name is not ("key" or "stroke" or "stroke-width"))
                .ToList()))
            .OrderBy(e => e.Attributes.Any(a => a.Name == "opacity") ? 0 : 1)
            .ToList();
        if (elements.Count == 0)
        {
            throw new InvalidDataException("no elements in a Hugeicons icon");
        }

        var body = new StringBuilder();
        foreach (var (tag, attributes) in elements)
        {
            body.Append('<').Append(tag);
            foreach (var (name, value) in attributes.Where(a => !(a.Name is "stroke-linecap" or "stroke-linejoin" && a.Value == "round")))
            {
                body.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');
            }
            if (attributes.All(a => a.Name != "stroke-linecap"))
            {
                body.Append(" stroke-linecap=\"butt\"");
            }
            if (attributes.All(a => a.Name != "stroke-linejoin"))
            {
                body.Append(" stroke-linejoin=\"miter\"");
            }
            body.Append(" />");
        }
        return body.ToString();
    }

    [GeneratedRegex(@"^package/dist/esm/(?<name>[A-Z][A-Za-z0-9]*)Icon\.js$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HugeiconsFile();

    [GeneratedRegex(@"\[""(?<tag>\w+)"",\s*\{(?<attrs>[^}]*)\}\]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HugeiconsElement();

    [GeneratedRegex(@"(?<name>\w+):\s*""(?<value>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HugeiconsAttribute();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Z])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KebabBoundary();
}
