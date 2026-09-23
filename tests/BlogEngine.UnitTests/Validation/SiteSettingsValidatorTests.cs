using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Validation;

namespace BlogEngine.UnitTests.Validation;

/// <summary>
/// Tests <see cref="SiteSettingsValidator"/> and <see cref="SocialLinkValidator"/>: the settings rules checked
/// before a save.
/// </summary>
public class SiteSettingsValidatorTests
{
    private static readonly SiteSettingsValidator Validator = new();

    /// <summary>The documented defaults are valid.</summary>
    [Test]
    public async Task Validate_Defaults_IsValid()
    {
        await Assert.That(Validate(ValidSettings())).IsEmpty();
    }

    /// <summary>The site title, time zone and date format are required.</summary>
    [Test]
    public async Task Validate_BlankRequiredFields_AreRejected()
    {
        var settings = ValidSettings();
        settings.SiteTitle = " ";
        settings.TimeZoneId = "";
        settings.DateFormat = "";

        await Assert.That(Validate(settings).Keys).IsEquivalentTo(
            [nameof(SiteSettingsDto.SiteTitle), nameof(SiteSettingsDto.TimeZoneId), nameof(SiteSettingsDto.DateFormat)]);
    }

    /// <summary>Both IANA and Windows time zone ids are accepted; unknown ids are not.</summary>
    [Test]
    [Arguments("America/Chicago", true)]
    [Arguments("Central Standard Time", true)]
    [Arguments("Mars/Olympus_Mons", false)]
    public async Task Validate_TimeZone(string timeZoneId, bool isValid)
    {
        var settings = ValidSettings();
        settings.TimeZoneId = timeZoneId;

        await Assert.That(Validate(settings).ContainsKey(nameof(SiteSettingsDto.TimeZoneId))).IsEqualTo(!isValid);
    }

    /// <summary>A format string that throws when formatting a date is rejected.</summary>
    [Test]
    public async Task Validate_UnusableDateFormat_IsRejected()
    {
        var settings = ValidSettings();
        settings.DateFormat = "%";

        await Assert.That(Validate(settings).Keys).IsEquivalentTo([nameof(SiteSettingsDto.DateFormat)]);
    }

    /// <summary>Numeric settings are limited to their documented ranges.</summary>
    [Test]
    [Arguments(nameof(SiteSettingsDto.PostsPerPage), 0)]
    [Arguments(nameof(SiteSettingsDto.PostsPerPage), 101)]
    [Arguments(nameof(SiteSettingsDto.CloseCommentsAfterDays), -1)]
    [Arguments(nameof(SiteSettingsDto.MaxCommentLinks), 51)]
    [Arguments(nameof(SiteSettingsDto.MaxUploadSizeMegabytes), 0)]
    [Arguments(nameof(SiteSettingsDto.DownscaleOriginalsAbovePixels), 20001)]
    public async Task Validate_OutOfRangeNumber_IsRejected(string property, int value)
    {
        var settings = ValidSettings();
        typeof(SiteSettingsDto).GetProperty(property)!.SetValue(settings, value);

        await Assert.That(Validate(settings).Keys).IsEquivalentTo([property]);
    }

    /// <summary>At least one rendition width is needed, and every width must be usable.</summary>
    [Test]
    [Arguments(new int[0])]
    [Arguments(new[] { 320, 15 })]
    [Arguments(new[] { 320, 8193 })]
    public async Task Validate_BadRenditionWidths_AreRejected(int[] widths)
    {
        var settings = ValidSettings();
        settings.RenditionWidths = [.. widths];

        await Assert.That(Validate(settings).Keys).IsEquivalentTo([nameof(SiteSettingsDto.RenditionWidths)]);
    }

    /// <summary>Each social link is checked, and its errors are reported under the link's index.</summary>
    [Test]
    public async Task Validate_InvalidSocialLink_IsReportedByIndex()
    {
        var settings = ValidSettings();
        settings.SocialLinks =
        [
            new SocialLinkDto { Network = "GitHub", Url = "https://github.com/example" },
            new SocialLinkDto { Network = "", Url = "ftp://example.com" }
        ];

        await Assert.That(Validate(settings).Keys).IsEquivalentTo(["SocialLinks[1].Network", "SocialLinks[1].Url"]);
    }

    /// <summary>Only absolute http and https URLs are accepted for social links.</summary>
    [Test]
    [Arguments("https://mastodon.social/@example", true)]
    [Arguments("http://example.com", true)]
    [Arguments("example.com", false)]
    [Arguments("/relative", false)]
    [Arguments("javascript:alert(1)", false)]
    public async Task SocialLink_Url(string url, bool isValid)
    {
        var result = new SocialLinkValidator().Validate(new SocialLinkDto { Network = "Site", Url = url });

        await Assert.That(result.IsValid).IsEqualTo(isValid);
    }

    /// <summary>Runs the validator and returns its errors keyed by property.</summary>
    private static IDictionary<string, string[]> Validate(SiteSettingsDto settings)
    {
        return Validator.Validate(settings).ToDictionary();
    }

    private static SiteSettingsDto ValidSettings()
    {
        return new SiteSettingsDto
        {
            SiteTitle = SiteSettingsDefaults.SiteTitle,
            PostsPerPage = SiteSettingsDefaults.PostsPerPage,
            TimeZoneId = SiteSettingsDefaults.TimeZoneId,
            DateFormat = SiteSettingsDefaults.DateFormat,
            CloseCommentsAfterDays = SiteSettingsDefaults.CloseCommentsAfterDays,
            MaxCommentLinks = SiteSettingsDefaults.MaxCommentLinks,
            MaxUploadSizeMegabytes = SiteSettingsDefaults.MaxUploadSizeMegabytes,
            DownscaleOriginalsAbovePixels = SiteSettingsDefaults.DownscaleOriginalsAbovePixels,
            RenditionWidths = [.. SiteSettingsDefaults.RenditionWidths]
        };
    }
}
