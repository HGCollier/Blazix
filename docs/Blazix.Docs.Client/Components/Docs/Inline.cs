using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Blazix.Docs.Components.Docs;

/// <summary>The little Markdown the docs' short strings use: `code` in headings and table of contents entries.</summary>
internal static partial class Inline
{
    public static MarkupString Render(string text) =>
        new(Backticks().Replace(WebUtility.HtmlEncode(text), "<code>$1</code>"));

    /// <summary>The text without its backticks.</summary>
    public static string Plain(string text) => text.Replace("`", "");

    /// <summary>Heading ids: whitespace to "-", apostrophes and question marks dropped, lower case.</summary>
    public static string Slug(string text) =>
        Whitespace().Replace(Plain(text).Trim(), "-").Replace("'", "").Replace("?", "").ToLowerInvariant();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Backticks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
