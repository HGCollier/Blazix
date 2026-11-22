using System.Collections.Concurrent;
using System.Text;

namespace Blazix.Registry;

/// <summary>
/// Like clsx + tailwind-merge: joins class lists, dropping nulls
/// and blanks, and resolves Tailwind conflicts so the later class wins: <c>Cn("px-2 py-1", "px-4")</c>
/// is <c>"py-1 px-4"</c>. Classes conflict when they set the same CSS under the same variants
/// (<c>hover:bg-muted</c> and <c>bg-accent</c> don't), and a shorthand overrides its parts
/// (<c>p-4</c> removes an earlier <c>px-2</c>; a later <c>px-2</c> refines <c>p-4</c> and both stay).
/// Classes it doesn't know are kept as they are.
/// </summary>
public static class ClassNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Cn(params string?[] classLists)
    {
        var joined = string.Join(' ', classLists.Where(list => !string.IsNullOrWhiteSpace(list)));
        return joined.Length == 0 ? "" : Cache.GetOrAdd(joined, TailwindMerge.Merge);
    }
}

/// <summary>A port of tailwind-merge's rules for the utilities Tailwind CSS v4 generates.</summary>
internal static class TailwindMerge
{
    private const string Color = "color";

    private static readonly string[] Empty = [];

    public static string Merge(string classes)
    {
        var tokens = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>(tokens.Length);
        // Later classes win, so walk backwards and keep a class only if nothing after it took its slot.
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            var token = tokens[i];
            var (variants, utility, important) = Parse(token);
            var group = Groups.Of(utility);
            var scope = variants + (important ? "!" : "") + "|";
            if (!taken.Add(scope + (group ?? "=" + utility)))
            {
                continue;
            }
            if (group is not null)
            {
                foreach (var overridden in Groups.Overrides(group))
                {
                    taken.Add(scope + overridden);
                }
            }
            kept.Add(token);
        }
        kept.Reverse();
        return string.Join(' ', kept);
    }

    /// <summary>Splits <c>md:hover:!px-2</c> into its variants (sorted), the utility and importance.</summary>
    private static (string Variants, string Utility, bool Important) Parse(string token)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < token.Length; i++)
        {
            switch (token[i])
            {
                case '[' or '(':
                    depth++;
                    break;
                case ']' or ')':
                    depth--;
                    break;
                case ':' when depth == 0:
                    parts.Add(token[start..i]);
                    start = i + 1;
                    break;
            }
        }
        var utility = token[start..];
        var important = false;
        if (utility.StartsWith('!'))
        {
            important = true;
            utility = utility[1..];
        }
        else if (utility.EndsWith('!'))
        {
            important = true;
            utility = utility[..^1];
        }
        // Order doesn't matter for ordinary variants (hover:focus: is focus:hover:), but does for
        // arbitrary ones ([&>svg]:) and for * (children): keep those where they are.
        var ordered = new StringBuilder();
        var run = new List<string>();
        foreach (var part in parts)
        {
            if (part.StartsWith('[') || part.StartsWith('*'))
            {
                run.Sort(StringComparer.Ordinal);
                ordered.Append(string.Join(':', run)).Append(':').Append(part).Append(':');
                run.Clear();
            }
            else
            {
                run.Add(part);
            }
        }
        run.Sort(StringComparer.Ordinal);
        ordered.Append(string.Join(':', run));
        return (ordered.ToString(), utility, important);
    }

    private static class Groups
    {
        // Utilities that are a whole keyword.
        private static readonly Dictionary<string, string> Exact = Build(
            ("display", ["block", "inline-block", "inline", "flex", "inline-flex", "table", "inline-table", "table-caption", "table-cell",
                "table-column", "table-column-group", "table-footer-group", "table-header-group", "table-row-group", "table-row", "flow-root",
                "grid", "inline-grid", "contents", "list-item", "hidden"]),
            ("position", ["static", "fixed", "absolute", "relative", "sticky"]),
            ("visibility", ["visible", "invisible", "collapse"]),
            ("isolation", ["isolate", "isolation-auto"]),
            ("sr", ["sr-only", "not-sr-only"]),
            ("text-transform", ["uppercase", "lowercase", "capitalize", "normal-case"]),
            ("font-style", ["italic", "not-italic"]),
            ("text-decoration-line", ["underline", "overline", "line-through", "no-underline"]),
            ("font-smoothing", ["antialiased", "subpixel-antialiased"]),
            ("text-overflow", ["truncate", "text-ellipsis", "text-clip"]),
            ("border-w", ["border"]),
            ("border-x-w", ["border-x"]), ("border-y-w", ["border-y"]), ("border-s-w", ["border-s"]), ("border-e-w", ["border-e"]),
            ("border-t-w", ["border-t"]), ("border-r-w", ["border-r"]), ("border-b-w", ["border-b"]), ("border-l-w", ["border-l"]),
            ("divide-x", ["divide-x"]), ("divide-y", ["divide-y"]),
            ("rounded", ["rounded"]),
            ("rounded-s", ["rounded-s"]), ("rounded-e", ["rounded-e"]), ("rounded-t", ["rounded-t"]), ("rounded-r", ["rounded-r"]),
            ("rounded-b", ["rounded-b"]), ("rounded-l", ["rounded-l"]), ("rounded-tl", ["rounded-tl"]), ("rounded-tr", ["rounded-tr"]),
            ("rounded-br", ["rounded-br"]), ("rounded-bl", ["rounded-bl"]),
            ("inset-ring-w", ["inset-ring"]), ("inset-shadow", ["inset-shadow"]), ("drop-shadow", ["drop-shadow"]),
            ("blur", ["blur"]), ("grayscale", ["grayscale"]), ("invert", ["invert"]), ("sepia", ["sepia"]),
            ("ring-w", ["ring"]),
            ("ring-inset", ["ring-inset"]),
            ("shadow", ["shadow"]),
            ("outline-w", ["outline"]),
            ("transition", ["transition"]),
            ("grow", ["grow"]),
            ("shrink", ["shrink"]),
            ("container", ["container"]),
            ("box-sizing", ["box-border", "box-content"]),
            ("table-layout", ["table-auto", "table-fixed"]),
            ("border-collapse", ["border-collapse", "border-separate"]),
            ("filter", ["filter", "filter-none"]),
            ("backdrop-filter", ["backdrop-filter", "backdrop-filter-none"]));

        // Utilities with a value: prefix -> what the value says the group is. Longest prefixes match first.
        private static readonly (string Prefix, Func<string, string> Classify)[] Prefixed = BuildPrefixed(
            Fixed("w"), Fixed("h"), Fixed("size"), Fixed("min-w"), Fixed("min-h"), Fixed("max-w"), Fixed("max-h"),
            Fixed("p"), Fixed("px"), Fixed("py"), Fixed("ps"), Fixed("pe"), Fixed("pt"), Fixed("pr"), Fixed("pb"), Fixed("pl"),
            Fixed("m"), Fixed("mx"), Fixed("my"), Fixed("ms"), Fixed("me"), Fixed("mt"), Fixed("mr"), Fixed("mb"), Fixed("ml"),
            Fixed("gap"), Fixed("gap-x"), Fixed("gap-y"), Fixed("space-x"), Fixed("space-y"),
            Fixed("inset"), Fixed("inset-x"), Fixed("inset-y"), Fixed("inset-s"), Fixed("inset-e"), Fixed("start"), Fixed("end"), Fixed("top"), Fixed("right"),
            Fixed("bottom"), Fixed("left"), Fixed("z"), Fixed("order"), Fixed("basis"), Fixed("grow"), Fixed("shrink"),
            ("flex", v => v switch
            {
                "row" or "row-reverse" or "col" or "col-reverse" => "flex-direction",
                "wrap" or "wrap-reverse" or "nowrap" => "flex-wrap",
                _ => "flex",
            }),
            Fixed("items"), Fixed("justify"), Fixed("justify-items"), Fixed("justify-self"), Fixed("self"),
            ("content", v => v.StartsWith('[') && v.Contains('\'') ? "content" : "align-content"),
            Fixed("place-content"), Fixed("place-items"), Fixed("place-self"),
            Fixed("grid-cols"), Fixed("grid-rows"), Fixed("grid-flow"), Fixed("auto-cols"), Fixed("auto-rows"),
            ("col", v => v.StartsWith("span") ? "col-span" : "col"), Fixed("col-start"), Fixed("col-end"),
            ("row", v => v.StartsWith("span") ? "row-span" : "row"), Fixed("row-start"), Fixed("row-end"),
            ("text", Text),
            ("font", v => IsFontWeight(v) ? "font-weight" : "font-family"),
            Fixed("leading"), Fixed("tracking"), Fixed("line-clamp"), Fixed("indent"), Fixed("align"),
            Fixed("whitespace"), Fixed("break"), Fixed("hyphens"), Fixed("wrap"),
            ("list", v => v is "inside" or "outside" ? "list-position" : "list-style"),
            ("decoration", Decoration),
            Fixed("underline-offset"),
            ("bg", Background),
            Side("border", "border"), Side("border-x", "border"), Side("border-y", "border"), Side("border-s", "border"),
            Side("border-e", "border"), Side("border-t", "border"), Side("border-r", "border"), Side("border-b", "border"),
            Side("border-l", "border"),
            ("divide-x", v => IsLength(v) || v == "reverse" ? "divide-x" : "divide-color"),
            ("divide-y", v => IsLength(v) || v == "reverse" ? "divide-y" : "divide-color"),
            ("divide", v => IsDecorationStyle(v) ? "divide-style" : "divide-color"),
            Fixed("rounded"), Fixed("rounded-s"), Fixed("rounded-e"), Fixed("rounded-t"), Fixed("rounded-r"), Fixed("rounded-b"),
            Fixed("rounded-l"), Fixed("rounded-ss"), Fixed("rounded-se"), Fixed("rounded-ee"), Fixed("rounded-es"),
            Fixed("rounded-tl"), Fixed("rounded-tr"), Fixed("rounded-br"), Fixed("rounded-bl"),
            ("ring", v => IsLength(v) ? "ring-w" : "ring-color"),
            ("ring-offset", v => IsLength(v) ? "ring-offset-w" : "ring-offset-color"),
            ("inset-ring", v => IsLength(v) ? "inset-ring-w" : "inset-ring-color"),
            ("shadow", v => IsShadowSize(v) ? "shadow" : "shadow-color"),
            ("inset-shadow", v => IsShadowSize(v) ? "inset-shadow" : "inset-shadow-color"),
            ("outline", Outline),
            Fixed("outline-offset"),
            Fixed("opacity"), Fixed("overflow"), Fixed("overflow-x"), Fixed("overflow-y"),
            Fixed("overscroll"), Fixed("overscroll-x"), Fixed("overscroll-y"),
            ("object", v => v is "contain" or "cover" or "fill" or "none" or "scale-down" ? "object-fit" : "object-position"),
            Fixed("aspect"), Fixed("columns"), Fixed("float"), Fixed("clear"),
            Fixed("transition"), Fixed("duration"), Fixed("ease"), Fixed("delay"), Fixed("animate"),
            Fixed("translate"), Fixed("translate-x"), Fixed("translate-y"), Fixed("translate-z"),
            Fixed("scale"), Fixed("scale-x"), Fixed("scale-y"), Fixed("rotate"), Fixed("skew-x"), Fixed("skew-y"), Fixed("origin"),
            Fixed("transform"), Fixed("perspective"),
            ("fill", _ => "fill"),
            ("stroke", v => IsNumber(v) || IsArbitraryLength(v) ? "stroke-w" : "stroke"),
            Fixed("cursor"), Fixed("pointer-events"), Fixed("select"), Fixed("resize"), Fixed("appearance"), Fixed("touch"),
            Fixed("will-change"), Fixed("field-sizing"), Fixed("mix-blend"), Fixed("bg-blend"), Fixed("caption"),
            Fixed("scroll"), Fixed("scroll-m"), Fixed("scroll-mx"), Fixed("scroll-my"), Fixed("scroll-mt"), Fixed("scroll-mr"),
            Fixed("scroll-mb"), Fixed("scroll-ml"), Fixed("scroll-p"), Fixed("scroll-px"), Fixed("scroll-py"), Fixed("scroll-pt"),
            Fixed("scroll-pr"), Fixed("scroll-pb"), Fixed("scroll-pl"),
            // snap-x snap-mandatory sets the type and the strictness, which don't conflict.
            ("snap", v => v switch
            {
                "start" or "end" or "center" or "align-none" => "snap-align",
                "normal" or "always" => "snap-stop",
                "mandatory" or "proximity" => "snap-strictness",
                _ => "snap-type",
            }),
            Fixed("accent"), Fixed("caret"),
            Fixed("blur"), Fixed("brightness"), Fixed("contrast"), Fixed("grayscale"), Fixed("invert"), Fixed("saturate"), Fixed("sepia"),
            Fixed("drop-shadow"), Fixed("backdrop-blur"), Fixed("backdrop-brightness"), Fixed("backdrop-saturate"),
            Fixed("backdrop-opacity"), Fixed("from"), Fixed("via"), Fixed("to"), Fixed("content-visibility"));

        // A class in the key group overrides earlier classes in these groups.
        private static readonly Dictionary<string, string[]> Overriding = new(StringComparer.Ordinal)
        {
            ["size"] = ["w", "h"],
            ["p"] = ["px", "py", "ps", "pe", "pt", "pr", "pb", "pl"],
            ["px"] = ["pr", "pl"],
            ["py"] = ["pt", "pb"],
            ["m"] = ["mx", "my", "ms", "me", "mt", "mr", "mb", "ml"],
            ["mx"] = ["mr", "ml"],
            ["my"] = ["mt", "mb"],
            ["gap"] = ["gap-x", "gap-y"],
            ["inset"] = ["inset-x", "inset-y", "inset-s", "inset-e", "start", "end", "top", "right", "bottom", "left"],
            ["inset-x"] = ["inset-s", "inset-e", "start", "end", "right", "left"],
            ["inset-y"] = ["top", "bottom"],
            ["flex"] = ["basis", "grow", "shrink"],
            ["col-span"] = ["col-start", "col-end"],
            ["row-span"] = ["row-start", "row-end"],
            ["font-size"] = ["leading"],
            // line-clamp sets display and overflow itself, as tailwind-merge has it: `flex line-clamp-1` must clamp.
            ["line-clamp"] = ["display", "overflow"],
            ["rounded"] = ["rounded-s", "rounded-e", "rounded-t", "rounded-r", "rounded-b", "rounded-l", "rounded-ss", "rounded-se",
                "rounded-ee", "rounded-es", "rounded-tl", "rounded-tr", "rounded-br", "rounded-bl"],
            ["rounded-s"] = ["rounded-ss", "rounded-es"],
            ["rounded-e"] = ["rounded-se", "rounded-ee"],
            ["rounded-t"] = ["rounded-tl", "rounded-tr"],
            ["rounded-r"] = ["rounded-tr", "rounded-br"],
            ["rounded-b"] = ["rounded-br", "rounded-bl"],
            ["rounded-l"] = ["rounded-tl", "rounded-bl"],
            ["border-w"] = ["border-x-w", "border-y-w", "border-s-w", "border-e-w", "border-t-w", "border-r-w", "border-b-w", "border-l-w"],
            ["border-x-w"] = ["border-r-w", "border-l-w"],
            ["border-y-w"] = ["border-t-w", "border-b-w"],
            ["border-color"] = ["border-x-color", "border-y-color", "border-s-color", "border-e-color", "border-t-color", "border-r-color",
                "border-b-color", "border-l-color"],
            ["border-x-color"] = ["border-r-color", "border-l-color"],
            ["border-y-color"] = ["border-t-color", "border-b-color"],
            ["overflow"] = ["overflow-x", "overflow-y"],
            ["overscroll"] = ["overscroll-x", "overscroll-y"],
            ["translate"] = ["translate-x", "translate-y", "translate-z"],
            ["scale"] = ["scale-x", "scale-y"],
            ["scroll-m"] = ["scroll-mx", "scroll-my", "scroll-mt", "scroll-mr", "scroll-mb", "scroll-ml"],
            ["scroll-p"] = ["scroll-px", "scroll-py", "scroll-pt", "scroll-pr", "scroll-pb", "scroll-pl"],
        };

        private static readonly HashSet<string> FontSizes = new(StringComparer.Ordinal)
            { "xs", "sm", "base", "lg", "xl", "2xl", "3xl", "4xl", "5xl", "6xl", "7xl", "8xl", "9xl", "2xs" };

        public static string? Of(string utility)
        {
            if (utility.StartsWith('['))
            {
                // An arbitrary property ([--card-spacing:--spacing(4)]): its property is the group.
                var colon = utility.IndexOf(':');
                return colon > 1 ? "arbitrary:" + utility[1..colon] : null;
            }
            var name = utility.StartsWith('-') ? utility[1..] : utility;
            if (Exact.TryGetValue(name, out var group))
            {
                return group;
            }
            foreach (var (prefix, classify) in Prefixed)
            {
                if (name.Length > prefix.Length + 1 && name[prefix.Length] == '-' && name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return classify(name[(prefix.Length + 1)..]);
                }
            }
            return null;
        }

        public static string[] Overrides(string group) => Overriding.GetValueOrDefault(group, Empty);

        private static Dictionary<string, string> Build(params (string Group, string[] Names)[] groups)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (group, names) in groups)
            {
                foreach (var name in names)
                {
                    map[name] = group;
                }
            }
            return map;
        }

        private static (string, Func<string, string>)[] BuildPrefixed(params (string Prefix, Func<string, string> Classify)[] entries) =>
            [.. entries.OrderByDescending(entry => entry.Prefix.Length)];

        private static (string, Func<string, string>) Fixed(string prefix) => (prefix, _ => prefix);

        // border-*, border-t-*...: a width, a style (border only) or a color.
        private static (string, Func<string, string>) Side(string prefix, string root) => (prefix, v =>
        {
            if (IsLength(v))
            {
                return prefix + "-w";
            }
            if ((prefix == root && IsDecorationStyle(v)) || v is "none" or "hidden")
            {
                return "border-style";
            }
            return prefix + "-" + Color;
        });

        private static string Text(string v)
        {
            var size = v.Contains('/') ? v[..v.IndexOf('/')] : v;
            if (FontSizes.Contains(size) || IsArbitraryLength(v))
            {
                return "font-size";
            }
            return v switch
            {
                "left" or "center" or "right" or "justify" or "start" or "end" => "text-align",
                "wrap" or "nowrap" or "balance" or "pretty" => "text-wrap",
                "ellipsis" or "clip" => "text-overflow",
                _ => "text-" + Color,
            };
        }

        private static string Decoration(string v)
        {
            if (IsLength(v) || v is "auto" or "from-font")
            {
                return "decoration-w";
            }
            return IsDecorationStyle(v) ? "decoration-style" : "decoration-" + Color;
        }

        private static string Outline(string v)
        {
            if (v is "none" or "hidden" || IsDecorationStyle(v))
            {
                return "outline-style";
            }
            return IsLength(v) ? "outline-w" : "outline-" + Color;
        }

        private static string Background(string v) => v switch
        {
            "fixed" or "local" or "scroll" => "bg-attachment",
            "auto" or "cover" or "contain" => "bg-size",
            "center" or "top" or "bottom" or "left" or "right" or "left-top" or "left-bottom" or "right-top" or "right-bottom" => "bg-position",
            "repeat" or "no-repeat" or "repeat-x" or "repeat-y" or "repeat-round" or "repeat-space" => "bg-repeat",
            "none" => "bg-image",
            _ when v.StartsWith("clip") => "bg-clip",
            _ when v.StartsWith("origin") => "bg-origin",
            _ when v.StartsWith("linear") || v.StartsWith("radial") || v.StartsWith("conic") || v.StartsWith("gradient") => "bg-image",
            _ when v.StartsWith("[url") || v.StartsWith("(image") => "bg-image",
            _ => "bg-" + Color,
        };

        private static bool IsFontWeight(string v) =>
            v is "thin" or "extralight" or "light" or "normal" or "medium" or "semibold" or "bold" or "extrabold" or "black"
            || IsNumber(v) || v.StartsWith("[number:") || (v.StartsWith('[') && IsNumber(v[1..^1]));

        private static bool IsShadowSize(string v) =>
            v is "2xs" or "xs" or "sm" or "md" or "lg" or "xl" or "2xl" or "none" or "inner" || v.StartsWith("[0") || v.StartsWith("[inset");

        private static bool IsDecorationStyle(string v) => v is "solid" or "dashed" or "dotted" or "double" or "wavy";

        private static bool IsNumber(string v) => v.Length > 0 && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

        private static bool IsLength(string v) => IsNumber(v) || v == "px" || IsArbitraryLength(v);

        // [3px], [0.5rem], [calc(...)], (length:--x)
        private static bool IsArbitraryLength(string v)
        {
            if (v.StartsWith("(length:") || v.StartsWith("[length:"))
            {
                return true;
            }
            if (!v.StartsWith('[') || !v.EndsWith(']'))
            {
                return false;
            }
            var inner = v[1..^1];
            if (inner.StartsWith("calc(") || inner.StartsWith("min(") || inner.StartsWith("max(") || inner.StartsWith("clamp("))
            {
                return true;
            }
            var digits = inner.TrimStart('-', '.');
            return digits.Length > 0 && char.IsDigit(digits[0]) && (inner.EndsWith("px") || inner.EndsWith("rem") || inner.EndsWith("em")
                || inner.EndsWith('%') || inner.EndsWith("vh") || inner.EndsWith("vw") || inner.EndsWith("ch") || inner.EndsWith("dvh")
                || inner.EndsWith("svh") || inner.EndsWith("lh") || IsNumber(inner));
        }
    }
}
