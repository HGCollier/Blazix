using Microsoft.AspNetCore.Components;

namespace Blazix.Docs.Components.Docs;

/// <summary>A page in the docs navigation.</summary>
public sealed record DocsLink(string Title, string Href, bool IsNew = false);

/// <summary>
/// A sidebar group, such as "Components". <paramref name="SearchTitle"/> is its heading in search, which lists both sets'
/// groups and so has to say which set a group is in.
/// </summary>
public sealed record DocsGroup(string Title, IReadOnlyList<DocsLink> Pages, string? SearchTitle = null)
{
    public string SearchHeading => SearchTitle ?? Title;
}

/// <summary>
/// One of the site's two doc sets, the styled components or the primitives:
/// its own sidebar groups, and the order previous/next walks its pages in.
/// </summary>
public sealed record DocsSet(string Title, string Href, IReadOnlyList<DocsGroup> Groups, IReadOnlyList<DocsLink> ReadingOrder);

/// <summary>The site's navigation: what the header, sidebar, search and previous/next links list.</summary>
internal static class DocsNav
{
    /// <summary>Where the primitives' pages are. Every other docs page is in the components' set.</summary>
    private const string PrimitivesPath = "/docs/primitives";

    /// <summary>The header's links. Components and Primitives lead to the two sets.</summary>
    public static readonly IReadOnlyList<DocsLink> MainNav =
    [
        new("Home", "/"),
        new("Components", "/docs/components"),
        new("Primitives", PrimitivesPath),
    ];

    /// <summary>The styled components' getting-started pages.</summary>
    private static readonly IReadOnlyList<DocsLink> GuidePages =
    [
        new("Introduction", "/docs"),
        new("Installation", "/docs/installation"),
        new("Components", "/docs/components"),
        new("blazix.json", "/docs/blazix-json"),
        new("Theming", "/docs/theming"),
        new("Icons", "/docs/icons"),
        new("Typeset", "/docs/typeset"),
        new("Dark Mode", "/docs/dark-mode"),
        new("RTL", "/docs/rtl"),
        new("CLI", "/docs/cli"),
    ];

    public static readonly IReadOnlyList<DocsLink> Components =
    [
        new("Accordion", "/docs/components/accordion"),
        new("Alert", "/docs/components/alert"),
        new("Alert Dialog", "/docs/components/alert-dialog"),
        new("Aspect Ratio", "/docs/components/aspect-ratio"),
        new("Attachment", "/docs/components/attachment"),
        new("Avatar", "/docs/components/avatar"),
        new("Badge", "/docs/components/badge"),
        new("Breadcrumb", "/docs/components/breadcrumb"),
        new("Bubble", "/docs/components/bubble"),
        new("Button", "/docs/components/button"),
        new("Button Group", "/docs/components/button-group"),
        new("Calendar", "/docs/components/calendar"),
        new("Card", "/docs/components/card"),
        new("Carousel", "/docs/components/carousel"),
        new("Checkbox", "/docs/components/checkbox"),
        new("Code Block", "/docs/components/code-block", IsNew: true),
        new("Collapsible", "/docs/components/collapsible"),
        new("Combobox", "/docs/components/combobox"),
        new("Command", "/docs/components/command"),
        new("Context Menu", "/docs/components/context-menu"),
        new("Data Table", "/docs/components/data-table"),
        new("Date Picker", "/docs/components/date-picker"),
        new("Dialog", "/docs/components/dialog"),
        new("Direction", "/docs/components/direction"),
        new("Drawer", "/docs/components/drawer"),
        new("Dropdown Menu", "/docs/components/dropdown-menu"),
        new("Empty", "/docs/components/empty"),
        new("Field", "/docs/components/field"),
        new("Hover Card", "/docs/components/hover-card"),
        new("Input", "/docs/components/input"),
        new("Input Group", "/docs/components/input-group"),
        new("Input OTP", "/docs/components/input-otp"),
        new("Item", "/docs/components/item"),
        new("Kbd", "/docs/components/kbd"),
        new("Label", "/docs/components/label"),
        new("Marker", "/docs/components/marker"),
        new("Menubar", "/docs/components/menubar"),
        new("Message", "/docs/components/message"),
        new("Message Scroller", "/docs/components/message-scroller"),
        new("Native Select", "/docs/components/native-select"),
        new("Navigation Menu", "/docs/components/navigation-menu"),
        new("Pagination", "/docs/components/pagination"),
        new("Popover", "/docs/components/popover"),
        new("Progress", "/docs/components/progress"),
        new("Questionnaire", "/docs/components/questionnaire"),
        new("Radio Group", "/docs/components/radio-group"),
        new("Resizable", "/docs/components/resizable"),
        new("Scroll Area", "/docs/components/scroll-area"),
        new("Select", "/docs/components/select"),
        new("Separator", "/docs/components/separator"),
        new("Sheet", "/docs/components/sheet"),
        new("Sidebar", "/docs/components/sidebar"),
        new("Skeleton", "/docs/components/skeleton"),
        new("Slider", "/docs/components/slider"),
        new("Spinner", "/docs/components/spinner"),
        new("Switch", "/docs/components/switch"),
        new("Table", "/docs/components/table"),
        new("Tabs", "/docs/components/tabs"),
        new("Textarea", "/docs/components/textarea"),
        new("Toast", "/docs/components/toast"),
        new("Toggle", "/docs/components/toggle"),
        new("Toggle Group", "/docs/components/toggle-group"),
        new("Tooltip", "/docs/components/tooltip"),
        new("Trace", "/docs/components/trace", IsNew: true),
        new("Typography", "/docs/components/typography"),
    ];

    /// <summary>The form guides, one per validation library.</summary>
    private static readonly IReadOnlyList<DocsLink> Forms =
    [
        new("Data Annotations", "/docs/forms/data-annotations"),
        new("FluentValidation", "/docs/forms/fluent-validation"),
    ];

    /// <summary>The utility classes from Setup/Vendor/variants.css.</summary>
    private static readonly IReadOnlyList<DocsLink> Utilities =
    [
        new("scroll-fade", "/docs/utils/scroll-fade"),
        new("shimmer", "/docs/utils/shimmer"),
    ];

    /// <summary>The primitives' getting-started pages.</summary>
    private static readonly IReadOnlyList<DocsLink> PrimitiveOverview =
    [
        new("Introduction", PrimitivesPath),
        new("Installation", PrimitivesPath + "/installation"),
    ];

    /// <summary>The primitives' guides.</summary>
    private static readonly IReadOnlyList<DocsLink> PrimitiveGuides =
    [
        new("Styling", "/docs/primitives/styling"),
        new("Animation", "/docs/primitives/animation"),
        new("Composition", "/docs/primitives/composition"),
        new("Customization", "/docs/primitives/customization"),
        new("Accessibility", "/docs/primitives/accessibility"),
    ];

    /// <summary>A page per primitive.</summary>
    public static readonly IReadOnlyList<DocsLink> Primitives =
    [
        new("Accordion", "/docs/primitives/accordion"),
        new("Alert Dialog", "/docs/primitives/alert-dialog"),
        new("Autocomplete", "/docs/primitives/autocomplete"),
        new("Avatar", "/docs/primitives/avatar"),
        new("Button", "/docs/primitives/button"),
        new("Checkbox", "/docs/primitives/checkbox"),
        new("Checkbox Group", "/docs/primitives/checkbox-group"),
        new("Collapsible", "/docs/primitives/collapsible"),
        new("Combobox", "/docs/primitives/combobox"),
        new("Context Menu", "/docs/primitives/context-menu"),
        new("Dialog", "/docs/primitives/dialog"),
        new("Drawer", "/docs/primitives/drawer"),
        new("Field", "/docs/primitives/field"),
        new("Fieldset", "/docs/primitives/fieldset"),
        new("Form", "/docs/primitives/form"),
        new("Input", "/docs/primitives/input"),
        new("Menu", "/docs/primitives/menu"),
        new("Menubar", "/docs/primitives/menubar"),
        new("Message Scroller", "/docs/primitives/message-scroller"),
        new("Meter", "/docs/primitives/meter"),
        new("Navigation Menu", "/docs/primitives/navigation-menu"),
        new("Number Field", "/docs/primitives/number-field"),
        new("OTP Field", "/docs/primitives/otp-field"),
        new("Popover", "/docs/primitives/popover"),
        new("Preview Card", "/docs/primitives/preview-card"),
        new("Progress", "/docs/primitives/progress"),
        new("Questionnaire", "/docs/primitives/questionnaire"),
        new("Radio Group", "/docs/primitives/radio-group"),
        new("Scroll Area", "/docs/primitives/scroll-area"),
        new("Select", "/docs/primitives/select"),
        new("Separator", "/docs/primitives/separator"),
        new("Slider", "/docs/primitives/slider"),
        new("Switch", "/docs/primitives/switch"),
        new("Tabs", "/docs/primitives/tabs"),
        new("Toast", "/docs/primitives/toast"),
        new("Toggle", "/docs/primitives/toggle"),
        new("Toggle Group", "/docs/primitives/toggle-group"),
        new("Toolbar", "/docs/primitives/toolbar"),
        new("Tooltip", "/docs/primitives/tooltip"),
    ];

    /// <summary>The primitives' utilities, and the helpers the styled components are built with.</summary>
    private static readonly IReadOnlyList<DocsLink> PrimitiveUtilities =
    [
        new("Browser", "/docs/primitives/browser"),
        new("CSP Provider", "/docs/primitives/csp-provider"),
        new("Direction Provider", "/docs/primitives/direction-provider"),
        new("Keyboard Shortcut", "/docs/primitives/keyboard-shortcut"),
        new("Media Query", "/docs/primitives/media-query"),
        new("Merge Props", "/docs/primitives/merge-props"),
        new("Use Render", "/docs/primitives/use-render"),
    ];

    /// <summary>The styled components: what you copy into your app with the CLI.</summary>
    public static readonly DocsSet ComponentsSet = new(
        "Components", "/docs",
        [
            new("Overview", GuidePages),
            new("Components", Components),
            new("Forms", Forms),
            new("Utilities", Utilities),
        ],
        [.. GuidePages, .. Components, new("Forms", "/docs/forms"), .. Forms, .. Utilities]);

    /// <summary>The primitives: the headless <c>Blazix.Primitives</c> package.</summary>
    public static readonly DocsSet PrimitivesSet = new(
        "Primitives", PrimitivesPath,
        [
            new("Overview", PrimitiveOverview, "Primitives Overview"),
            new("Handbook", PrimitiveGuides, "Primitives Handbook"),
            new("Components", Primitives, "Primitives"),
            new("Utilities", PrimitiveUtilities, "Primitive Utilities"),
        ],
        [.. PrimitiveOverview, .. PrimitiveGuides, .. Primitives, .. PrimitiveUtilities]);

    /// <summary>Both sets.</summary>
    private static readonly IReadOnlyList<DocsSet> Sets = [ComponentsSet, PrimitivesSet];

    /// <summary>Every group of both sets, for search.</summary>
    public static readonly IReadOnlyList<DocsGroup> AllGroups = [.. Sets.SelectMany(s => s.Groups)];

    /// <summary>
    /// Each styled component and the primitive it's built on. A primitive can have several (Dialog: Dialog and Sheet). Only
    /// real dependencies: Field and Input OTP don't use the Field and OTP Field primitives, so they aren't here.
    /// </summary>
    private static readonly (string Component, string Primitive)[] BuiltOn =
    [
        .. new[]
        {
            "accordion", "alert-dialog", "avatar", "button", "checkbox", "collapsible", "combobox", "context-menu", "dialog",
            "drawer", "input", "menubar", "message-scroller", "navigation-menu", "popover", "progress", "questionnaire",
            "radio-group", "scroll-area", "select", "separator", "slider", "switch", "tabs", "toast", "toggle", "toggle-group",
            "tooltip",
        }.Select(slug => ($"/docs/components/{slug}", $"{PrimitivesPath}/{slug}")),
        ("/docs/components/direction", PrimitivesPath + "/direction-provider"),
        ("/docs/components/dropdown-menu", PrimitivesPath + "/menu"),
        ("/docs/components/hover-card", PrimitivesPath + "/preview-card"),
        ("/docs/components/sheet", PrimitivesPath + "/dialog"),
    ];

    private static readonly Dictionary<string, DocsLink> PagesByHref =
        Sets.SelectMany(s => s.ReadingOrder).DistinctBy(p => p.Href, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p.Href, StringComparer.OrdinalIgnoreCase);

    /// <summary>The set a page is in, or null off the docs (the home page).</summary>
    public static DocsSet? SetFor(string path)
    {
        if (IsUnder(path, PrimitivesPath))
        {
            return PrimitivesSet;
        }
        return IsUnder(path, "/docs") ? ComponentsSet : null;
    }

    /// <summary>The primitive a styled component is built on, or the styled components built on a primitive.</summary>
    public static IReadOnlyList<DocsLink> RelatedTo(string path) =>
    [
        .. BuiltOn
            .SelectMany(p => new[] { Same(p.Component, path) ? p.Primitive : null, Same(p.Primitive, path) ? p.Component : null })
            .OfType<string>()
            .Select(href => PagesByHref.GetValueOrDefault(href))
            .OfType<DocsLink>()
            .OrderBy(p => p.Title, StringComparer.Ordinal),
    ];

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root) =>
        Same(path, root) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The page's path, without query, fragment or trailing slash: "/docs/components/button".</summary>
    public static string CurrentPath(NavigationManager navigation)
    {
        var relative = navigation.ToBaseRelativePath(navigation.Uri);
        var end = relative.IndexOfAny(['?', '#']);
        return '/' + (end < 0 ? relative : relative[..end]).TrimEnd('/');
    }

    /// <summary>The pages before and after this one in its set. Previous/next never crosses into the other set.</summary>
    public static (DocsLink? Previous, DocsLink? Next) Neighbours(string path)
    {
        var order = SetFor(path)?.ReadingOrder ?? [];
        for (var i = 0; i < order.Count; i++)
        {
            if (Same(order[i].Href, path))
            {
                return (i > 0 ? order[i - 1] : null, i < order.Count - 1 ? order[i + 1] : null);
            }
        }
        return (null, null);
    }
}
