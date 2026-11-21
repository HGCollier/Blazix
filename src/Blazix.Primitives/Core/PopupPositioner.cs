using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>Placement and positioner attributes a Positioner part hands to the popup inside it.</summary>
public sealed class PositionerSettings
{
    internal PositionerSettings(PopupRootBase root) => Root = root;

    internal PopupRootBase Root { get; }

    public Side? Side { get; internal set; }

    public Align? Align { get; internal set; }

    public int? SideOffset { get; internal set; }

    public int? AlignOffset { get; internal set; }

    public int? CollisionPadding { get; internal set; }

    public int? ArrowPadding { get; internal set; }

    public bool? Sticky { get; internal set; }

    public string? Anchor { get; internal set; }

    public IReadOnlyDictionary<string, object>? Attributes { get; internal set; }
}

/// <summary>
/// Positions the popup against its anchor. Put the popup inside
/// it: its placement parameters and attributes (a z-index class, a data-testid...) apply to the
/// popup's positioner, the element in the top layer that's moved around. The popup's own placement
/// parameters, when set, win. It renders no element of its own: the popup renders the positioner.
/// </summary>
public abstract class PopupPositionerPart<TRoot> : ComponentBase where TRoot : PopupRootBase
{
    private PositionerSettings? _settings;

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Which side of the anchor to open on. It flips when there isn't room.</summary>
    [Parameter] public Side? Side { get; set; }

    /// <summary>How to line up with the anchor along that side.</summary>
    [Parameter] public Align? Align { get; set; }

    /// <summary>Gap between the anchor and the popup, in pixels.</summary>
    [Parameter] public int? SideOffset { get; set; }

    /// <summary>Shift along the anchor, in pixels.</summary>
    [Parameter] public int? AlignOffset { get; set; }

    /// <summary>Space to keep from the viewport edges, in pixels.</summary>
    [Parameter] public int? CollisionPadding { get; set; }

    /// <summary>Space between the arrow and the popup's corners, in pixels.</summary>
    [Parameter] public int? ArrowPadding { get; set; }

    /// <summary>Keep the popup in view when its anchor scrolls out of it.</summary>
    [Parameter] public bool? Sticky { get; set; }

    /// <summary>The id of an element to position against instead of the trigger.</summary>
    [Parameter] public string? Anchor { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnParametersSet()
    {
        _settings ??= new PositionerSettings(Root);
        _settings.Side = Side;
        _settings.Align = Align;
        _settings.SideOffset = SideOffset;
        _settings.AlignOffset = AlignOffset;
        _settings.CollisionPadding = CollisionPadding;
        _settings.ArrowPadding = ArrowPadding;
        _settings.Sticky = Sticky;
        _settings.Anchor = Anchor;
        _settings.Attributes = AdditionalAttributes;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<PositionerSettings>>(0);
        builder.AddComponentParameter(1, "Value", _settings);
        builder.AddComponentParameter(2, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}

/// <summary>What a Portal part hands to the popup and backdrop inside it.</summary>
public sealed record PortalSettings(PopupRootBase Root, bool KeepMounted);

/// <summary>
/// The base of the <c>Portal</c> parts: popups render in the browser's top
/// layer instead of being moved into a container, so this renders its content where it is.
/// <see cref="KeepMounted"/> keeps the popup in the DOM (hidden) while closed.
/// </summary>
public abstract class PopupPortalPart<TRoot> : ComponentBase where TRoot : PopupRootBase
{
    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Keep the popup in the DOM while closed. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<PortalSettings>>(0);
        builder.AddComponentParameter(1, "Value", new PortalSettings(Root, KeepMounted));
        builder.AddComponentParameter(2, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}

/// <summary>
/// A viewport for the popup's content, for popups that several
/// triggers share: the content renders in a <c>data-current</c> wrapper, the viewport is marked with
/// <c>data-activation-direction</c> (where the new trigger is relative to the last one) and
/// <c>data-transitioning</c> while it changes, and <c>--popup-width</c>/<c>--popup-height</c> on the
/// popup and the viewport follow the content, so CSS can animate between triggers.
/// </summary>
public abstract class PopupViewportPart<TRoot> : PrimitivePart<PopupRenderProps> where TRoot : PopupRootBase
{
    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-viewport", "")
        .Set("data-activation-direction", Root.TriggerActivationDirection);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "data-current", "");
        builder.AddAttribute(5, "data-activation-direction", Root.TriggerActivationDirection);
        builder.AddContent(6, ChildContent);
        builder.CloseElement();
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}
