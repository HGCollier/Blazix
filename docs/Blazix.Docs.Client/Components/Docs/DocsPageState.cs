namespace Blazix.Docs.Components.Docs;

/// <summary>A heading the table of contents links to.</summary>
internal sealed record TocEntry(string Id, string Title, int Depth);

/// <summary>What a docs page shares with its headings and table of contents.</summary>
internal sealed class DocsPageState
{
    private readonly List<TocEntry> _toc = [];

    public IReadOnlyList<TocEntry> Toc => _toc;

    public event Action? Changed;

    public void Add(TocEntry entry)
    {
        if (_toc.Exists(e => e.Id == entry.Id))
        {
            return;
        }
        _toc.Add(entry);
        Changed?.Invoke();
    }
}
