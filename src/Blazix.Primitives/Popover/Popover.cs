using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>
/// An accessible popup anchored to a button. Non-modal by default:
/// Tab moves through it as if it followed the trigger, and focus leaving closes it.
/// </summary>
public class PopoverRoot() : PopupRootBase("popover")
{
    /// <summary>Whether the popover traps focus and makes the page inert while open. Defaults to <see cref="PopupModal.False"/>.</summary>
    [Parameter] public PopupModal Modal { get; set; } = PopupModal.False;

    internal bool OpenOnHover { get; set; }

    internal int Delay { get; set; } = 300;

    internal int CloseDelay { get; set; }

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["openOnHover"] = OpenOnHover,
        ["delay"] = Delay,
        ["closeDelay"] = CloseDelay,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["modal"] = Modal == PopupModal.True ? true : null,
        ["tab"] = Modal == PopupModal.False ? "flow" : "trap",
        ["closeOnFocusOut"] = Modal == PopupModal.False,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
    };

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<PopoverRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();
    }
}

/// <summary>A button that opens the popover.</summary>
public class PopoverTrigger : PopupTriggerPart<PopoverRoot>
{
    /// <summary>Also open when the pointer rests on the trigger. Defaults to false.</summary>
    [Parameter] public bool OpenOnHover { get; set; }

    /// <summary>How long the pointer rests before opening, in milliseconds. Defaults to 300.</summary>
    [Parameter] public int Delay { get; set; } = 300;

    /// <summary>How long after the pointer leaves before closing, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int CloseDelay { get; set; }

    protected override string HasPopup => "dialog";

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (HasRoot)
        {
            Root.OpenOnHover = OpenOnHover;
            Root.Delay = Delay;
            Root.CloseDelay = CloseDelay;
        }
    }
}

/// <summary>
/// The popover itself: a
/// <c>role="dialog"</c> element in the top layer, positioned against the trigger.
/// </summary>
public class PopoverPopup : AnchoredPopupPart<PopoverRoot, PopupRenderProps>
{
    protected override bool DefaultFallbackAxis => true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", Root.PopupId)
            .Set("role", "dialog")
            .Set("tabindex", "-1")
            // No PopoverTitle: named after the trigger that opened it, rather than not at all.
            .LabelledBy(Root.TitleId ?? (Root.HasTrigger ? Root.TriggerId : null))
            .DescribedBy(Root.DescriptionId);
        Root.AddOpenAttributes(attributes);
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>An arrow pointing at the trigger.</summary>
public class PopoverArrow : PopupArrowPart<PopoverRoot>;

/// <summary>An overlay beneath the popover.</summary>
public class PopoverBackdrop : PopupBackdropPart<PopoverRoot>;

/// <summary>A heading that labels the popover.</summary>
public class PopoverTitle : PopupTitlePart<PopoverRoot>;

/// <summary>A paragraph that describes the popover.</summary>
public class PopoverDescription : PopupDescriptionPart<PopoverRoot>;

/// <summary>A button that closes the popover.</summary>
public class PopoverClose : PopupClosePart<PopoverRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class PopoverPositioner : PopupPositionerPart<PopoverRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class PopoverPortal : PopupPortalPart<PopoverRoot>;

/// <summary>A viewport for the popup's content, animating between the triggers that share it.</summary>
public class PopoverViewport : PopupViewportPart<PopoverRoot>;

/// <summary>
/// Connects triggers outside the <see cref="PopoverRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class PopoverHandle : PopupHandle;
#pragma warning restore S2094
