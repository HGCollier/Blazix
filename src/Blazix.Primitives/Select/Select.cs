using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record SelectRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, IReadOnlyList<string> Value, bool Disabled, bool Placeholder)
    : RenderProps(Attributes);

public sealed record SelectItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Selected, bool Highlighted, bool Disabled)
    : RenderProps(Attributes);

/// <summary>An option known to a select before its popup renders, for showing the selected label and typeahead while closed.</summary>
public sealed record SelectOption(string Value, string Label);

/// <summary>
/// A common form component for choosing a predefined value in a dropdown menu.
/// Bind <see cref="PopupRootBase.Open"/>, and <see cref="Value"/> (or
/// <see cref="Values"/> with <see cref="Multiple"/>). Modal by default, and the popup lines the
/// selected option up with the trigger.
/// </summary>
public class SelectRoot() : PopupRootBase("select")
{
    private const string VisuallyHidden =
        "clip-path:inset(50%);overflow:hidden;white-space:nowrap;border:0;padding:0;width:1px;height:1px;margin:-1px;position:absolute;pointer-events:none";

    private readonly Controlled<IReadOnlyList<string>> _values = new([], SequenceComparer<string>.Instance);
    private readonly Dictionary<string, string> _labels = [];

    [CascadingParameter] internal FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The selected option's value. Use <c>@bind-Value</c>, or set it once for an uncontrolled select.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The selected options' values, with <see cref="Multiple"/>. Use <c>@bind-Values</c>.</summary>
    [Parameter] public IReadOnlyList<string>? Values { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>?> ValuesChanged { get; set; }

    /// <summary>Runs before the selection changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<string>>> OnValueChange { get; set; }

    /// <summary>Whether several options can be selected (the popup stays open while choosing). Defaults to false.</summary>
    [Parameter] public bool Multiple { get; set; }

    /// <summary>
    /// The options, so the selected label shows and typeahead works before the popup has rendered.
    /// Optional: options that have rendered are remembered too.
    /// </summary>
    [Parameter] public IEnumerable<SelectOption>? Items { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Whether the page behind is inert and doesn't scroll while open. Defaults to true.</summary>
    [Parameter] public bool Modal { get; set; } = true;

    /// <summary>Whether the selected option lines up with the trigger (falling back to below it). Defaults to true.</summary>
    [Parameter] public bool AlignItemWithTrigger { get; set; } = true;

    /// <summary>Set by a <see cref="SelectPositioner"/>'s <c>AlignItemWithTrigger</c>, which wins.</summary>
    internal bool? PositionerAlignItemWithTrigger { get; set; }

    /// <summary>Whether arrow keys wrap from the last option to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    [Parameter] public bool HighlightItemOnHover { get; set; } = true;

    internal IReadOnlyList<string> SelectedValues => _values.Value;

    internal override bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    internal string? HighlightedItemId { get; private set; }

    internal string? LabelId { get; set; }

    /// <summary>The ids of the &lt;label&gt;s pointing at the trigger (found in the browser), when there's no select or field label.</summary>
    internal string? TriggerLabelIds { get; set; }

    /// <summary>The aria-label the author put on the trigger.</summary>
    internal string? TriggerAriaLabel { get; set; }

    /// <summary>
    /// Names the list of options: by the select's label, its field's, or a &lt;label&gt; of the trigger; else with the
    /// trigger's aria-label; else by the trigger. Pointing aria-labelledby at the trigger names the list after the
    /// trigger's value (the selected option), not its label, so that's the last resort.
    /// </summary>
    internal void AddListName(AttributeBuilder attributes)
    {
        var labelledBy = LabelId ?? (Field is { HasLabel: true } ? Field.LabelId : null) ?? TriggerLabelIds;
        if (labelledBy is null && TriggerAriaLabel is not null)
        {
            attributes.Default("aria-label", TriggerAriaLabel);
            return;
        }
        attributes.LabelledBy(labelledBy ?? TriggerId);
    }

    internal bool HasList { get; set; }

    private string HiddenInputId { get; } = Ids.Next("select-value");

    private string PendingFocus { get; set; } = "selected";

    protected override string Module => "select.js";

    protected override string Factory => "createSelect";

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["loopFocus"] = LoopFocus,
        ["highlightItemOnHover"] = HighlightItemOnHover,
    };

    internal override Dictionary<string, object?> BehaviorOptions() => new()
    {
        ["modal"] = Modal ? true : null,
        ["tab"] = "close",
        ["closeOnFocusOut"] = true,
        ["dismissible"] = true,
        ["keepTrigger"] = true,
        ["focus"] = PendingFocus,
        ["alignItemWithTrigger"] = (PositionerAlignItemWithTrigger ?? AlignItemWithTrigger) && !Multiple,
    };

    /// <summary>The label for a value: from <see cref="Items"/>, an option that has rendered, or the value itself.</summary>
    public string LabelFor(string value) =>
        Items?.FirstOrDefault(item => item.Value == value)?.Label ?? _labels.GetValueOrDefault(value, value);

    internal void RememberLabel(string value, string label) => _labels[value] = label;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (Values is not null)
        {
            _values.Sync(Values);
        }
        else
        {
            _values.Sync(Value is null ? [] : [Value]);
        }
        if (!IsOpen)
        {
            HighlightedItemId = null;
        }
        if (Field is { IsItem: false })
        {
            Field.RegisterControl(new FieldControlRegistration(TriggerId, HiddenInputId, () => Multiple ? _values.Value : _values.Value.FirstOrDefault(), TriggerId));
        }
    }

    internal override Task OnRequestOpenAsync(string reason, string? detail)
    {
        PendingFocus = detail ?? "selected";
        return RequestOpenChangeAsync(true, PopupState.ParseReason(reason));
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        if (open && ReadOnly)
        {
            return false;
        }
        if (reason == OpenChangeReason.TriggerPress)
        {
            PendingFocus = "selected";
        }
        var changed = await base.RequestOpenChangeAsync(open, reason);
        if (changed && !open)
        {
            HighlightedItemId = null;
            if (Field is not null)
            {
                await Field.BlurAsync();
            }
        }
        return changed;
    }

    internal override async Task OnNotifyAsync(string name, string? value)
    {
        if (name == "highlight" && IsOpen && value != HighlightedItemId)
        {
            HighlightedItemId = value;
            StateHasChanged();
        }
        else if (name == "typeahead" && value is { Length: 1 } && !Multiple)
        {
            await TypeaheadAsync(value);
        }
    }

    // Closed, typing picks the next option whose label starts with the character, like a native select.
    private Task TypeaheadAsync(string key)
    {
        var options = (Items?.Select(item => (item.Value, item.Label)) ?? _labels.Select(pair => (pair.Key, pair.Value))).ToList();
        if (options.Count == 0)
        {
            return Task.CompletedTask;
        }
        var current = options.FindIndex(option => _values.Value.Contains(option.Item1));
        for (var step = 1; step <= options.Count; step++)
        {
            var option = options[(current + step + options.Count) % options.Count];
            if (option.Item2.StartsWith(key, StringComparison.CurrentCultureIgnoreCase))
            {
                return SelectAsync(option.Item1, "keyboard");
            }
        }
        return Task.CompletedTask;
    }

    internal async Task SelectAsync(string value, string reason = "item-press")
    {
        if (IsDisabled || ReadOnly)
        {
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

        if (!SequenceComparer<string>.Instance.Equals(next, _values.Value))
        {
            var args = new ValueChangeEventArgs<IReadOnlyList<string>>(next, reason);
            await OnValueChange.InvokeAsync(args);
            if (args.Canceled)
            {
                return;
            }
            _values.Value = next;
            await ValuesChanged.InvokeAsync(next);
            await ValueChanged.InvokeAsync(next.FirstOrDefault());
            if (Field is not null)
            {
                await Field.ValueChangedAsync(Multiple ? next : next.FirstOrDefault());
            }
        }
        if (!Multiple && IsOpen)
        {
            await RequestOpenChangeAsync(false, OpenChangeReason.ItemPress);
        }
        StateHasChanged();
    }

    internal void AddState(AttributeBuilder attributes) => attributes
        .FieldState(Field, IsDisabled)
        .Flag("data-readonly", ReadOnly)
        .Flag("data-required", Required)
        .Flag("data-placeholder", _values.Value.Count == 0);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<SelectRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", BodyContent);
        builder.CloseComponent();

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

/// <summary>An accessible label for the select. Pressing it focuses the trigger.</summary>
public class SelectLabel : PrimitivePart<SelectRenderProps>
{
    private string _id = "";

    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    protected override string Tag => "label";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("select-label"));
        if (Root.LabelId != _id)
        {
            Root.LabelId = _id;
            Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", _id).Set("for", Root.TriggerId);
        Root.AddState(attributes);
    }

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);
}

/// <summary>A button that opens the select popup: <c>role="combobox"</c> with <c>aria-haspopup="listbox"</c>.</summary>
public class SelectTrigger : PopupTriggerPart<SelectRoot>
{
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;

    protected override string HasPopup => "listbox";

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Root.Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Root.IsOpen ? Task.CompletedTask : Root.Field?.BlurAsync() ?? Task.CompletedTask);
    }

    // Inside a field, the trigger takes the field's control id, so its label points at it.
    protected override string? PreferredId => HasRoot ? Root.Field?.ControlId : null;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    private string? _labelsFoundFor;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (HasRoot)
        {
            Root.TriggerAriaLabel = AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("aria-label", out var label) && label is string { Length: > 0 } text
                ? text
                : null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (!HasRoot || _labelsFoundFor == ButtonId)
        {
            return;
        }
        _labelsFoundFor = ButtonId;
        if (Root.LabelId is not null || Root.Field is { HasLabel: true })
        {
            return;
        }
        // A <label for> at the trigger names the trigger, but only the browser knows it's there: the list needs its id.
        var labelIds = await JsModules.LabelIdsAsync(JsRuntime, ButtonId);
        if (labelIds != Root.TriggerLabelIds)
        {
            Root.TriggerLabelIds = labelIds;
            Root.Rerender();
            StateHasChanged();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes
            .Set("role", "combobox")
            .Set("tabindex", IsDisabled ? "-1" : "0")
            .LabelledBy(Root.LabelId ?? (Root.Field is { HasLabel: true } ? Root.Field.LabelId : null) ?? Root.TriggerLabelIds)
            .Set("aria-readonly", Root.ReadOnly ? "true" : null)
            .Set("aria-required", Root.Required ? "true" : null)
            .Set("aria-pressed", null)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .FieldAria(Root.Field, labelledByLabel: false);
        Root.AddState(attributes);
    }
}

/// <summary>
/// The text label of the selected option, or <see cref="Placeholder"/>
/// when nothing is selected. Give it content to render the value your own way.
/// </summary>
public class SelectValue : PrimitivePart<SelectRenderProps>
{
    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    /// <summary>Shown when no option is selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Renders the selected values your own way.</summary>
    [Parameter] public RenderFragment<IReadOnlyList<string>>? Template { get; set; }

    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("data-blazix-select-value", "");
        Root.AddState(attributes);
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        var values = Root.SelectedValues;
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
            builder.AddContent(5, values.Count == 0 ? Placeholder : string.Join(", ", values.Select(Root.LabelFor)));
        }
    }

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);
}

/// <summary>An icon that indicates the trigger opens a popup.</summary>
public class SelectIcon : PrimitivePart<SelectRenderProps>
{
    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-popup-open", Root.IsOpen);

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);
}

/// <summary>
/// The select's popup: a listbox, unless a
/// <see cref="SelectList"/> inside it is the listbox. With <c>AlignItemWithTrigger</c>, it renders
/// <c>data-side="none"</c> when the selected option is lined up over the trigger.
/// </summary>
public class SelectPopup : AnchoredPopupPart<SelectRoot, SelectRenderProps>
{
    protected override Align DefaultAlign => Primitives.Align.Start;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", Root.PopupId)
            .Set("tabindex", "-1")
            .Set("role", Root.HasList ? "presentation" : "listbox")
            .Set("aria-multiselectable", Root is { HasList: false, Multiple: true } ? "true" : null)
            .Set("aria-readonly", Root is { HasList: false, ReadOnly: true } ? "true" : null);
        if (!Root.HasList)
        {
            Root.AddListName(attributes);
        }
        Root.AddOpenAttributes(attributes);
    }

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);
}

/// <summary>A container for the options, when the popup also holds other things like scroll arrows.</summary>
public class SelectList : PrimitivePart<SelectRenderProps>
{
    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    protected override void OnInitialized()
    {
        Root.HasList = true;
        Root.Rerender();
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("role", "listbox")
            .Set("aria-multiselectable", Root.Multiple ? "true" : null)
            .Set("aria-readonly", Root.ReadOnly ? "true" : null)
            .Set("style", "overflow-x:hidden;overflow-y:auto");
        Root.AddListName(attributes);
    }

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);

    protected override void Dispose(bool disposing)
    {
        Root.HasList = false;
        base.Dispose(disposing);
    }
}

/// <summary>State a <see cref="SelectItem"/> shares with its text and indicator.</summary>
public sealed class SelectItemContext
{
    internal SelectItemContext(SelectItem item) => Item = item;

    private SelectItem Item { get; }

    public bool Selected => Item.IsSelected;
}

/// <summary>An option in the select popup: <c>role="option"</c>.</summary>
public class SelectItem : PrimitivePart<SelectItemRenderProps>
{
    private readonly SelectItemContext _context;
    private EventCallback<MouseEventArgs> _onClick;
    private string _id = "";

    public SelectItem() => _context = new SelectItemContext(this);

    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    /// <summary>The value this option selects.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>The text shown in the trigger and used for typeahead. Defaults to the value.</summary>
    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    internal bool IsSelected => Root.SelectedValues.Contains(Value);

    private bool IsDisabled => Disabled || Root.IsDisabled;

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => IsDisabled ? Task.CompletedTask : Root.SelectAsync(Value));

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("select-item"));
        Root.RememberLabel(Value, Label ?? Value);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "option")
        .Set("tabindex", "-1")
        .Set("aria-selected", IsSelected ? "true" : "false")
        .Set("aria-disabled", IsDisabled ? "true" : null)
        .Flag("data-selected", IsSelected)
        .Flag("data-disabled", IsDisabled)
        .Set("data-label", Label)
        .Set("onclick", _onClick);

    protected override SelectItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsSelected, Root.HighlightedItemId == _id, IsDisabled);
}

/// <summary>An option's text: what lines up with the trigger's value.</summary>
public class SelectItemText : PrimitivePart<SelectItemRenderProps>
{
    [CascadingParameter] private SelectItemContext Item { get; set; } = null!;

    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("data-blazix-select-item-text", "");

    protected override SelectItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Item.Selected, false, false);
}

/// <summary>Indicates whether the option is selected. Renders while selected.</summary>
public class SelectItemIndicator : PrimitivePart<SelectItemRenderProps>
{
    [CascadingParameter] private SelectItemContext Item { get; set; } = null!;

    /// <summary>Render while unselected too (e.g. to animate it out). Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => KeepMounted || Item.Selected;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Flag("data-selected", Item.Selected);

    protected override SelectItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Item.Selected, false, false);
}

/// <summary>Groups related options with a label.</summary>
public class SelectGroup : PrimitivePart<SelectRenderProps>
{
    private readonly MenuGroupContext _group;

    [CascadingParameter] private SelectRoot Root { get; set; } = null!;

    public SelectGroup() => _group = new MenuGroupContext(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _group, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "group")
        .LabelledBy(_group.HasLabel ? _group.LabelId : null);

    protected override SelectRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Root.IsOpen, Root.SelectedValues, Root.IsDisabled, Root.SelectedValues.Count == 0);
}

/// <summary>A group's label.</summary>
public class SelectGroupLabel : PrimitivePart<PartRenderProps>
{
    [CascadingParameter] private MenuGroupContext? Group { get; set; }

    protected override void OnInitialized() => Group?.RegisterLabel();

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", Group?.LabelId);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>A separator between options. Decorative (aria-hidden): a listbox holds only options and groups.</summary>
public class SelectSeparator : SimplePart
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true");
}

/// <summary>Scrolls the list up while the pointer rests on it. Shown only when the list can scroll up.</summary>
public class SelectScrollUpArrow : SimplePart
{
    protected virtual string Direction => "up";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Set("data-direction", Direction)
        .Set("data-blazix-select-scroll", Direction)
        .Set("hidden", true);
}

/// <summary>Scrolls the list down while the pointer rests on it. Shown only when the list can scroll down.</summary>
public class SelectScrollDownArrow : SelectScrollUpArrow
{
    protected override string Direction => "down";
}

/// <summary>An arrow pointing at the trigger.</summary>
public class SelectArrow : PopupArrowPart<SelectRoot>;

/// <summary>An overlay beneath the select popup.</summary>
public class SelectBackdrop : PopupBackdropPart<SelectRoot>;

/// <summary>Positions the popup against its anchor. Its placement parameters and attributes apply to the popup inside it.</summary>
public class SelectPositioner : PopupPositionerPart<SelectRoot>
{
    /// <summary>
    /// Line the selected option up over the trigger, like a native select, instead of opening below
    /// it (when there's room, and not with Multiple). Defaults to the root's <c>AlignItemWithTrigger</c>.
    /// </summary>
    [Parameter] public bool? AlignItemWithTrigger { get; set; }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Root.PositionerAlignItemWithTrigger = AlignItemWithTrigger;
    }
}

/// <summary>A portal for the popup. Popups render in the top layer, so this renders its content in place; <c>KeepMounted</c> keeps the popup in the DOM while closed.</summary>
public class SelectPortal : PopupPortalPart<SelectRoot>;
