using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests.Common;

/// <summary>
/// Tests <see cref="TimeZoneChoices"/>, the settings page's time zone picker (T1.15, Q10).
/// </summary>
public class TimeZoneChoicesTests
{
    private static readonly DateTimeOffset Summer = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Winter = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>IANA region ids are offered, with the default UTC.</summary>
    [Test]
    public async Task GetAll_OffersUtcAndIanaRegions()
    {
        var ids = TimeZoneChoices.GetAll(Summer).Select(c => c.Id).ToList();

        await Assert.That(ids).Contains("UTC");
        await Assert.That(ids).Contains("America/Chicago");
        await Assert.That(ids).Contains("Europe/London");
    }

    /// <summary>Aliases and raw offsets aren't places, and would only confuse the choice.</summary>
    [Test]
    public async Task GetAll_ExcludesAliasesAndOffsetZones()
    {
        var ids = TimeZoneChoices.GetAll(Summer).Select(c => c.Id).ToList();

        await Assert.That(ids.Any(id => id.StartsWith("Etc/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(ids.Any(id => id.StartsWith("US/", StringComparison.Ordinal))).IsFalse();
    }

    /// <summary>Labels show the offset at the given instant, so daylight saving time is reflected.</summary>
    [Test]
    public async Task GetAll_LabelsShowCurrentOffset()
    {
        var summer = TimeZoneChoices.GetAll(Summer).Single(c => c.Id == "America/Chicago");
        var winter = TimeZoneChoices.GetAll(Winter).Single(c => c.Id == "America/Chicago");

        await Assert.That(summer.DisplayName).IsEqualTo("(UTC-05:00) America/Chicago");
        await Assert.That(winter.DisplayName).IsEqualTo("(UTC-06:00) America/Chicago");
    }

    /// <summary>Choices are ordered west to east, then by name.</summary>
    [Test]
    public async Task GetAll_IsSortedByOffsetThenId()
    {
        var choices = TimeZoneChoices.GetAll(Summer);

        var sorted = choices.OrderBy(c => c.Offset).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        await Assert.That(choices.SequenceEqual(sorted)).IsTrue();
    }

    /// <summary>The saved value is always offered, even when this host doesn't list it, so it never changes silently.</summary>
    [Test]
    public async Task GetAll_IncludesCurrentValue()
    {
        var choices = TimeZoneChoices.GetAll(Summer, "Etc/GMT+6");

        await Assert.That(choices.Select(c => c.Id)).Contains("Etc/GMT+6");
    }
}
