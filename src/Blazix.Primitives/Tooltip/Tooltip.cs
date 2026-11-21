using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// Shares delays between the tooltips inside it: once one has
/// shown, the next opens at once if the pointer reaches it within <see cref="Timeout"/>.
/// </summary>
public sealed class TooltipProvider : ComponentBase
{
    internal string GroupId { get; } = Ids.Next("tooltip-group");

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The open delay for the tooltips inside, in milliseconds. A tooltip's own Delay wins.</summary>
    [Parameter] public int? Delay { get; set; }

    /// <summary>The close delay for the tooltips inside, in milliseconds.</summary>
    [Parameter] public int? CloseDelay { get; set; }

    /// <summary>How long after one tooltip closes the next still opens at once, in milliseconds. Defaults to 400.</summary>
    [Parameter] public int Timeout { get; set; } = 400;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<TooltipProvider>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}

/// <summary>
/// A popup that appears when an element is hovered or focused, showing a hint for sighted users
///. It never takes focus; the trigger is described by it while open.
/// </summary>
public class TooltipRoot() : PopupRootBase("tooltip")
{
    [CascadingParameter] private TooltipProvider? Provider { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether the pointer may move into the popup without closing it. Defaults to true.</summary>
    [Parameter] public bool HoverablePopup { get; set; } = true;

    internal int? Delay { get; set; }

    internal int? CloseDelay { get; set; }

    internal override bool IsDisabled => Disabled;

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["openOnHover"] = !Disabled,
        ["openOnFocus"] = !Disabled,
        ["closeOnTriggerPress"] = true,
        ["delay"] = Delay ?? Provider?.Delay ?? 600,
        ["closeDelay"] = CloseDelay ?? Provider?.CloseDelay ?? 0,
        ["hoverable"] = HoverablePopup,
        ["group"] = Provider?.GroupId,
        ["groupTimeout"] = Provider?.Timeout ?? 400,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["tab"] = "native",
        ["initialFocus"] = "none",
        ["closeOnFocusOut"] = false,
        ["closeOnBlur"] = true,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
    };

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (Disabled && IsOpen)
        {
            State.Apply(false, OpenChangeReason.Disabled);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<TooltipRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();
    }
}

/// <summary>The element the tooltip describes. Renders a &lt;button&gt;.</summary>
public class TooltipTrigger : PopupTriggerPart<TooltipRoot>
{
    /// <summary>How long the pointer rests before opening, in milliseconds. Defaults to 600 (or the provider's).</summary>
    [Parameter] public int? Delay { get; set; }

    /// <summary>How long after the pointer leaves before closing, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int? CloseDelay { get; set; }

    protected override string HasPopup => "false";

    protected override bool TogglesOnPress => false;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (HasRoot)
        {
            Root.Delay = Delay;
            Root.CloseDelay = CloseDelay;
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Disabled);
        attributes
            .DescribedBy(Root.IsOpen ? Root.PopupId : null)
            .Flag("data-popup-open", Root.IsOpen)
            .Flag("data-trigger-disabled", Root.IsDisabled);
    }
}

/// <summary>The tooltip itself. Opens on top by default.</summary>
public class TooltipPopup : AnchoredPopupPart<TooltipRoot, PopupRenderProps>
{
    protected override Side DefaultSide => Primitives.Side.Top;

    protected override bool DefaultFallbackAxis => true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", Root.PopupId).Set("role", "tooltip");
        Root.AddOpenAttributes(attributes);
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>An arrow pointing at the trigger.</summary>
public class TooltipArrow : PopupArrowPart<TooltipRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class TooltipPositioner : PopupPositionerPart<TooltipRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class TooltipPortal : PopupPortalPart<TooltipRoot>;

/// <summary>A viewport for the popup's content, animating between the triggers that share it.</summary>
public class TooltipViewport : PopupViewportPart<TooltipRoot>;

/// <summary>
/// Connects triggers outside the <see cref="TooltipRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class TooltipHandle : PopupHandle;
#pragma warning restore S2094
