using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Blazix.Primitives;

public sealed record MenuTriggerRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Disabled)
    : RenderProps(Attributes);

public sealed record MenuPopupRenderProps(IReadOnlyDictionary<string, object> Attributes)
    : RenderProps(Attributes);

/// <summary><c>Highlighted</c> mirrors the browser's keyboard/hover focus (see <see cref="MenuContext.HighlightedItemId"/>).</summary>
public sealed record MenuItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Highlighted, bool Disabled)
    : RenderProps(Attributes);

public sealed record MenuCheckboxItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Highlighted, bool Checked, bool Disabled)
    : RenderProps(Attributes);

public sealed record MenuCheckboxItemIndicatorRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Checked)
    : RenderProps(Attributes);

public sealed record MenuRadioItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Highlighted, bool Checked, bool Disabled)
    : RenderProps(Attributes);

public sealed record MenuRadioItemIndicatorRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Checked)
    : RenderProps(Attributes);

public sealed record MenuGroupRenderProps(IReadOnlyDictionary<string, object> Attributes)
    : RenderProps(Attributes);

public sealed record MenuGroupLabelRenderProps(IReadOnlyDictionary<string, object> Attributes)
    : RenderProps(Attributes);

public sealed record MenuSeparatorRenderProps(IReadOnlyDictionary<string, object> Attributes)
    : RenderProps(Attributes);

/// <summary>
/// Cascaded from <see cref="MenuRoot"/> to every part. The public members let you build your own
/// parts: add <c>[CascadingParameter] MenuContext Menu</c> to any component inside a menu.
/// </summary>
public sealed class MenuContext
{
    internal MenuContext(MenuRoot root) => Root = root;

    private MenuRoot Root { get; }

    public bool Open => Root.IsOpen;

    public bool Disabled => Root.IsDisabled;

    /// <summary>True while the popup plays its exit animation. <see cref="Open"/> is already false by then.</summary>
    public bool Exiting => Root.State.IsExiting;

    public string TriggerId => Root.TriggerId;

    public string PopupId => Root.PopupId;

    /// <summary>
    /// The id of the item that has keyboard/hover focus, or null. Kept in sync with the browser:
    /// menu.js reports every focus move, and parts re-render when it changes.
    /// </summary>
    public string? HighlightedItemId => Root.HighlightedItemId;
}

/// <summary>
/// A list of actions in a dropdown, enhanced with keyboard navigation.
/// Groups the parts of a menu and owns its open state. Modal by default: while open,
/// the page doesn't scroll and an outside press only closes the menu.
/// </summary>
public class MenuRoot : PopupRootBase
{
    private readonly MenuContext _context;
    private bool _registeredWithMenubar;

    public MenuRoot() : this("menu")
    {
    }

    protected MenuRoot(string prefix) : base(prefix) => _context = new MenuContext(this);

    [CascadingParameter] private MenuRoot? ParentMenu { get; set; }

    [CascadingParameter] internal MenubarContext? Menubar { get; set; }

    [CascadingParameter] private MenuFilterContext? FilterProvider { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether arrow keys wrap from the last item to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    /// <summary>
    /// Whether the menu is modal while open: the page doesn't scroll and is inert, and an outside
    /// press only closes the menu. Defaults to true. Submenus and menus opened by hovering never are.
    /// </summary>
    [Parameter] public bool Modal { get; set; } = true;

    /// <summary>Whether moving the pointer over an item highlights it. Defaults to true.</summary>
    [Parameter] public bool HighlightItemOnHover { get; set; } = true;

    internal override bool IsDisabled => Disabled || (Menubar?.Disabled ?? false);

    internal string? HighlightedItemId { get; private set; }

    /// <summary>What gets focus when the popup opens: "first", "last", "popup" or "none".</summary>
    internal string PendingFocus { get; set; } = "popup";

    private string? AnchorPoint { get; set; }

    internal bool OpenOnHover { get; set; }

    internal int Delay { get; set; } = 100;

    internal int CloseDelay { get; set; }

    internal virtual bool IsSubmenu => false;

    internal virtual bool IsContextMenu => false;

    internal MenuRoot? Parent => IsSubmenu ? ParentMenu : null;

    // A submenu that's open when its parent menu opens animates in with it.
    private protected override bool AnimatesInitialOpen => Parent is { State.IsStarting: true };

    internal MenuContext Context => _context;

    /// <summary>The filter of a <see cref="MenuFilterProvider"/> directly around this menu.</summary>
    internal MenuFilterContext? Filter => FilterProvider?.Menu == this ? FilterProvider : null;

    /// <summary>The id of the <see cref="MenuList"/> holding the items, when there is one.</summary>
    internal string? ListId { get; set; }

    protected override string Module => "menu.js";

    protected override string Factory => "createMenu";

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["openOnHover"] = OpenOnHover || (Menubar is { HasOpenMenu: true } && !IsOpen),
        ["delay"] = Menubar is { HasOpenMenu: true } ? 0 : Delay,
        ["closeDelay"] = CloseDelay,
        ["loopFocus"] = LoopFocus,
        ["highlightItemOnHover"] = HighlightItemOnHover,
        ["submenu"] = IsSubmenu,
        ["menubar"] = Menubar is not null && !IsSubmenu,
        ["context"] = IsContextMenu,
    };

    private bool EffectiveModal =>
        !IsSubmenu && !IsContextMenu && State.Reason != OpenChangeReason.TriggerHover && (Menubar?.Modal ?? Modal);

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["modal"] = EffectiveModal ? true : null,
        ["tab"] = "close",
        ["closeOnFocusOut"] = true,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
        ["insideIds"] = Menubar is null ? Array.Empty<string>() : new[] { Menubar.Id },
        ["focus"] = PendingFocus,
        ["anchorPoint"] = AnchorPoint,
    };

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (FilterProvider is { Menu: null })
        {
            // A provider applies to the first menu inside it, not to its submenus.
            FilterProvider.Menu = this;
        }
        if (!IsOpen)
        {
            HighlightedItemId = null;
        }
        // A submenu closes with its parent.
        if (IsSubmenu && IsOpen && ParentMenu is { IsOpen: false })
        {
            State.Apply(false, OpenChangeReason.None);
            HighlightedItemId = null;
        }
        if (Menubar is not null && !IsSubmenu && !_registeredWithMenubar)
        {
            _registeredWithMenubar = true;
            Menubar.Register(this);
        }
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        if (open && IsOpen && PendingFocus is "first" or "last")
        {
            // e.g. ArrowDown on the trigger while the menu is already showing.
            await InvokeJsAsync("focusItem", PendingFocus);
            return false;
        }

        var changed = await base.RequestOpenChangeAsync(open, reason);
        if (!changed)
        {
            return false;
        }
        if (!open)
        {
            HighlightedItemId = null;
        }
        if (Menubar is not null && !IsSubmenu)
        {
            await Menubar.MenuOpenChangedAsync(this, open);
        }
        return true;
    }

    internal override Task OnRequestOpenAsync(string reason, string? detail)
    {
        if (detail is "first" or "last" or "popup" or "none")
        {
            PendingFocus = detail;
        }
        else if (detail is not null && detail.Contains(','))
        {
            AnchorPoint = detail;
            PendingFocus = "popup";
        }
        else
        {
            PendingFocus = "popup";
        }
        return RequestOpenChangeAsync(true, PopupState.ParseReason(reason));
    }

    internal override Task OnNotifyAsync(string name, string? value)
    {
        if (name == "highlight")
        {
            if (IsOpen && value != HighlightedItemId)
            {
                HighlightedItemId = value;
                StateHasChanged();
            }
        }
        else if (name == "menubar" && Menubar is not null)
        {
            return Menubar.NavigateAsync(this, value == "previous" ? -1 : 1);
        }
        return Task.CompletedTask;
    }

    /// <summary>Opens the menu from the trigger: keyboard presses focus the first item, pointer presses the menu itself.</summary>
    internal Task ToggleFromTriggerAsync(bool keyboard)
    {
        PendingFocus = keyboard ? "first" : "popup";
        return RequestOpenChangeAsync(!IsOpen, OpenChangeReason.TriggerPress);
    }

    /// <summary>Activating an item closes the whole menu tree, and focus goes back to the outermost trigger.</summary>
    internal Task CloseTreeAsync(OpenChangeReason reason)
    {
        var root = this;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }
        return root.RequestOpenChangeAsync(false, reason);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<MenuRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", (RenderFragment)(inner =>
        {
            inner.OpenComponent<CascadingValue<MenuContext>>(0);
            inner.AddComponentParameter(1, "Value", _context);
            inner.AddComponentParameter(2, "ChildContent", BodyContent);
            inner.CloseComponent();
        }));
        builder.CloseComponent();
    }

    private protected override Task OnExitCompleteAsync() =>
        // The query clears once the menu has closed, so items don't reappear during the exit animation.
        Filter?.SetValueAsync("", "close") ?? Task.CompletedTask;

    public override ValueTask DisposeAsync()
    {
        if (FilterProvider?.Menu == this)
        {
            FilterProvider.Menu = null;
        }
        Menubar?.Unregister(this);
        return base.DisposeAsync();
    }
}

/// <summary>
/// A button that opens the menu. Renders a &lt;button&gt;, or your
/// element through <c>Render</c>; ArrowDown and ArrowUp open it and focus the first or last item.
/// </summary>
public class MenuTrigger : ButtonPart<MenuTriggerRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;
    private EventCallback<FocusEventArgs> _onFocus;
    private RovingItem? _item;

    [CascadingParameter] private MenuRoot? CascadedRoot { get; set; }

    [CascadingParameter] private RovingGroup? Roving { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Connects this trigger to a menu elsewhere on the page.</summary>
    [Parameter] public PopupHandle? Handle { get; set; }

    /// <summary>Data the menu renders with (through its root's <c>ContentTemplate</c>) when this trigger opens it.</summary>
    [Parameter] public object? Payload { get; set; }

    private MenuRoot Root => Handle?.Root as MenuRoot ?? CascadedRoot
        ?? throw new InvalidOperationException($"{nameof(MenuTrigger)} must be inside a {nameof(MenuRoot)}, or have a Handle that one uses.");

    /// <summary>Also open when the pointer rests on the trigger. Defaults to false.</summary>
    [Parameter] public bool OpenOnHover { get; set; }

    /// <summary>How long the pointer rests before opening, in milliseconds. Defaults to 100.</summary>
    [Parameter] public int Delay { get; set; } = 100;

    /// <summary>How long after the pointer leaves before closing, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int CloseDelay { get; set; }

    protected override string IdPrefix => "menu-trigger";

    private bool InMenubar => Root.Menubar is not null && Roving is not null;

    protected override bool IsComposite => InMenubar;

    private bool IsDisabled => Disabled || Root.IsDisabled;

    protected override void OnInitialized()
    {
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Roving?.Focused(_item!));
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Root.RegisterTrigger(ButtonId, Payload);
        Root.OpenOnHover = OpenOnHover;
        Root.Delay = Delay;
        Root.CloseDelay = CloseDelay;
        if (InMenubar)
        {
            _item ??= new RovingItem(ButtonId, () => IsDisabled, () => false);
            _item.Id = ButtonId;
            Roving!.Register(_item);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        await Root.EnsureJsAsync();
    }

    // Detail is 0 when the click came from Enter/Space: keyboard users land on the first item.
    // Pointer users get focus on the menu itself, with nothing highlighted yet.
    private Task HandleClickAsync(MouseEventArgs e)
    {
        if (IsDisabled)
        {
            return Task.CompletedTask;
        }
        // Open against another trigger: move the menu here rather than closing it.
        var elsewhere = Root.IsOpen && Root.TriggerId != ButtonId;
        Root.Activate(ButtonId);
        return elsewhere ? Task.CompletedTask : Root.ToggleFromTriggerAsync(e.Detail == 0);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, IsDisabled);
        Root.AddTriggerAttributes(attributes, "menu", ButtonId);
        var open = Root.IsOpen && Root.TriggerId == ButtonId;
        attributes
            .Set("data-state", open ? "open" : "closed")
            .Set("onclick", _onClick);
        if (InMenubar)
        {
            attributes.Set("role", "menuitem");
            Roving!.AddItemAttributes(attributes, _item!, _onFocus);
        }
    }

    protected override MenuTriggerRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, IsDisabled);

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Roving?.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// The menu itself: a
/// <c>role="menu"</c> element in the top layer. The positioner is internal (menu.js owns its inline
/// styles) and sets <c>--anchor-width</c>, <c>--available-height</c> and <c>--transform-origin</c>.
/// On close it stays mounted with <c>data-closed</c> and <c>data-ending-style</c> until its exit
/// animation finishes.
/// </summary>
public class MenuPopup : AnchoredPopupPart<MenuRoot, MenuPopupRenderProps>
{
    protected override Side DefaultSide => Root.IsSubmenu ? Primitives.Side.InlineEnd : Primitives.Side.Bottom;

    protected override Align DefaultAlign => Primitives.Align.Start;

    protected override int DefaultSideOffset => Root.IsContextMenu && RequestedSide is null ? -5 : 0;

    protected override int DefaultAlignOffset => Root.IsContextMenu && RequestedSide is null ? 2 : 0;

    protected override bool DefaultFallbackAxis => Root.IsSubmenu;

    protected override void AddHostAttributes(RenderTreeBuilder builder)
    {
        base.AddHostAttributes(builder);
        builder.AddAttribute(11, "data-menu-positioner", "");
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // With a Menu.List, the list is the menu and the popup only holds it (and the filter input).
        var hasList = Root.ListId is not null;
        attributes
            .Set("id", Root.PopupId)
            .Set("role", hasList ? "presentation" : "menu")
            .Set("tabindex", "-1")
            .LabelledBy(Root.IsContextMenu || hasList ? null : Root.TriggerId)
            .Set("aria-orientation", hasList ? null : "vertical")
            .Set("data-blazix-menu", Root.PopupId)
            .Set("data-state", Root.IsOpen ? "open" : "closed");
        Root.AddOpenAttributes(attributes);
    }

    protected override MenuPopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>What every menu item shares: its id (so the browser can report the highlight), role, disabled state and activation.</summary>
public abstract class MenuItemPart<TRenderProps> : PrimitivePart<TRenderProps> where TRenderProps : RenderProps
{
    private EventCallback<MouseEventArgs> _onClick;
    private string _id = "";

    [CascadingParameter] protected MenuRoot Menu { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Text used for typeahead when the item's text content isn't right (e.g. icons only).</summary>
    [Parameter] public string? Label { get; set; }

    protected string ItemId => _id;

    /// <summary>The menu this item belongs to (a submenu trigger belongs to the parent menu).</summary>
    protected virtual MenuRoot OwnerMenu => Menu;

    protected bool Highlighted => OwnerMenu.HighlightedItemId == _id;

    protected abstract string Role { get; }

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);

    // A stable id lets menu.js report which item is highlighted.
    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("menu-item"));

    protected void AddItemAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", Role)
        .Set("tabindex", "-1")
        .Set("aria-disabled", Disabled ? "true" : null)
        .Flag("data-disabled", Disabled)
        .Set("data-label", Label)
        .Set("onclick", _onClick);

    private Task HandleClickAsync(MouseEventArgs e) => Disabled ? Task.CompletedTask : ActivateAsync(e);

    protected abstract Task ActivateAsync(MouseEventArgs e);
}

/// <summary>An individual interactive item in the menu. Activating it closes the menu by default.</summary>
public class MenuItem : MenuItemPart<MenuItemRenderProps>
{
    /// <summary>Runs when the item is chosen, with a click or the keyboard.</summary>
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>Close the menu after the item is activated. Defaults to true.</summary>
    [Parameter] public bool CloseOnClick { get; set; } = true;

    protected override string Role => "menuitem";

    protected override void BuildAttributes(AttributeBuilder attributes) => AddItemAttributes(attributes);

    protected override async Task ActivateAsync(MouseEventArgs e)
    {
        await OnClick.InvokeAsync(e);
        if (CloseOnClick)
        {
            await Menu.CloseTreeAsync(OpenChangeReason.ItemPress);
        }
    }

    protected override MenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Highlighted, Disabled);
}

/// <summary>A link in the menu that can be used to navigate to a different page. Renders an &lt;a&gt;.</summary>
public class MenuLinkItem : MenuItemPart<MenuItemRenderProps>
{
    /// <summary>Close the menu when the link is followed. Defaults to true.</summary>
    [Parameter] public bool CloseOnClick { get; set; } = true;

    protected override string Tag => "a";

    protected override string Role => "menuitem";

    protected override void BuildAttributes(AttributeBuilder attributes) => AddItemAttributes(attributes);

    protected override Task ActivateAsync(MouseEventArgs e) =>
        CloseOnClick ? Menu.CloseTreeAsync(OpenChangeReason.ItemPress) : Task.CompletedTask;

    protected override MenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Highlighted, Disabled);
}

/// <summary>A menu item that toggles a setting on or off. Stays open by default so several can be toggled.</summary>
public class MenuCheckboxItem : MenuItemPart<MenuCheckboxItemRenderProps>
{
    internal const string IndicatorCascadeName = "Blazix.Primitives.MenuCheckboxItem.Checked";

    private readonly Controlled<bool> _checked = new(false);

    /// <summary>Use <c>@bind-Checked</c>, or leave unset for an uncontrolled item.</summary>
    [Parameter] public bool Checked { get; set; }

    [Parameter] public EventCallback<bool> CheckedChanged { get; set; }

    /// <summary>Runs before the checked state changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<bool>> OnCheckedChange { get; set; }

    /// <summary>Close the menu when the item is toggled. Defaults to false.</summary>
    [Parameter] public bool CloseOnClick { get; set; }

    protected override string Role => "menuitemcheckbox";

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _checked.Sync(Checked);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, _checked.Value, BuildPart, name: IndicatorCascadeName);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddItemAttributes(attributes);
        attributes
            .Set("aria-checked", _checked.Value ? "true" : "false")
            .Flag("data-checked", _checked.Value)
            .Flag("data-unchecked", !_checked.Value)
            .Set("data-state", _checked.Value ? "checked" : "unchecked");
    }

    protected override async Task ActivateAsync(MouseEventArgs e)
    {
        var next = !_checked.Value;
        var args = new ValueChangeEventArgs<bool>(next, "item-press");
        await OnCheckedChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return;
        }
        _checked.Value = next;
        await CheckedChanged.InvokeAsync(next);
        if (CloseOnClick)
        {
            await Menu.CloseTreeAsync(OpenChangeReason.ItemPress);
        }
    }

    protected override MenuCheckboxItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Highlighted, _checked.Value, Disabled);
}

/// <summary>Indicates whether the checkbox item is ticked. Renders a &lt;span&gt; while checked.</summary>
public class MenuCheckboxItemIndicator : PrimitivePart<MenuCheckboxItemIndicatorRenderProps>
{
    [CascadingParameter(Name = MenuCheckboxItem.IndicatorCascadeName)]
    private bool Checked { get; set; }

    /// <summary>Render while unchecked too (e.g. to animate it out with CSS).</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => Checked || KeepMounted;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-checked", Checked)
        .Flag("data-unchecked", !Checked)
        .Set("data-state", Checked ? "checked" : "unchecked");

    protected override MenuCheckboxItemIndicatorRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Checked);
}

/// <summary>Cascaded from <see cref="MenuRadioGroup"/> to its radio items.</summary>
public sealed class MenuRadioGroupContext
{
    private readonly MenuRadioGroup _group;

    internal MenuRadioGroupContext(MenuRadioGroup group) => _group = group;

    public string? Value => _group.CurrentValue;

    public bool Disabled => _group.Disabled;

    internal Task<bool> SelectAsync(string value) => _group.SelectAsync(value);
}

/// <summary>Groups related radio items. Its value is the checked item's.</summary>
public class MenuRadioGroup : PrimitivePart<MenuGroupRenderProps>
{
    private readonly Controlled<string?> _value = new(null);
    private readonly MenuRadioGroupContext _context;

    public MenuRadioGroup() => _context = new MenuRadioGroupContext(this);

    /// <summary>The checked item's value. Use <c>@bind-Value</c>, or set it once for an uncontrolled group.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public EventCallback<ValueChangeEventArgs<string?>> OnValueChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    internal string? CurrentValue => _value.Value;

    protected override void OnParametersSet() => _value.Sync(Value);

    internal async Task<bool> SelectAsync(string value)
    {
        if (_value.Value == value)
        {
            return true;
        }
        var args = new ValueChangeEventArgs<string?>(value, "item-press");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }
        _value.Value = value;
        await ValueChanged.InvokeAsync(value);
        StateHasChanged();
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "group")
        .Flag("data-disabled", Disabled);

    protected override MenuGroupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>A menu item that works like a radio button in a <see cref="MenuRadioGroup"/>.</summary>
public class MenuRadioItem : MenuItemPart<MenuRadioItemRenderProps>
{
    internal const string IndicatorCascadeName = "Blazix.Primitives.MenuRadioItem.Checked";

    [CascadingParameter] private MenuRadioGroupContext Group { get; set; } = null!;

    /// <summary>Identifies the item in its group's value.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>Close the menu when the item is chosen. Defaults to false.</summary>
    [Parameter] public bool CloseOnClick { get; set; }

    protected override string Role => "menuitemradio";

    private bool IsChecked => Group.Value == Value;

    private bool IsDisabled => Disabled || Group.Disabled;

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, IsChecked, BuildPart, name: IndicatorCascadeName);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddItemAttributes(attributes);
        attributes
            .Set("aria-checked", IsChecked ? "true" : "false")
            .Set("aria-disabled", IsDisabled ? "true" : null)
            .Flag("data-disabled", IsDisabled)
            .Flag("data-checked", IsChecked)
            .Flag("data-unchecked", !IsChecked)
            .Set("data-state", IsChecked ? "checked" : "unchecked");
    }

    protected override async Task ActivateAsync(MouseEventArgs e)
    {
        if (IsDisabled || !await Group.SelectAsync(Value))
        {
            return;
        }
        if (CloseOnClick)
        {
            await Menu.CloseTreeAsync(OpenChangeReason.ItemPress);
        }
    }

    protected override MenuRadioItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Highlighted, IsChecked, IsDisabled);
}

/// <summary>Indicates whether the radio item is selected.</summary>
public class MenuRadioItemIndicator : PrimitivePart<MenuRadioItemIndicatorRenderProps>
{
    [CascadingParameter(Name = MenuRadioItem.IndicatorCascadeName)]
    private bool Checked { get; set; }

    /// <summary>Render while unchecked too (e.g. to animate it out with CSS).</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => Checked || KeepMounted;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-checked", Checked)
        .Flag("data-unchecked", !Checked);

    protected override MenuRadioItemIndicatorRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Checked);
}

internal sealed class MenuGroupContext(Action requestRender)
{
    public string LabelId { get; } = Ids.Next("menu-group-label");

    public bool HasLabel { get; private set; }

    // aria-labelledby must only point at an element that exists.
    public void RegisterLabel()
    {
        if (HasLabel)
        {
            return;
        }
        HasLabel = true;
        requestRender();
    }
}

/// <summary>Groups related menu items with an optional label.</summary>
[PublicAPI]
public class MenuGroup : PrimitivePart<MenuGroupRenderProps>
{
    private readonly MenuGroupContext _context;

    public MenuGroup() => _context = new MenuGroupContext(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "group")
        .LabelledBy(_context.HasLabel ? _context.LabelId : null);

    protected override MenuGroupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>
/// An accessible label for a menu group. Outside a group it's just
/// a heading row. Rendered with <c>role="none"</c>, so even a heading rendered through Render stays valid inside a menu.
/// </summary>
public class MenuGroupLabel : PrimitivePart<MenuGroupLabelRenderProps>
{
    [CascadingParameter] private MenuGroupContext? Group { get; set; }

    protected override void OnInitialized() => Group?.RegisterLabel();

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", Group?.LabelId)
        .Set("role", "none");

    protected override MenuGroupLabelRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>A separator between menu items.</summary>
public class MenuSeparator : PrimitivePart<MenuSeparatorRenderProps>
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "separator")
        .Set("aria-orientation", "horizontal");

    protected override MenuSeparatorRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>An arrow pointing at the trigger.</summary>
public class MenuArrow : PopupArrowPart<MenuRoot>;

/// <summary>An overlay beneath the menu.</summary>
public class MenuBackdrop : PopupBackdropPart<MenuRoot>;

/// <summary>
/// Groups the parts of a submenu. Put it inside a menu's popup,
/// with a <see cref="MenuSubmenuTrigger"/> and a <see cref="MenuPopup"/>. Opens when its trigger is
/// hovered or with the forward arrow key, and closes with its parent.
/// </summary>
[PublicAPI]
public class MenuSubmenuRoot() : MenuRoot("submenu")
{
    internal override bool IsSubmenu => true;

    protected override void OnInitialized()
    {
        OpenOnHover = true;
        base.OnInitialized();
    }
}

/// <summary>
/// A menu item that opens a submenu. It's an item of the
/// parent menu: arrow keys reach it, and the forward arrow, Enter or Space open the submenu.
/// </summary>
public class MenuSubmenuTrigger : MenuItemPart<MenuItemRenderProps>
{
    /// <summary>How long the pointer rests before the submenu opens, in milliseconds. Defaults to 100.</summary>
    [Parameter] public int Delay { get; set; } = 100;

    /// <summary>How long after the pointer leaves before it closes, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int CloseDelay { get; set; }

    /// <summary>Open the submenu when the pointer rests on the trigger. Defaults to true.</summary>
    [Parameter] public bool OpenOnHover { get; set; } = true;

    protected override string Role => "menuitem";

    protected override MenuRoot OwnerMenu => Menu.Parent ?? Menu;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Menu.TriggerId = ItemId;
        Menu.HasTrigger = true;
        Menu.OpenOnHover = OpenOnHover;
        Menu.Delay = Delay;
        Menu.CloseDelay = CloseDelay;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) => await Menu.EnsureJsAsync();

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddItemAttributes(attributes);
        Menu.AddTriggerAttributes(attributes, "menu");
        attributes.Set("data-state", Menu.IsOpen ? "open" : "closed");
    }

    protected override Task ActivateAsync(MouseEventArgs e)
    {
        Menu.PendingFocus = e.Detail == 0 ? "first" : "popup";
        return Menu.IsOpen ? Task.CompletedTask : Menu.RequestOpenChangeAsync(true, OpenChangeReason.TriggerPress);
    }

    protected override MenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Highlighted, Disabled);
}

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class MenuPositioner : PopupPositionerPart<MenuRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class MenuPortal : PopupPortalPart<MenuRoot>;

/// <summary>A viewport for the popup's content, animating between the triggers that share it.</summary>
public class MenuViewport : PopupViewportPart<MenuRoot>;

/// <summary>
/// Connects triggers outside the <see cref="MenuRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class MenuHandle : PopupHandle;
#pragma warning restore S2094
