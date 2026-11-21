using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>Writes cookies from the browser, e.g. to remember a sidebar's state across visits.</summary>
public static class BrowserCookies
{
    /// <summary>Sets a cookie for the whole site, kept for <paramref name="maxAge"/>.</summary>
    public static async Task SetAsync(IJSRuntime js, string name, string value, TimeSpan maxAge)
    {
        try
        {
            await JsModules.InvokeAsync<object>(js, JsModules.Util, "setCookie", name, value, (long)maxAge.TotalSeconds);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there's no browser yet.
        }
    }
}

/// <summary>Copies text to the clipboard from the browser (a code block's copy button...).</summary>
public static class BrowserClipboard
{
    /// <summary>Copies <paramref name="text"/>; false if the browser refused (no permission, no secure context).</summary>
    public static async Task<bool> CopyAsync(IJSRuntime js, string text)
    {
        try
        {
            return await JsModules.InvokeAsync<bool>(js, JsModules.Util, "copyText", text);
        }
        catch (InvalidOperationException)
        {
            // Prerendering: there's no browser yet.
            return false;
        }
    }
}
