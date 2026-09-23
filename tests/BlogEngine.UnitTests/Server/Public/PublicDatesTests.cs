using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>
/// Tests <see cref="PublicDates"/>: dates on public pages follow the date format setting (design 13).
/// </summary>
public class PublicDatesTests
{
    private static readonly DateOnly Date = new(2026, 9, 2);

    /// <summary>The setting's custom format is used, with invariant (English) month names.</summary>
    [Test]
    [Arguments("MMMM d, yyyy", "September 2, 2026")]
    [Arguments("d MMM yyyy", "2 Sep 2026")]
    [Arguments("yyyy-MM-dd", "2026-09-02")]
    public async Task Format_UsesSetting(string format, string expected)
    {
        await Assert.That(PublicDates.Format(Date, format)).IsEqualTo(expected);
    }

    /// <summary>A blank format, or one that can't format a date, falls back to the default instead of failing.</summary>
    [Test]
    [Arguments("")]
    [Arguments(null)]
    [Arguments("HH:mm")]
    public async Task Format_UnusableFormat_FallsBackToDefault(string? format)
    {
        await Assert.That(PublicDates.Format(Date, format)).IsEqualTo("September 2, 2026");
    }

    /// <summary>The machine-readable and month forms.</summary>
    [Test]
    public async Task OtherForms()
    {
        await Assert.That(PublicDates.Iso(Date)).IsEqualTo("2026-09-02");
        await Assert.That(PublicDates.MonthYear(2026, 9)).IsEqualTo("September 2026");
        await Assert.That(PublicDates.MonthName(3)).IsEqualTo("March");
    }
}
