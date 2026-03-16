using System.Globalization;
using System.Text.RegularExpressions;

namespace ContractDb.Shared.Services;

/// <summary>
/// Utility methods for date extraction and normalization from contract text.
/// </summary>
public static partial class DateUtils
{
    private static readonly string[] DateFormats = new[]
    {
        "MMMM d, yyyy",
        "MMMM dd, yyyy",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "yyyy-MM-dd",
        "dd-MMM-yyyy",
        "dd MMMM yyyy",
        "d MMMM yyyy",
    };

    /// <summary>
    /// Attempts to extract dates from text using common contract date patterns.
    /// </summary>
    public static List<DateTime> ExtractDates(string text)
    {
        var results = new List<DateTime>();
        var matches = DatePattern().Matches(text);

        foreach (Match match in matches)
        {
            if (DateTime.TryParseExact(match.Value.Trim(), DateFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                results.Add(dt);
            }
            else if (DateTime.TryParse(match.Value.Trim(), CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var dt2))
            {
                results.Add(dt2);
            }
        }

        return results.Distinct().ToList();
    }

    /// <summary>
    /// Formats a date for display.
    /// </summary>
    public static string Format(DateTime dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(
        @"(?:\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b)|(?:\b\d{4}-\d{2}-\d{2}\b)|(?:\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}\b)|(?:\b\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b)|(?:\b\d{1,2}-(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)-\d{4}\b)",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DatePattern();
}
