using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContractDb.IndexerWorker.Services;

/// <summary>
/// Verifies that a specific file has been indexed in Azure AI Search.
/// </summary>
public sealed class SearchVerificationClient
{
    private readonly SearchClient _searchClient;
    private readonly ILogger<SearchVerificationClient> _logger;

    public SearchVerificationClient(SearchConfig config, IConfiguration configuration, ILogger<SearchVerificationClient> logger)
    {
        var indexName = configuration["SearchIndexName"] ?? "contracts-index";
        _searchClient = new SearchClient(
            new Uri(config.Endpoint),
            indexName,
            new AzureKeyCredential(config.AdminKey));
        _logger = logger;
    }

    /// <summary>
    /// Checks if a file with the given name exists in the search index.
    /// </summary>
    public async Task<bool> VerifyDocumentIndexedAsync(string fileName)
    {
        var options = new SearchOptions
        {
            Filter = $"sourceFileName eq '{EscapeFilterValue(fileName)}'",
            Size = 1,
            IncludeTotalCount = true,
            Select = { "sourceFileName" }
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", options);
        var count = results.Value.TotalCount ?? 0;

        _logger.LogInformation("Verification query for '{FileName}': {Count} result(s)", fileName, count);
        return count > 0;
    }

    private static string EscapeFilterValue(string value)
    {
        return value.Replace("'", "''");
    }
}
