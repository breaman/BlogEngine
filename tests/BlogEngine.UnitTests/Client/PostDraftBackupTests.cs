using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests the editor's crash-recovery backup (<see cref="PostDraftBackup"/>) and change detection
/// (<see cref="PostEdits"/>) used by the post editor (design 10.2, T1.13).
/// </summary>
public class PostDraftBackupTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 9, 23, 10, 42, 0, TimeSpan.Zero);

    /// <summary>A backup taken from a post restores to an identical post.</summary>
    [Test]
    public async Task From_ThenDiffersFrom_IsFalseForTheSamePost()
    {
        var post = SamplePost();

        var backup = PostDraftBackup.From(post, SavedAt);

        await Assert.That(backup.DiffersFrom(post)).IsFalse();
    }

    /// <summary>A backup with different content is offered for restore, and restoring copies it back.</summary>
    [Test]
    public async Task ApplyTo_RestoresEditedFields()
    {
        var edited = SamplePost();
        edited.ContentMarkdown = "Typed before the crash";
        edited.Tags = ["Blazor", "C#"];
        var backup = PostDraftBackup.From(edited, SavedAt);
        var stored = SamplePost();

        await Assert.That(backup.DiffersFrom(stored)).IsTrue();

        backup.ApplyTo(stored);

        await Assert.That(stored.ContentMarkdown).IsEqualTo("Typed before the crash");
        await Assert.That(stored.Tags).IsEquivalentTo(["Blazor", "C#"]);
    }

    /// <summary>Restoring never brings back server-owned values such as a stale concurrency token.</summary>
    [Test]
    public async Task ApplyTo_KeepsServerValues()
    {
        var backup = PostDraftBackup.From(SamplePost(), SavedAt);
        var stored = SamplePost();
        stored.RowVersion = [9, 9];
        stored.Status = PostStatus.Published;

        backup.ApplyTo(stored);

        await Assert.That(stored.RowVersion).IsEquivalentTo(new byte[] { 9, 9 });
        await Assert.That(stored.Status).IsEqualTo(PostStatus.Published);
    }

    /// <summary>A blank slug or summary means "generate it", so null and empty are the same.</summary>
    [Test]
    public async Task PostEdits_TreatsNullAndEmptyTextAsEqual()
    {
        var left = SamplePost();
        left.Slug = null;
        left.Summary = "";
        var right = SamplePost();
        right.Slug = "";
        right.Summary = null;

        await Assert.That(PostEdits.AreEqual(left, right)).IsTrue();
    }

    /// <summary>Content and details are compared separately, because autosave only stages content on a published post.</summary>
    [Test]
    public async Task PostEdits_SeparatesContentFromDetails()
    {
        var stored = SamplePost();
        var edited = SamplePost();
        edited.Slug = "new-slug";

        await Assert.That(PostEdits.ContentEquals(edited, stored)).IsTrue();
        await Assert.That(PostEdits.DetailsEqual(edited, stored)).IsFalse();
    }

    /// <summary>A clone shares nothing mutable, so editing it never changes the original.</summary>
    [Test]
    public async Task Clone_IsIndependent()
    {
        var original = SamplePost();
        original.PendingChanges = new PostPendingChangesDto { Title = "Pending" };

        var clone = original.Clone();
        clone.Tags.Add("Extra");
        clone.RowVersion![0] = 42;
        clone.PendingChanges!.Title = "Changed";

        await Assert.That(original.Tags).IsEquivalentTo(["C#"]);
        await Assert.That(original.RowVersion![0]).IsEqualTo((byte)1);
        await Assert.That(original.PendingChanges.Title).IsEqualTo("Pending");
    }

    private static PostEditDto SamplePost()
    {
        return new PostEditDto
        {
            Id = 7,
            Title = "Hello",
            Slug = "hello",
            Summary = "Summary",
            ContentMarkdown = "# Hello",
            Tags = ["C#"],
            AllowComments = true,
            RowVersion = [1, 2, 3]
        };
    }
}
