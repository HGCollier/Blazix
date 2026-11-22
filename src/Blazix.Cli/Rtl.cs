using System.Text;
using System.Text.RegularExpressions;

namespace Blazix.Cli;

/// <summary>
/// Makes components right-to-left ready as they're copied, like shadcn's transform-rtl: physical Tailwind classes
/// become logical ones (<c>ml-2</c> → <c>ms-2</c>, <c>left-0</c> → <c>start-0</c>), classes with no logical form get an
/// <c>rtl:</c> twin (<c>translate-x-*</c>, <c>cursor-w-resize</c>), and icons marked <c>cn-rtl-flip</c> turn around.
/// The registry keeps physical classes, so an app that never sets <c>"rtl": true</c> gets them unchanged.
/// </summary>
internal static partial class Rtl
{
    /// <summary>
    /// Marks an icon that points along the inline axis (a chevron, an arrow): <c>rtl:rotate-180</c> in RTL. Left in place
    /// in left-to-right apps, where no CSS matches it, so <c>migrate rtl</c> can still find the icon.
    /// </summary>
    public const string FlipMarker = "cn-rtl-flip";

    // Physical → logical, as transform-rtl has them. Order matters: negative before positive, corners before
    // sides, with a value before without one. A mapping without a trailing '-' must match the whole value.
    private static readonly (string Physical, string Logical)[] Mappings =
    [
        ("-ml-", "-ms-"), ("-mr-", "-me-"), ("ml-", "ms-"), ("mr-", "me-"),
        ("pl-", "ps-"), ("pr-", "pe-"),
        ("-left-", "-start-"), ("-right-", "-end-"), ("left-", "start-"), ("right-", "end-"),
        ("inset-l-", "inset-inline-start-"), ("inset-r-", "inset-inline-end-"),
        ("rounded-tl-", "rounded-ss-"), ("rounded-tr-", "rounded-se-"), ("rounded-bl-", "rounded-es-"), ("rounded-br-", "rounded-ee-"),
        ("rounded-l-", "rounded-s-"), ("rounded-r-", "rounded-e-"),
        ("border-l-", "border-s-"), ("border-r-", "border-e-"), ("border-l", "border-s"), ("border-r", "border-e"),
        ("text-left", "text-start"), ("text-right", "text-end"),
        ("scroll-ml-", "scroll-ms-"), ("scroll-mr-", "scroll-me-"), ("scroll-pl-", "scroll-ps-"), ("scroll-pr-", "scroll-pe-"),
        ("float-left", "float-start"), ("float-right", "float-end"),
        ("clear-left", "clear-start"), ("clear-right", "clear-end"),
        ("origin-top-left", "origin-top-start"), ("origin-top-right", "origin-top-end"),
        ("origin-bottom-left", "origin-bottom-start"), ("origin-bottom-right", "origin-bottom-end"),
        ("origin-left", "origin-start"), ("origin-right", "origin-end"),
    ];

    // No logical form: keep the class and add an rtl: twin with the sign flipped.
    private static readonly (string Physical, string Rtl)[] TranslateX = [("-translate-x-", "translate-x-"), ("translate-x-", "-translate-x-")];

    // shadcn also adds rtl:space-x-reverse and rtl:divide-x-reverse. Tailwind 4 writes space-x and divide-x with
    // margin-inline and border-inline, so they already follow the direction, and the reverse would undo that.

    private static readonly (string Physical, string Swapped)[] Swaps = [("cursor-w-resize", "cursor-e-resize"), ("cursor-e-resize", "cursor-w-resize")];

    // A popup on a logical side slides in from the logical side it came from.
    private static readonly (string Variant, string Physical, string Logical)[] LogicalSideSlides =
    [
        ("data-[side=inline-start]", "slide-in-from-right", "slide-in-from-end"),
        ("data-[side=inline-start]", "slide-out-to-right", "slide-out-to-end"),
        ("data-[side=inline-end]", "slide-in-from-left", "slide-in-from-start"),
        ("data-[side=inline-end]", "slide-out-to-left", "slide-out-to-start"),
    ];

    // Under a physical variant (data-[side=left], a drawer's data-[swipe-direction=right], Navigation Menu's
    // data-activation-direction=left) the element sits or moves on that physical edge, so what follows the edge
    // stays physical: position, border, corners, origin, translate and cursor (a tooltip on the left keeps its
    // arrow on the right; a sheet on the left keeps its border on the right and slides out to the left).
    // Margins, padding and text alignment still map. shadcn skips only the position here and mirrors the
    // rest, which puts a left sheet's border on its outer edge in RTL.
    private static readonly string[] PhysicalOnly =
    [
        "-left-", "-right-", "left-", "right-", "inset-l-", "inset-r-", "rounded-", "border-", "origin-",
    ];

    // The registry's tailwind-merge port: its class group names (border-r-w, left-top) aren't classes.
    private static readonly string[] Untouched = ["ClassNames.cs"];

    // Components whose default Side is physical, opened toward the inline end in RTL.
    private static readonly string[] SideDefaultComponents = ["ContextMenuContent", "ContextMenuSubContent", "DropdownMenuSubContent"];

    /// <summary>Components that need a look by hand after <c>migrate rtl</c>, as in shadcn's guide.</summary>
    public static readonly string[] ManualReview = ["Sidebar.razor", "Pagination.razor", "Calendar.razor"];

    /// <summary>Maps one space-separated class list (shadcn's applyRtlMapping).</summary>
    public static string MapClasses(string input) =>
        string.Join(' ', input.Split(' ').SelectMany(MapClass));

    private static IEnumerable<string> MapClass(string className)
    {
        if (className.StartsWith("rtl:", StringComparison.Ordinal) || className.StartsWith("ltr:", StringComparison.Ordinal))
        {
            return [className];
        }
        if (className == FlipMarker)
        {
            return ["rtl:rotate-180"];
        }

        var (variant, value, modifier) = Split(className);
        if (value.Length == 0)
        {
            return [className];
        }
        var suffix = modifier is null ? "" : "/" + modifier;
        var prefix = variant is null ? "" : variant + ":";
        var physicalSide = variant is not null && PhysicalVariant.IsMatch(variant);

        foreach (var (physical, rtl) in physicalSide ? [] : TranslateX)
        {
            if (value.StartsWith(physical, StringComparison.Ordinal))
            {
                return [className, $"rtl:{prefix}{ReplaceFirst(value, physical, rtl)}{suffix}"];
            }
        }
        foreach (var (physical, swapped) in physicalSide ? [] : Swaps)
        {
            if (value == physical)
            {
                return [className, $"rtl:{prefix}{swapped}"];
            }
        }
        foreach (var (side, physical, logical) in LogicalSideSlides)
        {
            if (variant?.Contains(side, StringComparison.Ordinal) == true && value.StartsWith(physical, StringComparison.Ordinal))
            {
                return [$"{prefix}{ReplaceFirst(value, physical, logical)}{suffix}"];
            }
        }

        var mapped = value;
        foreach (var (physical, logical) in Mappings)
        {
            if (physicalSide && PhysicalOnly.Any(p => physical.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }
            if (!value.StartsWith(physical, StringComparison.Ordinal) || (!physical.EndsWith('-') && value != physical))
            {
                continue;
            }
            mapped = ReplaceFirst(value, physical, logical);
            break;
        }
        return [$"{prefix}{mapped}{suffix}"];
    }

    /// <summary>
    /// Variant, value and alpha modifier, as shadcn's splitClassName: the variant ends at the last colon outside
    /// brackets, and the modifier follows the value's last slash (<c>-translate-x-1/2</c> is value <c>-translate-x-1</c>).
    /// </summary>
    private static (string? Variant, string Value, string? Modifier) Split(string className)
    {
        if (!className.Contains('/') && !className.Contains(':'))
        {
            return (null, className, null);
        }
        var colon = -1;
        var depth = 0;
        for (var i = className.Length - 1; i >= 0; i--)
        {
            var c = className[i];
            if (c == ']')
            {
                depth++;
            }
            else if (c == '[')
            {
                depth--;
            }
            else if (c == ':' && depth == 0)
            {
                colon = i;
                break;
            }
        }
        var variant = colon < 0 ? null : className[..colon];
        var name = colon < 0 ? className : className[(colon + 1)..];
        var slash = name.LastIndexOf('/');
        return slash < 0 ? (variant, name, null) : (variant, name[..slash], name[(slash + 1)..]);
    }

    private static string ReplaceFirst(string value, string physical, string logical)
    {
        var at = value.IndexOf(physical, StringComparison.Ordinal);
        return at < 0 ? value : string.Concat(value.AsSpan(0, at), logical, value.AsSpan(at + physical.Length));
    }

    /// <summary>
    /// Makes a component file RTL ready: maps every class outside comments, and opens the menus that default to
    /// <c>Side.Right</c> toward <c>Side.InlineEnd</c>. Classes are found as tokens between whitespace and quotes, so
    /// Razor attributes that hold C# (<c>class="@Cn("pr-1.5!", Class)"</c>) need no parsing. Not idempotent, as
    /// shadcn's isn't: a second run adds the <c>rtl:</c> twins again.
    /// </summary>
    public static string Transform(string fileName, string source)
    {
        if (Untouched.Contains(Path.GetFileName(fileName)))
        {
            return source;
        }
        var result = OutsideComments(source, text => Token.Replace(text, m => string.Join(' ', MapClass(m.Value))));
        if (SideDefaultComponents.Contains(Path.GetFileNameWithoutExtension(fileName)))
        {
            result = SideDefault.Replace(result, m => m.Groups["side"].Value == "Right" ? "= Side.InlineEnd;" : "= Side.InlineStart;");
        }
        return result;
    }

    private static string OutsideComments(string source, Func<string, string> map)
    {
        var result = new StringBuilder(source.Length + 256);
        var at = 0;
        foreach (Match comment in Comment.Matches(source))
        {
            result.Append(map(source[at..comment.Index])).Append(comment.Value);
            at = comment.Index + comment.Length;
        }
        return result.Append(map(source[at..])).ToString();
    }

    // Comments in C# and Razor. A // comment starts a line or follows whitespace, so http:// isn't one.
    [GeneratedRegex(@"(?<=^|\s)//[^\n]*|/\*.*?\*/|@\*.*?\*@|<!--.*?-->", RegexOptions.Singleline | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Comment { get; }

    [GeneratedRegex(@"[^\s""]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Token { get; }

    [GeneratedRegex(@"(?:side|swipe-direction|activation-direction)=(?:left|right)\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhysicalVariant { get; }

    [GeneratedRegex(@"= Side\.(?<side>Right|Left);", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SideDefault { get; }
}
