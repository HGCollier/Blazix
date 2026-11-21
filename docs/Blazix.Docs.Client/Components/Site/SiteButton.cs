using static Blazix.Registry.ClassNames;

namespace Blazix.Docs.Components.Site;

/// <summary>
/// The button classes of the site's own chrome: the header's links,
/// the theme toggle, the mobile menu's button and the buttons on code blocks (View Code, Expand and Copy). Unlike
/// the registry's buttons only outline ones have a border, there's no open (aria-expanded) look, ghost buttons
/// hover with the accent color, and sm is h-8 with text-sm (the registry's is h-7 with 0.8rem).
/// </summary>
internal static class SiteButton
{
    private const string Base =
        "inline-flex shrink-0 items-center justify-center gap-2 rounded-md text-sm font-medium whitespace-nowrap transition-all " +
        "outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:pointer-events-none " +
        "disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4";

    public const string Ghost = "hover:bg-accent hover:text-accent-foreground dark:hover:bg-accent/50";

    public const string Outline =
        "border bg-background shadow-xs hover:bg-accent hover:text-accent-foreground dark:border-input dark:bg-input/15 " +
        "dark:hover:bg-input/20";

    public const string SizeDefault = "h-9 px-4 py-2 has-[>svg]:px-3";

    public const string SizeSm = "h-8 gap-1.5 rounded-md px-3 has-[>svg]:px-2.5";

    public const string SizeIcon = "size-9";

    public static string Get(string variant, string size, string? className = null) => Cn(Base, variant, size, className);
}
