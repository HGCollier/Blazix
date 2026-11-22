using Microsoft.JSInterop;

namespace Blazix.Registry;

/// <summary>
/// The carousel's API (Embla's <c>emblaApi</c>), handed to a Carousel's <c>SetApi</c> once it starts.
/// Its properties hold the state from the latest event: they change on "select" and "reInit", and on any
/// other event you subscribe to with <see cref="On(string, Action{CarouselApi})"/>.
/// </summary>
public sealed class CarouselApi
{
    private readonly IJSObjectReference _controller;
    private readonly Dictionary<string, List<(Delegate Key, Func<CarouselApi, Task> Run)>> _handlers = new(StringComparer.Ordinal);

    internal CarouselApi(IJSObjectReference controller, CarouselState state, IReadOnlyList<CarouselPlugin> plugins)
    {
        _controller = controller;
        Plugins = plugins;
        Update(state);
    }

    /// <summary>The index of the selected snap.</summary>
    public int SelectedScrollSnap { get; private set; }

    /// <summary>The index of the snap selected before this one.</summary>
    public int PreviousScrollSnap { get; private set; }

    /// <summary>One entry per snap point: its scroll progress, from 0 to 1. Its length is the number of snaps.</summary>
    public IReadOnlyList<double> ScrollSnapList { get; private set; } = [];

    /// <summary>Whether there's a snap before the selected one (always, when looping).</summary>
    public bool CanScrollPrev { get; private set; }

    /// <summary>Whether there's a snap after the selected one (always, when looping).</summary>
    public bool CanScrollNext { get; private set; }

    /// <summary>How far it has scrolled, from 0 to 1.</summary>
    public double ScrollProgress { get; private set; }

    /// <summary>The indexes of the slides in view.</summary>
    public IReadOnlyList<int> SlidesInView { get; private set; } = [];

    /// <summary>The indexes of the slides out of view.</summary>
    public IReadOnlyList<int> SlidesNotInView { get; private set; } = [];

    /// <summary>The carousel's plugins.</summary>
    public IReadOnlyList<CarouselPlugin> Plugins { get; internal set; }

    /// <summary>Scrolls to the previous snap. Pass jump to skip the animation.</summary>
    public Task ScrollPrevAsync(bool jump = false) => _controller.InvokeVoidAsync("scrollPrev", jump).AsTask();

    /// <summary>Scrolls to the next snap. Pass jump to skip the animation.</summary>
    public Task ScrollNextAsync(bool jump = false) => _controller.InvokeVoidAsync("scrollNext", jump).AsTask();

    /// <summary>Scrolls to a snap. Pass jump to skip the animation.</summary>
    public Task ScrollToAsync(int index, bool jump = false) => _controller.InvokeVoidAsync("scrollTo", index, jump).AsTask();

    /// <summary>Measures the slides again, keeping the selected snap.</summary>
    public Task ReInitAsync() => _controller.InvokeVoidAsync("reInit", null, null).AsTask();

    /// <summary>
    /// Runs handler on an Embla event: "select", "scroll", "settle", "init", "reInit", "resize", "pointerDown",
    /// "pointerUp", "slidesInView", "slidesChanged", "slideFocus", or a plugin's, like "autoplay:play".
    /// Handlers run outside your component's event handling: call StateHasChanged to show what they change.
    /// </summary>
    public CarouselApi On(string eventName, Func<CarouselApi, Task> handler) => Add(eventName, handler, handler);

    /// <inheritdoc cref="On(string, Func{CarouselApi, Task})"/>
    public CarouselApi On(string eventName, Action<CarouselApi> handler) => Add(eventName, handler, api =>
    {
        handler(api);
        return Task.CompletedTask;
    });

    /// <summary>Removes a handler added with On.</summary>
    public CarouselApi Off(string eventName, Func<CarouselApi, Task> handler) => Remove(eventName, handler);

    /// <inheritdoc cref="Off(string, Func{CarouselApi, Task})"/>
    public CarouselApi Off(string eventName, Action<CarouselApi> handler) => Remove(eventName, handler);

    private CarouselApi Add(string eventName, Delegate key, Func<CarouselApi, Task> run)
    {
        if (!_handlers.TryGetValue(eventName, out var list))
        {
            _handlers[eventName] = list = [];
            _ = ListenAsync(eventName);
        }
        list.Add((key, run));
        return this;
    }

    private CarouselApi Remove(string eventName, Delegate key)
    {
        if (_handlers.TryGetValue(eventName, out var list))
            list.RemoveAll(h => h.Key.Equals(key));
        return this;
    }

    private async Task ListenAsync(string eventName)
    {
        try
        {
            await _controller.InvokeVoidAsync("listen", eventName);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and the carousel with it.
        }
    }

    internal void Update(CarouselState state)
    {
        SelectedScrollSnap = state.SelectedScrollSnap;
        PreviousScrollSnap = state.PreviousScrollSnap;
        ScrollSnapList = state.ScrollSnapList;
        CanScrollPrev = state.CanScrollPrev;
        CanScrollNext = state.CanScrollNext;
        ScrollProgress = state.ScrollProgress;
        SlidesInView = state.SlidesInView;
        SlidesNotInView = state.SlidesNotInView;
    }

    internal async Task EmitAsync(string eventName)
    {
        if (!_handlers.TryGetValue(eventName, out var list))
            return;
        foreach (var (_, run) in list.ToArray())
            await run(this);
    }
}

/// <summary>The carousel's state, as carousel.js reports it.</summary>
internal sealed record CarouselState(
    int SelectedScrollSnap,
    int PreviousScrollSnap,
    double[] ScrollSnapList,
    bool CanScrollPrev,
    bool CanScrollNext,
    double ScrollProgress,
    int[] SlidesInView,
    int[] SlidesNotInView);
