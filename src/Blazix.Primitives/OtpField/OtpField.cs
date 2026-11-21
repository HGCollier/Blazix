using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>Which characters an OTP field accepts.</summary>
public enum OtpValidationType
{
    Numeric,
    Alpha,
    Alphanumeric,
    None,
}

public sealed record OtpFieldRenderProps(IReadOnlyDictionary<string, object> Attributes, string Value, bool Complete, bool Disabled)
    : RenderProps(Attributes);

public sealed record OtpFieldInputRenderProps(IReadOnlyDictionary<string, object> Attributes, int Index, string Character, bool Filled, bool Complete, bool Disabled)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="OtpFieldRoot"/> to its inputs.</summary>
public sealed class OtpFieldContext
{
    internal OtpFieldContext(OtpFieldRoot root) => Root = root;

    internal OtpFieldRoot Root { get; }

    public string Value => Root.CurrentValue;

    public bool Complete => Root.CurrentValue.Length == Root.Length;

    public bool Disabled => Root.IsDisabled;

    internal List<OtpFieldInput> Inputs { get; } = [];
}

/// <summary>
/// A one-time-code input split into one box per character. Typing
/// moves to the next box, Backspace goes back, and a paste or the browser's one-time-code autofill
/// fills them all.
/// </summary>
public class OtpFieldRoot : PrimitivePart<OtpFieldRenderProps>, IAsyncDisposable
{
    private readonly OtpFieldContext _context;
    private readonly Controlled<string> _value = new("");
    private DotNetObjectReference<OtpFieldInterop>? _interop;
    private JsController? _js;
    private string _id = "";
    private string _optionsKey = "";

    public OtpFieldRoot() => _context = new OtpFieldContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] internal FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>How many characters the code has.</summary>
    [Parameter, EditorRequired] public int Length { get; set; } = 6;

    /// <summary>The code typed so far. Use <c>@bind-Value</c>, or leave unset for an uncontrolled field.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    [Parameter] public EventCallback<ValueChangeEventArgs<string>> OnValueChange { get; set; }

    /// <summary>Runs when every box is filled.</summary>
    [Parameter] public EventCallback<string> OnValueComplete { get; set; }

    /// <summary>Submit the form as soon as the code is complete. Defaults to false.</summary>
    [Parameter] public bool AutoSubmit { get; set; }

    /// <summary>Hide the characters, like a password. Defaults to false.</summary>
    [Parameter] public bool Mask { get; set; }

    /// <summary>Which characters are accepted. Defaults to numeric.</summary>
    [Parameter] public OtpValidationType ValidationType { get; set; } = OtpValidationType.Numeric;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>The first box's autocomplete hint. Defaults to "one-time-code", so browsers offer codes from SMS or email.</summary>
    [Parameter] public string AutoComplete { get; set; } = "one-time-code";

    internal string CurrentValue => _value.Value;

    internal bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    private string HiddenInputId { get; } = Ids.Next("otp-value");

    internal string InputMode => ValidationType == OtpValidationType.Numeric ? "numeric" : "text";

    private string Pattern => ValidationType switch
    {
        OtpValidationType.Numeric => "^[0-9]$",
        OtpValidationType.Alpha => "^[a-zA-Z]$",
        OtpValidationType.Alphanumeric => "^[a-zA-Z0-9]$",
        _ => "",
    };

    internal Task Dispatch(Func<Task> work) => InvokeAsync(work);

    private string Sanitize(string value)
    {
        var chars = value.Where(c => !char.IsWhiteSpace(c) && ValidationType switch
        {
            OtpValidationType.Numeric => char.IsAsciiDigit(c),
            OtpValidationType.Alpha => char.IsAsciiLetter(c),
            OtpValidationType.Alphanumeric => char.IsAsciiLetterOrDigit(c),
            _ => true,
        });
        return new string(chars.Take(Length).ToArray());
    }

    protected override void OnParametersSet()
    {
        _value.Sync(Sanitize(Value ?? ""));
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("otp-field"));
        if (Field is { IsItem: false })
        {
            var first = _context.Inputs.FirstOrDefault()?.InputId ?? HiddenInputId;
            Field.RegisterControl(new FieldControlRegistration(first, HiddenInputId, () => _value.Value, first));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var options = new Dictionary<string, object?>
        {
            ["length"] = Length,
            ["pattern"] = Pattern,
            ["disabled"] = IsDisabled,
            ["readOnly"] = ReadOnly,
        };
        var key = string.Join(',', options.Values);
        if (_js is null)
        {
            _js = new JsController(JsRuntime, "otp-field.js", "createOtpField");
            _interop = DotNetObjectReference.Create(new OtpFieldInterop(this));
            _optionsKey = key;
            await _js.EnsureAsync(_interop, _id, options);
        }
        else if (key != _optionsKey)
        {
            _optionsKey = key;
            await _js.InvokeVoidAsync("update", options);
        }
    }

    internal async Task SetValueAsync(string value, string reason)
    {
        var next = Sanitize(value);
        var wasComplete = _value.Value.Length == Length;
        if (next != _value.Value)
        {
            var args = new ValueChangeEventArgs<string>(next, reason);
            await OnValueChange.InvokeAsync(args);
            if (args.Canceled)
            {
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
        StateHasChanged();
        if (next.Length == Length && (!wasComplete || reason == "input-paste"))
        {
            await OnValueComplete.InvokeAsync(next);
            if (AutoSubmit && _js is not null)
            {
                await _js.InvokeVoidAsync("submit");
            }
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Cascade(builder, _context, BuildPart);
        builder.OpenElement(20, "input");
        builder.AddAttribute(21, "type", "hidden");
        builder.AddAttribute(22, "id", HiddenInputId);
        builder.AddAttribute(23, "name", Name ?? Field?.Name);
        builder.AddAttribute(24, "value", _value.Value);
        builder.CloseElement();
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "group")
        .Set("data-blazix-otp-field", "")
        .Flag("data-complete", _value.Value.Length == Length)
        .Flag("data-readonly", ReadOnly)
        .Flag("data-required", Required)
        .FieldState(Field, IsDisabled)
        .Default("aria-labelledby", Field is { HasLabel: true } ? Field.LabelId : null)
        .Default("aria-describedby", Field?.DescribedBy);

    protected override OtpFieldRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _value.Value, _value.Value.Length == Length, IsDisabled);

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

internal sealed class OtpFieldInterop(OtpFieldRoot root)
{
    [JSInvokable]
    public Task SetValue(string value, string reason) => root.Dispatch(() => root.SetValueAsync(value, reason));
}

/// <summary>One box of the code. Renders an &lt;input&gt; holding one character.</summary>
public class OtpFieldInput : PrimitivePart<OtpFieldInputRenderProps>
{
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;

    [CascadingParameter] private OtpFieldContext Otp { get; set; } = null!;

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    internal string InputId { get; private set; } = Ids.Next("otp-input");

    private int Index => Otp.Inputs.IndexOf(this);

    private string Character => Index >= 0 && Index < Otp.Value.Length ? Otp.Value[Index].ToString() : "";

    protected override void OnInitialized()
    {
        Otp.Inputs.Add(this);
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Otp.Root.Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Otp.Root.Field?.BlurAsync() ?? Task.CompletedTask);
    }

    protected override void OnParametersSet()
    {
        var fieldId = Index == 0 ? Otp.Root.Field?.ControlId : null;
        InputId = ElementAttributes.IdOr(AdditionalAttributes, () => fieldId ?? InputId);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var root = Otp.Root;
        var position = (Index + 1).ToString(CultureInfo.InvariantCulture);
        var length = root.Length.ToString(CultureInfo.InvariantCulture);
        attributes
            .Set("id", InputId)
            .Set("type", root.Mask ? "password" : "text")
            .Set("inputmode", root.InputMode)
            .Set("autocomplete", Index == 0 ? root.AutoComplete : "off")
            .Set("autocorrect", "off")
            .Set("spellcheck", "false")
            .Set("maxlength", Index == 0 ? null : "1")
            .Set("value", Character)
            .Set("disabled", root.IsDisabled)
            .Set("readonly", root.ReadOnly)
            .Set("required", root.Required && Index == 0)
            .Default("aria-label", $"Character {position} of {length}")
            .Set("data-blazix-otp-input", "")
            .Set("data-index", Index)
            .Flag("data-filled", Character.Length > 0)
            .Flag("data-complete", Otp.Complete)
            .Flag("data-readonly", root.ReadOnly)
            .Set("onfocus", _onFocus)
            .Set("onblur", _onBlur)
            .FieldState(root.Field, root.IsDisabled);
        if (root.Field is { Invalid: true })
        {
            attributes.Set("aria-invalid", "true");
        }
    }

    protected override OtpFieldInputRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Index, Character, Character.Length > 0, Otp.Complete, Otp.Disabled);

    protected override void Dispose(bool disposing)
    {
        Otp.Inputs.Remove(this);
        base.Dispose(disposing);
    }
}
