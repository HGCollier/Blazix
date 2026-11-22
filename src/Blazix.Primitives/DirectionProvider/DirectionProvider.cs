using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// Tells the components inside it the reading direction. It renders no element and sets no <c>dir</c>
/// attribute: set <c>dir="rtl"</c> on an element yourself for the layout. Keyboard navigation and
/// placement read the computed CSS <c>direction</c>; the provider covers what's decided while rendering
/// (the <c>data-activation-direction</c> of Tabs and Navigation Menu).
/// </summary>
public sealed class DirectionProvider : ComponentBase
{
    /// <summary>The reading direction of the components inside. Defaults to left-to-right.</summary>
    [Parameter] public TextDirection Direction { get; set; } = TextDirection.Ltr;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<TextDirection>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<>.Value), Direction);
        builder.AddComponentParameter(2, nameof(CascadingValue<>.ChildContent), ChildContent);
        builder.CloseComponent();
    }
}
