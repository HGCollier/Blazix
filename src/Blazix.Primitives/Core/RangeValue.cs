using System.Globalization;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>A value within a range, formatted like a native range value.</summary>
internal static class RangeValue
{
    public static double Percent(double value, double min, double max) =>
        max <= min ? 0 : Math.Clamp((value - min) / (max - min) * 100, 0, 100);

    /// <summary>With a format: the value itself. Without one: the percentage of the range ("50%").</summary>
    public static string Format(double? value, double percent, string? format, CultureInfo? culture)
    {
        if (value is null)
        {
            return "";
        }
        var provider = culture ?? CultureInfo.CurrentCulture;
        return format is null
            ? (percent / 100).ToString("P0", provider)
            : value.Value.ToString(format, provider);
    }

    public static string Css(double percent) => percent.ToString("0.####", CultureInfo.InvariantCulture);

    public static void AddVisuallyHiddenLabelHack(RenderTreeBuilder builder)
    {
        // Forces NVDA to read the label.
        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "role", "presentation");
        builder.AddAttribute(12, "style", "position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0");
        builder.AddContent(13, "x");
        builder.CloseElement();
    }
}
