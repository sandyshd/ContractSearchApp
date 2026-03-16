using ContractDb.Shared.Models;
using ContractDb.Shared.Services;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Executes prompt templates against the search index.
/// Builds a query plan (multiple search queries with synonyms),
/// calls SearchContractsTool, assembles results with citations and guardrails.
/// </summary>
public sealed class PromptExecutionService
{
    private readonly SearchContractsTool _searchTool;
    private readonly PromptLibraryLoader _promptLoader;
    private readonly RunHistoryService _history;
    private readonly ILogger<PromptExecutionService> _logger;

    public PromptExecutionService(
        SearchContractsTool searchTool,
        PromptLibraryLoader promptLoader,
        RunHistoryService history,
        ILogger<PromptExecutionService> logger)
    {
        _searchTool = searchTool;
        _promptLoader = promptLoader;
        _history = history;
        _logger = logger;
    }

    public async Task<PromptRunResponse> ExecuteAsync(PromptRunRequest request)
    {
        // Resolve prompt
        PromptTemplate? template = null;
        string promptText;
        List<string> searchTerms;

        if (!string.IsNullOrEmpty(request.PromptId))
        {
            template = await _promptLoader.GetByIdAsync(request.PromptId);
            if (template is null)
                throw new ArgumentException($"Prompt '{request.PromptId}' not found");
            promptText = template.PromptText;
            searchTerms = template.SearchSynonyms;
        }
        else if (!string.IsNullOrEmpty(request.CustomPromptText))
        {
            promptText = request.CustomPromptText;
            searchTerms = ExtractKeyTerms(promptText);
        }
        else
        {
            throw new ArgumentException("Either PromptId or CustomPromptText is required");
        }

        // Build query plan: main query + synonym queries
        var queries = BuildQueryPlan(searchTerms, request.Scope == "all" ? null : request.ScopeFileName);

        // Execute all queries via SearchContractsTool
        var allHits = new Dictionary<string, SearchHit>(); // key = sourceFileName
        var allCitations = new Dictionary<string, List<Citation>>(); // key = sourceFileName
        string queryDescription = string.Join(" | ", queries.Select(q => q.QueryText));

        foreach (var query in queries)
        {
            var response = await _searchTool.SearchAsync(query);
            foreach (var hit in response.Hits)
            {
                allHits.TryAdd(hit.SourceFileName, hit);

                if (!allCitations.ContainsKey(hit.SourceFileName))
                    allCitations[hit.SourceFileName] = new List<Citation>();

                var excerpts = CitationUtils.ExtractExcerpts(hit);
                foreach (var excerpt in excerpts)
                {
                    allCitations[hit.SourceFileName].Add(
                        CitationUtils.BuildCitation(hit, excerpt.Text, response.QueryUsed));
                }
            }
        }

        // Build results per contract
        var results = new List<PromptResult>();
        foreach (var kvp in allHits)
        {
            var hit = kvp.Value;
            var citations = allCitations.GetValueOrDefault(hit.SourceFileName, new());
            var excerpts = CitationUtils.ExtractExcerpts(hit);

            var result = new PromptResult
            {
                ContractName = Path.GetFileNameWithoutExtension(hit.SourceFileName),
                SourceFileName = hit.SourceFileName,
                ClauseExcerpts = excerpts,
                Citations = citations
            };

            // Attempt rule-based extraction
            if (template is not null)
            {
                result = ApplyRuleBasedExtraction(result, template);
            }
            else
            {
                // Infer result type from custom prompt and apply extraction
                var inferredType = InferResultType(promptText);
                if (inferredType != "text" && excerpts.Count > 0)
                {
                    var syntheticTemplate = new PromptTemplate
                    {
                        ExpectedResultType = inferredType,
                        PromptText = promptText
                    };
                    result = ApplyRuleBasedExtraction(result, syntheticTemplate);

                    // For date-type prompts, try to pick the most relevant date near expiry keywords
                    if (inferredType == "date" && result.ExtractedValue is not null)
                    {
                        var bestDate = ExtractMostRelevantDate(result.ClauseExcerpts, promptText);
                        if (bestDate is not null)
                        {
                            result.ExtractedValue = DateUtils.Format(bestDate.Value);
                            result.Conclusion = "Explicit Date";
                        }
                    }
                }
                else
                {
                    result.Conclusion = excerpts.Count > 0 ? "Found" : "Not Found";
                }
            }

            // Apply guardrails
            result = Guardrails.Enforce(result);
            results.Add(result);
        }

        // If no hits at all
        if (results.Count == 0)
        {
            results.Add(new PromptResult
            {
                ContractName = "(no contracts matched)",
                SourceFileName = "",
                Conclusion = "Not Found"
            });
        }

        var runResponse = new PromptRunResponse
        {
            PromptText = promptText,
            Scope = request.Scope,
            ExecutedAt = DateTime.UtcNow,
            Results = results,
            TotalContracts = allHits.Count,
            MatchedContracts = results.Count(r => r.Conclusion != "Not Found")
        };

        _history.Add(runResponse);
        return runResponse;
    }

    private List<SearchQuery> BuildQueryPlan(List<string> terms, string? scopeFileName)
    {
        var queries = new List<SearchQuery>();

        // Main query: combine all terms
        queries.Add(new SearchQuery
        {
            QueryText = string.Join(" ", terms),
            Top = 10,
            Scope = scopeFileName,
            IncludeHighlights = true
        });

        // Individual synonym queries for breadth
        foreach (var term in terms.Take(5))
        {
            queries.Add(new SearchQuery
            {
                QueryText = term,
                Top = 5,
                Scope = scopeFileName,
                IncludeHighlights = true
            });
        }

        return queries;
    }

    private static PromptResult ApplyRuleBasedExtraction(PromptResult result, PromptTemplate template)
    {
        var allText = string.Join(" ", result.ClauseExcerpts.Select(e => e.Text));

        switch (template.ExpectedResultType)
        {
            case "date":
                var dates = DateUtils.ExtractDates(allText);
                if (dates.Count == 1)
                {
                    result.ExtractedValue = DateUtils.Format(dates[0]);
                    result.Conclusion = "Explicit Date";
                }
                else if (dates.Count > 1)
                {
                    result.ExtractedValue = string.Join(", ", dates.Select(DateUtils.Format));
                    result.Conclusion = "Ambiguous";
                }
                else
                {
                    result.Conclusion = result.ClauseExcerpts.Count > 0 ? "Implied" : "Not Found";
                }
                break;

            case "boolean":
                var positiveSignals = new[] { "shall", "will", "must", "agrees to", "is required", "automatic renewal", "auto-renewal", "evergreen" };
                var negativeSignals = new[] { "shall not", "will not", "does not", "no auto", "no automatic", "non-renewable" };

                bool hasPositive = positiveSignals.Any(s => allText.Contains(s, StringComparison.OrdinalIgnoreCase));
                bool hasNegative = negativeSignals.Any(s => allText.Contains(s, StringComparison.OrdinalIgnoreCase));

                if (hasPositive && hasNegative)
                {
                    result.Conclusion = "Ambiguous";
                }
                else if (hasPositive)
                {
                    result.ExtractedValue = "Yes";
                    result.Conclusion = "Yes";
                }
                else if (hasNegative)
                {
                    result.ExtractedValue = "No";
                    result.Conclusion = "No";
                }
                else
                {
                    result.Conclusion = result.ClauseExcerpts.Count > 0 ? "Ambiguous" : "Not Found";
                }
                break;

            default: // text, currency
                result.Conclusion = result.ClauseExcerpts.Count > 0 ? "Found" : "Not Found";
                break;
        }

        return result;
    }

    private static string InferResultType(string promptText)
    {
        var lower = promptText.ToLowerInvariant();
        var dateKeywords = new[] { "expire", "expiry", "expiration", "end date", "termination date",
            "renewal date", "effective date", "start date", "begin date", "date" };
        if (dateKeywords.Any(k => lower.Contains(k)))
            return "date";

        var boolKeywords = new[] { "does it", "is there", "contains", "include", "auto-renew",
            "automatic renewal", "evergreen", "is the contract" };
        if (boolKeywords.Any(k => lower.Contains(k)))
            return "boolean";

        return "text";
    }

    private static DateTime? ExtractMostRelevantDate(List<ClauseExcerpt> excerpts, string promptText)
    {
        // Identify which date-context keywords the prompt is asking about
        var lower = promptText.ToLowerInvariant();
        var contextKeywords = new List<string>();
        if (lower.Contains("expir") || lower.Contains("end"))
            contextKeywords.AddRange(new[] { "end", "expir", "termination", "original end" });
        if (lower.Contains("start") || lower.Contains("begin") || lower.Contains("effective"))
            contextKeywords.AddRange(new[] { "start", "begin", "effective", "commence" });
        if (lower.Contains("renewal"))
            contextKeywords.AddRange(new[] { "renewal", "renew" });

        if (contextKeywords.Count == 0)
            return null;

        // Scan each excerpt for dates near context keywords
        DateTime? bestDate = null;
        int bestDistance = int.MaxValue;

        foreach (var excerpt in excerpts)
        {
            var text = excerpt.Text;
            var dates = DateUtils.ExtractDates(text);
            if (dates.Count == 0) continue;

            var textLower = text.ToLowerInvariant();
            foreach (var keyword in contextKeywords)
            {
                int kwIndex = textLower.IndexOf(keyword, StringComparison.Ordinal);
                if (kwIndex < 0) continue;

                // Find the date closest to this keyword
                foreach (var dt in dates)
                {
                    var dateStr = DateUtils.Format(dt);
                    // Also try common formats to locate position
                    int dateIndex = text.IndexOf(dateStr, StringComparison.OrdinalIgnoreCase);
                    if (dateIndex < 0)
                    {
                        // Try month-name format
                        dateIndex = text.IndexOf(dt.ToString("MMMM"), StringComparison.OrdinalIgnoreCase);
                    }
                    if (dateIndex < 0)
                        dateIndex = 0; // fallback

                    int dist = Math.Abs(kwIndex - dateIndex);
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestDate = dt;
                    }
                }
            }
        }

        return bestDate;
    }

    private static List<string> ExtractKeyTerms(string text)
    {
        // Simple keyword extraction: split on common words and punctuation
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "what", "is", "the", "of", "this", "a", "an", "in", "for", "and",
            "or", "does", "do", "are", "has", "have", "any", "how", "when",
            "where", "which", "contract", "agreement", "provide", "list"
        };

        return text.Split(new[] { ' ', ',', '?', '!', '.', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }
}
