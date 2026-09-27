namespace BlogEngine.Shared.Text;

/// <summary>How a row of a side-by-side diff differs between the old and the new text.</summary>
public enum DiffRowKind
{
    /// <summary>The line is the same on both sides.</summary>
    Unchanged,

    /// <summary>The line exists only in the old text.</summary>
    Removed,

    /// <summary>The line exists only in the new text.</summary>
    Added,

    /// <summary>An old line was replaced by a new one: both sides are shown next to each other.</summary>
    Changed
}

/// <summary>One row of a side-by-side diff: a line of the old text, of the new text, or of both.</summary>
/// <param name="Kind">How the row differs.</param>
/// <param name="OldNumber">1-based line number in the old text, or <see langword="null"/> for an added line.</param>
/// <param name="OldText">The old line, or <see langword="null"/> for an added line.</param>
/// <param name="NewNumber">1-based line number in the new text, or <see langword="null"/> for a removed line.</param>
/// <param name="NewText">The new line, or <see langword="null"/> for a removed line.</param>
public sealed record DiffRow(DiffRowKind Kind, int? OldNumber, string? OldText, int? NewNumber, string? NewText);

/// <summary>
/// A line-by-line diff of two texts for the side-by-side revision comparison (design 7.3, A13, T4.3).
/// </summary>
/// <remarks>
/// <para>
/// Lines common to the start and the end are matched first, then the changed middle is aligned with a longest common
/// subsequence. That is quadratic in the size of the middle, so a middle larger than <see cref="MaxCells"/> (a rewrite
/// of thousands of lines) is shown as removed and re-added instead of risking memory in the browser, where this runs.
/// </para>
/// <para>
/// Removals followed by additions are paired into <see cref="DiffRowKind.Changed"/> rows, so an edited line appears
/// next to its old version rather than far below it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var rows = LineDiff.Compare(revision.ContentMarkdown, post.ContentMarkdown);
/// var changed = rows.Count(r => r.Kind != DiffRowKind.Unchanged);
/// </code>
/// </example>
public static class LineDiff
{
    /// <summary>
    /// Largest changed middle (old lines times new lines) aligned line by line; about 8 MB of working memory.
    /// </summary>
    public const long MaxCells = 2_000_000;

    /// <summary>Compares <paramref name="oldText"/> with <paramref name="newText"/> line by line.</summary>
    /// <param name="oldText">The earlier text; <see langword="null"/> is treated as empty.</param>
    /// <param name="newText">The later text; <see langword="null"/> is treated as empty.</param>
    /// <returns>Rows in document order, covering every line of both texts.</returns>
    public static IReadOnlyList<DiffRow> Compare(string? oldText, string? newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
        {
            suffix++;
        }

        var edits = new List<Edit>(oldLines.Length + newLines.Length);
        for (var i = 0; i < prefix; i++)
        {
            edits.Add(new Edit(EditKind.Equal, i, i));
        }

        AlignMiddle(oldLines, prefix, oldLines.Length - suffix, newLines, prefix, newLines.Length - suffix, edits);

        for (var i = suffix; i > 0; i--)
        {
            edits.Add(new Edit(EditKind.Equal, oldLines.Length - i, newLines.Length - i));
        }

        return ToRows(edits, oldLines, newLines);
    }

    /// <summary>Splits text into lines, accepting any line ending; a final line break doesn't add an empty line.</summary>
    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Aligns the changed middle, <c>old[oldStart..oldEnd)</c> against <c>new[newStart..newEnd)</c>.</summary>
    private static void AlignMiddle(string[] oldLines, int oldStart, int oldEnd, string[] newLines, int newStart, int newEnd,
        List<Edit> edits)
    {
        var n = oldEnd - oldStart;
        var m = newEnd - newStart;

        if (n == 0 || m == 0 || (long)n * m > MaxCells)
        {
            for (var i = oldStart; i < oldEnd; i++)
            {
                edits.Add(new Edit(EditKind.Delete, i, -1));
            }

            for (var j = newStart; j < newEnd; j++)
            {
                edits.Add(new Edit(EditKind.Insert, -1, j));
            }

            return;
        }

        // lengths[i, j] = length of the longest common subsequence of old[oldStart + i..] and new[newStart + j..].
        var width = m + 1;
        var lengths = new int[(n + 1) * width];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lengths[(i * width) + j] = oldLines[oldStart + i] == newLines[newStart + j]
                    ? lengths[((i + 1) * width) + j + 1] + 1
                    : Math.Max(lengths[((i + 1) * width) + j], lengths[(i * width) + j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (oldLines[oldStart + x] == newLines[newStart + y])
            {
                edits.Add(new Edit(EditKind.Equal, oldStart + x, newStart + y));
                x++;
                y++;
            }
            else if (lengths[((x + 1) * width) + y] >= lengths[(x * width) + y + 1])
            {
                // Deletions first, so a replaced block reads as "old lines, then new lines" and pairs up below.
                edits.Add(new Edit(EditKind.Delete, oldStart + x, -1));
                x++;
            }
            else
            {
                edits.Add(new Edit(EditKind.Insert, -1, newStart + y));
                y++;
            }
        }

        for (; x < n; x++)
        {
            edits.Add(new Edit(EditKind.Delete, oldStart + x, -1));
        }

        for (; y < m; y++)
        {
            edits.Add(new Edit(EditKind.Insert, -1, newStart + y));
        }
    }

    /// <summary>Turns the edit script into rows, pairing each run of deletions with the insertions that follow it.</summary>
    private static List<DiffRow> ToRows(List<Edit> edits, string[] oldLines, string[] newLines)
    {
        var rows = new List<DiffRow>(edits.Count);
        var removed = new List<int>();
        var added = new List<int>();

        void Flush()
        {
            for (var k = 0; k < Math.Max(removed.Count, added.Count); k++)
            {
                int? oldIndex = k < removed.Count ? removed[k] : null;
                int? newIndex = k < added.Count ? added[k] : null;
                var kind = (oldIndex, newIndex) switch
                {
                    (not null, not null) => DiffRowKind.Changed,
                    (not null, null) => DiffRowKind.Removed,
                    _ => DiffRowKind.Added
                };

                rows.Add(new DiffRow(kind,
                    oldIndex + 1, oldIndex is { } o ? oldLines[o] : null,
                    newIndex + 1, newIndex is { } n ? newLines[n] : null));
            }

            removed.Clear();
            added.Clear();
        }

        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case EditKind.Delete:
                    removed.Add(edit.OldIndex);
                    break;
                case EditKind.Insert:
                    added.Add(edit.NewIndex);
                    break;
                default:
                    Flush();
                    rows.Add(new DiffRow(DiffRowKind.Unchanged, edit.OldIndex + 1, oldLines[edit.OldIndex], edit.NewIndex + 1,
                        newLines[edit.NewIndex]));
                    break;
            }
        }

        Flush();
        return rows;
    }

    private enum EditKind
    {
        Equal,
        Delete,
        Insert
    }

    /// <summary>One step of the edit script: 0-based line indexes, -1 for the side the step doesn't touch.</summary>
    private readonly record struct Edit(EditKind Kind, int OldIndex, int NewIndex);
}