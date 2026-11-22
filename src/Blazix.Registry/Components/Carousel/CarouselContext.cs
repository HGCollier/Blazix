namespace Blazix.Registry;

/// <summary>
/// What a Carousel cascades to its parts:
/// <c>[CascadingParameter] CarouselContext Carousel { get; set; }</c>. Use it to build your own controls.
/// </summary>
public sealed class CarouselContext
{
    internal CarouselContext(Orientation orientation, CarouselOptions? opts)
    {
        Orientation = orientation;
        Opts = opts;
    }

    /// <summary>The carousel's API, or null until it starts (it starts once the page is interactive).</summary>
    public CarouselApi? Api { get; internal set; }

    public Orientation Orientation { get; internal set; }

    public CarouselOptions? Opts { get; internal set; }

    /// <summary>Whether there's a snap before the selected one. False until the carousel starts.</summary>
    public bool CanScrollPrev => Api?.CanScrollPrev ?? false;

    /// <summary>Whether there's a snap after the selected one. False until the carousel starts.</summary>
    public bool CanScrollNext => Api?.CanScrollNext ?? false;

    public Task ScrollPrevAsync() => Api?.ScrollPrevAsync() ?? Task.CompletedTask;

    public Task ScrollNextAsync() => Api?.ScrollNextAsync() ?? Task.CompletedTask;

    private readonly List<object> _items = [];

    /// <summary>Raised when a slide is added or removed, so the slides can number themselves again.</summary>
    internal event Action? ItemsChanged;

    internal int ItemCount => _items.Count;

    internal int IndexOf(object item) => _items.IndexOf(item);

    internal void AddItem(object item)
    {
        _items.Add(item);
        ItemsChanged?.Invoke();
    }

    internal void RemoveItem(object item)
    {
        if (_items.Remove(item))
        {
            ItemsChanged?.Invoke();
        }
    }
}
