using Azure.Data.Tables;
using Azure.Identity;
using ContractDb.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContractDb.IndexerWorker.Services;

/// <summary>
/// Updates indexing job status in Azure Table Storage.
/// </summary>
public sealed class JobStoreClient
{
    private readonly TableClient _tableClient;
    private readonly ILogger<JobStoreClient> _logger;

    public JobStoreClient(IConfiguration configuration, ILogger<JobStoreClient> logger)
    {
        // Environment variables with __ are normalized to : by .NET configuration
        var accountName = configuration["AzureWebJobsStorage:accountName"];
        if (!string.IsNullOrEmpty(accountName))
        {
            var serviceClient = new TableServiceClient(
                new Uri($"https://{accountName}.table.core.windows.net"),
                new DefaultAzureCredential());
            _tableClient = serviceClient.GetTableClient("indexingJobs");
        }
        else
        {
            var connStr = configuration["AzureWebJobsStorage"]
                ?? throw new InvalidOperationException("AzureWebJobsStorage__accountName or AzureWebJobsStorage must be configured");
            _tableClient = new TableClient(connStr, "indexingJobs");
        }
        _tableClient.CreateIfNotExists();
        _logger = logger;
    }

    public async Task UpdateStatusAsync(string jobId, string status, string? errorMessage = null)
    {
        _logger.LogInformation("Updating job {JobId} to status {Status}", jobId, status);

        var entity = new TableEntity("jobs", jobId)
        {
            ["status"] = status,
            ["updatedAt"] = DateTime.UtcNow,
        };

        if (errorMessage is not null)
            entity["errorMessage"] = errorMessage;

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge);
    }
}
