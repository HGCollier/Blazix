using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record ScrollAreaRenderProps(IReadOnlyDictionary<string, object> Attributes, Orientation? Orientation)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="ScrollAreaRoot"/>.</summary>
public sealed class ScrollAreaContext
{
    internal ScrollAreaContext(ScrollAreaRoot root) => Root = root;

    internal ScrollAreaRoot Root { get; }
}

/// <summary>
/// A native scroll container with custom scrollbars. Scrolling,
/// overflow and hover state are written as data attributes as they change: <c>data-scrolling</c>,
/// <c>data-has-overflow-x/y</c>, <c>data-overflow-x/y-start/end</c> on every part, and
/// <c>data-hovering</c> on the scrollbars.
/// </summary>
public class ScrollAreaRoot : PrimitivePart<ScrollAreaRenderProps>, IAsyncDisposable
{
    private readonly ScrollAreaContext _context;
    private JsController? _js;
    private string _id = "";
    private int _threshold = -1;

    public ScrollAreaRoot() => _context = new ScrollAreaContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>How far from an edge, in pixels, before <c>data-overflow-*-start/end</c> apply. Defaults to 0.</summary>
    [Parameter] public int OverflowEdgeThreshold { get; set; }

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("scroll-area"));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var options = new Dictionary<string, object?> { ["overflowEdgeThreshold"] = OverflowEdgeThreshold };
        if (_js is null)
        {
            _js = new JsController(JsRuntime, "scroll-area.js", "createScrollArea");
            _threshold = OverflowEdgeThreshold;
            await _js.EnsureAsync(_id, options);
        }
        else if (_threshold != OverflowEdgeThreshold)
        {
            _threshold = OverflowEdgeThreshold;
            await _js.InvokeVoidAsync("update", options);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("data-blazix-scroll-area", "")
        .Set("style", "position:relative");

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, null);

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>The actual scrollable container. Its native scrollbars are hidden.</summary>
public class ScrollAreaViewport : PrimitivePart<ScrollAreaRenderProps>
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-scroll-viewport", "")
        .Set("style", "overflow:scroll;scrollbar-width:none");

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, null);
}

/// <summary>A container for the content, so its size can be measured.</summary>
public class ScrollAreaContent : PrimitivePart<ScrollAreaRenderProps>
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-scroll-content", "")
        .Set("style", "min-width:fit-content");

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, null);
}

/// <summary>
/// A vertical or horizontal scrollbar. Hidden while there's
/// nothing to scroll that way, unless <see cref="KeepMounted"/>. Its thumb's size is
/// <c>--scroll-area-thumb-height</c> (or <c>-width</c>).
/// </summary>
public class ScrollAreaScrollbar : PrimitivePart<ScrollAreaRenderProps>
{
    [Parameter] public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Keep the scrollbar rendered when there's nothing to scroll. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-scrollbar", Js.Orientation(Orientation))
        .Set("data-orientation", Js.Orientation(Orientation))
        .Flag("data-keep-mounted", KeepMounted)
        .Set("style", "position:absolute;touch-action:none;user-select:none;" + (Orientation == Orientation.Vertical
            ? "top:0;bottom:var(--scroll-area-corner-height,0);inset-inline-end:0"
            : "bottom:0;inset-inline-start:0;inset-inline-end:var(--scroll-area-corner-width,0)"));

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Orientation);
}

/// <summary>The draggable part of the scrollbar.</summary>
public class ScrollAreaThumb : PrimitivePart<ScrollAreaRenderProps>
{
    [CascadingParameter] private ScrollAreaScrollbar Scrollbar { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var vertical = Scrollbar.Orientation == Orientation.Vertical;
        attributes
            .Set("data-blazix-scroll-thumb", "")
            .Set("data-orientation", Js.Orientation(Scrollbar.Orientation))
            .Set("style", vertical ? "height:var(--scroll-area-thumb-height)" : "width:var(--scroll-area-thumb-width)");
    }

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Scrollbar.Orientation);
}

/// <summary>A small rectangle where the two scrollbars meet. Shown only when both are.</summary>
public class ScrollAreaCorner : PrimitivePart<ScrollAreaRenderProps>
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-scroll-corner", "")
        .Set("style", "position:absolute;bottom:0;inset-inline-end:0;width:var(--scroll-area-corner-width);height:var(--scroll-area-corner-height)")
        .Set("hidden", true);

    protected override ScrollAreaRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, null);
}
