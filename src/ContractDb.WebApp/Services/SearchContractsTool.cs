using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using ContractDb.Shared.Models;

namespace ContractDb.WebApp.Services;

/// <summary>
/// SearchContracts tool — the ONLY retrieval path for contract data.
/// Wraps Azure AI Search queries using Azure.Search.Documents SDK.
/// </summary>
public sealed class SearchContractsTool
{
    private readonly SearchClient _client;

    public SearchContractsTool(IConfiguration config)
    {
        var endpoint = config["SearchEndpoint"]
            ?? throw new InvalidOperationException("SearchEndpoint not configured");
        var adminKey = config["SearchAdminKey"]
            ?? throw new InvalidOperationException("SearchAdminKey not configured");
        var indexName = config["SearchIndexName"] ?? "contracts-index";

        _client = new SearchClient(new Uri(endpoint), indexName, new AzureKeyCredential(adminKey));
    }

    /// <summary>
    /// Executes a search query against the contracts index.
    /// </summary>
    public async Task<SearchResponse> SearchAsync(SearchQuery query)
    {
        var options = new SearchOptions
        {
            Size = query.Top,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Simple,
            Select = { "id", "sourceFileName", "blobPath", "content", "lastModified" }
        };

        if (query.IncludeHighlights)
        {
            options.HighlightFields.Add("content");
            options.HighlightPreTag = "<em>";
            options.HighlightPostTag = "</em>";
        }

        // Build filter
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(query.Filter))
            filters.Add(query.Filter);
        if (!string.IsNullOrEmpty(query.Scope) && query.Scope != "all")
            filters.Add($"sourceFileName eq '{query.Scope.Replace("'", "''")}'");

        if (filters.Count > 0)
            options.Filter = string.Join(" and ", filters);

        var results = await _client.SearchAsync<SearchDocument>(query.QueryText, options);

        var response = new SearchResponse
        {
            QueryUsed = query.QueryText,
            TotalCount = results.Value.TotalCount ?? 0
        };

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var hit = new SearchHit
            {
                Id = result.Document.GetString("id") ?? "",
                SourceFileName = result.Document.GetString("sourceFileName") ?? "",
                BlobPath = result.Document.GetString("blobPath") ?? "",
                Content = result.Document.GetString("content") ?? "",
                Score = result.Score ?? 0
            };

            if (result.Document.TryGetValue("lastModified", out var lm) && lm is DateTimeOffset dto)
                hit.LastModified = dto.UtcDateTime;

            if (result.Highlights != null && result.Highlights.TryGetValue("content", out var highlights))
                hit.Highlights = highlights.ToList();

            response.Hits.Add(hit);
        }

        return response;
    }

    /// <summary>
    /// Gets total document count in the index.
    /// </summary>
    public async Task<long> GetDocumentCountAsync()
    {
        var options = new SearchOptions
        {
            Size = 0,
            IncludeTotalCount = true
        };
        var results = await _client.SearchAsync<SearchDocument>("*", options);
        return results.Value.TotalCount ?? 0;
    }

    /// <summary>
    /// Gets distinct file names in the index (for scope selector).
    /// </summary>
    public async Task<List<string>> GetIndexedFileNamesAsync()
    {
        var options = new SearchOptions
        {
            Size = 1000,
            Select = { "sourceFileName" }
        };

        var names = new HashSet<string>();
        var results = await _client.SearchAsync<SearchDocument>("*", options);

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var name = result.Document.GetString("sourceFileName");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names.OrderBy(n => n).ToList();
    }
}
