using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// A component's handle on its browser-side controller: lazily imports the module (it ships in this
/// package as a static web asset, so there's no &lt;script&gt; tag), creates the controller once,
/// and ignores calls made after the Blazor Server circuit went away.
/// </summary>
internal sealed class JsController(IJSRuntime js, string module, string factory) : IAsyncDisposable
{
    private Task<IJSObjectReference?>? _creation;
    private IJSObjectReference? _module;
    private IJSObjectReference? _controller;
    private bool _disposed;

    public bool IsCreated => _controller is not null;

    /// <summary>Creates the controller the first time; later calls return the same one.</summary>
    public Task<IJSObjectReference?> EnsureAsync(params object?[] args) => _creation ??= CreateAsync(args);

    private async Task<IJSObjectReference?> CreateAsync(object?[] args)
    {
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", Js.Root + module);
            _controller = await _module.InvokeAsync<IJSObjectReference>(factory, args);
            if (_disposed)
            {
                await DisposeRefsAsync();
                return null;
            }
            return _controller;
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    public async Task InvokeVoidAsync(string method, params object?[] args)
    {
        var controller = _creation is null ? null : await _creation;
        if (controller is null || _disposed)
        {
            return;
        }

        try
        {
            await controller.InvokeVoidAsync(method, args);
        }
        catch (Exception e) when (IsGone(e))
        {
            // The browser side is gone; nothing left to update.
        }
    }

    public async Task<T?> InvokeAsync<[DynamicallyAccessedMembers(Js.Deserialized)] T>(string method, params object?[] args)
    {
        var controller = _creation is null ? null : await _creation;
        if (controller is null || _disposed)
        {
            return default;
        }

        try
        {
            return await controller.InvokeAsync<T>(method, args);
        }
        catch (Exception e) when (IsGone(e))
        {
            return default;
        }
    }

    private static bool IsGone(Exception e) =>
        e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException;

    private async Task DisposeRefsAsync()
    {
        try
        {
            if (_controller is not null)
            {
                await _controller.InvokeVoidAsync("dispose");
                await _controller.DisposeAsync();
            }
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (Exception e) when (IsGone(e) || e is JSException)
        {
            // Already gone.
        }
        _controller = null;
        _module = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_creation is not null)
        {
            try
            {
                await _creation;
            }
            catch (Exception e) when (IsGone(e))
            {
                // Never created.
            }
        }
        await DisposeRefsAsync();
    }
}
