using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// Runs <see cref="OnPressed"/> when a key is pressed with Ctrl (⌘ on macOS) anywhere on the page,
/// e.g. Ctrl+B to toggle a sidebar.
/// Renders nothing.
/// </summary>
public sealed class KeyboardShortcut : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<KeyboardShortcutInterop>? _interop;
    private IJSObjectReference? _watch;
    private string? _watching;
    private int _setup;
    private bool _disposed;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>The key, as <c>KeyboardEvent.key</c> reports it (case-insensitive), e.g. <c>"b"</c>.</summary>
    [Parameter, EditorRequired] public string Key { get; set; } = "";

    /// <summary>Whether Ctrl or ⌘ must be held. Defaults to true.</summary>
    [Parameter] public bool CtrlOrMeta { get; set; } = true;

    /// <summary>Whether Shift must be held too. Defaults to false.</summary>
    [Parameter] public bool Shift { get; set; }

    /// <summary>Whether Alt (Option on macOS) must be held too. Defaults to false.</summary>
    [Parameter] public bool Alt { get; set; }

    /// <summary>Stop the browser's own handling of the shortcut. Defaults to true.</summary>
    [Parameter] public bool PreventDefault { get; set; } = true;

    /// <summary>Runs when the shortcut is pressed.</summary>
    [Parameter] public EventCallback OnPressed { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var key = $"{Key}|{CtrlOrMeta}|{Shift}|{Alt}|{PreventDefault}";
        if (_watching == key)
        {
            return;
        }
        _watching = key;
        var setup = ++_setup;
        // A dictionary, not an anonymous type: JS interop reflects over the argument, and trimming removes an
        // anonymous type's members.
        var options = new Dictionary<string, object?> { ["ctrlOrMeta"] = CtrlOrMeta, ["shift"] = Shift, ["alt"] = Alt, ["preventDefault"] = PreventDefault };
        var shortcutKey = Key;
        await StopAsync();
        var module = await JsModules.EnsureAsync(JsRuntime, JsModules.Util);
        // Removed, or given another shortcut, while the module loaded. The first import can take a while (in Firefox
        // especially), long enough to navigate away: the interop reference may be disposed already.
        if (module is null || _disposed || setup != _setup)
        {
            return;
        }
        _interop ??= DotNetObjectReference.Create(new KeyboardShortcutInterop(this));
        try
        {
            var watch = await module.InvokeAsync<IJSObjectReference>("watchShortcut", _interop, shortcutKey, options);
            if (_disposed || setup != _setup)
            {
                // ...or while the browser started listening: nothing else will stop this watch.
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

    internal Task PressedAsync() => InvokeAsync(() => OnPressed.InvokeAsync());

    // Takes the watch first, so a new shortcut and disposal, both stopping it at once, can't stop it twice.
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

internal sealed class KeyboardShortcutInterop(KeyboardShortcut owner)
{
    [JSInvokable]
    public Task Pressed() => owner.PressedAsync();
}
