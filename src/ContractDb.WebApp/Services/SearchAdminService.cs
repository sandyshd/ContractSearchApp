using System.Net;
using System.Text.Json;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Admin operations on Azure AI Search: run indexer, get status via REST API.
/// Used by the dashboard to show indexer health.
/// </summary>
public sealed class SearchAdminService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _adminKey;
    private readonly string _indexerName;
    private const string ApiVersion = "2025-05-01-preview";

    public SearchAdminService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _http = httpFactory.CreateClient("SearchAdmin");
        _endpoint = (config["SearchEndpoint"] ?? "").TrimEnd('/');
        _adminKey = config["SearchAdminKey"] ?? "";
        _indexerName = config["IndexerName"] ?? "contracts-indexer";
    }

    public async Task<JsonDocument?> GetIndexerStatusAsync()
    {
        var url = $"{_endpoint}/indexers('{_indexerName}')/search.status?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("api-key", _adminKey);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    public async Task<bool> RunIndexerAsync()
    {
        var url = $"{_endpoint}/indexers('{_indexerName}')/search.run?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _adminKey);

        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict;
    }
}
