using System.Globalization;

namespace BlogEngine.Shared.Common;

/// <summary>
/// The IANA time zones offered by the settings page's time zone picker (design 13, Q10).
/// </summary>
/// <remarks>
/// <para>
/// The blog stores IANA ids such as <c>America/Chicago</c>. Linux, macOS and WebAssembly report those ids
/// directly, but Windows reports its own (<c>Central Standard Time</c>), so Windows ids are converted with
/// <see cref="TimeZoneInfo.TryConvertWindowsIdToIanaId(string, out string?)"/>. The list is the same whichever
/// host builds it, apart from the time zone data each host ships.
/// </para>
/// <para>
/// Only region-style ids (<c>Area/Location</c>) and <c>UTC</c> are offered. Legacy aliases such as
/// <c>US/Central</c> and the <c>Etc/GMT+6</c> zones, whose signs are inverted, would only confuse the choice.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var choices = TimeZoneChoices.GetAll(timeProvider.GetUtcNow(), settings.TimeZoneId);
/// // [..., { Id = "America/Chicago", DisplayName = "(UTC-05:00) America/Chicago" }, ...]
/// </code>
/// </example>
public static class TimeZoneChoices
{
    /// <summary>Area prefixes of IANA ids that are aliases or offsets rather than places.</summary>
    private static readonly string[] ExcludedAreas = ["Etc/", "SystemV/", "US/", "Canada/", "Mexico/", "Brazil/", "Chile/"];

    /// <summary>
    /// Every selectable zone, sorted by its UTC offset at <paramref name="now"/> and then by id.
    /// </summary>
    /// <param name="now">The instant whose offsets are shown, so daylight saving time is reflected.</param>
    /// <param name="alwaysInclude">
    /// A zone to include even if it would be filtered out or is unknown on this host, typically the currently
    /// saved value, so the picker never silently changes it.
    /// </param>
    public static IReadOnlyList<TimeZoneChoice> GetAll(DateTimeOffset now, string? alwaysInclude = null)
    {
        var choices = new Dictionary<string, TimeZoneChoice>(StringComparer.OrdinalIgnoreCase)
        {
            [SiteSettingsDefaults.TimeZoneId] = Create(SiteSettingsDefaults.TimeZoneId, TimeSpan.Zero)
        };

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            if (ToIanaId(zone) is { } id && IsPlace(id) && !choices.ContainsKey(id))
            {
                choices[id] = Create(id, zone.GetUtcOffset(now));
            }
        }

        if (!string.IsNullOrWhiteSpace(alwaysInclude) && !choices.ContainsKey(alwaysInclude))
        {
            var offset = TimeZoneInfo.TryFindSystemTimeZoneById(alwaysInclude, out var zone)
                ? zone.GetUtcOffset(now)
                : TimeSpan.Zero;
            choices[alwaysInclude] = Create(alwaysInclude, offset);
        }

        return [.. choices.Values.OrderBy(c => c.Offset).ThenBy(c => c.Id, StringComparer.Ordinal)];
    }

    /// <summary>The zone's IANA id, converting a Windows id when needed; <see langword="null"/> if it has none.</summary>
    private static string? ToIanaId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) ? ianaId : null;
    }

    /// <summary>Whether the id names a place (<c>Area/Location</c>) rather than an alias or raw offset.</summary>
    private static bool IsPlace(string id)
    {
        return id.Contains('/', StringComparison.Ordinal)
            && !ExcludedAreas.Any(area => id.StartsWith(area, StringComparison.OrdinalIgnoreCase));
    }

    private static TimeZoneChoice Create(string id, TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var label = string.Create(CultureInfo.InvariantCulture, $"(UTC{sign}{offset.Duration():hh\\:mm}) {id.Replace('_', ' ')}");

        return new TimeZoneChoice(id, label, offset);
    }
}

/// <summary>One option of the time zone picker.</summary>
/// <param name="Id">The IANA id stored in the settings, for example <c>America/Chicago</c>.</param>
/// <param name="DisplayName">The label shown, for example <c>(UTC-05:00) America/Chicago</c>.</param>
/// <param name="Offset">The zone's UTC offset at the time the list was built.</param>
public sealed record TimeZoneChoice(string Id, string DisplayName, TimeSpan Offset);
