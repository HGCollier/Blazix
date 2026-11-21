using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>Content Security Policy settings, cascaded by <see cref="CspProvider"/>.</summary>
public sealed record CspSettings(string? Nonce, bool DisableStyleElements);

/// <summary>
/// Provides a Content Security Policy nonce to the components inside it.
/// Blazix.Primitives itself never injects &lt;style&gt; or &lt;script&gt; elements: its browser-side
/// styles are set through the CSSOM, which CSP allows, and its scripts are ES modules loaded from
/// this package's static assets. So nothing here needs the nonce today; it's cascaded as
/// <see cref="CspSettings"/> for parts you build that do. Note that the inline <c>style</c>
/// attributes some parts render (positions, sizes) need <c>style-src-attr 'unsafe-inline'</c>
/// under a strict policy, since Blazor sets them as attributes.
/// </summary>
public sealed class CspProvider : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The nonce for inline &lt;style&gt; and &lt;script&gt; elements.</summary>
    [Parameter] public string? Nonce { get; set; }

    /// <summary>Don't render &lt;style&gt; elements at all. Defaults to false.</summary>
    [Parameter] public bool DisableStyleElements { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<CspSettings>>(0);
        builder.AddComponentParameter(1, "Value", new CspSettings(Nonce, DisableStyleElements));
        builder.AddComponentParameter(2, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}
