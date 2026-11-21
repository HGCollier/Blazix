using System.Collections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record FieldRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled, bool? Valid, bool Touched, bool Dirty, bool Filled, bool Focused)
    : RenderProps(Attributes);

/// <summary>
/// Groups all parts of a form field and manages its validation state.
/// Native constraints on the control (<c>required</c>, <c>pattern</c>, <c>min</c>...) and
/// <see cref="Validate"/> both feed its validity; errors show in <see cref="FieldError"/>.
/// </summary>
public class FieldRoot : PrimitivePart<FieldRenderProps>
{
    private FieldContext? _context;
    private FieldValidityData _validity = new(new FieldValidityState(), [], null);
    private bool _validated;
    private bool _touched;
    private bool _dirty;
    private bool _filled;
    private bool _focused;
    private object? _initialValue;
    private CancellationTokenSource? _debounce;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private FormContext? Form { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>Identifies the field when a form is submitted, and matches it to the form's <c>Errors</c>.</summary>
    [Parameter] public string? Name { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Marks the field invalid whatever its validity, e.g. from your own validation.</summary>
    [Parameter] public bool Invalid { get; set; }

    /// <summary>Custom validation: return an error message, or null when the value is valid.</summary>
    [Parameter] public Func<object?, string?>? Validate { get; set; }

    /// <summary>Asynchronous custom validation (e.g. a server check). Runs after <see cref="Validate"/>.</summary>
    [Parameter] public Func<object?, Task<string?>>? ValidateAsyncFunc { get; set; }

    /// <summary>When to validate. Defaults to the form's mode, else <see cref="ValidationMode.OnSubmit"/>.</summary>
    [Parameter] public ValidationMode? ValidationMode { get; set; }

    /// <summary>How long to wait after a change before validating, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int ValidationDebounceTime { get; set; }

    internal bool IsDisabled => Disabled || (Fieldset?.Disabled ?? false);

    internal bool IsTouched => _touched;

    internal bool IsDirty => _dirty;

    internal bool IsFilled => _filled;

    internal bool IsFocused => _focused;

    private IReadOnlyList<string> FormErrors => Name is not null && Form?.ErrorsFor(Name) is { Count: > 0 } errors ? errors : [];

    internal bool IsInvalid => Invalid || FormErrors.Count > 0 || (_validated && !_validity.State.Valid);

    internal bool? Valid
    {
        get
        {
            if (IsInvalid)
            {
                return false;
            }
            return IsDisabled || !_validated ? null : _validity.State.Valid;
        }
    }

    internal FieldValidityData ValidityData
    {
        get
        {
            var formErrors = FormErrors;
            if (formErrors.Count > 0)
            {
                return _validity with { Errors = formErrors };
            }
            return _validity;
        }
    }

    private ValidationMode Mode => ValidationMode ?? Form?.ValidationMode ?? Primitives.ValidationMode.OnSubmit;

    protected override void OnInitialized()
    {
        _context = new FieldContext(this, Ids.Next("field-control"));
        Form?.RegisterField(this);
    }

    internal void RequestRender() => StateHasChanged();

    internal object? CurrentValue => _context?.Control?.GetValue();

    internal string ControlElementId => _context!.Control?.FocusTarget ?? _context.ControlId;

    internal void ControlRegistered(bool changed)
    {
        if (!changed)
        {
            return;
        }
        _initialValue = Snapshot(_context!.Control!.GetValue());
        _filled = IsFilledValue(_initialValue);
        StateHasChanged();
    }

    internal async Task SetFocusedAsync(bool focused)
    {
        _focused = focused;
        if (!focused)
        {
            _touched = true;
            if (Mode == Primitives.ValidationMode.OnBlur)
            {
                await ValidateAsync(force: true);
                return;
            }
        }
        StateHasChanged();
    }

    internal async Task ValueChangedAsync(object? value)
    {
        _dirty = !ValuesEqual(value, _initialValue);
        _filled = IsFilledValue(value);
        if (Name is not null)
        {
            Form?.ClearErrors(Name);
        }

        var revalidate = Mode switch
        {
            Primitives.ValidationMode.OnChange => true,
            Primitives.ValidationMode.OnBlur => _touched,
            _ => Form?.Submitted ?? _validated,
        };
        if (!revalidate)
        {
            StateHasChanged();
            return;
        }

        if (ValidationDebounceTime > 0)
        {
            await DebouncedValidateAsync();
            return;
        }
        await ValidateAsync(force: true);
    }

    private async Task DebouncedValidateAsync()
    {
        if (_debounce is not null)
        {
            await _debounce.CancelAsync();
            _debounce.Dispose();
        }
        var debounce = _debounce = new CancellationTokenSource();
        StateHasChanged();
        try
        {
            await Task.Delay(ValidationDebounceTime, debounce.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await ValidateAsync(force: true);
    }

    /// <summary>Validates the control: native constraints plus <see cref="Validate"/>. Returns whether it's valid.</summary>
    internal async Task<bool> ValidateAsync(bool force)
    {
        var control = _context?.Control;
        if (control is null || (!force && _validated))
        {
            return !IsInvalid;
        }

        var value = control.GetValue();
        var message = Validate?.Invoke(value);
        if (message is null && ValidateAsyncFunc is not null)
        {
            message = await ValidateAsyncFunc(value);
        }

        FieldValidityState state;
        if (control.ValidityElementId is { } id && !IsDisabled)
        {
            await FieldContext.SetCustomValidityAsync(JsRuntime, id, message ?? "");
            state = await FieldContext.ReadValidityAsync(JsRuntime, id) ?? new FieldValidityState();
        }
        else
        {
            state = new FieldValidityState { Valid = message is null, CustomError = message is not null, ValidationMessage = message ?? "" };
        }

        IReadOnlyList<string> errors;
        if (message is not null)
        {
            errors = [message];
        }
        else
        {
            errors = state.Valid || string.IsNullOrEmpty(state.ValidationMessage) ? [] : [state.ValidationMessage];
        }

        _validity = new FieldValidityData(state, errors, value);
        _validated = true;
        StateHasChanged();
        return !IsInvalid;
    }

    private static object? Snapshot(object? value) => value is IEnumerable sequence and not string ? sequence.Cast<object?>().ToList() : value;

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is IEnumerable left and not string && b is IEnumerable right and not string)
        {
            return left.Cast<object?>().SequenceEqual(right.Cast<object?>());
        }
        return Equals(a, b);
    }

    private static bool IsFilledValue(object? value) => value switch
    {
        null => false,
        string s => s.Length > 0,
        bool b => b,
        ICollection c => c.Count > 0,
        IEnumerable e => e.Cast<object>().Any(),
        _ => true,
    };

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context!, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => _context!.AddStateAttributes(attributes);

    protected override FieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, Valid, _touched, _dirty, _filled, _focused);

    protected override void Dispose(bool disposing)
    {
        Form?.UnregisterField(this);
        _debounce?.Cancel();
        _debounce?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Base for the parts inside a field, which all carry its state attributes.</summary>
public abstract class FieldPart : PrimitivePart<FieldRenderProps>
{
    [CascadingParameter] protected FieldContext? Field { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.FieldState(Field, false);

    protected override FieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Field?.Disabled ?? false, Field?.Valid, Field?.Touched ?? false, Field?.Dirty ?? false, Field?.Filled ?? false, Field?.Focused ?? false);
}

/// <summary>An accessible label for the field's control. Renders a &lt;label&gt;.</summary>
public class FieldLabel : FieldPart
{
    protected override string Tag => "label";

    protected override void OnParametersSet() => Field?.RegisterLabel();

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        if (Field is not null)
        {
            attributes.Set("id", Field.LabelId).Set("for", Field.ControlId);
        }
    }
}

/// <summary>A paragraph with additional information about the field.</summary>
public class FieldDescription : FieldPart
{
    private string _id = "";

    protected override string Tag => "p";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("field-description"));
        Field?.RegisterDescription(_id);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id);
    }

    protected override void Dispose(bool disposing)
    {
        Field?.UnregisterDescription(_id);
        base.Dispose(disposing);
    }
}

/// <summary>
/// An error message shown when the field is invalid. Renders the
/// first error unless you give it content. <see cref="Match"/> shows it only for one failed constraint.
/// </summary>
public class FieldError : FieldPart
{
    private string _id = "";

    /// <summary>Show only when this constraint fails, e.g. <see cref="ValidityKey.ValueMissing"/>.</summary>
    [Parameter] public ValidityKey? Match { get; set; }

    /// <summary>Always render, even while the field is valid. Defaults to false.</summary>
    [Parameter] public bool AlwaysShow { get; set; }

    protected override bool IsPresent => Shown;

    private bool Shown
    {
        get
        {
            if (Field is null)
            {
                return AlwaysShow;
            }
            if (AlwaysShow)
            {
                return true;
            }
            return Match is { } key ? Field.Validity.State.Matches(key) : Field.Invalid;
        }
    }

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("field-error"));

    protected override void OnAfterRender(bool firstRender) => Field?.SetErrorShown(Shown ? _id : null);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id);
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Field?.Validity.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Field?.ErrorId == _id)
        {
            Field.SetErrorShown(null);
        }
        base.Dispose(disposing);
    }
}

/// <summary>Renders its content with the field's validity, to show custom messages.</summary>
public sealed class FieldValidity : ComponentBase
{
    [CascadingParameter] private FieldContext? Field { get; set; }

    [Parameter] public RenderFragment<FieldValidityData>? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Field is not null && ChildContent is not null)
        {
            builder.AddContent(0, ChildContent(Field.Validity));
        }
    }
}

/// <summary>
/// The field's native input control (<see cref="InputRoot"/> is the
/// same part for use without a field). Renders an &lt;input&gt; wired to the field's label,
/// description, error and validation.
/// </summary>
public class FieldControl : PrimitivePart<FieldRenderProps>
{
    private readonly Controlled<string?> _value = new(null);
    private readonly InputValue _text = new(null);
    private EventCallback<ChangeEventArgs> _onInput;
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;
    private string _id = "";

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] protected FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The input's value. Use <c>@bind-Value</c>, or leave it unset for an uncontrolled input.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>Runs before the value changes; call <c>Cancel()</c> to keep it (the input reverts).</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<string?>> OnValueChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    private bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    protected override void OnInitialized()
    {
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, HandleInputAsync);
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Field?.BlurAsync() ?? Task.CompletedTask);
    }

    protected override void OnParametersSet()
    {
        // A parent's new value shows in the input; what the user typed already does.
        if (_value.Sync(Value))
        {
            _text.Show(_value.Value);
        }
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => Field?.ControlId ?? (_id is { Length: > 0 } ? _id : Ids.Next("input")));
        Field?.RegisterControl(new FieldControlRegistration(_id, _id, () => _value.Value));
    }

    protected override Task OnAfterRenderAsync(bool firstRender) => _text.FlushAsync(JsRuntime, _id);

    private async Task HandleInputAsync(ChangeEventArgs e)
    {
        var next = e.Value?.ToString();
        var args = new ValueChangeEventArgs<string?>(next, "input-change");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            _text.Show(_value.Value);
            StateHasChanged();
            return;
        }
        _value.Value = next;
        await ValueChanged.InvokeAsync(next);
        if (Field is not null)
        {
            await Field.ValueChangedAsync(next);
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _id)
            .Default("name", Field?.Name)
            .Set("value", _text.Rendered)
            .Set("disabled", IsDisabled)
            .Set("oninput", _onInput)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .FieldState(Field, IsDisabled)
            .FieldAria(Field, labelledByLabel: false);
    }

    protected override FieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, Field?.Valid, Field?.Touched ?? false, Field?.Dirty ?? false, Field?.Filled ?? false, Field?.Focused ?? false);
}

/// <summary>
/// Groups an individual checkbox or radio with its own label and description inside a field that
/// holds a <see cref="CheckboxGroupRoot"/> or <see cref="RadioGroupRoot"/>.
/// </summary>
public class FieldItem : FieldPart
{
    private FieldContext? _item;

    [Parameter] public bool Disabled { get; set; }

    protected override void OnInitialized()
    {
        if (Field is not null)
        {
            _item = Field.CreateItem(() => Disabled);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_item is null)
        {
            BuildPart(builder);
            return;
        }
        Cascade(builder, _item, BuildPart);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.FieldState(_item ?? Field, Disabled);
}
