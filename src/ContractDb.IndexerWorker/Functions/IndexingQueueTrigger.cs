using System.Text.Json;
using ContractDb.IndexerWorker.Services;
using ContractDb.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ContractDb.IndexerWorker.Functions;

public sealed class IndexingQueueTrigger
{
    private readonly SearchIndexerClient _indexerClient;
    private readonly SearchVerificationClient _verificationClient;
    private readonly JobStoreClient _jobStore;
    private readonly RetryPolicy _retryPolicy;
    private readonly ILogger<IndexingQueueTrigger> _logger;

    public IndexingQueueTrigger(
        SearchIndexerClient indexerClient,
        SearchVerificationClient verificationClient,
        JobStoreClient jobStore,
        RetryPolicy retryPolicy,
        ILogger<IndexingQueueTrigger> logger)
    {
        _indexerClient = indexerClient;
        _verificationClient = verificationClient;
        _jobStore = jobStore;
        _retryPolicy = retryPolicy;
        _logger = logger;
    }

    [Function("IndexingQueueTrigger")]
    public async Task RunAsync(
        [QueueTrigger("indexing-requests", Connection = "AzureWebJobsStorage")] string messageJson)
    {
        var message = JsonSerializer.Deserialize<IndexingQueueMessage>(messageJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (message is null)
        {
            _logger.LogError("Failed to deserialize queue message");
            return;
        }

        _logger.LogInformation("Processing indexing job {JobId} for file {FileName}",
            message.JobId, message.FileName);

        try
        {
            // Step 1: Set status to Indexing
            await _jobStore.UpdateStatusAsync(message.JobId, JobStatus.Indexing);

            // Step 2: Run the indexer (with retry for "already running")
            await _retryPolicy.ExecuteWithRetryAsync(
                () => _indexerClient.RunIndexerAsync(),
                maxRetries: 5,
                label: "RunIndexer");

            // Step 3: Poll indexer status until complete
            var success = await PollIndexerCompletionAsync(message.UploadedAt);

            if (!success)
            {
                await _jobStore.UpdateStatusAsync(message.JobId, JobStatus.Failed, "Indexer run did not complete successfully");
                return;
            }

            // Step 4: Verify the document is in the index
            var verified = await _retryPolicy.ExecuteWithRetryAsync(
                () => _verificationClient.VerifyDocumentIndexedAsync(message.FileName),
                maxRetries: 5,
                label: "VerifyDocument");

            if (verified)
            {
                await _jobStore.UpdateStatusAsync(message.JobId, JobStatus.Indexed);
                _logger.LogInformation("Job {JobId} completed. File {FileName} is indexed.", message.JobId, message.FileName);
            }
            else
            {
                await _jobStore.UpdateStatusAsync(message.JobId, JobStatus.Failed,
                    "File was not found in search index after indexer completed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing job {JobId} failed", message.JobId);
            await _jobStore.UpdateStatusAsync(message.JobId, JobStatus.Failed, ex.Message);
        }
    }

    private async Task<bool> PollIndexerCompletionAsync(DateTime uploadedAt)
    {
        const int maxPolls = 60;
        const int pollIntervalMs = 5000;

        for (int i = 0; i < maxPolls; i++)
        {
            await Task.Delay(pollIntervalMs);

            var status = await _indexerClient.GetIndexerStatusAsync();
            if (status is null) continue;

            _logger.LogInformation("Indexer status: {Status}, LastEndTime: {EndTime}",
                status.LastResultStatus, status.LastResultEndTime);

            if (status.LastResultEndTime.HasValue && status.LastResultEndTime.Value >= uploadedAt)
            {
                if (string.Equals(status.LastResultStatus, "success", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (string.Equals(status.LastResultStatus, "transientFailure", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status.LastResultStatus, "persistentFailure", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Indexer completed with status: {Status}, Error: {Error}",
                        status.LastResultStatus, status.ErrorMessage);
                    return false;
                }
            }
        }

        _logger.LogWarning("Indexer polling timed out after {MaxPolls} attempts", maxPolls);
        return false;
    }
}
