namespace Blazix.Icons;

/// <summary>How an icon is drawn, which decides the attributes on its <c>&lt;svg&gt;</c>.</summary>
public enum IconStyle
{
    /// <summary>Outlines (Lucide, Tabler outline, Hugeicons): <c>fill="none" stroke="currentColor"</c>, 2 wide, round caps and joins.</summary>
    Stroke,

    /// <summary>Filled shapes (Phosphor, Remix Icon, Font Awesome, Tabler filled): <c>fill="currentColor"</c>.</summary>
    Fill,
}

/// <summary>
/// One icon: its SVG's viewBox and inner markup. The icon set packages have one per icon, as a property
/// (<c>LucideIcons.ChevronDown</c>); render it with <c>&lt;Icon Data="LucideIcons.ChevronDown" /&gt;</c>.
/// </summary>
/// <param name="viewBox">The SVG's viewBox, e.g. <c>0 0 24 24</c>.</param>
/// <param name="body">The SVG's inner markup. It's rendered as markup, so it must come from a trusted source.</param>
/// <param name="style">Whether it's drawn with strokes or fills.</param>
public sealed class IconData(string viewBox, string body, IconStyle style)
{
    /// <summary>The SVG's viewBox, e.g. <c>0 0 24 24</c>.</summary>
    public string ViewBox { get; } = viewBox;

    /// <summary>The SVG's inner markup.</summary>
    public string Body { get; } = body;

    /// <summary>Whether it's drawn with strokes or fills.</summary>
    public IconStyle Style { get; } = style;
}
