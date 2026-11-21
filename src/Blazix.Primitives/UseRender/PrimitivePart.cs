using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// A part that renders one element, or the author's own element through <see cref="Render"/>
///. Subclasses add their attributes in <see cref="BuildAttributes"/>
/// and describe their state in <see cref="CreateRenderProps"/>.
/// </summary>
public abstract class PrimitivePart<TRenderProps> : ComponentBase, IDisposable where TRenderProps : RenderProps
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Release what the part registered (e.g. with its group). Called once when it leaves the page.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Render your own element or component instead of the default one. Spread
    /// <c>props.Attributes</c> onto it; they carry the id, role, ARIA, data-* state and handlers.
    /// </summary>
    [Parameter] public RenderFragment<TRenderProps>? Render { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The element rendered when there's no <see cref="Render"/>.</summary>
    protected virtual string Tag => "div";

    /// <summary>False renders nothing at all (e.g. a closed panel that isn't kept mounted).</summary>
    protected virtual bool IsPresent => true;

    /// <summary>Whether the default element is a void element (input, img...) with no children.</summary>
    protected virtual bool IsVoid => false;

    protected abstract void BuildAttributes(AttributeBuilder attributes);

    protected abstract TRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes);

    /// <summary>The final attributes: the author's, merged with the part's.</summary>
    private IReadOnlyDictionary<string, object> GetAttributes()
    {
        var builder = new AttributeBuilder(AdditionalAttributes);
        BuildAttributes(builder);
        return builder.Build();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => BuildPart(builder);

    /// <summary>Renders the part's element (or <see cref="Render"/>). Call it from an overridden BuildRenderTree.</summary>
    protected void BuildPart(RenderTreeBuilder builder)
    {
        if (!IsPresent)
        {
            return;
        }

        var attributes = GetAttributes();
        if (Render is not null)
        {
            builder.AddContent(0, Render(CreateRenderProps(attributes)));
            return;
        }

        builder.OpenElement(1, Tag);
        builder.AddMultipleAttributes(2, attributes);
        if (!IsVoid)
        {
            BuildChildren(builder);
        }
        builder.CloseElement();
    }

    /// <summary>What goes inside the default element. Defaults to <see cref="ChildContent"/>.</summary>
    protected virtual void BuildChildren(RenderTreeBuilder builder) => builder.AddContent(3, ChildContent);

    /// <summary>Wraps <paramref name="content"/> in a cascading value.</summary>
    protected static void Cascade<T>(RenderTreeBuilder builder, T value, RenderFragment content, bool isFixed = false, string? name = null)
    {
        builder.OpenComponent<CascadingValue<T>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<>.Value), value);
        builder.AddComponentParameter(2, nameof(CascadingValue<>.IsFixed), isFixed);
        if (name is not null)
        {
            builder.AddComponentParameter(3, nameof(CascadingValue<>.Name), name);
        }
        builder.AddComponentParameter(4, nameof(CascadingValue<>.ChildContent), content);
        builder.CloseComponent();
    }
}

/// <summary>What a part's <c>Render</c> fragment receives when the part has no state worth naming.</summary>
public sealed record PartRenderProps(IReadOnlyDictionary<string, object> Attributes) : RenderProps(Attributes);

/// <summary>A part with no state of its own: just attributes on a default element.</summary>
public abstract class SimplePart : PrimitivePart<PartRenderProps>
{
    protected sealed override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}
