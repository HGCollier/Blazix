using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record TabsRenderProps(IReadOnlyDictionary<string, object> Attributes, string? Value, Orientation Orientation, string ActivationDirection)
    : RenderProps(Attributes);

public sealed record TabsTabRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Active, bool Disabled, Orientation Orientation)
    : RenderProps(Attributes);

public sealed record TabsPanelRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Hidden, int Index, Orientation Orientation)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="TabsRoot"/>.</summary>
public sealed class TabsContext
{
    private readonly TabsRoot _root;

    internal TabsContext(TabsRoot root) => _root = root;

    /// <summary>The selected tab's value.</summary>
    public string? Value => _root.SelectedValue;

    public Orientation Orientation => _root.Orientation;

    /// <summary>"left", "right", "up", "down" or "none": where the newly selected tab is relative to the previous one.</summary>
    public string ActivationDirection => _root.ActivationDirection;

    internal List<TabsTab> Tabs { get; } = [];

    internal List<TabsPanel> Panels { get; } = [];

    internal RovingGroup? Roving { get; set; }

    internal string? TabIdFor(string value) => Tabs.Find(tab => tab.TabValue == value)?.ButtonIdValue;

    internal string? PanelIdFor(string value) => Panels.Find(panel => panel.PanelValue == value)?.PanelId;

    internal Task SelectAsync(TabsTab tab) => _root.SelectAsync(tab);
}

/// <summary>
/// Toggles between related panels on the same page. Tabs without a
/// <c>Value</c> are numbered from "0" in order; with no value set, the first enabled tab is selected.
/// </summary>
public class TabsRoot : PrimitivePart<TabsRenderProps>
{
    private readonly Controlled<string?> _value = new(null);
    private readonly TabsContext _context;

    public TabsRoot() => _context = new TabsContext(this);

    [CascadingParameter] private TextDirection? Direction { get; set; }

    /// <summary>The selected tab's value. Use <c>@bind-Value</c>, or set it once for uncontrolled tabs.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Runs before the selection changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<string?>> OnValueChange { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    internal string ActivationDirection { get; private set; } = "none";

    internal string? SelectedValue => _value.Value ?? _context.Tabs.Find(tab => !tab.Disabled)?.TabValue;

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            _value.Sync(Value);
        }
    }

    internal async Task SelectAsync(TabsTab tab)
    {
        if (tab.Disabled || tab.TabValue == SelectedValue)
        {
            return;
        }

        var args = new ValueChangeEventArgs<string?>(tab.TabValue, "trigger-press");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return;
        }

        var from = _context.Tabs.FindIndex(t => t.TabValue == SelectedValue);
        var to = _context.Tabs.IndexOf(tab);
        ActivationDirection = DirectionBetween(from, to);
        _value.Value = tab.TabValue;
        await ValueChanged.InvokeAsync(tab.TabValue);
        StateHasChanged();
    }

    private string DirectionBetween(int from, int to)
    {
        if (from < 0 || from == to)
        {
            return "none";
        }
        var forward = to > from;
        if (Orientation == Orientation.Vertical)
        {
            return forward ? "down" : "up";
        }
        var rtl = Direction == TextDirection.Rtl;
        return forward != rtl ? "right" : "left";
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-orientation", Js.Orientation(Orientation))
        .Set("data-activation-direction", ActivationDirection);

    protected override TabsRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, SelectedValue, Orientation, ActivationDirection);
}

/// <summary>Groups the individual tab buttons. One tab stop; arrow keys move between tabs.</summary>
public class TabsList : PrimitivePart<TabsRenderProps>
{
    private string _id = "";

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private TabsContext Tabs { get; set; } = null!;

    /// <summary>Whether moving focus with the arrow keys also selects the tab. Defaults to false.</summary>
    [Parameter] public bool ActivateOnFocus { get; set; }

    /// <summary>Whether arrow keys wrap from the last tab to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    internal string ListId => _id;

    protected override void OnInitialized() => Tabs.Roving = new RovingGroup(StateHasChanged, remember: false);

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("tabs-list"));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _id)
            .Set("role", "tablist")
            .Set("aria-orientation", Tabs.Orientation == Orientation.Vertical ? "vertical" : null)
            .Set("data-orientation", Js.Orientation(Tabs.Orientation))
            .Set("data-activation-direction", Tabs.ActivationDirection);
        RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Tabs.Orientation), LoopFocus, ActivateOnFocus);
    }

    protected override TabsRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Tabs.Value, Tabs.Orientation, Tabs.ActivationDirection);
}

/// <summary>An individual interactive tab button.</summary>
public class TabsTab : ButtonPart<TabsTabRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;
    private EventCallback<FocusEventArgs> _onFocus;
    private RovingItem? _item;

    [CascadingParameter] private TabsContext Tabs { get; set; } = null!;

    /// <summary>Identifies the tab and its panel. Defaults to the tab's position, from "0".</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public bool Disabled { get; set; }

    protected override string IdPrefix => "tab";

    protected override bool IsComposite => true;

    internal string TabValue => Value ?? Tabs.Tabs.IndexOf(this).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal string ButtonIdValue => ButtonId;

    private bool Active => Tabs.Value == TabValue;

    protected override void OnInitialized()
    {
        Tabs.Tabs.Add(this);
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Tabs.SelectAsync(this));
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Tabs.Roving?.Focused(_item!));
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _item ??= new RovingItem(ButtonId, () => Disabled, () => Active);
        _item.Id = ButtonId;
        Tabs.Roving?.Register(_item);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Disabled);
        attributes
            .Set("role", "tab")
            .Set("aria-selected", Active ? "true" : "false")
            .Set("aria-controls", Tabs.PanelIdFor(TabValue))
            .Flag("data-active", Active)
            .Set("data-orientation", Js.Orientation(Tabs.Orientation))
            .Set("data-activation-direction", Tabs.ActivationDirection)
            .Set("onclick", _onClick);
        Tabs.Roving?.AddItemAttributes(attributes, _item!, _onFocus);
    }

    protected override TabsTabRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Active, Disabled, Tabs.Orientation);

    protected override void Dispose(bool disposing)
    {
        Tabs.Tabs.Remove(this);
        if (_item is not null)
        {
            Tabs.Roving?.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// A visual indicator under the selected tab. Place it with
/// <c>--active-tab-left</c>, <c>--active-tab-width</c> (and top/right/bottom/height), which follow the selected tab.
/// </summary>
public class TabsIndicator : PrimitivePart<TabsRenderProps>, IAsyncDisposable
{
    private JsController? _js;
    private string _id = "";
    private string? _measured;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private TabsContext Tabs { get; set; } = null!;

    [CascadingParameter] private TabsList List { get; set; } = null!;

    protected override string Tag => "span";

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("tabs-indicator"));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _js = new JsController(JsRuntime, "tabs.js", "createIndicator");
            await _js.EnsureAsync(_id, List.ListId);
            _measured = Tabs.Value;
        }
        else if (_measured != Tabs.Value && _js is not null)
        {
            _measured = Tabs.Value;
            await _js.InvokeVoidAsync("update");
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "presentation")
        .Set("data-orientation", Js.Orientation(Tabs.Orientation))
        .Set("data-activation-direction", Tabs.ActivationDirection);

    protected override TabsRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Tabs.Value, Tabs.Orientation, Tabs.ActivationDirection);

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>A panel shown when its tab is selected.</summary>
public class TabsPanel : PrimitivePart<TabsPanelRenderProps>
{
    private string _id = "";

    [CascadingParameter] private TabsContext Tabs { get; set; } = null!;

    /// <summary>The value of the tab that shows this panel. Defaults to the panel's position, from "0".</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Keep the panel in the DOM (hidden) while its tab isn't selected. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    internal string PanelValue => Value ?? Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal string PanelId => _id;

    private int Index => Tabs.Panels.IndexOf(this);

    private bool Hidden => Tabs.Value != PanelValue;

    protected override bool IsPresent => !Hidden || KeepMounted;

    protected override void OnInitialized() => Tabs.Panels.Add(this);

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("tabs-panel"));

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "tabpanel")
        .Set("tabindex", Hidden ? "-1" : "0")
        .Set("hidden", Hidden)
        .Set("aria-labelledby", Tabs.TabIdFor(PanelValue))
        .Set("data-index", Index)
        .Flag("data-hidden", Hidden)
        .Set("data-orientation", Js.Orientation(Tabs.Orientation))
        .Set("data-activation-direction", Tabs.ActivationDirection);

    protected override TabsPanelRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Hidden, Index, Tabs.Orientation);

    protected override void Dispose(bool disposing)
    {
        Tabs.Panels.Remove(this);
        base.Dispose(disposing);
    }
}
