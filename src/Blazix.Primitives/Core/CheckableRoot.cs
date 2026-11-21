using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// What Checkbox, Switch and Radio share, each rendered as a &lt;span&gt; with a
/// hidden &lt;input&gt; beside it: the root takes focus and clicks and carries the role and ARIA; the
/// hidden native input carries the name, value, <c>required</c> and validity, so the control
/// submits with forms and a field's label (<c>for</c>) and validation work as with a native one.
/// The input's labels, a &lt;label for&gt; at it or one around the control, also name the root (aria-labelledby).
/// </summary>
public abstract class CheckableRoot<TRenderProps> : PrimitivePart<TRenderProps> where TRenderProps : RenderProps
{
    private const string VisuallyHidden =
        "clip-path:inset(50%);overflow:hidden;white-space:nowrap;border:0;padding:0;width:1px;height:1px;margin:-1px;position:absolute;pointer-events:none";

    private EventCallback<MouseEventArgs> _onClick;
    private EventCallback<ChangeEventArgs> _onChange;
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;
    private string _rootId = "";
    private string _inputId = "";

    // The ids of the labels of the hidden input (a <label for> at it, or one around the control), which name the root.
    // Only the browser can find them, so they're looked up after rendering, once per input id.
    private string? _labelIds;
    private string? _labelsFoundFor;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] protected FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether the user can't change it (it stays focusable). Defaults to false.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Whether it must be checked before the form submits. Defaults to false.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>The id of the hidden native input (a &lt;label for&gt; can point at it).</summary>
    [Parameter] public string? InputId { get; set; }

    protected override string Tag => "span";

    protected abstract string Role { get; }

    protected abstract string InputType { get; }

    protected abstract bool IsChecked { get; }

    /// <summary>aria-checked: "true", "false" or "mixed".</summary>
    protected virtual string AriaChecked => IsChecked ? "true" : "false";

    /// <summary>The value the hidden input submits when checked.</summary>
    protected virtual string? InputValue => null;

    protected virtual bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    protected string RootId => _rootId;

    protected string HiddenInputId => _inputId;

    protected virtual string? EffectiveName => Name ?? (Field is { IsItem: false } ? Field.Name : null);

    protected virtual bool IsReadOnly => ReadOnly;

    protected virtual bool IsRequired => Required;

    /// <summary>Whether this control registers with its field (a grouped control's group does instead).</summary>
    protected virtual bool RegistersWithField => true;

    /// <summary>Roving items (radios in a group) render their own tab stop.</summary>
    protected virtual void AddTabStop(AttributeBuilder attributes) =>
        attributes.Set("tabindex", IsDisabled ? "-1" : "0");

    /// <summary>The user toggled it: through a click, Space, or a label clicking the hidden input.</summary>
    protected abstract Task ToggleAsync(bool? checkedByInput);

    protected virtual object FieldValue => IsChecked;

    protected override void OnInitialized()
    {
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => ToggleIfAllowedAsync(null));
        _onChange = EventCallback.Factory.Create<ChangeEventArgs>(this, e => ToggleIfAllowedAsync(e.Value is bool b ? b : null));
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Field?.BlurAsync() ?? Task.CompletedTask);
    }

    protected override void OnParametersSet()
    {
        // An id you give the control names its hidden input, so <label for="..."> clicks
        // that input and toggles the control. The root gets an id of its own.
        if (_rootId.Length == 0)
        {
            _rootId = Ids.Next(Role);
        }
        var authoredId = AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("id", out var id) && id is string { Length: > 0 } s ? s : null;
        var fieldId = Field is not null && (RegistersWithField || Field.IsItem) ? Field.ControlId : null;
        _inputId = InputId ?? authoredId ?? fieldId ?? (_inputId is { Length: > 0 } ? _inputId : Ids.Next($"{Role}-input"));
        if (Field is not null && (RegistersWithField || Field.IsItem))
        {
            Field.RegisterControl(new FieldControlRegistration(_inputId, _inputId, () => FieldValue, _rootId));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JsModules.EnsureAsync(JsRuntime, JsModules.Button);
        }
        if (_labelsFoundFor != _inputId)
        {
            await FindLabelsAsync();
        }
    }

    /// <summary>Whether the author or the field names the root, so its input's labels aren't needed.</summary>
    private bool IsNamedElsewhere =>
        Field is { HasLabel: true } || HasAuthored("aria-label") || HasAuthored("aria-labelledby");

    private bool HasAuthored(string name) =>
        AdditionalAttributes?.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is not null) == true;

    private async Task FindLabelsAsync()
    {
        var inputId = _inputId;
        _labelsFoundFor = inputId;
        if (IsNamedElsewhere)
        {
            return;
        }
        var labelIds = await JsModules.LabelIdsAsync(JsRuntime, inputId) ?? "";
        if (inputId != _inputId)
        {
            return; // the input changed while the call was on its way: look again after the next render
        }
        if (labelIds != _labelIds)
        {
            _labelIds = labelIds;
            StateHasChanged();
        }
    }

    /// <summary>
    /// What names the root, a &lt;span&gt; that a &lt;label&gt; can't label: the ids of its input's labels. Until the
    /// browser has reported them (e.g. prerendered), the hidden input itself, whose labels Chromium follows it to.
    /// </summary>
    private string? LabelledBy => IsNamedElsewhere ? null : _labelIds switch
    {
        null => _inputId,
        "" => null,
        var ids => ids,
    };

    private async Task ToggleIfAllowedAsync(bool? checkedByInput)
    {
        if (IsDisabled || IsReadOnly)
        {
            if (checkedByInput is not null)
            {
                StateHasChanged(); // the browser toggled the hidden input: render it back
            }
            return;
        }
        await ToggleAsync(checkedByInput);
    }

    protected Task ReportValueAsync(object? value) =>
        Field is not null && RegistersWithField && !Field.IsItem ? Field.ValueChangedAsync(value) : Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        BuildPart(builder);
        BuildHiddenInput(builder);
    }

    protected virtual void BuildHiddenInput(RenderTreeBuilder builder)
    {
        builder.OpenElement(20, "input");
        builder.AddAttribute(21, "type", InputType);
        builder.AddAttribute(22, "id", _inputId);
        builder.AddAttribute(23, "name", EffectiveName);
        builder.AddAttribute(24, "value", InputValue);
        builder.AddAttribute(25, "checked", IsChecked);
        builder.AddAttribute(26, "disabled", IsDisabled);
        builder.AddAttribute(27, "required", IsRequired);
        builder.AddAttribute(28, "tabindex", "-1");
        builder.AddAttribute(29, "aria-hidden", "true");
        builder.AddAttribute(30, "style", VisuallyHidden);
        builder.AddAttribute(31, "onchange", _onChange);
        // A label click focuses this input; button.js hands the focus to the root.
        builder.AddAttribute(32, "data-blazix-checkable-input", _rootId);
        builder.CloseElement();
    }

    /// <summary>The root's role, ARIA, state attributes and handlers.</summary>
    protected void AddCheckableAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _rootId)
            .Set("role", Role)
            .Set("aria-checked", AriaChecked)
            .Set("aria-disabled", IsDisabled ? "true" : null)
            .Set("aria-readonly", IsReadOnly ? "true" : null)
            .Set("aria-required", IsRequired ? "true" : null)
            .Set("data-blazix-checkable", "")
            .Set("onclick", _onClick)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .FieldState(Field, IsDisabled)
            .FieldAria(Field, labelledByLabel: true)
            .Default("aria-labelledby", LabelledBy)
            .Flag("data-readonly", IsReadOnly)
            .Flag("data-required", IsRequired);
        AddTabStop(attributes);
    }
}
