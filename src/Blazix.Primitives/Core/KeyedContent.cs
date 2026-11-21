using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// Renders a fragment under a key, so a templated list (toasts, filtered items) keeps each item's
/// components with its item when others are added or removed. Keys go on component frames, not regions.
/// </summary>
internal sealed class KeyedContent : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);

    public static void Add(RenderTreeBuilder builder, int sequence, object key, RenderFragment content)
    {
        builder.OpenComponent<KeyedContent>(sequence);
        builder.SetKey(key);
        builder.AddComponentParameter(sequence + 1, nameof(ChildContent), content);
        builder.CloseComponent();
    }
}
