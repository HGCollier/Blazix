using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// Behavior modules that install delegated listeners once per page (roving focus, non-native
/// buttons). Imported on first use and remembered per JS runtime, so each costs one round trip.
/// </summary>
internal static class JsModules
{
    private static readonly ConditionalWeakTable<IJSRuntime, ConcurrentDictionary<string, Task<IJSObjectReference?>>> Loaded = new();

    public const string Roving = "core/roving.js";
    public const string Button = "core/button.js";
    public const string Panel = "panel.js";
    public const string Util = "core/util.js";

    public static Task<IJSObjectReference?> EnsureAsync(IJSRuntime js, string module) =>
        Loaded.GetOrCreateValue(js).GetOrAdd(module, path => ImportAsync(js, path));

    private static async Task<IJSObjectReference?> ImportAsync(IJSRuntime js, string path)
    {
        try
        {
            return await js.InvokeAsync<IJSObjectReference>("import", Js.Root + path);
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The ids of the &lt;label&gt;s of the element with this id, space-separated, or null when it has none: what names a
    /// control a label can't reach by itself. Best effort: a failure in the browser leaves the control as it was.
    /// </summary>
    public static async Task<string?> LabelIdsAsync(IJSRuntime js, string id)
    {
        try
        {
            var ids = await InvokeAsync<string[]>(js, Button, "labelIds", id);
            return ids is { Length: > 0 } ? string.Join(' ', ids) : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    public static async Task<T?> InvokeAsync<[DynamicallyAccessedMembers(Js.Deserialized)] T>(IJSRuntime js, string module, string method, params object?[] args)
    {
        var reference = await EnsureAsync(js, module);
        if (reference is null)
        {
            return default;
        }

        try
        {
            return await reference.InvokeAsync<T>(method, args);
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return default;
        }
    }
}
