using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="PostSchedule"/>: "scheduled" and "live" are derived from the status and the publish time (design 6.3).
/// </summary>
public class PostScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Each combination of status and publish time maps to exactly one state.</summary>
    [Test]
    [Arguments(PostStatus.Draft, null, false, false)]
    [Arguments(PostStatus.Draft, 1, false, false)]
    [Arguments(PostStatus.Published, 1, true, false)]
    [Arguments(PostStatus.Published, 0, false, true)]
    [Arguments(PostStatus.Published, -1, false, true)]
    [Arguments(PostStatus.Published, null, false, false)]
    public async Task States_FollowStatusAndTime(PostStatus status, int? hoursFromNow, bool scheduled, bool live)
    {
        DateTimeOffset? publishedOn = hoursFromNow is { } hours ? Now.AddHours(hours) : null;

        await Assert.That(PostSchedule.IsScheduled(status, publishedOn, Now)).IsEqualTo(scheduled);
        await Assert.That(PostSchedule.IsLive(status, publishedOn, Now)).IsEqualTo(live);
    }
}
