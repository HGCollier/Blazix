namespace Blazix.Cli;

/// <summary>
/// The line changes from one text to another, shown as <c>diff -u</c> shows them: a <c>-</c> line is in the first text
/// only, a <c>+</c> line in the second only, and three unchanged lines of context surround each change. Line endings
/// don't count (CRLF and LF read alike), nor does a missing final newline, so a copy made on Windows matches the same
/// text from a Linux checkout.
/// </summary>
internal static class LineDiff
{
    private const int Context = 3;

    // The alignment table has a cell per pair of lines in the changed middle. Beyond this many cells, the middle is
    // shown as removed and then added, without alignment, rather than allocating the table.
    private const long MaxCells = 4_000_000;

    /// <param name="Added">The lines of the second text that the first doesn't have.</param>
    /// <param name="Removed">The lines of the first text that the second doesn't have.</param>
    /// <param name="Unified">The diff's lines, headers first; empty when the texts have the same lines.</param>
    public sealed record Changes(int Added, int Removed, IReadOnlyList<string> Unified)
    {
        public bool IsEmpty => Added == 0 && Removed == 0;
    }

    private enum Kind { Same, Removed, Added }

    private sealed record Line(Kind Kind, string Text);

    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/>, named in the headers.</summary>
    public static Changes Compare(string before, string after, string beforeName, string afterName)
    {
        var lines = Align(Split(before), Split(after));
        var added = lines.Count(line => line.Kind == Kind.Added);
        var removed = lines.Count(line => line.Kind == Kind.Removed);
        return new Changes(added, removed, added + removed == 0 ? [] : UnifiedLines(lines, beforeName, afterName));
    }

    // The lines of a text. A final newline ends the last line rather than starting another, so "a\n" is one line.
    private static string[] Split(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }

    // Lines the texts share at their start and end stay as they are; only the middle between them is aligned.
    private static List<Line> Align(string[] before, string[] after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        var lines = new List<Line>(Math.Max(before.Length, after.Length));
        lines.AddRange(before[..prefix].Select(text => new Line(Kind.Same, text)));
        Middle(before[prefix..^suffix], after[prefix..^suffix], lines);
        lines.AddRange(before[^suffix..].Select(text => new Line(Kind.Same, text)));
        return lines;
    }

    // Aligns the lines along a longest common subsequence. Within each run of changes, the removed lines come before the
    // added ones, as diff writes them.
    private static void Middle(string[] before, string[] after, List<Line> lines)
    {
        var n = before.Length;
        var m = after.Length;
        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            lines.AddRange(before.Select(text => new Line(Kind.Removed, text)));
            lines.AddRange(after.Select(text => new Line(Kind.Added, text)));
            return;
        }

        // common[i, j]: the length of the longest common subsequence of before[i..] and after[j..].
        var common = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                common[i, j] = before[i] == after[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var removed = new List<string>();
        var added = new List<string>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (before[x] == after[y])
            {
                Flush(lines, removed, added);
                lines.Add(new Line(Kind.Same, before[x]));
                x++;
                y++;
            }
            else if (common[x + 1, y] >= common[x, y + 1])
            {
                removed.Add(before[x++]);
            }
            else
            {
                added.Add(after[y++]);
            }
        }
        removed.AddRange(before[x..]);
        added.AddRange(after[y..]);
        Flush(lines, removed, added);
    }

    private static void Flush(List<Line> lines, List<string> removed, List<string> added)
    {
        lines.AddRange(removed.Select(text => new Line(Kind.Removed, text)));
        lines.AddRange(added.Select(text => new Line(Kind.Added, text)));
        removed.Clear();
        added.Clear();
    }

    // The headers and lines of each hunk. A hunk's range in each text starts at its first line there, or, when the hunk
    // has none of that text's lines, at the line before it, as diff writes it: @@ -0,0 +1 @@ adds a file's first line.
    private static List<string> UnifiedLines(List<Line> lines, string beforeName, string afterName)
    {
        var beforeAt = new int[lines.Count + 1];
        var afterAt = new int[lines.Count + 1];
        for (var i = 0; i < lines.Count; i++)
        {
            beforeAt[i + 1] = beforeAt[i] + (lines[i].Kind == Kind.Added ? 0 : 1);
            afterAt[i + 1] = afterAt[i] + (lines[i].Kind == Kind.Removed ? 0 : 1);
        }

        var output = new List<string> { "--- " + beforeName, "+++ " + afterName };
        foreach (var (start, end) in Hunks(lines))
        {
            var beforeCount = beforeAt[end + 1] - beforeAt[start];
            var afterCount = afterAt[end + 1] - afterAt[start];
            output.Add($"@@ -{HunkRange(beforeAt[start], beforeCount)} +{HunkRange(afterAt[start], afterCount)} @@");
            for (var i = start; i <= end; i++)
            {
                output.Add(Marker(lines[i].Kind) + lines[i].Text);
            }
        }
        return output;
    }

    // Each change with its context. Changes whose contexts meet or overlap share a hunk.
    private static List<(int Start, int End)> Hunks(List<Line> lines)
    {
        var hunks = new List<(int Start, int End)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == Kind.Same)
            {
                continue;
            }
            var start = Math.Max(0, i - Context);
            var end = Math.Min(lines.Count - 1, i + Context);
            if (hunks.Count > 0 && start <= hunks[^1].End + 1)
            {
                hunks[^1] = (hunks[^1].Start, end);
            }
            else
            {
                hunks.Add((start, end));
            }
        }
        return hunks;
    }

    // "start,count" as a hunk header writes it: the count is left out when it's one, and a range with
    // no lines names the line before it.
    private static string HunkRange(int linesBefore, int count) => count switch
    {
        0 => $"{linesBefore},0",
        1 => $"{linesBefore + 1}",
        _ => $"{linesBefore + 1},{count}",
    };

    private static string Marker(Kind kind) => kind switch
    {
        Kind.Removed => "-",
        Kind.Added => "+",
        _ => " ",
    };
}
