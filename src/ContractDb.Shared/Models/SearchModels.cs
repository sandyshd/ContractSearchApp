namespace ContractDb.Shared.Models;

/// <summary>
/// A single search hit from Azure AI Search.
/// </summary>
public sealed class SearchHit
{
    public string Id { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Parameters for a search query.
/// </summary>
public sealed class SearchQuery
{
    public string QueryText { get; set; } = string.Empty;
    public int Top { get; set; } = 10;
    public string? Filter { get; set; }
    public string? Scope { get; set; } // sourceFileName filter
    public bool IncludeHighlights { get; set; } = true;
}

/// <summary>
/// Response from SearchContracts tool.
/// </summary>
public sealed class SearchResponse
{
    public List<SearchHit> Hits { get; set; } = new();
    public long TotalCount { get; set; }
    public string QueryUsed { get; set; } = string.Empty;
}
