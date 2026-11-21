namespace Blazix.Registry;

/// <summary>
/// A Carousel's <c>Opts</c>: Embla Carousel's options (https://www.embla-carousel.com/api/options/).
/// Leave a property null for Embla's default. The axis comes from the Carousel's <c>Orientation</c>.
/// </summary>
public sealed class CarouselOptions
{
    /// <summary>Where snapped slides sit in the viewport. Defaults to Center.</summary>
    public Align? Align { get; init; }

    /// <summary>Wrap from the last slide to the first, and back. Ignored when there are too few slides to fill the gap.</summary>
    public bool? Loop { get; init; }

    /// <summary>How many slides a snap moves. Defaults to 1.</summary>
    public int? SlidesToScroll { get; init; }

    /// <summary>Group as many slides per snap as fit in the viewport (Embla's <c>slidesToScroll: "auto"</c>). Overrides SlidesToScroll.</summary>
    public bool? SlidesToScrollAuto { get; init; }

    /// <summary>Remove the empty space at the start and end that alignment would leave. Defaults to TrimSnaps.</summary>
    public CarouselContainScroll? ContainScroll { get; init; }

    /// <summary>Drag with momentum, without snapping to a slide.</summary>
    public bool? DragFree { get; init; }

    /// <summary>Pixels a pointer moves before the drag counts as a drag rather than a click. Defaults to 10.</summary>
    public double? DragThreshold { get; init; }

    /// <summary>Scroll speed for buttons and the API: higher is slower. Embla recommends 20 to 60. Defaults to 25.</summary>
    public double? Duration { get; init; }

    /// <summary>Let a hard flick skip past snap points instead of stopping at the next one.</summary>
    public bool? SkipSnaps { get; init; }

    /// <summary>The snap to start at. Defaults to 0.</summary>
    public int? StartIndex { get; init; }

    /// <summary>Scroll direction. Should match the text direction around the carousel. Defaults to Ltr.</summary>
    public TextDirection? Direction { get; init; }

    /// <summary>Fraction of a slide that must be visible to count as in view (0 to 1).</summary>
    public double? InViewThreshold { get; init; }

    /// <summary>Set to false to turn the carousel off, e.g. in a breakpoint.</summary>
    public bool? Active { get; init; }

    /// <summary>Set to false to turn off dragging.</summary>
    public bool? WatchDrag { get; init; }

    /// <summary>Set to false to stop re-measuring when the viewport or a slide resizes.</summary>
    public bool? WatchResize { get; init; }

    /// <summary>Set to false to stop re-measuring when slides are added or removed.</summary>
    public bool? WatchSlides { get; init; }

    /// <summary>Set to false to stop scrolling to a slide that receives focus from Tab.</summary>
    public bool? WatchFocus { get; init; }

    /// <summary>Options that apply while a media query matches, e.g. <c>["(min-width: 768px)"] = new() { Loop = true }</c>.</summary>
    public IReadOnlyDictionary<string, CarouselOptions>? Breakpoints { get; init; }

    internal Dictionary<string, object> ToJs()
    {
        var js = new Dictionary<string, object>(StringComparer.Ordinal);
        Add(js, "align", Align?.ToString().ToLowerInvariant());
        Add(js, "loop", Loop);
        Add(js, "slidesToScroll", SlidesToScrollAuto == true ? "auto" : SlidesToScroll);
        Add(js, "containScroll", ContainScroll switch
        {
            CarouselContainScroll.None => false,
            CarouselContainScroll.KeepSnaps => "keepSnaps",
            CarouselContainScroll.TrimSnaps => "trimSnaps",
            _ => null,
        });
        Add(js, "dragFree", DragFree);
        Add(js, "dragThreshold", DragThreshold);
        Add(js, "duration", Duration);
        Add(js, "skipSnaps", SkipSnaps);
        Add(js, "startIndex", StartIndex);
        Add(js, "direction", Direction switch
        {
            TextDirection.Rtl => "rtl",
            TextDirection.Ltr => "ltr",
            _ => null,
        });
        Add(js, "inViewThreshold", InViewThreshold);
        Add(js, "active", Active);
        Add(js, "watchDrag", WatchDrag);
        Add(js, "watchResize", WatchResize);
        Add(js, "watchSlides", WatchSlides);
        Add(js, "watchFocus", WatchFocus);
        if (Breakpoints is { Count: > 0 })
            js["breakpoints"] = Breakpoints.ToDictionary(b => b.Key, b => (object)b.Value.ToJs(), StringComparer.Ordinal);
        return js;
    }

    private static void Add(Dictionary<string, object> js, string name, object? value)
    {
        if (value is not null)
            js[name] = value;
    }
}

/// <summary>Embla's <c>containScroll</c>: what to do with the empty space alignment leaves at the ends.</summary>
public enum CarouselContainScroll
{
    /// <summary>Keep the space (Embla's <c>false</c>).</summary>
    None,

    /// <summary>Remove the space and the snap points that would show it.</summary>
    TrimSnaps,

    /// <summary>Remove the space but keep every snap point.</summary>
    KeepSnaps,
}
