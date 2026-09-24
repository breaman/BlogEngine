using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="LineDiff"/>, the side-by-side comparison on the revision history page (A13, T4.3).
/// </summary>
public class LineDiffTests
{
    /// <summary>Identical texts are all unchanged rows, numbered on both sides.</summary>
    [Test]
    public async Task Identical_AllUnchanged()
    {
        var rows = LineDiff.Compare("one\ntwo", "one\ntwo");

        await Assert.That(rows.Select(r => r.Kind)).IsEquivalentTo([DiffRowKind.Unchanged, DiffRowKind.Unchanged]);
        await Assert.That(rows[1]).IsEqualTo(new DiffRow(DiffRowKind.Unchanged, 2, "two", 2, "two"));
    }

    /// <summary>An inserted line is an added row between unchanged ones, and later lines are renumbered on the new side.</summary>
    [Test]
    public async Task InsertedLine_IsAdded()
    {
        var rows = LineDiff.Compare("a\nc", "a\nb\nc");

        await Assert.That(rows).IsEquivalentTo([
            new DiffRow(DiffRowKind.Unchanged, 1, "a", 1, "a"),
            new DiffRow(DiffRowKind.Added, null, null, 2, "b"),
            new DiffRow(DiffRowKind.Unchanged, 2, "c", 3, "c")
        ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>A deleted line is a removed row.</summary>
    [Test]
    public async Task DeletedLine_IsRemoved()
    {
        var rows = LineDiff.Compare("a\nb\nc", "a\nc");

        await Assert.That(rows[1]).IsEqualTo(new DiffRow(DiffRowKind.Removed, 2, "b", null, null));
        await Assert.That(rows).Count().IsEqualTo(3);
    }

    /// <summary>An edited line appears next to its old version, and extra new lines follow as additions.</summary>
    [Test]
    public async Task EditedLines_ArePaired()
    {
        var rows = LineDiff.Compare("title\nold body\nend", "title\nnew body\nmore\nend");

        await Assert.That(rows).IsEquivalentTo([
            new DiffRow(DiffRowKind.Unchanged, 1, "title", 1, "title"),
            new DiffRow(DiffRowKind.Changed, 2, "old body", 2, "new body"),
            new DiffRow(DiffRowKind.Added, null, null, 3, "more"),
            new DiffRow(DiffRowKind.Unchanged, 3, "end", 4, "end")
        ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>Comparing with an empty text adds or removes every line; empty against empty has no rows.</summary>
    [Test]
    public async Task EmptySides()
    {
        await Assert.That(LineDiff.Compare(null, "a\nb").Select(r => r.Kind)).IsEquivalentTo([DiffRowKind.Added, DiffRowKind.Added]);
        await Assert.That(LineDiff.Compare("a\nb", "").Select(r => r.Kind)).IsEquivalentTo([DiffRowKind.Removed, DiffRowKind.Removed]);
        await Assert.That(LineDiff.Compare("", null)).IsEmpty();
    }

    /// <summary>Windows line endings and a final line break don't count as differences.</summary>
    [Test]
    public async Task LineEndings_AreIgnored()
    {
        var rows = LineDiff.Compare("a\r\nb\r\n", "a\nb");

        await Assert.That(rows.All(r => r.Kind == DiffRowKind.Unchanged)).IsTrue();
        await Assert.That(rows).Count().IsEqualTo(2);
    }

    /// <summary>Unchanged lines between two edits stay aligned, rather than the whole middle being replaced.</summary>
    [Test]
    public async Task SeparateEdits_KeepSharedLinesAligned()
    {
        var rows = LineDiff.Compare("1\n2\n3\n4\n5", "1\nx\n3\n4\ny");

        await Assert.That(rows.Select(r => r.Kind)).IsEquivalentTo([
            DiffRowKind.Unchanged, DiffRowKind.Changed, DiffRowKind.Unchanged, DiffRowKind.Unchanged, DiffRowKind.Changed
        ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>A middle too large to align is shown as removed then re-added, still covering every line once.</summary>
    [Test]
    public async Task HugeRewrite_FallsBackToReplaceAll()
    {
        var size = (int)Math.Sqrt(LineDiff.MaxCells) + 10;
        var oldText = string.Join('\n', Enumerable.Range(0, size).Select(i => $"old {i}"));
        var newText = string.Join('\n', Enumerable.Range(0, size).Select(i => $"new {i}"));

        var rows = LineDiff.Compare(oldText, newText);

        await Assert.That(rows).Count().IsEqualTo(size);
        await Assert.That(rows.All(r => r.Kind == DiffRowKind.Changed)).IsTrue();
        await Assert.That(rows[^1]).IsEqualTo(new DiffRow(DiffRowKind.Changed, size, $"old {size - 1}", size, $"new {size - 1}"));
    }
}
