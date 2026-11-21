using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// A popup that appears when a link is hovered or focused, showing a preview for sighted users
///. It doesn't take focus; Tab moves into it after the link.
/// </summary>
public class PreviewCardRoot() : PopupRootBase("preview-card")
{
    internal int Delay { get; set; } = 600;

    internal int CloseDelay { get; set; } = 300;

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["openOnHover"] = true,
        ["openOnFocus"] = true,
        ["delay"] = Delay,
        ["closeDelay"] = CloseDelay,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["tab"] = "flow",
        ["initialFocus"] = "none",
        ["closeOnFocusOut"] = true,
        ["closeOnBlur"] = true,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
    };

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<PreviewCardRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();
    }
}

/// <summary>A link that opens the preview card. Renders an &lt;a&gt;.</summary>
public class PreviewCardTrigger : PrimitivePart<PopupTriggerRenderProps>
{
    private string _id = "";

    [CascadingParameter] private PreviewCardRoot? CascadedRoot { get; set; }

    /// <summary>Connects this link to a preview card elsewhere on the page.</summary>
    [Parameter] public PopupHandle? Handle { get; set; }

    /// <summary>Data the card renders with (through its root's <c>ContentTemplate</c>) when this link opens it.</summary>
    [Parameter] public object? Payload { get; set; }

    private PreviewCardRoot Root => Handle?.Root as PreviewCardRoot ?? CascadedRoot
        ?? throw new InvalidOperationException($"{nameof(PreviewCardTrigger)} must be inside a {nameof(PreviewCardRoot)}, or have a Handle that one uses.");

    /// <summary>How long the pointer rests before opening, in milliseconds. Defaults to 600.</summary>
    [Parameter] public int Delay { get; set; } = 600;

    /// <summary>How long after the pointer leaves before closing, in milliseconds. Defaults to 300.</summary>
    [Parameter] public int CloseDelay { get; set; } = 300;

    protected override string Tag => "a";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("preview-card-trigger"));
        Root.RegisterTrigger(_id, Payload);
        Root.Delay = Delay;
        Root.CloseDelay = CloseDelay;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) => await Root.EnsureJsAsync();

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Flag("data-popup-open", Root.IsOpen && Root.TriggerId == _id);

    protected override PopupTriggerRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen, false);
}

/// <summary>The preview card itself.</summary>
public class PreviewCardPopup : AnchoredPopupPart<PreviewCardRoot, PopupRenderProps>
{
    protected override bool DefaultFallbackAxis => true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", Root.PopupId);
        Root.AddOpenAttributes(attributes);
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>An arrow pointing at the link.</summary>
public class PreviewCardArrow : PopupArrowPart<PreviewCardRoot>;

/// <summary>An overlay beneath the preview card.</summary>
public class PreviewCardBackdrop : PopupBackdropPart<PreviewCardRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class PreviewCardPositioner : PopupPositionerPart<PreviewCardRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class PreviewCardPortal : PopupPortalPart<PreviewCardRoot>;

/// <summary>A viewport for the popup's content, animating between the triggers that share it.</summary>
public class PreviewCardViewport : PopupViewportPart<PreviewCardRoot>;

/// <summary>
/// Connects triggers outside the <see cref="PreviewCardRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class PreviewCardHandle : PopupHandle;
#pragma warning restore S2094
