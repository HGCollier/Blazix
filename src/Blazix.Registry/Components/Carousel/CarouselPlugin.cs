using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Blazix.Registry;

/// <summary>
/// An Embla plugin for a Carousel's <c>Plugins</c>. Name picks the JavaScript plugin, which carousel.js
/// creates with GetOptions(). Keep one instance per carousel, e.g. in a field, as you would with useRef.
/// </summary>
public abstract class CarouselPlugin
{
    private IJSObjectReference? _controller;

    /// <summary>The plugin's name in carousel.js, e.g. "autoplay". Register more with its <c>registerPlugin</c>.</summary>
    public abstract string Name { get; }

    /// <summary>The options passed to the JavaScript plugin. Leave out unset options.</summary>
    protected internal abstract IReadOnlyDictionary<string, object> GetOptions();

    internal void Attach(IJSObjectReference? controller) => _controller = controller;

    /// <summary>Calls a method on the running JavaScript plugin. Does nothing before the carousel starts.</summary>
    protected async Task InvokeVoidAsync(string method, params object?[] args)
    {
        if (_controller is null)
            return;
        try
        {
            await _controller.InvokeVoidAsync("plugin", Name, method, args);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and the carousel with it.
        }
    }

    /// <summary>Calls a method on the running JavaScript plugin and returns its result, or default before the carousel starts.</summary>
    protected async Task<T?> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        string method, params object?[] args)
    {
        if (_controller is null)
            return default;
        try
        {
            return await _controller.InvokeAsync<T>("plugin", Name, method, args);
        }
        catch (JSDisconnectedException)
        {
            return default;
        }
    }
}
