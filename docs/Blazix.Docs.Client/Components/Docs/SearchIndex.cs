using System.Net;
using System.Text.RegularExpressions;

namespace Blazix.Docs.Components.Docs;

/// <summary>A place in the docs that matches a search: the text, and the page section it's in.</summary>
internal sealed record SearchResult(string Text, string Href, string Page);

/// <summary>
/// Full-text search over the docs pages. The pages' Razor sources are embedded at build
/// time, and their headings, paragraphs and list items are read from them on first use, so the index follows the pages.
/// </summary>
internal static partial class SearchIndex
{
    private sealed record Entry(string Text, string Href, string Page);

    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(Build);

    /// <summary>Up to <paramref name="limit"/> places whose text contains the query, headings first.</summary>
    public static IReadOnlyList<SearchResult> Find(string query, int limit = 8)
    {
        query = query.Trim();
        if (query.Length < 2)
        {
            return [];
        }
        return Entries.Value
            .Where(e => e.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(e => new SearchResult(Snippet(e.Text, query), e.Href, e.Page))
            .DistinctBy(r => (r.Text, r.Href))
            .Take(limit)
            .ToList();
    }

    private static List<Entry> Build()
    {
        var assembly = typeof(SearchIndex).Assembly;
        var entries = new List<Entry>();
        var paragraphs = new List<Entry>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("pages/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            Read(reader.ReadToEnd(), entries, paragraphs);
        }
        // Headings rank above running text.
        entries.AddRange(paragraphs);
        return entries;
    }

    private static void Read(string source, List<Entry> headings, List<Entry> paragraphs)
    {
        var route = PageRoute().Match(source);
        var title = PageTitle().Match(source);
        if (!route.Success || !title.Success)
        {
            return;
        }
        var markup = source.Split("\n@code {")[0];
        var href = route.Groups[1].Value;
        var page = Decode(title.Groups[1].Value);
        var anchor = href;
        foreach (var groups in Block().Matches(markup).Select(m => m.Groups))
        {
            if (groups["heading"].Success)
            {
                var text = Decode(groups["heading"].Value.Replace("`", ""));
                var id = groups["id"].Success ? groups["id"].Value : Inline.Slug(text);
                anchor = $"{href}#{id}";
                headings.Add(new Entry(text, anchor, page));
            }
            else
            {
                var text = Decode(Tags().Replace(groups["text"].Value, ""));
                if (text.Length > 0)
                {
                    paragraphs.Add(new Entry(text, anchor, page));
                }
            }
        }
    }

    private static string Decode(string text) =>
        Whitespace().Replace(WebUtility.HtmlDecode(text.Replace("@@", "@")), " ").Trim();

    /// <summary>The text around the match, so a long paragraph shows the part that matched.</summary>
    private static string Snippet(string text, string query)
    {
        const int room = 90;
        if (text.Length <= room)
        {
            return text;
        }
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, Math.Min(at - room / 3, text.Length - room));
        var snippet = text.Substring(start, room).Trim();
        return (start > 0 ? "…" : "") + snippet + (start + room < text.Length ? "…" : "");
    }

    [GeneratedRegex("""@page "([^"]+)"[\r\n]""")]
    private static partial Regex PageRoute();

    [GeneratedRegex("<DocsPage Title=\"([^\"]+)\"")]
    private static partial Regex PageTitle();

    [GeneratedRegex("""<DocsHeading(?: Level="\d")? Title="(?<heading>[^"]+)"(?: Id="(?<id>[^"]+)")?|<(?:p|li)(?:\s[^>]*)?>(?<text>.*?)</(?:p|li)>""",
        RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
