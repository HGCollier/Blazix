using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// Whether a CSS media query matches, kept up to date:
/// <c>&lt;MediaQuery Query="(min-width: 768px)" Context="desktop"&gt;...&lt;/MediaQuery&gt;</c>.
/// Until the browser has answered (and while prerendering), <see cref="DefaultMatches"/> applies.
/// </summary>
[PublicAPI]
public sealed class MediaQuery : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<MediaQueryInterop>? _interop;
    private IJSObjectReference? _watch;
    private string? _watching;
    private int _setup;
    private bool? _matches;
    private bool _disposed;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>The media query, e.g. <c>(prefers-reduced-motion: reduce)</c>.</summary>
    [Parameter, EditorRequired] public string Query { get; set; } = "";

    /// <summary>What to assume before the browser answers. Defaults to false.</summary>
    [Parameter] public bool DefaultMatches { get; set; }

    /// <summary>Renders with whether the query matches.</summary>
    [Parameter] public RenderFragment<bool>? ChildContent { get; set; }

    /// <summary>Called when the answer changes.</summary>
    [Parameter] public EventCallback<bool> MatchesChanged { get; set; }

    /// <summary>Whether the query matches (or <see cref="DefaultMatches"/> before the browser answered).</summary>
    public bool Matches => _matches ?? DefaultMatches;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent?.Invoke(Matches));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_watching == Query)
        {
            return;
        }
        var query = _watching = Query;
        var setup = ++_setup;
        await StopAsync();
        var module = await JsModules.EnsureAsync(JsRuntime, JsModules.Util);
        // Removed, or given another query, while the module loaded. The first import can take a while (in Firefox
        // especially), long enough to navigate away: the interop reference may be disposed already.
        if (module is null || _disposed || setup != _setup)
        {
            return;
        }
        _interop ??= DotNetObjectReference.Create(new MediaQueryInterop(this));
        try
        {
            var watch = await module.InvokeAsync<IJSObjectReference>("watchMediaQuery", _interop, query);
            if (_disposed || setup != _setup)
            {
                // ...or while the browser started watching: nothing else will stop this watch.
                await StopAsync(watch);
                return;
            }
            _watch = watch;
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            // The circuit went away; nothing to watch.
        }
    }

    internal Task ChangedAsync(bool matches) => InvokeAsync(async () =>
    {
        if (_matches == matches)
        {
            return;
        }
        _matches = matches;
        await MatchesChanged.InvokeAsync(matches);
        StateHasChanged();
    });

    // Takes the watch first, so a new query and disposal, both stopping it at once, can't stop it twice.
    private Task StopAsync()
    {
        var watch = _watch;
        _watch = null;
        return StopAsync(watch);
    }

    private static async Task StopAsync(IJSObjectReference? watch)
    {
        if (watch is null)
        {
            return;
        }
        try
        {
            await watch.InvokeVoidAsync("dispose");
            await watch.DisposeAsync();
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            // Already gone with the circuit.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await StopAsync();
        _interop?.Dispose();
    }
}

internal sealed class MediaQueryInterop(MediaQuery owner)
{
    [JSInvokable]
    public Task Changed(bool matches) => owner.ChangedAsync(matches);
}
