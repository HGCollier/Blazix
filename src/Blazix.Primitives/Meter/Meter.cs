using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record MeterRenderProps(IReadOnlyDictionary<string, object> Attributes, double Value, string FormattedValue)
    : RenderProps(Attributes);


/// <summary>Cascaded from <see cref="MeterRoot"/>.</summary>
public sealed class MeterContext
{
    private readonly MeterRoot _root;

    internal MeterContext(MeterRoot root) => _root = root;

    public double Value => _root.ClampedValue;

    public double Percent => _root.PercentValue;

    public string FormattedValue => _root.FormattedValue;

    internal string? LabelId { get; set; }

    internal void RegisterLabel(string id) => _root.RegisterLabel(id);
}

/// <summary>A graphical display of a numeric value within a range.</summary>
public class MeterRoot : PrimitivePart<MeterRenderProps>
{
    private readonly MeterContext _context;

    public MeterRoot() => _context = new MeterContext(this);

    /// <summary>The current value.</summary>
    [Parameter, EditorRequired] public double Value { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public double Max { get; set; } = 100;

    /// <summary>A .NET numeric format string for the value. Without one, it shows the percentage of the range.</summary>
    [Parameter] public string? Format { get; set; }

    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>The accessible text for the value, from the formatted value and the raw value.</summary>
    [Parameter] public Func<string, double, string>? GetAriaValueText { get; set; }

    internal double ClampedValue => double.IsFinite(Value) ? Math.Clamp(Value, Min, Max) : Min;

    internal double PercentValue => RangeValue.Percent(ClampedValue, Min, Max);

    internal string FormattedValue => RangeValue.Format(ClampedValue, PercentValue, Format, Culture);

    internal void RegisterLabel(string id)
    {
        if (_context.LabelId == id)
        {
            return;
        }
        _context.LabelId = id;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        builder.AddContent(3, ChildContent);
        RangeValue.AddVisuallyHiddenLabelHack(builder);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "meter")
        .LabelledBy(_context.LabelId)
        .Set("aria-valuemin", Min.ToString(CultureInfo.InvariantCulture))
        .Set("aria-valuemax", Max.ToString(CultureInfo.InvariantCulture))
        .Set("aria-valuenow", ClampedValue.ToString(CultureInfo.InvariantCulture))
        .Set("aria-valuetext", GetAriaValueText?.Invoke(FormattedValue, Value) ?? FormattedValue);

    protected override MeterRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, ClampedValue, FormattedValue);
}

/// <summary>Base for the parts inside a meter.</summary>
public abstract class MeterPart : PrimitivePart<MeterRenderProps>
{
    [CascadingParameter] protected MeterContext Meter { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // Meter parts have no state attributes of their own.
    }

    protected override MeterRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Meter.Value, Meter.FormattedValue);
}

/// <summary>Contains the meter indicator.</summary>
public class MeterTrack : MeterPart;

/// <summary>Visualizes the position of the value along the range.</summary>
public class MeterIndicator : MeterPart
{
    protected override void BuildAttributes(AttributeBuilder attributes) =>
        attributes.Set("style", $"inset-inline-start:0;height:inherit;width:{RangeValue.Css(Meter.Percent)}%");
}

/// <summary>A text element displaying the current value.</summary>
public class MeterValue : MeterPart
{
    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("aria-hidden", "true");

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Meter.FormattedValue);
        }
    }
}

/// <summary>An accessible label for the meter.</summary>
public class MeterLabel : MeterPart
{
    private string _id = "";

    protected override string Tag => "span";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("meter-label"));
        Meter.RegisterLabel(_id);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id).Set("role", "presentation");
}
