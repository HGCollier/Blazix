using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>When a field validates.</summary>
public enum ValidationMode
{
    /// <summary>On form submit; after the first submit, on every change too.</summary>
    OnSubmit,
    /// <summary>When the control loses focus; once touched, on every change too.</summary>
    OnBlur,
    /// <summary>On every change.</summary>
    OnChange,
}

/// <summary>Which constraint failed (the browser's ValidityState), for <see cref="FieldError.Match"/>.</summary>
public enum ValidityKey
{
    ValueMissing,
    TypeMismatch,
    PatternMismatch,
    TooLong,
    TooShort,
    RangeUnderflow,
    RangeOverflow,
    StepMismatch,
    BadInput,
    CustomError,
}

/// <summary>A control's validity as the browser reports it (constraint validation), plus custom errors.</summary>
public sealed record FieldValidityState
{
    public bool Valid { get; init; } = true;
    public bool ValueMissing { get; init; }
    public bool TypeMismatch { get; init; }
    public bool PatternMismatch { get; init; }
    public bool TooLong { get; init; }
    public bool TooShort { get; init; }
    public bool RangeUnderflow { get; init; }
    public bool RangeOverflow { get; init; }
    public bool StepMismatch { get; init; }
    public bool BadInput { get; init; }
    public bool CustomError { get; init; }
    public string ValidationMessage { get; init; } = "";

    public bool Matches(ValidityKey key) => key switch
    {
        ValidityKey.ValueMissing => ValueMissing,
        ValidityKey.TypeMismatch => TypeMismatch,
        ValidityKey.PatternMismatch => PatternMismatch,
        ValidityKey.TooLong => TooLong,
        ValidityKey.TooShort => TooShort,
        ValidityKey.RangeUnderflow => RangeUnderflow,
        ValidityKey.RangeOverflow => RangeOverflow,
        ValidityKey.StepMismatch => StepMismatch,
        ValidityKey.BadInput => BadInput,
        _ => CustomError,
    };
}

/// <summary>What <see cref="FieldValidity"/> renders with: the validity state, the errors and the value.</summary>
public sealed record FieldValidityData(FieldValidityState State, IReadOnlyList<string> Errors, object? Value)
{
    public string Error => Errors.Count > 0 ? Errors[0] : "";
}

/// <summary>
/// A control registered with its field: the element a label points at, the element the browser
/// validates, the element that takes focus when the field is invalid, and how to read its value.
/// </summary>
internal sealed record FieldControlRegistration(string ControlId, string? ValidityElementId, Func<object?> GetValue, string? FocusId = null)
{
    public string FocusTarget => FocusId ?? ControlId;
}

/// <summary>
/// Cascaded from <see cref="FieldRoot"/> to the control and parts inside it. Controls (Input,
/// Checkbox, Switch, Select...) read their id, name, disabled state and ARIA references from here
/// and report focus, blur and value changes, which drive touched, dirty, filled, focused and validation.
/// </summary>
public sealed class FieldContext
{
    private readonly FieldRoot _root;
    private readonly FieldContext? _parent;
    private readonly Func<bool>? _itemDisabled;
    private readonly List<string> _descriptionIds = [];

    internal FieldContext(FieldRoot root, string controlId)
    {
        _root = root;
        ControlId = controlId;
        LabelId = Ids.Next("field-label");
    }

    // A Field.Item: its own control and label ids, sharing the field's state and validation.
    private FieldContext(FieldContext parent, Func<bool> disabled)
    {
        _root = parent._root;
        _parent = parent;
        _itemDisabled = disabled;
        ControlId = Ids.Next("field-item-control");
        LabelId = Ids.Next("field-item-label");
    }

    internal FieldContext CreateItem(Func<bool> disabled) => new(this, disabled);

    /// <summary>Whether this is a Field.Item scope inside a field, rather than the field itself.</summary>
    internal bool IsItem => _parent is not null;

    public string? Name => _root.Name;

    public bool Disabled => _root.IsDisabled || (_itemDisabled?.Invoke() ?? false);

    public bool Touched => _root.IsTouched;

    public bool Dirty => _root.IsDirty;

    public bool Filled => _root.IsFilled;

    public bool Focused => _root.IsFocused;

    /// <summary>True, false, or null before the field has been validated (or while disabled).</summary>
    public bool? Valid => _root.Valid;

    public bool Invalid => _root.IsInvalid;

    public FieldValidityData Validity => _root.ValidityData;

    /// <summary>The id the control renders (a Field.Label's <c>for</c> points at it).</summary>
    public string ControlId { get; private set; }

    public string LabelId { get; }

    internal bool HasLabel { get; private set; }

    internal string? ErrorId { get; private set; }

    internal FieldControlRegistration? Control { get; private set; }

    /// <summary>The ids for the control's <c>aria-describedby</c>: descriptions, then the shown error.</summary>
    public string? DescribedBy
    {
        get
        {
            var errorId = ErrorId ?? _parent?.ErrorId;
            var ids = errorId is null ? _descriptionIds : [.. _descriptionIds, errorId];
            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    internal void RegisterControl(FieldControlRegistration control)
    {
        if (_parent is not null)
        {
            // An item's control (one checkbox of a group): labels point at it, but the group is what validates.
            ControlId = control.ControlId;
            Control = control;
            return;
        }
        var changed = Control?.ControlId != control.ControlId;
        Control = control;
        ControlId = control.ControlId;
        _root.ControlRegistered(changed);
    }

    internal void RegisterLabel()
    {
        if (!HasLabel)
        {
            HasLabel = true;
            _root.RequestRender();
        }
    }

    internal void RegisterDescription(string id)
    {
        if (!_descriptionIds.Contains(id))
        {
            _descriptionIds.Add(id);
            _root.RequestRender();
        }
    }

    internal void UnregisterDescription(string id)
    {
        if (_descriptionIds.Remove(id))
        {
            _root.RequestRender();
        }
    }

    internal void SetErrorShown(string? id)
    {
        if (ErrorId != id)
        {
            ErrorId = id;
            _root.RequestRender();
        }
    }

    internal Task FocusAsync() => _root.SetFocusedAsync(true);

    internal Task BlurAsync() => _root.SetFocusedAsync(false);

    internal Task ValueChangedAsync(object? value) => _root.ValueChangedAsync(value);

    /// <summary>Runs validation now, as a submit would.</summary>
    public Task ValidateAsync() => _root.ValidateAsync(force: true);

    /// <summary>Field state as data attributes: data-disabled, data-valid, data-invalid, data-touched, data-dirty, data-filled, data-focused.</summary>
    internal void AddStateAttributes(AttributeBuilder attributes)
    {
        attributes
            .Flag("data-disabled", Disabled)
            .Flag("data-valid", Valid == true)
            .Flag("data-invalid", Invalid)
            .Flag("data-touched", Touched)
            .Flag("data-dirty", Dirty)
            .Flag("data-filled", Filled)
            .Flag("data-focused", Focused);
    }

    internal static Task<FieldValidityState?> ReadValidityAsync(IJSRuntime js, string id) =>
        JsModules.InvokeAsync<FieldValidityState>(js, JsModules.Util, "validity", id);

    internal static Task SetCustomValidityAsync(IJSRuntime js, string id, string message) =>
        JsModules.InvokeAsync<object>(js, JsModules.Util, "setCustomValidity", id, message);
}

/// <summary>Field state attributes for parts that may or may not sit inside a field.</summary>
internal static class FieldStateExtensions
{
    public static AttributeBuilder FieldState(this AttributeBuilder attributes, FieldContext? field, bool disabled)
    {
        if (field is null)
        {
            return attributes.Flag("data-disabled", disabled);
        }
        field.AddStateAttributes(attributes);
        return attributes.Flag("data-disabled", disabled || field.Disabled);
    }

    /// <summary>The ARIA a control renders for its field: labelled by the label, described by descriptions and errors.</summary>
    public static AttributeBuilder FieldAria(this AttributeBuilder attributes, FieldContext? field, bool labelledByLabel)
    {
        if (field is null)
        {
            return attributes;
        }
        if (labelledByLabel && field.HasLabel)
        {
            attributes.Default("aria-labelledby", field.LabelId);
        }
        return attributes
            .Default("aria-describedby", field.DescribedBy)
            .Set("aria-invalid", field.Invalid ? "true" : null);
    }
}
