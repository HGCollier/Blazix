namespace Blazix.Registry;

/// <summary>
/// Embla's Autoplay plugin (embla-carousel-autoplay): scrolls to the next snap every Delay milliseconds,
/// and back to the first after the last. Options left null use the plugin's defaults.
/// </summary>
public sealed class CarouselAutoplay : CarouselPlugin
{
    public override string Name => "autoplay";

    /// <summary>Milliseconds between scrolls. Defaults to 4000.</summary>
    public int? Delay { get; init; }

    /// <summary>Jump to the next snap instead of animating.</summary>
    public bool? Jump { get; init; }

    /// <summary>Start playing when the carousel starts. Defaults to true.</summary>
    public bool? PlayOnInit { get; init; }

    /// <summary>Stop when a slide receives focus from Tab. Defaults to true.</summary>
    public bool? StopOnFocusIn { get; init; }

    /// <summary>Stop for good once the user drags. When false, a drag pauses and releasing resumes. Defaults to true.</summary>
    public bool? StopOnInteraction { get; init; }

    /// <summary>Stop while the mouse is over the carousel. Defaults to false.</summary>
    public bool? StopOnMouseEnter { get; init; }

    /// <summary>Stop on reaching the last snap. Defaults to false.</summary>
    public bool? StopOnLastSnap { get; init; }

    /// <summary>Set to false to turn the plugin off.</summary>
    public bool? Active { get; init; }

    /// <summary>Starts playing. Pass jump to override the Jump option.</summary>
    public Task PlayAsync(bool? jump = null) => InvokeVoidAsync("play", jump);

    /// <summary>Stops playing.</summary>
    public Task StopAsync() => InvokeVoidAsync("stop");

    /// <summary>Restarts the timer if playing. Does nothing when stopped.</summary>
    public Task ResetAsync() => InvokeVoidAsync("reset");

    /// <summary>Whether it's playing.</summary>
    public async Task<bool> IsPlayingAsync() => await InvokeAsync<bool>("isPlaying");

    /// <summary>Milliseconds until the next scroll, or null when stopped.</summary>
    public Task<double?> TimeUntilNextAsync() => InvokeAsync<double?>("timeUntilNext");

    protected internal override IReadOnlyDictionary<string, object> GetOptions()
    {
        var options = new Dictionary<string, object>(StringComparer.Ordinal);
        if (Delay is { } delay)
            options["delay"] = delay;
        if (Jump is { } jump)
            options["jump"] = jump;
        if (PlayOnInit is { } playOnInit)
            options["playOnInit"] = playOnInit;
        if (StopOnFocusIn is { } stopOnFocusIn)
            options["stopOnFocusIn"] = stopOnFocusIn;
        if (StopOnInteraction is { } stopOnInteraction)
            options["stopOnInteraction"] = stopOnInteraction;
        if (StopOnMouseEnter is { } stopOnMouseEnter)
            options["stopOnMouseEnter"] = stopOnMouseEnter;
        if (StopOnLastSnap is { } stopOnLastSnap)
            options["stopOnLastSnap"] = stopOnLastSnap;
        if (Active is { } active)
            options["active"] = active;
        return options;
    }
}
