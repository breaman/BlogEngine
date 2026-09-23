using System.Globalization;

using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Formats the dates shown on public pages with the site's date format setting (design 13).
/// </summary>
/// <remarks>
/// Dates are formatted with the invariant culture, so month names don't depend on the server's locale. A
/// format that can't format a date (the settings validator should prevent that) falls back to the default
/// rather than failing the page.
/// </remarks>
/// <example>
/// <code>
/// PublicDates.Format(new DateOnly(2026, 9, 22), "MMMM d, yyyy"); // "September 22, 2026"
/// PublicDates.MonthYear(2026, 9);                                 // "September 2026"
/// </code>
/// </example>
public static class PublicDates
{
    /// <summary>The date in the <paramref name="format"/> custom date format, or the default format if it's unusable.</summary>
    public static string Format(DateOnly date, string? format)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            try
            {
                return date.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // A time specifier such as "HH" can't format a DateOnly; use the default below.
            }
        }

        return date.ToString(SiteSettingsDefaults.DateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The ISO 8601 date for a <c>&lt;time datetime="…"&gt;</c> attribute, such as <c>2026-09-22</c>.</summary>
    public static string Iso(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The month's name and the year, such as <c>September 2026</c>.</summary>
    public static string MonthYear(int year, int month)
    {
        return new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>The month's name, such as <c>September</c>.</summary>
    public static string MonthName(int month)
    {
        return CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
    }
}
