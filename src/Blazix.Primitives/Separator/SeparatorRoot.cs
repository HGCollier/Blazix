using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>
/// A separator element accessible to screen readers.
/// Renders a <c>&lt;div role="separator"&gt;</c> with <c>aria-orientation</c> and <c>data-orientation</c>.
/// </summary>
public class SeparatorRoot : SimplePart
{
    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "separator")
        .Set("aria-orientation", Js.Orientation(Orientation))
        .Set("data-orientation", Js.Orientation(Orientation));
}
