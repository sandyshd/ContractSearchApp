using System.Text.RegularExpressions;
using ContractDb.Shared.Models;

namespace ContractDb.Shared.Services;

/// <summary>
/// Utilities for building citations from search hits.
/// </summary>
public static partial class CitationUtils
{
    /// <summary>
    /// Extracts the best excerpt snippets from a search hit's highlights or content.
    /// Returns quoted text suitable for citation.
    /// </summary>
    public static List<ClauseExcerpt> ExtractExcerpts(SearchHit hit, int maxExcerpts = 3)
    {
        var excerpts = new List<ClauseExcerpt>();

        // Prefer highlights (already contain relevant snippets)
        if (hit.Highlights.Count > 0)
        {
            foreach (var hl in hit.Highlights.Take(maxExcerpts))
            {
                var clean = StripHighlightTags(hl);
                excerpts.Add(new ClauseExcerpt { Text = clean, Score = hit.Score });
            }
        }
        else if (!string.IsNullOrWhiteSpace(hit.Content))
        {
            // Fallback: take first N sentences from content
            var sentences = SentenceSplit().Split(hit.Content)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(maxExcerpts);

            foreach (var s in sentences)
            {
                excerpts.Add(new ClauseExcerpt
                {
                    Text = s.Trim().Length > 500 ? s.Trim()[..500] + "..." : s.Trim(),
                    Score = hit.Score
                });
            }
        }

        return excerpts;
    }

    /// <summary>
    /// Builds a Citation from a SearchHit and excerpt.
    /// </summary>
    public static Citation BuildCitation(SearchHit hit, string excerpt, string queryUsed)
    {
        return new Citation
        {
            SourceFileName = hit.SourceFileName,
            BlobPath = hit.BlobPath,
            Excerpt = excerpt,
            QueryUsed = queryUsed
        };
    }

    private static string StripHighlightTags(string text)
    {
        return HighlightTagRegex().Replace(text, string.Empty);
    }

    [GeneratedRegex(@"</?em>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex HighlightTagRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex SentenceSplit();
}
