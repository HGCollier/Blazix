using Microsoft.JSInterop;

namespace Blazix.Docs.Components.Docs;

/// <summary>wwwroot/site.js, the site chrome's browser code, imported once per circuit.</summary>
internal sealed class SiteScript(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    /// <summary>The module. Pass <c>Assets["site.js"]</c>, the URL App.razor's script tag loads, so both share one instance.</summary>
    public Task<IJSObjectReference> GetAsync(string url) => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./" + url).AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone, and the module with it.
            }
        }
    }
}
