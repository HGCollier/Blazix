using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>
/// A menu that appears at the pointer on right click or long press.
/// Use <see cref="ContextMenuTrigger"/> for the area, and the Menu parts (<see cref="MenuPopup"/>,
/// <see cref="MenuItem"/>, submenus...) inside.
/// </summary>
public class ContextMenuRoot() : MenuRoot("context-menu")
{
    internal override bool IsContextMenu => true;
}

/// <summary>The area that opens the context menu. Renders a &lt;div&gt;.</summary>
public class ContextMenuTrigger : PrimitivePart<MenuTriggerRenderProps>
{
    private string _id = "";

    [CascadingParameter] private MenuRoot Root { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("context-menu-trigger"));
        Root.TriggerId = _id;
        Root.HasTrigger = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) => await Root.EnsureJsAsync();

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Flag("data-popup-open", Root.IsOpen)
        .Flag("data-disabled", Root.IsDisabled);

    protected override MenuTriggerRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.IsDisabled);
}

#pragma warning disable S2094 // Aliases: the ContextMenu parts are Menu's parts under the ContextMenu name.
/// <summary>The context menu itself, positioned at the pointer.</summary>
public class ContextMenuPopup : MenuPopup;

/// <summary>An item in the context menu.</summary>
public class ContextMenuItem : MenuItem;

/// <summary>A link item in the context menu.</summary>
public class ContextMenuLinkItem : MenuLinkItem;

/// <summary>A checkbox item in the context menu.</summary>
public class ContextMenuCheckboxItem : MenuCheckboxItem;

/// <summary>A checkbox item's indicator.</summary>
public class ContextMenuCheckboxItemIndicator : MenuCheckboxItemIndicator;

/// <summary>A radio group in the context menu.</summary>
public class ContextMenuRadioGroup : MenuRadioGroup;

/// <summary>A radio item in the context menu.</summary>
public class ContextMenuRadioItem : MenuRadioItem;

/// <summary>A radio item's indicator.</summary>
public class ContextMenuRadioItemIndicator : MenuRadioItemIndicator;

/// <summary>A group of items.</summary>
public class ContextMenuGroup : MenuGroup;

/// <summary>A group's label.</summary>
public class ContextMenuGroupLabel : MenuGroupLabel;

/// <summary>A separator.</summary>
public class ContextMenuSeparator : MenuSeparator;

/// <summary>An arrow.</summary>
public class ContextMenuArrow : MenuArrow;

/// <summary>An overlay beneath the context menu.</summary>
public class ContextMenuBackdrop : MenuBackdrop;

/// <summary>A submenu.</summary>
public class ContextMenuSubmenuRoot : MenuSubmenuRoot;

/// <summary>A submenu's trigger.</summary>
public class ContextMenuSubmenuTrigger : MenuSubmenuTrigger;
/// <summary>Positions the popup against its anchor.</summary>
public class ContextMenuPositioner : MenuPositioner;

/// <summary>A portal for the popup.</summary>
public class ContextMenuPortal : MenuPortal;
#pragma warning restore S2094
