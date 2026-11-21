using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>Where a progress bar is: no value yet, under way, or at its maximum.</summary>
public enum ProgressStatus
{
    Indeterminate,
    Progressing,
    Complete,
}

public sealed record ProgressRenderProps(IReadOnlyDictionary<string, object> Attributes, ProgressStatus Status, double? Value, string FormattedValue)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="ProgressRoot"/>.</summary>
public sealed class ProgressContext
{
    private readonly ProgressRoot _root;

    internal ProgressContext(ProgressRoot root) => _root = root;

    public double? Value => _root.ClampedValue;

    public double Percent => _root.PercentValue;

    public ProgressStatus Status => _root.Status;

    public string FormattedValue => _root.FormattedValue;

    internal string? LabelId { get; set; }

    internal void RegisterLabel(string id) => _root.RegisterLabel(id);
}

/// <summary>
/// Displays the status of a task that takes a long time. A null
/// <see cref="Value"/> is indeterminate. Marked <c>data-indeterminate</c>, <c>data-progressing</c> or <c>data-complete</c>.
/// </summary>
public class ProgressRoot : PrimitivePart<ProgressRenderProps>
{
    private readonly ProgressContext _context;

    public ProgressRoot() => _context = new ProgressContext(this);

    /// <summary>The current value. Null while the progress is indeterminate.</summary>
    [Parameter] public double? Value { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public double Max { get; set; } = 100;

    /// <summary>A .NET numeric format string for the value (e.g. "N0", "C"). Without one, it shows the percentage of the range.</summary>
    [Parameter] public string? Format { get; set; }

    /// <summary>The culture to format with. Defaults to the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>The accessible text for the value, from the formatted value and the raw value.</summary>
    [Parameter] public Func<string, double?, string>? GetAriaValueText { get; set; }

    internal double? ClampedValue => Value is { } v && double.IsFinite(v) ? Math.Clamp(v, Min, Max) : null;

    internal double PercentValue => ClampedValue is { } v ? RangeValue.Percent(v, Min, Max) : 0;

    internal ProgressStatus Status => ClampedValue switch
    {
        null => ProgressStatus.Indeterminate,
        var v when v >= Max => ProgressStatus.Complete,
        _ => ProgressStatus.Progressing,
    };

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

    internal static void AddStatus(AttributeBuilder attributes, ProgressStatus status) => attributes
        .Flag("data-indeterminate", status == ProgressStatus.Indeterminate)
        .Flag("data-progressing", status == ProgressStatus.Progressing)
        .Flag("data-complete", status == ProgressStatus.Complete);

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        builder.AddContent(3, ChildContent);
        RangeValue.AddVisuallyHiddenLabelHack(builder);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var text = GetAriaValueText?.Invoke(FormattedValue, Value) ?? (ClampedValue is null ? "indeterminate progress" : FormattedValue);
        attributes
            .Set("role", "progressbar")
            .LabelledBy(_context.LabelId)
            .Set("aria-valuemin", Min.ToString(CultureInfo.InvariantCulture))
            .Set("aria-valuemax", Max.ToString(CultureInfo.InvariantCulture))
            .Set("aria-valuenow", ClampedValue?.ToString(CultureInfo.InvariantCulture))
            .Set("aria-valuetext", text);
        AddStatus(attributes, Status);
    }

    protected override ProgressRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Status, ClampedValue, FormattedValue);
}

/// <summary>Base for the parts inside a progress bar: they all carry its status attributes.</summary>
public abstract class ProgressPart : PrimitivePart<ProgressRenderProps>
{
    [CascadingParameter] protected ProgressContext Progress { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => ProgressRoot.AddStatus(attributes, Progress.Status);

    protected override ProgressRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Progress.Status, Progress.Value, Progress.FormattedValue);
}

/// <summary>Contains the progress bar indicator.</summary>
public class ProgressTrack : ProgressPart;

/// <summary>Visualizes the completion status: its width is the percentage complete.</summary>
public class ProgressIndicator : ProgressPart
{
    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        if (Progress.Value is not null)
        {
            attributes.Set("style", $"inset-inline-start:0;height:inherit;width:{RangeValue.Css(Progress.Percent)}%");
        }
    }
}

/// <summary>A text label displaying the current value. Renders the formatted value unless you give it content.</summary>
public class ProgressValue : ProgressPart
{
    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("aria-hidden", "true");
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Progress.FormattedValue);
        }
    }
}

/// <summary>An accessible label for the progress bar.</summary>
public class ProgressLabel : ProgressPart
{
    private string _id = "";

    protected override string Tag => "span";

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("progress-label"));
        Progress.RegisterLabel(_id);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes.Set("id", _id).Set("role", "presentation");
    }
}
