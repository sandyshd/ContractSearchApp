using Azure.Data.Tables;
using Azure.Identity;
using ContractDb.Shared.Models;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Manages indexing job records in Azure Table Storage.
/// </summary>
public sealed class JobStoreService
{
    private readonly TableClient _table;

    public JobStoreService(IConfiguration config)
    {
        var tableName = config["TableName"] ?? "indexingJobs";
        var accountName = config["StorageAccountName"];
        if (!string.IsNullOrEmpty(accountName))
        {
            var serviceClient = new TableServiceClient(
                new Uri($"https://{accountName}.table.core.windows.net"),
                new DefaultAzureCredential());
            _table = serviceClient.GetTableClient(tableName);
        }
        else
        {
            var connStr = config["StorageConnectionString"]
                ?? throw new InvalidOperationException("StorageAccountName or StorageConnectionString must be configured");
            _table = new TableClient(connStr, tableName);
        }
        _table.CreateIfNotExists();
    }

    public async Task CreateJobAsync(IndexingJob job)
    {
        var entity = new TableEntity("jobs", job.JobId)
        {
            ["fileName"] = job.FileName,
            ["blobPath"] = job.BlobPath,
            ["status"] = job.Status,
            ["createdAt"] = job.CreatedAt,
            ["updatedAt"] = job.UpdatedAt,
            ["errorMessage"] = job.ErrorMessage ?? ""
        };
        await _table.AddEntityAsync(entity);
    }

    public async Task<IndexingJob?> GetJobAsync(string jobId)
    {
        try
        {
            var entity = await _table.GetEntityAsync<TableEntity>("jobs", jobId);
            return MapToJob(entity.Value);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<IndexingJob>> GetAllJobsAsync()
    {
        var jobs = new List<IndexingJob>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(e => e.PartitionKey == "jobs"))
        {
            jobs.Add(MapToJob(entity));
        }
        return jobs.OrderByDescending(j => j.CreatedAt).ToList();
    }

    private static IndexingJob MapToJob(TableEntity entity)
    {
        return new IndexingJob
        {
            JobId = entity.RowKey,
            FileName = entity.GetString("fileName") ?? "",
            BlobPath = entity.GetString("blobPath") ?? "",
            Status = entity.GetString("status") ?? JobStatus.Queued,
            CreatedAt = entity.GetDateTime("createdAt") ?? DateTime.MinValue,
            UpdatedAt = entity.GetDateTime("updatedAt") ?? DateTime.MinValue,
            ErrorMessage = entity.GetString("errorMessage")
        };
    }
}
