using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>When a filterable menu highlights its first matching item by itself.</summary>
public enum MenuAutoHighlight
{
    /// <summary>Only the arrow keys and the pointer move the highlight.</summary>
    Off,

    /// <summary>Highlight the first match while the query isn't empty.</summary>
    Query,

    /// <summary>Highlight the first item even when the query is empty.</summary>
    Always,
}

/// <summary>The query of a filterable menu, shared by its Input, Clear and Empty parts.</summary>
public sealed class MenuFilterContext
{
    private readonly List<Action> _listeners = [];

    internal MenuFilterContext(MenuFilterProvider provider) => Provider = provider;

    internal MenuFilterProvider Provider { get; }

    /// <summary>The menu the provider applies to: the first one inside it.</summary>
    internal MenuRoot? Menu { get; set; }

    public string Value { get; private set; } = "";

    internal string? InputId { get; set; }

    internal void Initialize(string value) => Value = value;

    internal void Subscribe(Action listener) => _listeners.Add(listener);

    internal void Unsubscribe(Action listener) => _listeners.Remove(listener);

    /// <summary>The query changed: from typing (the input already shows it) or not (Clear, closing).</summary>
    internal async Task SetValueAsync(string value, string reason)
    {
        if (value == Value)
        {
            return;
        }
        Value = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener();
        }
        await Provider.ValueChangedAsync(value, reason);
    }

    internal void SyncValue(string value)
    {
        if (value == Value)
        {
            return;
        }
        Value = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener();
        }
    }
}

/// <summary>
/// Makes the menu directly inside it filterable: a
/// <see cref="MenuInput"/> in the popup filters the items by their text (or <c>Label</c>), ignoring
/// case, accents and punctuation; groups left without a match hide, and <see cref="MenuEmpty"/>
/// shows when nothing matches. Focus stays in the input while the arrow keys and the pointer move
/// the highlight, and Enter activates the highlighted item. The query clears when the menu closes.
/// </summary>
public class MenuFilterProvider : ComponentBase
{
    private readonly MenuFilterContext _context;
    private bool _initialized;

    public MenuFilterProvider() => _context = new MenuFilterContext(this);

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The query. Use with <see cref="ValueChanged"/> (or <c>@bind-Value</c>) when controlled.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Called whenever the query changes, with the reason ("input-change", "clear-press", "close").</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<string>> OnValueChange { get; set; }

    /// <summary>The query the menu starts with, when uncontrolled.</summary>
    [Parameter] public string DefaultValue { get; set; } = "";

    /// <summary>Whether filtering highlights the first matching item. Defaults to <see cref="MenuAutoHighlight.Off"/>.</summary>
    [Parameter] public MenuAutoHighlight AutoHighlight { get; set; }

    /// <summary>
    /// Leave the items alone: you render the filtered items yourself from <see cref="Value"/>
    ///. The empty state and the highlight still follow what's rendered.
    /// </summary>
    [Parameter] public bool ManualFiltering { get; set; }

    internal MenuFilterContext Context => _context;

    protected override void OnParametersSet()
    {
        if (!_initialized)
        {
            _initialized = true;
            _context.Initialize(Value ?? DefaultValue);
        }
        else if (Value is not null)
        {
            _context.SyncValue(Value);
        }
    }

    internal async Task ValueChangedAsync(string value, string reason)
    {
        await ValueChanged.InvokeAsync(value);
        await OnValueChange.InvokeAsync(new ValueChangeEventArgs<string>(value, reason));
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<MenuFilterContext>>(0);
        builder.AddComponentParameter(1, "Value", _context);
        builder.AddComponentParameter(2, "IsFixed", true);
        builder.AddComponentParameter(3, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}

/// <summary>
/// The search field of a filterable menu. Renders an
/// <c>&lt;input role="searchbox"&gt;</c>; requires <see cref="MenuFilterProvider"/> around the menu.
/// </summary>
public class MenuInput : PrimitivePart<PartRenderProps>
{
    private InputValue _value = new(null);
    private EventCallback<ChangeEventArgs> _onInput;
    private string _id = "";
    private bool _typing;
    private bool _refilter;

    [CascadingParameter] private MenuRoot Menu { get; set; } = null!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    private MenuFilterContext Filter => Menu.Filter ?? throw new InvalidOperationException("MenuInput must be inside a MenuFilterProvider's menu.");

    protected override void OnInitialized()
    {
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, HandleInputAsync);
        _value = new InputValue(Filter.Value);
        Filter.Subscribe(OnFilterChanged);
    }

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("menu-input"));
        Filter.InputId = _id;
    }

    private void OnFilterChanged()
    {
        // Typing already shows the text; anything else (Clear, closing, a controlled value) is shown here.
        if (!_typing)
        {
            _value.Show(Filter.Value);
            _refilter = true;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private async Task HandleInputAsync(ChangeEventArgs e)
    {
        _typing = true;
        try
        {
            await Filter.SetValueAsync(e.Value?.ToString() ?? "", "input-change");
        }
        finally
        {
            _typing = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Taken before awaiting: renders can overlap, and the value must reach the input before it's filtered.
        var refilter = _refilter && Menu.IsOpen;
        _refilter = false;
        if (refilter)
        {
            // The value was set from .NET: show it and filter again (the browser filters as the user types).
            _value.Flushed();
            await Menu.InvokeJsAsync("refilter", Filter.Value);
        }
        else
        {
            await _value.FlushAsync(JsRuntime, _id);
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("type", "text")
        .Set("role", "searchbox")
        .Set("autocomplete", "off")
        .Set("spellcheck", "false")
        .Set("aria-controls", Menu.ListId)
        .Set("value", _value.Rendered)
        .Flag("disabled", Disabled)
        .Flag("data-disabled", Disabled)
        .Set("data-blazix-menu-input", "")
        .Set("data-auto-highlight", Filter.Provider.AutoHighlight switch
        {
            MenuAutoHighlight.Query => "query",
            MenuAutoHighlight.Always => "always",
            _ => null,
        })
        .Flag("data-manual-filtering", Filter.Provider.ManualFiltering)
        .Set("oninput", _onInput);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Menu.Filter?.Unsubscribe(OnFilterChanged);
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Holds the menu items so other content, such as a filter input, can share the popup.
/// Renders a <c>&lt;div role="menu"&gt;</c>; the popup around it is then presentational.
/// </summary>
public class MenuList : PrimitivePart<PartRenderProps>
{
    private string _id = "";

    [CascadingParameter] private MenuRoot Menu { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("menu-list"));
        if (Menu.ListId != _id)
        {
            Menu.ListId = _id;
            Menu.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "menu")
        .Set("tabindex", "-1")
        .LabelledBy(Menu.IsContextMenu ? null : Menu.TriggerId)
        .Set("data-blazix-menu", Menu.PopupId);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);

    protected override void Dispose(bool disposing)
    {
        if (disposing && Menu.ListId == _id)
        {
            Menu.ListId = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Clears the filter query. Renders a &lt;button&gt; while the query
/// isn't empty; pressing it keeps focus in the input.
/// </summary>
public class MenuClear : PrimitivePart<PartRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private MenuRoot Menu { get; set; } = null!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "button";

    protected override bool IsPresent => Filter.Value.Length > 0;

    private MenuFilterContext Filter => Menu.Filter ?? throw new InvalidOperationException("MenuClear must be inside a MenuFilterProvider's menu.");

    protected override void OnInitialized()
    {
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);
        Filter.Subscribe(Changed);
    }

    private void Changed() => _ = InvokeAsync(StateHasChanged);

    private async Task HandleClickAsync(MouseEventArgs e)
    {
        if (Disabled)
        {
            return;
        }
        await Filter.SetValueAsync("", "clear-press");
        if (Filter.InputId is { } input)
        {
            await JsModules.InvokeAsync<object>(JsRuntime, JsModules.Util, "focusElement", input);
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("type", "button")
        .Set("tabindex", "-1")
        .Set("aria-hidden", "true")
        .Flag("disabled", Disabled)
        .Flag("data-disabled", Disabled)
        .Set("data-blazix-menu-clear", "")
        .Set("onclick", _onClick);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Menu.Filter?.Unsubscribe(Changed);
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// A message shown when no item matches the query. Renders a
/// <c>&lt;div role="status"&gt;</c>, hidden while there are matches.
/// </summary>
public class MenuEmpty : PrimitivePart<PartRenderProps>
{
    [CascadingParameter] private MenuRoot Menu { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "status")
        .Set("aria-live", "polite")
        .Set("aria-atomic", "true")
        .Flag("hidden", true)
        .Set("data-blazix-menu-empty", "")
        .Set("data-blazix-menu", Menu.PopupId);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}
