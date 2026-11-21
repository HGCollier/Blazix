using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Docs.Components.Docs;

/// <summary>An &lt;h1&gt;-&lt;h6&gt; chosen by <see cref="Level"/>.</summary>
public sealed class DynamicHeading : ComponentBase
{
    [Parameter] public int Level { get; set; } = 2;
    [Parameter] public string? Id { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, $"h{Math.Clamp(Level, 1, 6)}");
        builder.AddAttribute(1, "id", Id);
        builder.AddAttribute(2, "class", Class);
        builder.AddContent(3, ChildContent);
        builder.CloseElement();
    }
}
