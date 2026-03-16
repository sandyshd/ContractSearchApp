using ContractDb.Shared.Models;

namespace ContractDb.Shared.Services;

/// <summary>
/// Guardrail checks for prompt execution. Ensures no guessing, 
/// marks ambiguous/conflicting results, enforces citation requirements.
/// </summary>
public static class Guardrails
{
    /// <summary>
    /// Validates that a prompt result meets quality/audit requirements.
    /// If no excerpts support the value, clears it and sets Not Found.
    /// If conflicting excerpts exist, sets Ambiguous.
    /// </summary>
    public static PromptResult Enforce(PromptResult result)
    {
        if (result.ClauseExcerpts.Count == 0)
        {
            result.ExtractedValue = null;
            result.Conclusion = "Not Found";
            return result;
        }

        // If we have an extracted value but no excerpt text actually contains it, clear it
        if (result.ExtractedValue is not null && !HasSupportingExcerpt(result))
        {
            result.ExtractedValue = null;
            result.Conclusion = "Ambiguous";
        }

        // If multiple excerpts with divergent signals, mark ambiguous
        if (result.ClauseExcerpts.Count > 1 && HasConflict(result.ClauseExcerpts))
        {
            result.Conclusion = "Ambiguous";
        }

        // Ensure every result has at least one citation
        if (result.Citations.Count == 0 && result.ClauseExcerpts.Count > 0)
        {
            result.Citations.Add(new Citation
            {
                SourceFileName = result.SourceFileName,
                Excerpt = result.ClauseExcerpts[0].Text,
                QueryUsed = "(auto)"
            });
        }

        return result;
    }

    private static bool HasSupportingExcerpt(PromptResult result)
    {
        if (result.ExtractedValue is null) return false;
        return result.ClauseExcerpts.Any(e =>
            e.Text.Contains(result.ExtractedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasConflict(List<ClauseExcerpt> excerpts)
    {
        // Simple heuristic: if scores vary widely or texts are contradictory
        if (excerpts.Count < 2) return false;
        var maxScore = excerpts.Max(e => e.Score);
        var minScore = excerpts.Min(e => e.Score);
        // If the top and bottom scores differ by more than 50%, flag as potentially conflicting
        return maxScore > 0 && (maxScore - minScore) / maxScore > 0.5;
    }
}
