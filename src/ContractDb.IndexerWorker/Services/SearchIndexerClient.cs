using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContractDb.IndexerWorker.Services;

/// <summary>
/// Calls Azure AI Search REST API to run and monitor the indexer.
/// </summary>
public sealed class SearchIndexerClient
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _adminKey;
    private readonly string _indexerName;
    private readonly ILogger<SearchIndexerClient> _logger;
    private const string ApiVersion = "2025-05-01-preview";

    public SearchIndexerClient(
        IHttpClientFactory httpFactory,
        SearchConfig config,
        IConfiguration configuration,
        ILogger<SearchIndexerClient> logger)
    {
        _http = httpFactory.CreateClient("SearchAdmin");
        _endpoint = config.Endpoint.TrimEnd('/');
        _adminKey = config.AdminKey;
        _indexerName = configuration["IndexerName"] ?? "contracts-indexer";
        _logger = logger;
    }

    /// <summary>
    /// Triggers on-demand indexer run.
    /// POST {endpoint}/indexers('{name}')/search.run?api-version=...
    /// </summary>
    public async Task RunIndexerAsync()
    {
        var url = $"{_endpoint}/indexers('{_indexerName}')/search.run?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _adminKey);

        var response = await _http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // 409 = indexer already running; this is retriable
            _logger.LogWarning("Indexer is already running (409 Conflict). Will retry.");
            throw new InvalidOperationException("Indexer already running");
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Indexer run triggered successfully.");
    }

    /// <summary>
    /// Gets indexer status.
    /// GET {endpoint}/indexers('{name}')/search.status?api-version=...
    /// </summary>
    public async Task<IndexerStatusResult?> GetIndexerStatusAsync()
    {
        var url = $"{_endpoint}/indexers('{_indexerName}')/search.status?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("api-key", _adminKey);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse lastResult from status
        if (root.TryGetProperty("lastResult", out var lastResult))
        {
            var status = lastResult.GetProperty("status").GetString();
            DateTime? endTime = null;
            string? errorMessage = null;

            if (lastResult.TryGetProperty("endTime", out var endTimeProp) &&
                endTimeProp.ValueKind != JsonValueKind.Null)
            {
                endTime = endTimeProp.GetDateTime();
            }

            if (lastResult.TryGetProperty("errorMessage", out var errProp) &&
                errProp.ValueKind != JsonValueKind.Null)
            {
                errorMessage = errProp.GetString();
            }

            return new IndexerStatusResult(status, endTime, errorMessage);
        }

        return null;
    }
}

public record IndexerStatusResult(string? LastResultStatus, DateTime? LastResultEndTime, string? ErrorMessage);
