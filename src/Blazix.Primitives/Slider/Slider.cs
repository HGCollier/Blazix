using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record SliderRenderProps(IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<double> Values, bool Dragging, bool Disabled, Orientation Orientation)
    : RenderProps(Attributes);

public sealed record SliderThumbRenderProps(IReadOnlyDictionary<string, object> Attributes, int Index, double Value, bool Dragging, bool Disabled)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="SliderRoot"/> to its parts.</summary>
public sealed class SliderContext
{
    internal SliderContext(SliderRoot root) => Root = root;

    internal SliderRoot Root { get; }

    public IReadOnlyList<double> Values => Root.CurrentValues;

    public bool Dragging => Root.ActiveThumb is not null;

    public bool Disabled => Root.IsDisabled;

    public Orientation Orientation => Root.Orientation;

    internal string? LabelId { get; set; }

    internal List<SliderThumb> Thumbs { get; } = [];

    internal void AddState(AttributeBuilder attributes)
    {
        attributes
            .FieldState(Root.Field, Root.IsDisabled)
            .Flag("data-dragging", Dragging)
            .Set("data-orientation", Js.Orientation(Orientation));
    }
}

/// <summary>
/// An easily stylable range input. Bind <see cref="Value"/> for one
/// thumb or <see cref="Values"/> for a range with several thumbs. Name it with a <see cref="SliderLabel"/>, a field's
/// label, an <c>aria-label</c> or <c>aria-labelledby</c> on the root, or a <c>&lt;label for&gt;</c> at the root's
/// <c>id</c>; the thumbs take the name too.
/// </summary>
public class SliderRoot : PrimitivePart<SliderRenderProps>, IAsyncDisposable
{
    private readonly SliderContext _context;
    private readonly Controlled<IReadOnlyList<double>> _values = new([0], SequenceComparer<double>.Instance);
    private DotNetObjectReference<SliderInterop>? _interop;
    private JsController? _js;
    private string? _controlId;
    private string _optionsKey = "";

    public SliderRoot() => _context = new SliderContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] internal FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The value of a single-thumb slider. Use <c>@bind-Value</c>, or set it once for an uncontrolled slider.</summary>
    [Parameter] public double? Value { get; set; }

    [Parameter] public EventCallback<double> ValueChanged { get; set; }

    /// <summary>The values of a range slider, one per thumb. Use <c>@bind-Values</c>.</summary>
    [Parameter] public IReadOnlyList<double>? Values { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<double>?> ValuesChanged { get; set; }

    /// <summary>Runs before the value changes (reason "drag", "track-press" or "keyboard"); call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<double>>> OnValueChange { get; set; }

    /// <summary>Runs when a drag ends or a key changes the value: for work too heavy to do on every move.</summary>
    [Parameter] public EventCallback<IReadOnlyList<double>> OnValueCommitted { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public double Max { get; set; } = 100;

    [Parameter] public double Step { get; set; } = 1;

    /// <summary>The step for Page Up/Down and Shift + arrow keys. Defaults to 10.</summary>
    [Parameter] public double LargeStep { get; set; } = 10;

    /// <summary>The smallest number of steps allowed between the thumbs of a range. Defaults to 0.</summary>
    [Parameter] public int MinStepsBetweenValues { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>A .NET numeric format string for <see cref="SliderValue"/> and the accessible value text.</summary>
    [Parameter] public string? Format { get; set; }

    [Parameter] public CultureInfo? Culture { get; set; }

    internal IReadOnlyList<double> CurrentValues => _values.Value;

    internal bool IsRange => Values is not null || _values.Value.Count > 1;

    internal bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    internal int? ActiveThumb { get; private set; }

    internal string? EffectiveName => Name ?? Field?.Name;

    internal void SetControl(string id) => _controlId = id;

    /// <summary>An attribute the author put on the root, such as its aria-label, which also names the thumbs.</summary>
    internal string? Authored(string name) =>
        AdditionalAttributes is not null && AdditionalAttributes.TryGetValue(name, out var value) && value is string { Length: > 0 } text
            ? text
            : null;

    internal void Refresh() => StateHasChanged();

    /// <summary>The ids of the &lt;label for&gt;s that point at the root's id (found in the browser), which name the thumbs.</summary>
    internal string? ForLabelIds { get; private set; }

    private string? _labelsFoundFor;

    /// <summary>
    /// A &lt;label for="..."&gt; at the root's id labels nothing by itself (the root is a group, not a form control), so
    /// look for one once the root is in the page, when nothing else names the slider.
    /// </summary>
    private async Task FindForLabelsAsync()
    {
        var id = Authored("id");
        if (id == _labelsFoundFor)
        {
            return;
        }
        _labelsFoundFor = id;
        if (id is null || _context.LabelId is not null || Field is { HasLabel: true } || Authored("aria-label") is not null || Authored("aria-labelledby") is not null)
        {
            return;
        }
        var labelIds = await JsModules.LabelIdsAsync(JsRuntime, id);
        if (labelIds != ForLabelIds)
        {
            ForLabelIds = labelIds;
            StateHasChanged();
        }
    }

    // Calls from the browser may arrive outside the renderer's sync context.
    internal Task Dispatch(Func<Task> work) => InvokeAsync(work);

    internal string FormatValue(double value) =>
        value.ToString(Format ?? "0.##########", Culture ?? CultureInfo.CurrentCulture);

    internal double Percent(double value) => RangeValue.Percent(value, Min, Max);

    protected override void OnParametersSet()
    {
        if (Values is not null)
        {
            _values.Sync(Values);
        }
        else if (Value is { } single)
        {
            _values.Sync([single]);
        }
        else if (_values.Value is [0] && Min > 0)
        {
            _values.Value = [Min];
        }

        if (Field is { IsItem: false })
        {
            Field.RegisterControl(new FieldControlRegistration(
                _context.Thumbs.FirstOrDefault()?.InputId ?? "", _context.Thumbs.FirstOrDefault()?.InputId,
                () => IsRange ? _values.Value : _values.Value[0]));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await FindForLabelsAsync();
        if (_controlId is null)
        {
            return;
        }
        var options = new Dictionary<string, object?>
        {
            ["min"] = Min,
            ["max"] = Max,
            ["step"] = Step,
            ["largeStep"] = LargeStep,
            ["minStepsBetweenValues"] = MinStepsBetweenValues,
            ["orientation"] = Js.Orientation(Orientation),
            ["disabled"] = IsDisabled,
            ["readOnly"] = ReadOnly,
            ["values"] = _values.Value,
        };
        var key = string.Join(',', options.Values.Select(v => v is IEnumerable<double> list ? string.Join(';', list) : v?.ToString()));
        if (_js is null)
        {
            _js = new JsController(JsRuntime, "slider.js", "createSlider");
            _interop = DotNetObjectReference.Create(new SliderInterop(this));
            _optionsKey = key;
            await _js.EnsureAsync(_interop, _controlId, options);
        }
        else if (key != _optionsKey)
        {
            _optionsKey = key;
            await _js.InvokeVoidAsync("update", options);
        }
    }

    internal Task DragStartAsync(int index)
    {
        ActiveThumb = index;
        StateHasChanged();
        return Task.CompletedTask;
    }

    internal Task DragEndAsync()
    {
        ActiveThumb = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    internal async Task ChangeAsync(IReadOnlyList<double> values, bool commit, string reason)
    {
        if (IsDisabled || ReadOnly)
        {
            return;
        }
        if (!SequenceComparer<double>.Instance.Equals(values, _values.Value))
        {
            var args = new ValueChangeEventArgs<IReadOnlyList<double>>(values, reason);
            await OnValueChange.InvokeAsync(args);
            if (args.Canceled)
            {
                StateHasChanged();
                return;
            }
            _values.Value = values;
            if (Values is not null || ValuesChanged.HasDelegate)
            {
                await ValuesChanged.InvokeAsync(values);
            }
            if (values.Count > 0)
            {
                await ValueChanged.InvokeAsync(values[0]);
            }
            if (Field is not null)
            {
                await Field.ValueChangedAsync(IsRange ? values : values[0]);
            }
        }
        if (commit)
        {
            await OnValueCommitted.InvokeAsync(_values.Value);
        }
        StateHasChanged();
    }

    /// <summary>An assistive technology changed a thumb's native range input.</summary>
    internal Task InputChangedAsync(int index, double value)
    {
        var next = _values.Value.ToArray();
        if (index < next.Length)
        {
            next[index] = Math.Clamp(value, Min, Max);
        }
        return ChangeAsync(next, commit: true, "input-change");
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("role", "group")
            .LabelledBy(_context.LabelId ?? ForLabelIds)
            .Set("data-blazix-slider", "");
        _context.AddState(attributes);
    }

    protected override SliderRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _values.Value, ActiveThumb is not null, IsDisabled, Orientation);

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

internal sealed class SliderInterop(SliderRoot root)
{
    [JSInvokable]
    public Task Change(double[] values, bool commit) => root.Dispatch(() => root.ChangeAsync(values, commit, commit ? "keyboard" : "drag"));

    [JSInvokable]
    public Task DragStart(int index) => root.Dispatch(() => root.DragStartAsync(index));

    [JSInvokable]
    public Task DragEnd() => root.Dispatch(root.DragEndAsync);
}

/// <summary>Base for the parts inside a slider.</summary>
public abstract class SliderPart : PrimitivePart<SliderRenderProps>
{
    [CascadingParameter] protected SliderContext Slider { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => Slider.AddState(attributes);

    protected override SliderRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Slider.Values, Slider.Dragging, Slider.Disabled, Slider.Orientation);
}

/// <summary>An accessible label for the slider. Labels the root and every thumb.</summary>
public class SliderLabel : SliderPart
{
    private string _id = "";

    protected override string Tag => "label";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("slider-label"));
        if (Slider.LabelId != _id)
        {
            Slider.LabelId = _id;
            Slider.Root.Refresh();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id).Set("for", Slider.Thumbs.FirstOrDefault()?.InputId);
    }
}

/// <summary>Displays the current value as text. Renders an &lt;output&gt;.</summary>
public class SliderValue : SliderPart
{
    protected override string Tag => "output";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes
            .Set("aria-live", "off")
            .Set("for", string.Join(' ', Slider.Thumbs.Select(thumb => thumb.InputId)));
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
            return;
        }
        builder.AddContent(4, string.Join(" – ", Slider.Values.Select(Slider.Root.FormatValue)));
    }
}

/// <summary>The clickable, interactive part of the slider. Pressing it moves the closest thumb there.</summary>
public class SliderControl : SliderPart
{
    private string _id = "";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("slider-control"));
        Slider.Root.SetControl(_id);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id).Set("style", "touch-action:none");
    }
}

/// <summary>Contains the slider indicator and represents the entire range.</summary>
public class SliderTrack : SliderPart
{
    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("style", "position:relative");
    }
}

/// <summary>Visualizes the current value: from the start, or between a range's thumbs.</summary>
public class SliderIndicator : SliderPart
{
    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        var values = Slider.Values;
        var vertical = Slider.Orientation == Orientation.Vertical;
        var range = values.Count > 1;
        var start = range ? Slider.Root.Percent(values[0]) : 0;
        var end = Slider.Root.Percent(values[^1]);
        var edge = vertical ? "bottom" : "inset-inline-start";
        var size = vertical ? "height" : "width";
        var cross = vertical ? "width" : "height";
        attributes
            .Set("data-blazix-slider-indicator", "")
            .Set("style", $"position:{(vertical ? "absolute" : "relative")};{cross}:inherit;{edge}:{RangeValue.Css(start)}%;{size}:{RangeValue.Css(end - start)}%");
    }
}

/// <summary>
/// The draggable part of the slider. Holds a visually hidden
/// <c>&lt;input type="range"&gt;</c> that takes focus and carries the value for screen readers and forms.
/// </summary>
public class SliderThumb : PrimitivePart<SliderThumbRenderProps>
{
    private const string VisuallyHidden =
        "clip-path:inset(50%);overflow:hidden;white-space:nowrap;border:0;padding:0;width:100%;height:100%;margin:-1px;position:fixed;top:0;left:0";

    private EventCallback<ChangeEventArgs> _onInput;
    private EventCallback<FocusEventArgs> _onFocus;
    private EventCallback<FocusEventArgs> _onBlur;

    [CascadingParameter] private SliderContext Slider { get; set; } = null!;

    [CascadingParameter] private TextDirection? Direction { get; set; }

    /// <summary>Which value this thumb moves. Defaults to the thumb's position among the slider's thumbs.</summary>
    [Parameter] public int? Index { get; set; }

    /// <summary>An accessible name for the thumb, e.g. "Minimum price" for one end of a range. An <c>aria-label</c> on the thumb does the same.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>The accessible value text, from the formatted value and the raw value.</summary>
    [Parameter] public Func<string, double, string>? GetAriaValueText { get; set; }

    internal string InputId { get; } = Ids.Next("slider-thumb-input");

    private string? Authored(string name) =>
        AdditionalAttributes is not null && AdditionalAttributes.TryGetValue(name, out var value) && value is string { Length: > 0 } text
            ? text
            : null;

    private int ThumbIndex => Index ?? Slider.Thumbs.IndexOf(this);

    private double ThumbValue => ThumbIndex >= 0 && ThumbIndex < Slider.Values.Count ? Slider.Values[ThumbIndex] : Slider.Root.Min;

    protected override void OnInitialized()
    {
        Slider.Thumbs.Add(this);
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
            double.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? Slider.Root.InputChangedAsync(ThumbIndex, value)
                : Task.CompletedTask);
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Slider.Root.Field?.FocusAsync() ?? Task.CompletedTask);
        _onBlur = EventCallback.Factory.Create<FocusEventArgs>(this, () => Slider.Root.Field?.BlurAsync() ?? Task.CompletedTask);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var vertical = Slider.Orientation == Orientation.Vertical;
        var edge = vertical ? "bottom" : "inset-inline-start";
        var cross = vertical ? "left" : "top";
        // inset-inline-start becomes right in RTL, but translate is physical: centre the thumb the other way.
        var shift = !vertical && Direction == TextDirection.Rtl ? "50%" : "-50%";
        var translate = vertical ? $"{shift} 50%" : $"{shift} -50%";
        var active = Slider.Root.ActiveThumb == ThumbIndex;
        Slider.AddState(attributes);
        // An aria-label or aria-labelledby on the thumb names its range input, the element that takes focus.
        attributes.Set("aria-label", null).Set("aria-labelledby", null);
        attributes
            .Set("data-blazix-slider-thumb", "")
            .Set("data-index", ThumbIndex)
            .Flag("data-dragging", active)
            .Set("style", $"position:absolute;{edge}:{RangeValue.Css(Slider.Root.Percent(ThumbValue))}%;{cross}:50%;translate:{translate};{(active ? "z-index:2" : "")}");
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        BuildPart(builder);
        if (Render is not null)
        {
            // The author's element can't hold it, so the range input renders beside it.
            BuildInput(builder);
        }
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        builder.AddContent(3, ChildContent);
        BuildInput(builder);
    }

    private void BuildInput(RenderTreeBuilder builder)
    {
        var root = Slider.Root;
        var formatted = root.FormatValue(ThumbValue);
        var (labelledBy, label) = Name(root);
        builder.OpenElement(10, "input");
        builder.AddAttribute(11, "type", "range");
        builder.AddAttribute(12, "id", InputId);
        builder.AddAttribute(13, "min", root.Min.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(14, "max", root.Max.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(15, "step", root.Step.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(16, "value", ThumbValue.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(17, "name", root is { EffectiveName: { } name, IsRange: true } ? $"{name}[{ThumbIndex}]" : root.EffectiveName);
        builder.AddAttribute(18, "disabled", root.IsDisabled);
        builder.AddAttribute(19, "aria-labelledby", labelledBy);
        builder.AddAttribute(20, "aria-label", label);
        builder.AddAttribute(21, "aria-valuetext", GetAriaValueText?.Invoke(formatted, ThumbValue) ?? formatted);
        builder.AddAttribute(22, "aria-orientation", Js.Orientation(Slider.Orientation));
        builder.AddAttribute(23, "aria-describedby", root.Field?.DescribedBy);
        builder.AddAttribute(24, "aria-invalid", root.Field?.Invalid == true ? "true" : null);
        builder.AddAttribute(25, "style", VisuallyHidden);
        builder.AddAttribute(26, "oninput", _onInput);
        builder.AddAttribute(27, "onfocus", _onFocus);
        builder.AddAttribute(28, "onblur", _onBlur);
        builder.AddAttribute(29, "data-blazix-slider-input", "");
        builder.AddAttribute(30, "data-index", ThumbIndex);
        builder.CloseElement();
    }

    /// <summary>
    /// The range input's aria-labelledby and aria-label: the thumb's own (<see cref="AriaLabel"/>, or an aria-label or
    /// aria-labelledby on the thumb), else the slider's label, its field's, or the aria-labelledby or aria-label on the root.
    /// </summary>
    private (string? LabelledBy, string? Label) Name(SliderRoot root)
    {
        if ((AriaLabel ?? Authored("aria-label")) is { } own)
        {
            return (Authored("aria-labelledby"), own);
        }
        if (Authored("aria-labelledby") is { } ownLabelledBy)
        {
            return (ownLabelledBy, null);
        }
        var fieldLabel = root.Field is { HasLabel: true } field ? field.LabelId : null;
        var labelledBy = Slider.LabelId ?? fieldLabel ?? root.Authored("aria-labelledby") ?? root.ForLabelIds;
        return labelledBy is not null ? (labelledBy, null) : (null, root.Authored("aria-label"));
    }

    protected override SliderThumbRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, ThumbIndex, ThumbValue, Slider.Root.ActiveThumb == ThumbIndex, Slider.Disabled);

    protected override void Dispose(bool disposing)
    {
        Slider.Thumbs.Remove(this);
        base.Dispose(disposing);
    }
}
