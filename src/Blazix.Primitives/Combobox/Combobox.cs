using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record ComboboxRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, IReadOnlyList<string> Value, string InputValue, bool Disabled, bool Empty)
    : RenderProps(Attributes);

public sealed record ComboboxItemRenderProps(IReadOnlyDictionary<string, object> Attributes, string Value, bool Selected, bool Highlighted, bool Disabled)
    : RenderProps(Attributes);

/// <summary>
/// An input combined with a list of predefined items to select.
/// Typing filters the items; give it <see cref="Items"/> and a <see cref="ComboboxList"/>
/// <c>Template</c>, or write <see cref="ComboboxItem"/>s yourself (non-matching ones hide).
/// Focus stays in the input; the highlighted item follows the arrow keys and the pointer.
/// </summary>
public class ComboboxRoot : PopupRootBase
{
    private const string VisuallyHidden =
        "clip-path:inset(50%);overflow:hidden;white-space:nowrap;border:0;padding:0;width:1px;height:1px;margin:-1px;position:absolute;pointer-events:none";

    private readonly Controlled<IReadOnlyList<string>> _values = new([], SequenceComparer<string>.Instance);
    private readonly Controlled<string> _input = new("");
    private readonly HashSet<ComboboxItem> _registered = [];
    private readonly Dictionary<string, string> _labels = [];
    private string _query = "";
    // While the popup animates out, the list keeps the filter it closed with,
    // so choosing an item doesn't refill the list with every item as it fades.
    private string? _closingQuery;
    private string _pendingFocus = "none";

    public ComboboxRoot() : this("combobox")
    {
    }

    protected ComboboxRoot(string prefix) : base(prefix)
    {
    }

    [CascadingParameter] internal FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The items to choose from. Filtered by what's typed; render them with <see cref="ComboboxList.Template"/>.</summary>
    [Parameter] public IEnumerable<string>? Items { get; set; }

    /// <summary>The text shown for an item (in the input and for filtering). Defaults to the item itself.</summary>
    [Parameter] public Func<string, string>? ItemToLabel { get; set; }

    /// <summary>Whether an item's label matches what's typed. Defaults to a case- and accent-insensitive "contains".</summary>
    [Parameter] public Func<string, string, bool>? Filter { get; set; }

    /// <summary>Show at most this many matching items.</summary>
    [Parameter] public int? Limit { get; set; }

    /// <summary>The selected item. Use <c>@bind-Value</c>, or set it once for an uncontrolled combobox.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The selected items, with <see cref="Multiple"/>. Use <c>@bind-Values</c>.</summary>
    [Parameter] public IReadOnlyList<string>? Values { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>?> ValuesChanged { get; set; }

    /// <summary>Runs before the selection changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<string>>> OnValueChange { get; set; }

    /// <summary>The input's text. Use <c>@bind-InputValue</c> to read or set it.</summary>
    [Parameter] public string? InputValue { get; set; }

    [Parameter] public EventCallback<string> InputValueChanged { get; set; }

    /// <summary>Whether several items can be selected (shown as chips). Defaults to false.</summary>
    [Parameter] public bool Multiple { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Highlight the first matching item as you type, so Enter picks it. Defaults to false.</summary>
    [Parameter] public bool AutoHighlight { get; set; }

    [Parameter] public bool HighlightItemOnHover { get; set; } = true;

    /// <summary>Whether clicking the input opens the list. Defaults to true (false for Autocomplete).</summary>
    [Parameter] public bool? OpenOnInputClick { get; set; }

    /// <summary>Whether the arrow keys wrap from the last item to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    /// <summary>Whether the page behind is inert and doesn't scroll while open. Defaults to false.</summary>
    [Parameter] public bool Modal { get; set; }

    /// <summary>
    /// Lay the items out in rows (<see cref="ComboboxRow"/>): the list becomes a <c>role="grid"</c>,
    /// items are grid cells, and the arrow keys move in two dimensions. Defaults to false.
    /// </summary>
    [Parameter] public bool Grid { get; set; }

    internal IReadOnlyList<string> SelectedValues => _values.Value;

    internal string CurrentInput => _input.Value;

    /// <summary>What the list is filtered by, rendered for the browser to know when a filter has landed.</summary>
    internal string Query => _query;

    internal override bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    internal string? HighlightedItemId { get; private set; }

    internal string ListId { get; } = Ids.Next("combobox-list");

    internal string? AnchorId { get; set; }

    internal string? ButtonTriggerId { get; set; }

    internal string? LabelId { get; set; }

    /// <summary>The ids of the &lt;label&gt;s of the input (found in the browser), when there's no combobox or field label.</summary>
    internal string? InputLabelIds { get; set; }

    /// <summary>The aria-label the author put on the input.</summary>
    internal string? InputAriaLabel { get; set; }

    /// <summary>
    /// Names the list: by the combobox's label, its field's, or a &lt;label&gt; of the input; else with the input's
    /// aria-label. Never by the input itself, which would name the list after what's typed in it.
    /// </summary>
    internal void AddListName(AttributeBuilder attributes)
    {
        var labelledBy = LabelId ?? (Field is { HasLabel: true } ? Field.LabelId : null) ?? InputLabelIds;
        if (labelledBy is not null)
        {
            attributes.LabelledBy(labelledBy);
        }
        else if (InputAriaLabel is not null)
        {
            attributes.Default("aria-label", InputAriaLabel);
        }
    }

    private string HiddenInputId { get; } = Ids.Next("combobox-value");

    internal InputValue InputText { get; } = new("");

    /// <summary>Autocomplete: the value is the input's text, and choosing an item fills it in.</summary>
    internal virtual bool IsAutocomplete => false;

    /// <summary>Autocomplete's inline and none modes list every item, unfiltered.</summary>
    private protected virtual bool Filters => true;

    internal virtual bool InlinePreview => false;

    protected virtual bool DefaultOpenOnInputClick => true;

    protected override string Module => "combobox.js";

    protected override string Factory => "createCombobox";

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["loopFocus"] = LoopFocus,
        ["autoHighlight"] = AutoHighlight,
        ["highlightItemOnHover"] = HighlightItemOnHover,
        ["openOnInputClick"] = OpenOnInputClick ?? DefaultOpenOnInputClick,
        ["inlinePreview"] = InlinePreview,
        ["triggerId"] = ButtonTriggerId,
        ["readOnly"] = ReadOnly,
        ["disabled"] = IsDisabled,
        ["grid"] = Grid,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["modal"] = Modal ? true : null,
        ["tab"] = "native",
        ["closeOnFocusOut"] = false,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
        ["focus"] = _pendingFocus,
    };

    /// <summary>The text shown for a value.</summary>
    public string LabelFor(string value) =>
        ItemToLabel?.Invoke(value) ?? _labels.GetValueOrDefault(value, value);

    internal void RememberLabel(string value, string label) => _labels[value] = label;

    /// <summary>Whether an item's label matches what's typed.</summary>
    internal bool Matches(string label)
    {
        var query = _closingQuery ?? _query;
        if (!Filters || query.Length == 0)
        {
            return true;
        }
        return Filter?.Invoke(label, query)
            ?? CultureInfo.CurrentCulture.CompareInfo.IndexOf(label, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    /// <summary>The items to list: <see cref="Items"/> filtered by what's typed, up to <see cref="Limit"/>.</summary>
    internal IReadOnlyList<string> FilteredItems() => FilterItems(Items);

    /// <summary>Some items (the root's, or a group's) filtered by what's typed, up to <see cref="Limit"/>.</summary>
    internal IReadOnlyList<string> FilterItems(IEnumerable<string>? items)
    {
        var matching = (items ?? []).Where(item => Matches(LabelFor(item)));
        return (Limit is { } limit ? matching.Take(limit) : matching).ToList();
    }

    private int VisibleCount => Items is not null ? FilteredItems().Count : _registered.Count(item => item.IsShown);

    /// <summary>Whether the parts last rendered the list as empty; items register after that render.</summary>
    private bool? EmptyRendered { get; set; }

    internal bool IsEmpty
    {
        get
        {
            var empty = VisibleCount == 0;
            EmptyRendered = empty;
            return empty;
        }
    }

    /// <summary>Items came or went since the parts rendered the empty state: render them again.</summary>
    internal void CheckEmpty()
    {
        if (EmptyRendered is { } shown && shown != (VisibleCount == 0))
        {
            EmptyRendered = null;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    internal void Register(ComboboxItem item) => _registered.Add(item);

    internal void Unregister(ComboboxItem item)
    {
        _registered.Remove(item);
        CheckEmpty();
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (IsAutocomplete)
        {
            if (_input.Sync(Value ?? ""))
            {
                InputText.Show(_input.Value);
                _query = _input.Value;
            }
            return;
        }

        if (Values is not null)
        {
            _values.Sync(Values);
        }
        else if (_values.Sync(Value is null ? [] : [Value]) && !Multiple && InputValue is null)
        {
            ShowInput(Value is null ? "" : LabelFor(Value));
        }
        if (InputValue is not null && _input.Sync(InputValue))
        {
            InputText.Show(_input.Value);
            _query = _input.Value;
        }
        if (Field is { IsItem: false })
        {
            Field.RegisterControl(new FieldControlRegistration(TriggerId, HiddenInputId, () => Multiple ? _values.Value : _values.Value.FirstOrDefault(), TriggerId));
        }
    }

    // Programmatic text: shows in the input, and the list shows everything until the user types.
    private void ShowInput(string text)
    {
        _input.Value = text;
        InputText.Show(text);
        _query = "";
    }

    internal override async Task OnRequestOpenAsync(string reason, string? detail)
    {
        _pendingFocus = detail ?? "none";
        // Already open (the key arrived before the browser knew): just move the highlight.
        if (IsOpen && detail is "first" or "last")
        {
            await InvokeJsAsync("highlightEdge", detail);
            return;
        }
        await RequestOpenChangeAsync(true, PopupState.ParseReason(reason));
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        if (open && ReadOnly)
        {
            return false;
        }
        var closing = !open && IsOpen;
        if (closing)
        {
            _closingQuery ??= _query;
        }
        var changed = await base.RequestOpenChangeAsync(open, reason);
        if (changed && !open)
        {
            HighlightedItemId = null;
            await SettleInputAsync();
        }
        else if (closing || changed)
        {
            // The close was canceled, or the popup reopened before it finished closing: filter by the input again.
            _closingQuery = null;
        }
        return changed;
    }

    private protected override Task OnExitCompleteAsync()
    {
        if (_closingQuery is not null)
        {
            _closingQuery = null;
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    // Closing a single combobox puts the selected item's label back in the input (or clears the
    // value when the input was emptied), so the input never shows text that isn't the value.
    private async Task SettleInputAsync()
    {
        if (IsAutocomplete || Multiple)
        {
            _query = IsAutocomplete ? _input.Value : "";
            return;
        }
        var selected = _values.Value.FirstOrDefault();
        if (_input.Value.Length == 0 && selected is not null)
        {
            await SetValuesAsync([], "input-clear");
            return;
        }
        var label = selected is null ? "" : LabelFor(selected);
        if (_input.Value != label)
        {
            ShowInput(label);
            await InputValueChanged.InvokeAsync(label);
        }
        _query = "";
    }

    internal override Task OnNotifyAsync(string name, string? value)
    {
        if (name == "highlight" && value != HighlightedItemId)
        {
            HighlightedItemId = value;
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    internal async Task TypedAsync(string text)
    {
        _input.Value = text;
        _query = text;
        await InputValueChanged.InvokeAsync(text);
        if (IsAutocomplete)
        {
            await ValueChanged.InvokeAsync(text);
            if (Field is not null)
            {
                await Field.ValueChangedAsync(text);
            }
        }
        if (!IsOpen && !ReadOnly)
        {
            _pendingFocus = "none";
            await RequestOpenChangeAsync(true, OpenChangeReason.None);
        }
        StateHasChanged();
    }

    internal async Task SelectAsync(string value)
    {
        if (IsDisabled || ReadOnly)
        {
            return;
        }
        if (IsAutocomplete)
        {
            var text = LabelFor(value);
            ShowInput(text);
            _query = text;
            await InputValueChanged.InvokeAsync(text);
            await ValueChanged.InvokeAsync(text);
            if (Field is not null)
            {
                await Field.ValueChangedAsync(text);
            }
            await RequestOpenChangeAsync(false, OpenChangeReason.ItemPress);
            return;
        }

        IReadOnlyList<string> next;
        if (Multiple)
        {
            next = _values.Value.Contains(value) ? [.. _values.Value.Where(v => v != value)] : [.. _values.Value, value];
        }
        else
        {
            next = [value];
        }
        if (!await SetValuesAsync(next, "item-press"))
        {
            return;
        }
        if (Multiple)
        {
            ShowInput("");
            await InputValueChanged.InvokeAsync("");
        }
        else
        {
            var label = LabelFor(value);
            // The list keeps showing what was typed while it closes.
            _closingQuery ??= _query;
            ShowInput(label);
            await InputValueChanged.InvokeAsync(label);
            await RequestOpenChangeAsync(false, OpenChangeReason.ItemPress);
        }
        StateHasChanged();
    }

    private async Task<bool> SetValuesAsync(IReadOnlyList<string> next, string reason)
    {
        if (SequenceComparer<string>.Instance.Equals(next, _values.Value))
        {
            return true;
        }
        var args = new ValueChangeEventArgs<IReadOnlyList<string>>(next, reason);
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }
        _values.Value = next;
        await ValuesChanged.InvokeAsync(next);
        await ValueChanged.InvokeAsync(next.FirstOrDefault());
        if (Field is not null)
        {
            await Field.ValueChangedAsync(Multiple ? next : next.FirstOrDefault());
        }
        StateHasChanged();
        return true;
    }

    internal async Task ClearAsync()
    {
        if (!IsAutocomplete)
        {
            await SetValuesAsync([], "clear-press");
        }
        ShowInput("");
        await InputValueChanged.InvokeAsync("");
        if (IsAutocomplete)
        {
            await ValueChanged.InvokeAsync("");
        }
        StateHasChanged();
    }

    internal Task RemoveAsync(string value) => SetValuesAsync([.. _values.Value.Where(v => v != value)], "chip-remove-press");

    internal ComboboxRenderProps Props(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsOpen, _values.Value, _input.Value, IsDisabled, VisibleCount == 0);

    internal void AddState(AttributeBuilder attributes) => attributes
        .FieldState(Field, IsDisabled)
        .Flag("data-popup-open", IsOpen)
        .Flag("data-readonly", ReadOnly)
        .Flag("data-required", Required)
        .Flag("data-list-empty", VisibleCount == 0);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<ComboboxRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();
        if (IsAutocomplete)
        {
            return;
        }

        // Carries the value for forms and the field's validation (required).
        var name = Name ?? Field?.Name;
        builder.OpenElement(10, "input");
        builder.AddAttribute(11, "id", HiddenInputId);
        builder.AddAttribute(12, "name", Multiple ? null : name);
        builder.AddAttribute(13, "value", Multiple ? string.Join(',', _values.Value) : _values.Value.FirstOrDefault() ?? "");
        builder.AddAttribute(14, "required", Required);
        builder.AddAttribute(15, "disabled", IsDisabled);
        builder.AddAttribute(16, "tabindex", "-1");
        builder.AddAttribute(17, "aria-hidden", "true");
        builder.AddAttribute(18, "style", VisuallyHidden);
        builder.CloseElement();
        if (Multiple && name is not null)
        {
            foreach (var value in _values.Value)
            {
                builder.OpenElement(20, "input");
                builder.SetKey(value);
                builder.AddAttribute(21, "type", "hidden");
                builder.AddAttribute(22, "name", name);
                builder.AddAttribute(23, "value", value);
                builder.CloseElement();
            }
        }
    }
}

/// <summary>Base for the parts inside a combobox that carry its state.</summary>
public abstract class ComboboxPart : PrimitivePart<ComboboxRenderProps>
{
    [CascadingParameter] protected ComboboxRoot Root { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => Root.AddState(attributes);

    protected override ComboboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Root.Props(attributes);
}

/// <summary>An accessible label for the input.</summary>
public class ComboboxLabel : ComboboxPart
{
    private string _id = "";

    protected override string Tag => "label";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("combobox-label"));
        Root.LabelId = _id;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id).Set("for", Root.TriggerId);
    }
}

/// <summary>Wraps the input and its buttons; the popup lines up with it.</summary>
public class ComboboxInputGroup : ComboboxPart
{
    private string _id = "";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("combobox-group"));
        Root.AnchorId = _id;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id).Set("role", "group");
    }
}

/// <summary>
/// The text input: <c>role="combobox"</c>. Typing filters the list
/// and opens it; arrows move the highlight; Enter chooses; Backspace in an empty input removes the last chip.
/// </summary>
public class ComboboxInput : ComboboxPart
{
    private EventCallback<ChangeEventArgs> _onInput;
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;
    private EventCallback<KeyboardEventArgs> _onKeyDown;
    private string _id = "";

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>Shown while the input is empty.</summary>
    [Parameter] public string? Placeholder { get; set; }

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    protected override void OnInitialized()
    {
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, e => Root.TypedAsync(e.Value?.ToString() ?? ""));
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Root.Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Root.Field?.BlurAsync() ?? Task.CompletedTask);
        _onKeyDown = EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
            e.Key == "Backspace" && Root is { Multiple: true, CurrentInput.Length: 0, SelectedValues.Count: > 0 }
                ? Root.RemoveAsync(Root.SelectedValues[^1])
                : Task.CompletedTask);
    }

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => Root.Field?.ControlId ?? (_id is { Length: > 0 } ? _id : Ids.Next("combobox-input")));
        Root.TriggerId = _id;
        Root.HasTrigger = true;
        Root.InputAriaLabel = AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("aria-label", out var label) && label is string { Length: > 0 } text
            ? text
            : null;
    }

    private string? _labelsFoundFor;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await Root.EnsureJsAsync();
        await Root.InputText.FlushAsync(JsRuntime, _id);
        if (_labelsFoundFor != _id)
        {
            _labelsFoundFor = _id;
            await FindLabelsAsync();
        }
    }

    // A <label for> at the input names the input, but only the browser knows it's there: the list needs its id too.
    private async Task FindLabelsAsync()
    {
        if (Root.LabelId is not null || Root.Field is { HasLabel: true })
        {
            return;
        }
        var labelIds = await JsModules.LabelIdsAsync(JsRuntime, _id);
        if (labelIds != Root.InputLabelIds)
        {
            Root.InputLabelIds = labelIds;
            Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes
            .Set("id", _id)
            .Set("type", "text")
            .Set("role", "combobox")
            .Set("aria-autocomplete", Root.InlinePreview ? "both" : "list")
            .Set("aria-haspopup", "listbox")
            .Set("aria-expanded", Root.IsOpen ? "true" : "false")
            .Set("aria-controls", Root.IsOpen ? Root.ListId : null)
            .Set("aria-readonly", Root.ReadOnly ? "true" : null)
            .Set("aria-required", Root.Required ? "true" : null)
            .LabelledBy(Root.LabelId is null && Root.Field is { HasLabel: true } ? Root.Field.LabelId : null)
            .Set("autocomplete", "off")
            .Set("autocorrect", "off")
            .Set("spellcheck", "false")
            .Set("placeholder", Placeholder)
            .Set("value", Root.InputText.Rendered)
            .Set("disabled", Root.IsDisabled)
            .Set("readonly", Root.ReadOnly)
            .Set("oninput", _onInput)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .Set("onkeydown", _onKeyDown)
            .FieldAria(Root.Field, labelledByLabel: false);
    }
}

/// <summary>A button that opens and closes the list. Not a tab stop; focus stays in the input.</summary>
public class ComboboxTrigger : ButtonPart<ComboboxRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    protected override string IdPrefix => "combobox-trigger";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            Root.IsDisabled ? Task.CompletedTask : Root.RequestOpenChangeAsync(!Root.IsOpen, OpenChangeReason.TriggerPress));

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Root.ButtonTriggerId = ButtonId;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Root.IsDisabled);
        Root.AddState(attributes);
        attributes
            .Set("tabindex", "-1")
            .Set("aria-haspopup", "listbox")
            .Set("aria-expanded", Root.IsOpen ? "true" : "false")
            .Set("aria-controls", Root.IsOpen ? Root.ListId : null)
            .Default("aria-label", "Show options")
            .Set("onclick", _onClick);
    }

    protected override ComboboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Root.Props(attributes);
}

/// <summary>An icon beside the input.</summary>
public class ComboboxIcon : ComboboxPart
{
    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("aria-hidden", "true");
    }
}

/// <summary>Clears the selection and the input. Marked <c>data-visible</c> when there's something to clear.</summary>
public class ComboboxClear : ButtonPart<ComboboxRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    protected override string IdPrefix => "combobox-clear";

    private bool HasValue => Root.SelectedValues.Count > 0 || Root.CurrentInput.Length > 0;

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Root.IsDisabled || Root.ReadOnly ? Task.CompletedTask : Root.ClearAsync());

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Root.IsDisabled);
        attributes
            .Set("tabindex", "-1")
            .Default("aria-label", "Clear")
            .Flag("data-visible", HasValue)
            .Set("onclick", _onClick);
    }

    protected override ComboboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Root.Props(attributes);
}

/// <summary>The selected value's label, or <see cref="Placeholder"/>.</summary>
public class ComboboxValue : ComboboxPart
{
    /// <summary>Shown while nothing is selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Renders the selected values your own way.</summary>
    [Parameter] public RenderFragment<IReadOnlyList<string>>? Template { get; set; }

    protected override string Tag => "span";

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        var values = Root.IsAutocomplete ? [Root.CurrentInput] : Root.SelectedValues;
        if (Template is not null)
        {
            builder.AddContent(3, Template(values));
        }
        else if (ChildContent is not null)
        {
            builder.AddContent(4, ChildContent);
        }
        else
        {
            builder.AddContent(5, values.Count == 0 || values is [""] ? Placeholder : string.Join(", ", values.Select(Root.LabelFor)));
        }
    }
}

/// <summary>A container for the chips of a multiple combobox. Its <see cref="Template"/> renders one chip per selected value.</summary>
public class ComboboxChips : ComboboxPart
{
    /// <summary>Renders a chip for each selected value.</summary>
    [Parameter] public RenderFragment<string>? Template { get; set; }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (Template is not null)
        {
            foreach (var value in Root.SelectedValues)
            {
                builder.OpenComponent<CascadingValue<ComboboxChipValue>>(5);
                builder.SetKey(value);
                builder.AddComponentParameter(6, "Value", new ComboboxChipValue(value));
                builder.AddComponentParameter(7, "IsFixed", true);
                builder.AddComponentParameter(8, "ChildContent", Template(value));
                builder.CloseComponent();
            }
        }
        builder.AddContent(9, ChildContent);
    }
}

/// <summary>The value a chip stands for, cascaded to its remove button.</summary>
public sealed record ComboboxChipValue(string Value);

/// <summary>A selected item shown as a chip.</summary>
public class ComboboxChip : ComboboxPart
{
    [CascadingParameter] private ComboboxChipValue? Chip { get; set; }

    /// <summary>The value this chip stands for. Defaults to the one its <see cref="ComboboxChips"/> template renders.</summary>
    [Parameter] public string? Value { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("data-value", Value ?? Chip?.Value);
    }
}

/// <summary>Removes its chip's value from the selection.</summary>
public class ComboboxChipRemove : ButtonPart<ComboboxRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    [CascadingParameter] private ComboboxChipValue? Chip { get; set; }

    [Parameter] public string? Value { get; set; }

    protected override string IdPrefix => "combobox-chip-remove";

    private string? ChipValue => Value ?? Chip?.Value;

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () =>
            ChipValue is null || Root.IsDisabled || Root.ReadOnly ? Task.CompletedTask : Root.RemoveAsync(ChipValue));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Root.IsDisabled);
        attributes
            .Set("tabindex", "-1")
            .Default("aria-label", ChipValue is null ? "Remove" : $"Remove {Root.LabelFor(ChipValue)}")
            .Set("onclick", _onClick);
    }

    protected override ComboboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Root.Props(attributes);
}

/// <summary>The combobox's popup, lined up with the input group. Marked <c>data-empty</c> when nothing matches.</summary>
public class ComboboxPopup : AnchoredPopupPart<ComboboxRoot, ComboboxRenderProps>
{
    protected override Align DefaultAlign => Primitives.Align.Start;

    protected override int DefaultSideOffset => 4;

    protected override Dictionary<string, object?> Placement()
    {
        var placement = base.Placement();
        placement["anchorId"] ??= Root.AnchorId;
        return placement;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", Root.PopupId)
            .Set("role", "presentation")
            .Set("data-blazix-query", Root.Query)
            .Flag("data-empty", Root.IsEmpty);
        Root.AddOpenAttributes(attributes);
    }

    protected override ComboboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Root.Props(attributes);
}

/// <summary>
/// The list of items: <c>role="listbox"</c>. Its <see cref="Template"/>
/// renders each item of the root's filtered <c>Items</c>.
/// </summary>
public class ComboboxList : ComboboxPart
{
    /// <summary>Renders each item of the root's <c>Items</c> that matches the input.</summary>
    [Parameter] public RenderFragment<string>? Template { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", Root.ListId)
            .Set("role", Root.Grid ? "grid" : "listbox")
            .Set("aria-multiselectable", Root.Multiple ? "true" : null)
            .Flag("data-empty", Root.IsEmpty);
        Root.AddListName(attributes);
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (Template is not null)
        {
            foreach (var item in Root.FilteredItems())
            {
                KeyedContent.Add(builder, 5, item, Template(item));
            }
        }
        builder.AddContent(6, ChildContent);
    }
}

/// <summary>State a <see cref="ComboboxItem"/> shares with its indicator.</summary>
public sealed class ComboboxItemContext
{
    internal ComboboxItemContext(ComboboxItem item) => Item = item;

    private ComboboxItem Item { get; }

    public bool Selected => Item.IsSelected;
}

/// <summary>An item in the list: <c>role="option"</c>. Hidden while it doesn't match what's typed.</summary>
public class ComboboxItem : PrimitivePart<ComboboxItemRenderProps>
{
    private readonly ComboboxItemContext _context;
    private EventCallback<MouseEventArgs> _onClick;
    private string _id = "";

    public ComboboxItem() => _context = new ComboboxItemContext(this);

    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>The text shown in the input and used for filtering. Defaults to the root's ItemToLabel, else the value.</summary>
    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    internal bool IsSelected => !Root.IsAutocomplete && Root.SelectedValues.Contains(Value);

    internal bool IsShown => Root.Matches(Label ?? Root.LabelFor(Value));

    protected override bool IsPresent => IsShown;

    protected override void OnInitialized()
    {
        Root.Register(this);
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Disabled ? Task.CompletedTask : Root.SelectAsync(Value));
    }

    protected override void OnParametersSet()
    {
        // Ids from the value, so the highlight (aria-activedescendant) survives filtering re-renders.
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => $"{Root.ListId}-{Uri.EscapeDataString(Value).Replace('%', '_')}");
        if (Label is not null)
        {
            Root.RememberLabel(Value, Label);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            Root.CheckEmpty();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", Root.Grid ? "gridcell" : "option")
        .Set("aria-selected", IsSelected ? "true" : "false")
        .Set("aria-disabled", Disabled ? "true" : null)
        .Flag("data-selected", IsSelected)
        .Flag("data-disabled", Disabled)
        .Set("data-label", Label)
        .Set("onclick", _onClick);

    protected override ComboboxItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Value, IsSelected, Root.HighlightedItemId == _id, Disabled);

    protected override void Dispose(bool disposing)
    {
        Root.Unregister(this);
        base.Dispose(disposing);
    }
}

/// <summary>Indicates whether the item is selected.</summary>
public class ComboboxItemIndicator : PrimitivePart<PartRenderProps>
{
    [CascadingParameter] private ComboboxItemContext Item { get; set; } = null!;

    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => KeepMounted || Item.Selected;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-selected", Item.Selected);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>Rendered while no item matches.</summary>
public class ComboboxEmpty : ComboboxPart
{
    protected override bool IsPresent => Root.IsEmpty;
}

/// <summary>A live region for status messages, e.g. how many items match.</summary>
public class ComboboxStatus : ComboboxPart
{
    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("role", "status").Set("aria-live", "polite").Set("aria-atomic", "true");
    }
}

/// <summary>The items a <see cref="ComboboxGroup"/> lists, for a <see cref="ComboboxCollection"/> inside it.</summary>
public sealed class ComboboxGroupItems
{
    internal ComboboxGroupItems(ComboboxGroup group) => Group = group;

    internal ComboboxGroup Group { get; }
}

/// <summary>
/// Groups related items with a label. Give it <see cref="Items"/>
/// and a <see cref="ComboboxCollection"/> renders the ones that match what's typed; a group left
/// without matches isn't rendered.
/// </summary>
public class ComboboxGroup : PrimitivePart<PartRenderProps>
{
    private readonly MenuGroupContext _group;
    private readonly ComboboxGroupItems _items;

    public ComboboxGroup()
    {
        _group = new MenuGroupContext(StateHasChanged);
        _items = new ComboboxGroupItems(this);
    }

    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    /// <summary>This group's items, for a <see cref="ComboboxCollection"/> inside it.</summary>
    [Parameter] public IEnumerable<string>? Items { get; set; }

    internal IReadOnlyList<string> FilteredItems() => Root.FilterItems(Items);

    protected override bool IsPresent => Items is null || FilteredItems().Count > 0;

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, _items, inner => Cascade(inner, _group, BuildPart, isFixed: true), isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "group")
        .LabelledBy(_group.HasLabel ? _group.LabelId : null);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>
/// Renders the items that match what's typed: the enclosing
/// group's <see cref="ComboboxGroup.Items"/>, or else the root's <c>Items</c>. Renders no element.
/// For a flat list, <see cref="ComboboxList.Template"/> does the same.
/// </summary>
public class ComboboxCollection : ComponentBase
{
    [CascadingParameter] private ComboboxRoot Root { get; set; } = null!;

    [CascadingParameter] private ComboboxGroupItems? Group { get; set; }

    /// <summary>Renders each item.</summary>
    [Parameter, EditorRequired] public RenderFragment<string> ChildContent { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var items = Group?.Group.Items is not null ? Group.Group.FilteredItems() : Root.FilteredItems();
        foreach (var item in items)
        {
            KeyedContent.Add(builder, 0, item, ChildContent(item));
        }
    }
}

/// <summary>
/// A row of items in a grid list: <c>role="row"</c>. Set
/// <see cref="ComboboxRoot.Grid"/> on the root.
/// </summary>
public class ComboboxRow : PrimitivePart<PartRenderProps>
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("role", "row");

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

#pragma warning disable S2094 // Aliases: the same parts as Select's.
/// <summary>A group's label.</summary>
public class ComboboxGroupLabel : SelectGroupLabel;

/// <summary>A separator between items.</summary>
public class ComboboxSeparator : SelectSeparator;
#pragma warning restore S2094

/// <summary>An arrow pointing at the input.</summary>
public class ComboboxArrow : PopupArrowPart<ComboboxRoot>;

/// <summary>An overlay beneath the popup.</summary>
public class ComboboxBackdrop : PopupBackdropPart<ComboboxRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class ComboboxPositioner : PopupPositionerPart<ComboboxRoot>;

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class ComboboxPortal : PopupPortalPart<ComboboxRoot>;
