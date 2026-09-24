using BlogEngine.Client.Services;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="CommentCountNotifier"/>, which keeps the admin nav badge in step with moderation (T3.10).
/// </summary>
public class CommentCountNotifierTests
{
    /// <summary>Listeners hear about changes only, not repeats of the same count.</summary>
    [Test]
    public async Task ReportPending_NotifiesOnChange()
    {
        var notifier = new CommentCountNotifier();
        var heard = new List<int>();
        notifier.PendingCountChanged += heard.Add;

        notifier.ReportPending(3);
        notifier.ReportPending(3);
        notifier.ReportPending(0);

        await Assert.That(heard).IsEquivalentTo([3, 0]);
        await Assert.That(notifier.PendingCount).IsEqualTo(0);
    }
}
