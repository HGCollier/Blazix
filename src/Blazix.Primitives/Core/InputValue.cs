using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// The <c>value</c> attribute of an input the user types into. On Blazor Server a render can arrive
/// after newer keystrokes, and re-rendering the value from what was typed would overwrite them (and
/// move the caret). So typing never changes the rendered value; only programmatic changes do (a
/// parent's new value, a commit, a step), and when the rendered string wouldn't change but the
/// input shows something else, the value is set through JS after the render.
/// </summary>
internal sealed class InputValue(string? initial)
{
    private string? _force;

    /// <summary>What the value attribute renders.</summary>
    public string? Rendered { get; private set; } = initial;

    /// <summary>Show this text in the input, overwriting what the user typed.</summary>
    public void Show(string? text)
    {
        if (text == Rendered)
        {
            _force = text ?? "";
        }
        else
        {
            Rendered = text;
            _force = null;
        }
    }

    /// <summary>The caller wrote the text into the input itself.</summary>
    public void Flushed() => _force = null;

    /// <summary>Call from OnAfterRenderAsync.</summary>
    public async Task FlushAsync(IJSRuntime js, string id)
    {
        if (_force is { } text)
        {
            _force = null;
            await JsModules.InvokeAsync<object>(js, JsModules.Util, "setValue", id, text);
        }
    }
}
