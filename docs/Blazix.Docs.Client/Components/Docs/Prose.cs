namespace Blazix.Docs.Components.Docs;

/// <summary>
/// Styles for text outside the page's typeset (which styles the rest): a callout opts out of
/// typeset, so its inline code is styled here.
/// </summary>
internal static class Prose
{
    /// <summary>Inline code anywhere inside.</summary>
    public const string Code =
        "**:[code]:relative **:[code]:rounded-md **:[code]:bg-muted **:[code]:px-[0.3rem] **:[code]:py-[0.2rem] " +
        "**:[code]:font-mono **:[code]:text-[0.8rem] **:[code]:break-words **:[code]:outline-none";
}
