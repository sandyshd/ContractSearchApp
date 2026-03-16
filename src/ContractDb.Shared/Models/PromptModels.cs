using System.Text.Json.Serialization;

namespace ContractDb.Shared.Models;

/// <summary>
/// A single prompt template from the prompt library.
/// </summary>
public sealed class PromptTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty; // "A", "B", "Master"
    public string PromptText { get; set; } = string.Empty;
    public string ExpectedResultType { get; set; } = "text"; // text, date, boolean, currency
    public List<string> SearchSynonyms { get; set; } = new();
    public int Priority { get; set; }
}

/// <summary>
/// A grouping of prompt templates.
/// </summary>
public sealed class PromptGroup
{
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public List<string> PromptIds { get; set; } = new();
}

/// <summary>
/// Request to execute a prompt.
/// </summary>
public sealed class PromptRunRequest
{
    public string? PromptId { get; set; }
    public string? CustomPromptText { get; set; }
    public string Scope { get; set; } = "all"; // "all" or specific sourceFileName
    public string? ScopeFileName { get; set; }
}

/// <summary>
/// Result of a prompt execution against a single contract.
/// </summary>
public sealed class PromptResult
{
    public string ContractName { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public List<ClauseExcerpt> ClauseExcerpts { get; set; } = new();
    public string? ExtractedValue { get; set; }
    public string Conclusion { get; set; } = "Not Found"; // Explicit Date, Implied, Not Found, Ambiguous, Yes, No
    public List<Citation> Citations { get; set; } = new();
}

/// <summary>
/// A quoted clause excerpt from a contract.
/// </summary>
public sealed class ClauseExcerpt
{
    public string Text { get; set; } = string.Empty;
    public double Score { get; set; }
}

/// <summary>
/// Citation referencing the source of information.
/// </summary>
public sealed class Citation
{
    public string SourceFileName { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string QueryUsed { get; set; } = string.Empty;
}

/// <summary>
/// Full response from a prompt run.
/// </summary>
public sealed class PromptRunResponse
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public string PromptText { get; set; } = string.Empty;
    public string Scope { get; set; } = "all";
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public List<PromptResult> Results { get; set; } = new();
    public int TotalContracts { get; set; }
    public int MatchedContracts { get; set; }
}
