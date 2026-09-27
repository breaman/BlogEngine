using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="ArchivePeriod"/>: which archive URLs name a real period, their canonical paths and parents,
/// and which dates they contain (design 7.1, P2, P4).
/// </summary>
public class ArchivePeriodTests
{
    /// <summary>Real years, months and days are accepted, including leap days.</summary>
    [Test]
    [Arguments(2026, null, null)]
    [Arguments(2026, 9, null)]
    [Arguments(2026, 9, 30)]
    [Arguments(2024, 2, 29)]
    [Arguments(1900, 1, 1)]
    [Arguments(9999, 12, 31)]
    public async Task TryCreate_ValidPeriod_ReturnsPeriod(int year, int? month, int? day)
    {
        await Assert.That(ArchivePeriod.TryCreate(year, month, day)).IsNotNull();
    }

    /// <summary>Impossible dates and years outside the archive range are rejected, as is a day without a month.</summary>
    [Test]
    [Arguments(1899, null, null)]
    [Arguments(10000, null, null)]
    [Arguments(2026, 0, null)]
    [Arguments(2026, 13, null)]
    [Arguments(2026, 2, 29)]
    [Arguments(2026, 9, 31)]
    [Arguments(2026, 9, 0)]
    [Arguments(2026, null, 5)]
    public async Task TryCreate_InvalidPeriod_ReturnsNull(int year, int? month, int? day)
    {
        await Assert.That(ArchivePeriod.TryCreate(year, month, day)).IsNull();
    }

    /// <summary>Paths are always zero-padded, and each level's parent is the enclosing period.</summary>
    [Test]
    public async Task PathAndParent_FollowTheHierarchy()
    {
        var day = ArchivePeriod.TryCreate(2026, 9, 2)!;

        await Assert.That(day.Path).IsEqualTo("/posts/2026/09/02");
        await Assert.That(day.Parent!.Path).IsEqualTo("/posts/2026/09");
        await Assert.That(day.Parent.Parent!.Path).IsEqualTo("/posts/2026");
        await Assert.That(day.Parent.Parent.Parent).IsNull();
        await Assert.That(day.Start).IsEqualTo(new DateOnly(2026, 9, 2));
    }

    /// <summary>A period contains exactly the dates inside it.</summary>
    [Test]
    public async Task Contains_MatchesOnlyDatesInThePeriod()
    {
        var month = ArchivePeriod.TryCreate(2026, 9, null)!;
        var year = ArchivePeriod.TryCreate(2026, null, null)!;

        await Assert.That(month.Contains(new DateOnly(2026, 9, 1))).IsTrue();
        await Assert.That(month.Contains(new DateOnly(2026, 9, 30))).IsTrue();
        await Assert.That(month.Contains(new DateOnly(2026, 10, 1))).IsFalse();
        await Assert.That(month.Contains(new DateOnly(2025, 9, 15))).IsFalse();
        await Assert.That(year.Contains(new DateOnly(2026, 12, 31))).IsTrue();
        await Assert.That(year.Contains(new DateOnly(2027, 1, 1))).IsFalse();
    }
}