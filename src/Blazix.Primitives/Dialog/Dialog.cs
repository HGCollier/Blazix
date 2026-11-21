using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record DialogPopupRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Nested, int NestedDialogs)
    : RenderProps(Attributes);

/// <summary>
/// A popup that opens on top of the entire page. Modal by default:
/// focus is trapped inside, the page behind is inert and doesn't scroll. Dialogs nest: a dialog
/// opened inside another counts in the parent's <c>--nested-dialogs</c>.
/// </summary>
public class DialogRoot : PopupRootBase
{
    private int _openChildren;

    public DialogRoot() : this("dialog")
    {
    }

    protected DialogRoot(string prefix) : base(prefix)
    {
    }

    [CascadingParameter] private DialogRoot? Parent { get; set; }

    /// <summary>
    /// Whether the dialog traps focus and makes the page inert, or only traps focus, or neither.
    /// Defaults to <see cref="PopupModal.True"/>.
    /// </summary>
    [Parameter] public PopupModal Modal { get; set; } = PopupModal.True;

    /// <summary>Keep the dialog open when the user presses outside it. Defaults to false.</summary>
    [Parameter] public bool DisablePointerDismissal { get; set; }

    internal virtual string Role => "dialog";

    internal virtual PopupModal EffectiveModal => Modal;

    private protected virtual bool PointerDismissible => !DisablePointerDismissal;

    internal bool IsNested => Parent is not null;

    internal int NestedOpenCount => _openChildren;

    internal bool HasViewport { get; set; }

    internal override Dictionary<string, object?> BehaviorOptions()
    {
        var modal = EffectiveModal;
        return new()
        {
            ["modal"] = modal == PopupModal.True ? true : null,
            ["tab"] = modal == PopupModal.False ? "flow" : "trap",
            ["outsidePress"] = "click",
            ["dismissible"] = PointerDismissible,
            ["closeOnFocusOut"] = false,
            ["shield"] = modal != PopupModal.False,
            ["keepTrigger"] = modal != PopupModal.True,
        };
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        var changed = await base.RequestOpenChangeAsync(open, reason);
        if (changed)
        {
            Parent?.ChildOpenChanged(open);
        }
        return changed;
    }

    protected override void OnParametersSet()
    {
        var was = IsOpen;
        base.OnParametersSet();
        if (was != IsOpen)
        {
            Parent?.ChildOpenChanged(IsOpen);
        }
    }

    private void ChildOpenChanged(bool open)
    {
        _openChildren = Math.Max(0, _openChildren + (open ? 1 : -1));
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<DialogRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();
    }

    public override ValueTask DisposeAsync()
    {
        if (IsOpen)
        {
            Parent?.ChildOpenChanged(false);
        }
        return base.DisposeAsync();
    }
}

/// <summary>A button that opens the dialog.</summary>
public class DialogTrigger : PopupTriggerPart<DialogRoot>
{
    protected override string HasPopup => "dialog";
}

/// <summary>
/// An overlay beneath the dialog. Not rendered for nested dialogs
/// unless <see cref="ForceRender"/> is set, so backdrops don't stack up.
/// </summary>
public class DialogBackdrop : PopupBackdropPart<DialogRoot>
{
    /// <summary>Render even when the dialog is nested in another. Defaults to false.</summary>
    [Parameter] public bool ForceRender { get; set; }

    protected override void OnInitialized()
    {
        if (!Root.IsNested || ForceRender)
        {
            base.OnInitialized();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Root.IsNested || ForceRender)
        {
            base.BuildRenderTree(builder);
        }
    }
}

/// <summary>
/// An optional container for the dialog's popup, e.g. to center it and scroll a long dialog
///. When present, it's what renders in the top layer.
/// </summary>
public class DialogViewport : PopupPartBase<DialogRoot, DialogPopupRenderProps>
{
    protected override bool Anchored => false;

    protected override bool OwnsPopupId => false;

    protected override void OnInitialized() => Root.HasViewport = true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        Root.AddOpenAttributes(attributes);
        attributes
            .Flag("data-nested", Root.IsNested)
            .Flag("data-nested-dialog-open", Root.NestedOpenCount > 0);
    }

    protected override DialogPopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.IsNested, Root.NestedOpenCount);

    protected override void Dispose(bool disposing)
    {
        Root.HasViewport = false;
        base.Dispose(disposing);
    }
}

/// <summary>
/// The dialog itself: a <c>role="dialog"</c> element in the top
/// layer. Position it yourself, e.g. <c>position: fixed</c> centered; <c>--nested-dialogs</c> counts the dialogs open inside it.
/// </summary>
public class DialogPopup : PopupPartBase<DialogRoot, DialogPopupRenderProps>
{
    protected override bool Anchored => false;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Inside a Dialog.Viewport, the viewport is the top-layer host and drives presence.
        if (Root.HasViewport)
        {
            if (Root.State.IsMounted || KeepMounted)
            {
                BuildPart(builder);
            }
            return;
        }
        base.BuildRenderTree(builder);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!Root.HasViewport)
        {
            await base.OnAfterRenderAsync(firstRender);
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", Root.PopupId)
            .Set("role", Root.Role)
            .Set("tabindex", "-1")
            .Set("aria-modal", Root.EffectiveModal != PopupModal.False ? "true" : null)
            // No DialogTitle: named after the trigger that opened it, rather than not at all.
            .LabelledBy(Root.TitleId ?? (Root.HasTrigger ? Root.TriggerId : null))
            .DescribedBy(Root.DescriptionId)
            .Flag("data-nested", Root.IsNested)
            .Flag("data-nested-dialog-open", Root.NestedOpenCount > 0)
            .Set("style", $"--nested-dialogs:{Root.NestedOpenCount.ToString(CultureInfo.InvariantCulture)}");
        Root.AddOpenAttributes(attributes);
    }

    protected override DialogPopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.IsNested, Root.NestedOpenCount);
}

/// <summary>A heading that labels the dialog.</summary>
public class DialogTitle : PopupTitlePart<DialogRoot>;

/// <summary>A paragraph with additional information about the dialog.</summary>
public class DialogDescription : PopupDescriptionPart<DialogRoot>;

/// <summary>A button that closes the dialog.</summary>
public class DialogClose : PopupClosePart<DialogRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class DialogPortal : PopupPortalPart<DialogRoot>;

/// <summary>
/// Connects triggers outside the <see cref="DialogRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class DialogHandle : PopupHandle;
#pragma warning restore S2094
