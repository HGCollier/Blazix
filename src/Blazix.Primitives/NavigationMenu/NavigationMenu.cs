using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record NavigationMenuRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, string? Value, Orientation Orientation)
    : RenderProps(Attributes);

public sealed record NavigationMenuItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Disabled)
    : RenderProps(Attributes);

/// <summary>
/// A collection of links and menus for website navigation.
/// Renders a &lt;nav&gt;. Its value is the open item's; each item's <see cref="NavigationMenuContent"/>
/// shows in the shared <see cref="NavigationMenuPopup"/>'s <see cref="NavigationMenuViewport"/>,
/// anchored to that item's trigger.
/// </summary>
public class NavigationMenuRoot() : PopupRootBase("navigation-menu")
{
    private readonly Controlled<string?> _value = new(null);
    private string _id = "";
    private DateTime _hoverOpenedAt;

    /// <summary>The open item's value, or null when closed. Use <c>@bind-Value</c>.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Runs before the open item changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<string?>> OnValueChange { get; set; }

    /// <summary>How long the pointer rests on a trigger before its item opens, in milliseconds. Defaults to 50.</summary>
    [Parameter] public int Delay { get; set; } = 50;

    /// <summary>How long after the pointer leaves before the menu closes, in milliseconds. Defaults to 50.</summary>
    [Parameter] public int CloseDelay { get; set; } = 50;

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    internal string? CurrentValue => _value.Value;

    internal List<NavigationMenuItem> Items { get; } = [];

    internal NavigationMenuItem? ActiveItem => Items.Find(item => item.ItemValue == _value.Value);

    internal NavigationMenuItem? PreviousItem { get; private set; }

    /// <summary>The previous item, while its content fades out after a switch (it stays mounted).</summary>
    internal NavigationMenuItem? ExitingItem { get; private set; }

    /// <summary>The item whose content renders data-starting-style next, so it fades in.</summary>
    internal NavigationMenuItem? EnteringItem { get; private set; }

    /// <summary>"left", "right", "up", "down" or "none": where the newly opened item is relative to the previous one.</summary>
    internal string ActivationDirection { get; private set; } = "none";

    protected override string Module => "navigation-menu.js";

    protected override string Factory => "createNavigationMenu";

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["rootId"] = _id,
        ["delay"] = Delay,
        ["closeDelay"] = CloseDelay,
        ["orientation"] = Js.Orientation(Orientation),
        ["value"] = _value.Value,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["tab"] = "flow",
        ["closeOnFocusOut"] = true,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
        ["initialFocus"] = PendingFocus,
    };

    private string PendingFocus { get; set; } = "none";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("navigation-menu"));
        if (_value.Sync(Value))
        {
            ApplyValue(_value.Value, OpenChangeReason.ImperativeAction);
        }
    }

    private void ApplyValue(string? value, OpenChangeReason reason)
    {
        var previous = ActiveItem;
        _value.Value = value;
        var next = ActiveItem;
        if (next is not null)
        {
            TriggerId = next.TriggerId;
        }
        if (previous is not null && next is not null && previous != next)
        {
            PreviousItem = previous;
            ActivationDirection = DirectionOf(Items.IndexOf(next) > Items.IndexOf(previous));
            // The old content fades out as the new one fades in, while the popup stays open.
            ExitingItem = IsOpen ? previous : null;
        }
        else
        {
            ActivationDirection = "none";
            ExitingItem = previous is not null && next is null && IsOpen ? previous : null;
        }
        if (next is not null && next != previous)
        {
            EnteringItem = next;
        }
        if ((value is not null) != IsOpen)
        {
            State.Apply(value is not null, reason);
        }
    }

    private string DirectionOf(bool forward)
    {
        if (Orientation == Orientation.Vertical)
        {
            return forward ? "down" : "up";
        }
        return forward ? "right" : "left";
    }

    /// <summary>Opens the item with this value, or closes the menu with null.</summary>
    internal async Task SetValueAsync(string? value, OpenChangeReason reason)
    {
        if (value == _value.Value)
        {
            return;
        }
        var args = new ValueChangeEventArgs<string?>(value, reason == OpenChangeReason.TriggerHover ? "trigger-hover" : "trigger-press");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return;
        }
        if (value is null && State.RestoresFocus && reason is not OpenChangeReason.TriggerHover)
        {
            await InvokeJsAsync("restoreFocus");
        }
        if (reason == OpenChangeReason.TriggerHover && value is not null)
        {
            _hoverOpenedAt = DateTime.UtcNow;
        }
        ApplyValue(value, reason);
        await ValueChanged.InvokeAsync(value);
        await OpenChanged.InvokeAsync(value is not null);
        StateHasChanged();
    }

    internal Task ToggleAsync(NavigationMenuItem item)
    {
        // A press right after the pointer opened it would close it again: keep it open instead.
        if (_value.Value == item.ItemValue && DateTime.UtcNow - _hoverOpenedAt < TimeSpan.FromMilliseconds(500))
        {
            return Task.CompletedTask;
        }
        PendingFocus = "none";
        return SetValueAsync(_value.Value == item.ItemValue ? null : item.ItemValue, OpenChangeReason.TriggerPress);
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        // Opening needs an item: only the triggers open the menu.
        if (open || !IsOpen)
        {
            return false;
        }
        await SetValueAsync(null, reason);
        return !IsOpen;
    }

    internal override Task OnRequestOpenAsync(string reason, string? detail)
    {
        var parsed = PopupState.ParseReason(reason);
        PendingFocus = parsed == OpenChangeReason.ListNavigation ? "first" : "none";
        return SetValueAsync(detail, parsed);
    }

    internal override Task OnRequestCloseAsync(string reason) => SetValueAsync(null, PopupState.ParseReason(reason));

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<NavigationMenuRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", (RenderFragment)(inner =>
        {
            var attributes = new AttributeBuilder(AdditionalAttributes)
                .Set("id", _id)
                .Set("data-orientation", Js.Orientation(Orientation))
                .Flag("data-open", IsOpen)
                .Build();
            inner.OpenElement(0, "nav");
            inner.AddMultipleAttributes(1, attributes);
            inner.AddContent(2, ChildContent);
            inner.CloseElement();
        }));
        builder.CloseComponent();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Each render can change the active trigger the popup is anchored to.
        if (HasTrigger)
        {
            await EnsureJsAsync();
        }
    }

    /// <summary>
    /// The viewport rendered an entering or exiting content: the browser starts the entering one's
    /// transition, and once the exiting one's has finished, it goes.
    /// </summary>
    internal async Task SettleContentAsync()
    {
        var exiting = ExitingItem;
        EnteringItem = null;
        var finished = await InvokeJsSettleAsync();
        if (finished && exiting is not null && ExitingItem == exiting)
        {
            ExitingItem = null;
            StateHasChanged();
        }
    }

    private async Task<bool> InvokeJsSettleAsync()
    {
        try
        {
            return await InvokeJsAsync<bool>("settleContent");
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}

/// <summary>Contains the menu's items. Renders a &lt;ul&gt;; arrow keys move between triggers.</summary>
public class NavigationMenuList : PrimitivePart<NavigationMenuRenderProps>
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private NavigationMenuRoot Root { get; set; } = null!;

    protected override string Tag => "ul";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("data-orientation", Js.Orientation(Root.Orientation));
        RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Root.Orientation), loop: false);
    }

    protected override NavigationMenuRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.CurrentValue, Root.Orientation);
}

/// <summary>An individual navigation menu item. Renders an &lt;li&gt;.</summary>
public class NavigationMenuItem : PrimitivePart<NavigationMenuItemRenderProps>
{
    [CascadingParameter] private NavigationMenuRoot Root { get; set; } = null!;

    /// <summary>Identifies the item in the root's value. Defaults to a generated id.</summary>
    [Parameter] public string? Value { get; set; }

    protected override string Tag => "li";

    internal string ItemValue => Value ?? (field ??= Ids.Next("navigation-menu-item"));

    internal string TriggerId { get; set; } = Ids.Next("navigation-menu-trigger");

    internal bool IsOpen => Root.IsOpen && Root.CurrentValue == ItemValue;

    internal NavigationMenuContent? Content { get; set; }

    internal NavigationMenuRoot Menu => Root;

    protected override void OnInitialized() => Root.Items.Add(this);

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Flag("data-open", IsOpen);

    protected override NavigationMenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsOpen, false);

    protected override void Dispose(bool disposing)
    {
        Root.Items.Remove(this);
        base.Dispose(disposing);
    }
}

/// <summary>Opens its item's content: on press, and when the pointer rests on it.</summary>
public class NavigationMenuTrigger : ButtonPart<NavigationMenuItemRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private NavigationMenuItem Item { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override string IdPrefix => "navigation-menu-trigger";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Disabled ? Task.CompletedTask : Item.Menu.ToggleAsync(Item));

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Item.TriggerId = ButtonId;
        Item.Menu.HasTrigger = true;
        // The popup anchors to the open item's trigger; while closed, the first trigger stands in.
        // While it exits, it stays on the trigger it closed from.
        if (Item.IsOpen || (Item.Menu.ActiveItem is null && !Item.Menu.State.IsMounted && Item.Menu.Items.FirstOrDefault() == Item))
        {
            Item.Menu.TriggerId = ButtonId;
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Disabled);
        attributes
            .Set("aria-expanded", Item.IsOpen ? "true" : "false")
            .Set("aria-controls", Item.IsOpen ? Item.Menu.PopupId : null)
            .Flag("data-popup-open", Item.IsOpen)
            .Set("data-blazix-nav-trigger", "")
            .Set("data-value", Item.ItemValue)
            .Set("data-blazix-roving-item", "")
            .Set("onclick", _onClick);
    }

    protected override NavigationMenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item.IsOpen, Disabled);
}

/// <summary>An icon beside the trigger's label, e.g. a chevron.</summary>
public class NavigationMenuIcon : PrimitivePart<NavigationMenuItemRenderProps>
{
    [CascadingParameter] private NavigationMenuItem Item { get; set; } = null!;

    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-popup-open", Item.IsOpen);

    protected override NavigationMenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item.IsOpen, false);
}

/// <summary>
/// An item's content. Write it inside its item; it shows in
/// the popup's <see cref="NavigationMenuViewport"/> while the item is open, marked with
/// <c>data-activation-direction</c> for transitions between items.
/// </summary>
public sealed class NavigationMenuContent : ComponentBase, IDisposable
{
    [CascadingParameter] private NavigationMenuItem Item { get; set; } = null!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Render your own element or component instead of the default one. Spread <c>props.Attributes</c> onto it.</summary>
    [Parameter] public RenderFragment<NavigationMenuItemRenderProps>? Render { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnParametersSet()
    {
        // The viewport renders the content: let it know once, when the content first registers.
        if (Item.Content != this)
        {
            Item.Content = this;
            Item.Menu.Rerender();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Renders nothing in place: the viewport renders it (RenderInto).
    }

    internal void RenderInto(RenderTreeBuilder builder, NavigationMenuRoot root, ContentPhase phase)
    {
        var current = phase != ContentPhase.Exiting;
        var attributes = new AttributeBuilder(AdditionalAttributes)
            .Set("data-activation-direction", root.ActivationDirection)
            .Flag("data-open", current)
            .Flag("data-closed", !current)
            .Flag("data-starting-style", phase == ContentPhase.Starting)
            .Flag("data-ending-style", phase == ContentPhase.Exiting)
            // Fading out, it leaves the flow so the popup takes the new content's size.
            .Flag("data-blazix-nav-exiting", phase == ContentPhase.Exiting)
            .Flag("inert", phase == ContentPhase.Exiting)
            .Set("style", phase == ContentPhase.Exiting ? "position:absolute;top:0;left:0" : null)
            .Build();
        if (Render is not null)
        {
            builder.AddContent(0, Render(new NavigationMenuItemRenderProps(attributes, current, false)));
            return;
        }
        builder.OpenElement(1, "div");
        builder.AddMultipleAttributes(2, attributes);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }

    public void Dispose()
    {
        if (Item.Content == this)
        {
            Item.Content = null;
        }
    }

    /// <summary>Where a content is in a switch between items.</summary>
    internal enum ContentPhase
    {
        Current,
        Starting,
        Exiting,
    }
}

/// <summary>
/// The navigation menu's popup,
/// anchored to the open item's trigger. <c>--popup-width</c> and <c>--popup-height</c> follow the active content.
/// </summary>
public class NavigationMenuPopup : AnchoredPopupPart<NavigationMenuRoot, NavigationMenuRenderProps>
{
    protected override bool DefaultFallbackAxis => true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", Root.PopupId).Set("data-orientation", Js.Orientation(Root.Orientation));
        Root.AddOpenAttributes(attributes);
    }

    protected override NavigationMenuRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.CurrentValue, Root.Orientation);
}

/// <summary>Where the open item's content renders.</summary>
public class NavigationMenuViewport : PrimitivePart<NavigationMenuRenderProps>
{
    [CascadingParameter] private NavigationMenuRoot Root { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("data-blazix-nav-viewport", "");

    private bool _settle;

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        var active = Root.ActiveItem;
        _settle = false;
        // Keyed in one sequence, so an item's content keeps its element as it goes from active to exiting.
        if (Root.ExitingItem is { Content: { } old } exiting && exiting != active)
        {
            _settle = true;
            KeyedContent.Add(builder, 3, exiting, inner => old.RenderInto(inner, Root, NavigationMenuContent.ContentPhase.Exiting));
        }
        if (active?.Content is { } content)
        {
            var starting = Root.EnteringItem == active;
            _settle |= starting;
            KeyedContent.Add(builder, 3, active, inner => content.RenderInto(inner, Root,
                starting ? NavigationMenuContent.ContentPhase.Starting : NavigationMenuContent.ContentPhase.Current));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_settle)
        {
            _settle = false;
            await Root.SettleContentAsync();
            StateHasChanged();
        }
    }

    protected override NavigationMenuRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.CurrentValue, Root.Orientation);
}

/// <summary>A link in the navigation menu. Renders an &lt;a&gt;; <see cref="Active"/> marks the current page.</summary>
public class NavigationMenuLink : PrimitivePart<NavigationMenuItemRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private NavigationMenuRoot Root { get; set; } = null!;

    /// <summary>Whether the link is the current page (<c>aria-current="page"</c>, <c>data-active</c>).</summary>
    [Parameter] public bool Active { get; set; }

    /// <summary>Close the menu when the link is followed. Defaults to false.</summary>
    [Parameter] public bool CloseOnClick { get; set; }

    protected override string Tag => "a";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            CloseOnClick ? Root.SetValueAsync(null, OpenChangeReason.ItemPress) : Task.CompletedTask);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-current", Active ? "page" : null)
        .Flag("data-active", Active)
        .Set("onclick", _onClick);

    protected override NavigationMenuItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Active, false);
}

/// <summary>An arrow pointing at the open item's trigger.</summary>
public class NavigationMenuArrow : PopupArrowPart<NavigationMenuRoot>;

/// <summary>An overlay beneath the popup.</summary>
public class NavigationMenuBackdrop : PopupBackdropPart<NavigationMenuRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class NavigationMenuPositioner : PopupPositionerPart<NavigationMenuRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class NavigationMenuPortal : PopupPortalPart<NavigationMenuRoot>;
