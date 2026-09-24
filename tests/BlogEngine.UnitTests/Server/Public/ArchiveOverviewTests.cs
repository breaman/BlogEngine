using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Tests <see cref="ArchiveOverview"/>: the years and months of <c>/archive</c> (P11, T4.12).</summary>
public class ArchiveOverviewTests
{
    /// <summary>Posts are grouped by local year and month with counts, newest first at both levels.</summary>
    [Test]
    public async Task From_GroupsByYearAndMonth_NewestFirst()
    {
        var years = ArchiveOverview.From(
        [
            PublicTestData.Post(1, new DateOnly(2026, 9, 22)),
            PublicTestData.Post(2, new DateOnly(2026, 9, 1)),
            PublicTestData.Post(3, new DateOnly(2026, 2, 14)),
            PublicTestData.Post(4, new DateOnly(2024, 12, 31))
        ]);

        await Assert.That(years.Select(y => (y.Year, y.PostCount))).IsEquivalentTo([(2026, 3), (2024, 1)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(years[0].Months.Select(m => (m.Month, m.PostCount))).IsEquivalentTo([(9, 2), (2, 1)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(years[0].Path).IsEqualTo("/posts/2026");
        await Assert.That(years[0].Months[1].Path).IsEqualTo("/posts/2026/02");
        await Assert.That(years[0].Months[1].Name).IsEqualTo("February");
    }

    /// <summary>No posts, no years.</summary>
    [Test]
    public async Task From_NoPosts_IsEmpty()
    {
        await Assert.That(ArchiveOverview.From([])).IsEmpty();
    }
}
