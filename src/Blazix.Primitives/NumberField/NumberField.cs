using JetBrains.Annotations;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record NumberFieldRenderProps(IReadOnlyDictionary<string, object> Attributes, double? Value, string InputValue, bool Scrubbing, bool Disabled, bool ReadOnly)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="NumberFieldRoot"/> to its parts.</summary>
[PublicAPI]
public sealed class NumberFieldContext
{
    internal NumberFieldContext(NumberFieldRoot root) => Root = root;

    internal NumberFieldRoot Root { get; }

    public double? Value => Root.CurrentValue;

    public string InputValue => Root.InputText;

    public bool Scrubbing => Root.Scrubbing;

    public bool Disabled => Root.IsDisabled;

    public bool ReadOnly => Root.ReadOnly;

    internal NumberFieldRenderProps Props(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Value, InputValue, Scrubbing, Disabled, ReadOnly);

    internal void AddState(AttributeBuilder attributes) => attributes
        .FieldState(Root.Field, Root.IsDisabled)
        .Flag("data-scrubbing", Scrubbing)
        .Flag("data-readonly", Root.ReadOnly)
        .Flag("data-required", Root.Required);
}

/// <summary>
/// A numeric input element with increment and decrement buttons, and a scrub area.
/// Values are formatted and parsed with .NET formats in <see cref="Culture"/>.
/// </summary>
public class NumberFieldRoot : PrimitivePart<NumberFieldRenderProps>, IAsyncDisposable
{
    private const string VisuallyHidden =
        "clip-path:inset(50%);overflow:hidden;white-space:nowrap;border:0;padding:0;width:1px;height:1px;margin:-1px;position:absolute;pointer-events:none";

    private readonly NumberFieldContext _context;
    private readonly Controlled<double?> _value = new(null);
    private readonly InputValue _display = new(null);
    private DotNetObjectReference<NumberFieldInterop>? _interop;
    private JsController? _js;
    private string _id = "";
    private string? _text;
    private string _optionsKey = "";

    public NumberFieldRoot() => _context = new NumberFieldContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] internal FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The value, or null when empty. Use <c>@bind-Value</c>, or set it once for an uncontrolled field.</summary>
    [Parameter] public double? Value { get; set; }

    [Parameter] public EventCallback<double?> ValueChanged { get; set; }

    /// <summary>Runs before the value changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<double?>> OnValueChange { get; set; }

    /// <summary>Runs when a change is committed: on blur, Enter, a step, or the end of a scrub.</summary>
    [Parameter] public EventCallback<double?> OnValueCommitted { get; set; }

    [Parameter] public double? Min { get; set; }

    [Parameter] public double? Max { get; set; }

    [Parameter] public double Step { get; set; } = 1;

    /// <summary>The step with Alt held. Defaults to 0.1.</summary>
    [Parameter] public double SmallStep { get; set; } = 0.1;

    /// <summary>The step with Shift held, and for Page Up/Down. Defaults to 10.</summary>
    [Parameter] public double LargeStep { get; set; } = 10;

    /// <summary>Round stepped values to a multiple of the step. Defaults to false.</summary>
    [Parameter] public bool SnapOnStep { get; set; }

    /// <summary>Let typed values go past Min and Max (steps still stop at them). Defaults to false.</summary>
    [Parameter] public bool AllowOutOfRange { get; set; }

    /// <summary>Change the value with the mouse wheel while the input is focused. Defaults to false.</summary>
    [Parameter] public bool AllowWheelScrub { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>A .NET numeric format string, e.g. "N2", "C" or "P0" (which edits 0.5 as 50%). Defaults to the shortest exact form.</summary>
    [Parameter] public string? Format { get; set; }

    /// <summary>The culture to format and parse in. Defaults to the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    internal double? CurrentValue => _value.Value;

    internal string InputText => _text ?? FormatValue(_value.Value);

    internal bool Scrubbing { get; private set; }

    internal bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    internal string InputId { get; private set; } = Ids.Next("number-field-input");

    private string HiddenInputId { get; } = Ids.Next("number-field-value");

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    private bool IsPercent => Format is { Length: > 0 } f && char.ToUpperInvariant(f[0]) == 'P';

    internal bool CanIncrement => !IsDisabled && !ReadOnly && (Max is null || (_value.Value ?? double.MinValue) < Max);

    internal bool CanDecrement => !IsDisabled && !ReadOnly && (Min is null || (_value.Value ?? double.MaxValue) > Min);

    internal void SetInputId(string id) => InputId = id;

    internal void Refresh() => StateHasChanged();

    internal Task Dispatch(Func<Task> work) => InvokeAsync(work);

    private string FormatValue(double? value) => value is null ? "" : value.Value.ToString(Format ?? "0.###############", EffectiveCulture);

    /// <summary>Parses typed text leniently: group separators, currency and percent signs are fine.</summary>
    private double? Parse(string text)
    {
        var format = EffectiveCulture.NumberFormat;
        var cleaned = text.Trim()
            .Replace(format.PercentSymbol, "", StringComparison.Ordinal)
            .Replace(format.CurrencySymbol, "", StringComparison.Ordinal)
            .Replace('−', '-')
            .Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }
        if (!double.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowExponent, EffectiveCulture, out var value)
            && !double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return double.NaN;
        }
        return IsPercent ? value / 100 : value;
    }

    private string AllowedChars()
    {
        var format = EffectiveCulture.NumberFormat;
        return "0123456789+-−eE " + format.NumberDecimalSeparator + format.NumberGroupSeparator + format.CurrencySymbol
            + format.PercentSymbol + format.NegativeSign + format.PositiveSign + format.CurrencyDecimalSeparator + format.CurrencyGroupSeparator;
    }

    private double Clamp(double value)
    {
        if (Min is { } min && value < min)
        {
            return min;
        }
        return Max is { } max && value > max ? max : value;
    }

    private double RoundToStep(double value, double step)
    {
        var decimals = Math.Max(Decimals(step), Decimals(Step));
        var baseline = Min ?? 0;
        var snapped = SnapOnStep ? baseline + Math.Round((value - baseline) / step) * step : value;
        return Math.Round(snapped, Math.Min(15, decimals));
    }

    private static int Decimals(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    /// <summary>What the input's value attribute renders: changes only for programmatic changes, never from typing.</summary>
    internal string? DisplayText => _display.Rendered;

    protected override void OnInitialized() => _display.Show(FormatValue(_value.Value));

    protected override void OnParametersSet()
    {
        if (_value.Sync(Value))
        {
            _text = null;
            _display.Show(FormatValue(_value.Value));
        }
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("number-field"));
        if (Field is { IsItem: false })
        {
            Field.RegisterControl(new FieldControlRegistration(InputId, HiddenInputId, () => _value.Value, InputId));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await _display.FlushAsync(JsRuntime, InputId);
        var options = new Dictionary<string, object?>
        {
            ["disabled"] = IsDisabled,
            ["readOnly"] = ReadOnly,
            ["allowWheelScrub"] = AllowWheelScrub,
            ["allowedChars"] = AllowedChars(),
            ["scrubDirection"] = ScrubDirection,
            ["pixelSensitivity"] = PixelSensitivity,
        };
        var key = string.Join(',', options.Values);
        if (_js is null)
        {
            _js = new JsController(JsRuntime, "number-field.js", "createNumberField");
            _interop = DotNetObjectReference.Create(new NumberFieldInterop(this));
            _optionsKey = key;
            await _js.EnsureAsync(_interop, _id, options);
        }
        else if (key != _optionsKey)
        {
            _optionsKey = key;
            await _js.InvokeVoidAsync("update", options);
        }
    }

    internal string ScrubDirection { get; set; } = "horizontal";

    internal int PixelSensitivity { get; set; } = 2;

    private async Task<bool> SetValueAsync(double? value, string reason, bool commit)
    {
        if (value is { } v && (!AllowOutOfRange || reason != "input-change"))
        {
            value = Clamp(v);
        }
        // Exact comparison on purpose: any difference is a change the parent should hear about.
        var changed = !Nullable.Equals(value, _value.Value);
        if (changed)
        {
            var args = new ValueChangeEventArgs<double?>(value, reason);
            await OnValueChange.InvokeAsync(args);
            if (args.Canceled)
            {
                _text = null;
                _display.Show(FormatValue(_value.Value));
                StateHasChanged();
                return false;
            }
            _value.Value = value;
            await ValueChanged.InvokeAsync(value);
            if (Field is not null)
            {
                await Field.ValueChangedAsync(value);
            }
        }
        if (commit)
        {
            _text = null;
            _display.Show(FormatValue(_value.Value));
            await OnValueCommitted.InvokeAsync(_value.Value);
        }
        StateHasChanged();
        return changed;
    }

    internal Task StepAsync(int direction, string amount, string? typed)
    {
        if (IsDisabled || ReadOnly)
        {
            return Task.CompletedTask;
        }
        var step = amount switch
        {
            "large" => LargeStep,
            "small" => SmallStep,
            _ => Step,
        };
        // Start from what's typed if it parses, else the current value, else the minimum or zero.
        var parsed = typed is null ? null : Parse(typed);
        double current;
        if (parsed is { } p && !double.IsNaN(p))
        {
            current = p;
        }
        else
        {
            var start = direction > 0 ? Min ?? 0 : Max ?? 0;
            current = _value.Value ?? start - direction * step;
        }
        return SetValueAsync(RoundToStep(current + direction * step, step), direction > 0 ? "increment-press" : "decrement-press", commit: true);
    }

    internal Task EdgeAsync(string edge)
    {
        var target = edge == "min" ? Min : Max;
        return target is null ? Task.CompletedTask : SetValueAsync(target, "keyboard", commit: true);
    }

    internal async Task InputAsync(string text)
    {
        _text = text;
        var parsed = Parse(text);
        if (parsed is double.NaN)
        {
            StateHasChanged();
            return;
        }
        await SetValueAsync(parsed, "input-change", commit: false);
        _text = text;
    }

    internal async Task CommitAsync(string? text = null)
    {
        var parsed = Parse(text ?? InputText);
        if (parsed is double.NaN)
        {
            parsed = _value.Value;
        }
        await SetValueAsync(parsed, "input-blur", commit: true);
    }

    internal Task SetScrubbingAsync(bool scrubbing)
    {
        Scrubbing = scrubbing;
        StateHasChanged();
        return scrubbing ? Task.CompletedTask : OnValueCommitted.InvokeAsync(_value.Value);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Cascade(builder, _context, BuildPart);
        // Carries the value for forms and the field's validation (required, min, max, step).
        builder.OpenElement(20, "input");
        builder.AddAttribute(21, "type", "number");
        builder.AddAttribute(22, "id", HiddenInputId);
        builder.AddAttribute(23, "name", Name ?? Field?.Name);
        builder.AddAttribute(24, "value", _value.Value?.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(25, "min", Min?.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(26, "max", Max?.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(27, "step", "any");
        builder.AddAttribute(28, "required", Required);
        builder.AddAttribute(29, "disabled", IsDisabled);
        builder.AddAttribute(30, "tabindex", "-1");
        builder.AddAttribute(31, "aria-hidden", "true");
        builder.AddAttribute(32, "style", VisuallyHidden);
        builder.CloseElement();
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", _id).Set("data-blazix-number-field", "");
        _context.AddState(attributes);
    }

    protected override NumberFieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => _context.Props(attributes);

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class NumberFieldInterop(NumberFieldRoot root)
{
    [JSInvokable]
    public Task Step(int direction, string amount, string typed) => root.Dispatch(() => root.StepAsync(direction, amount, typed));

    [JSInvokable]
    public Task Edge(string edge) => root.Dispatch(() => root.EdgeAsync(edge));

    [JSInvokable]
    public Task Commit(string text) => root.Dispatch(() => root.CommitAsync(text));

    [JSInvokable]
    public Task Scrub(bool scrubbing) => root.Dispatch(() => root.SetScrubbingAsync(scrubbing));
}

/// <summary>Base for the parts inside a number field.</summary>
public abstract class NumberFieldPart : PrimitivePart<NumberFieldRenderProps>
{
    [CascadingParameter] protected NumberFieldContext NumberField { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => NumberField.AddState(attributes);

    protected override NumberFieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => NumberField.Props(attributes);
}

/// <summary>Groups the input with the increment and decrement buttons.</summary>
public class NumberFieldGroup : NumberFieldPart
{
    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("role", "group");
    }
}

/// <summary>The native input control in the number field.</summary>
public class NumberFieldInput : NumberFieldPart
{
    private EventCallback<ChangeEventArgs> _onInput;
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    protected override void OnInitialized()
    {
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, e => NumberField.Root.InputAsync(e.Value?.ToString() ?? ""));
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => NumberField.Root.Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, async () =>
        {
            await NumberField.Root.CommitAsync();
            if (NumberField.Root.Field is not null)
            {
                await NumberField.Root.Field.BlurAsync();
            }
        });
    }

    protected override void OnParametersSet() =>
        NumberField.Root.SetInputId(ElementAttributes.IdOr(AdditionalAttributes, () => NumberField.Root.Field?.ControlId ?? NumberField.Root.InputId));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var root = NumberField.Root;
        base.BuildAttributes(attributes);
        attributes
            .Set("id", root.InputId)
            .Set("type", "text")
            .Set("inputmode", root.Min is null or < 0 ? "text" : "decimal")
            .Set("autocomplete", "off")
            .Set("autocorrect", "off")
            .Set("spellcheck", "false")
            .Default("aria-roledescription", "Number field")
            .Set("value", root.DisplayText)
            .Set("disabled", root.IsDisabled)
            .Set("readonly", root.ReadOnly)
            .Set("aria-required", root.Required ? "true" : null)
            .Set("data-blazix-number-input", "")
            .Set("oninput", _onInput)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .FieldAria(root.Field, labelledByLabel: false);
    }
}

/// <summary>A stepper button that increases the value. Hold it to repeat.</summary>
public class NumberFieldIncrement : NumberFieldPart
{
    protected override string Tag => "button";

    protected virtual bool Up => true;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var root = NumberField.Root;
        var enabled = Up ? root.CanIncrement : root.CanDecrement;
        base.BuildAttributes(attributes);
        attributes
            .Set("type", "button")
            .Set("tabindex", "-1")
            .Default("aria-label", Up ? "Increase" : "Decrease")
            .Set("aria-controls", root.InputId)
            .Set("disabled", !enabled)
            .Flag("data-disabled", !enabled)
            .Set("data-blazix-number-step", Up ? "up" : "down")
            .Set("style", "touch-action:manipulation;user-select:none");
    }
}

/// <summary>A stepper button that decreases the value. Hold it to repeat.</summary>
public class NumberFieldDecrement : NumberFieldIncrement
{
    protected override bool Up => false;
}

/// <summary>An interactive area where the user can drag to change the value.</summary>
public class NumberFieldScrubArea : NumberFieldPart
{
    /// <summary>Which way dragging changes the value. Defaults to horizontal.</summary>
    [Parameter] public Orientation Direction { get; set; } = Orientation.Horizontal;

    /// <summary>How many pixels the pointer moves per step. Defaults to 2.</summary>
    [Parameter] public int PixelSensitivity { get; set; } = 2;

    protected override string Tag => "span";

    protected override void OnParametersSet()
    {
        NumberField.Root.ScrubDirection = Js.Orientation(Direction);
        NumberField.Root.PixelSensitivity = PixelSensitivity;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes
            .Set("role", "presentation")
            .Set("data-blazix-number-scrub", "")
            .Set("style", $"touch-action:none;user-select:none;cursor:{(Direction == Orientation.Vertical ? "ns-resize" : "ew-resize")}");
    }
}

/// <summary>
/// A custom cursor shown while scrubbing: the real one
/// is hidden by pointer lock. Renders only while scrubbing, positioned at the virtual pointer.
/// </summary>
public class NumberFieldScrubAreaCursor : NumberFieldPart
{
    protected override string Tag => "span";

    protected override bool IsPresent => NumberField.Scrubbing;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("role", "presentation").Set("data-blazix-number-scrub-cursor", "");
    }
}
