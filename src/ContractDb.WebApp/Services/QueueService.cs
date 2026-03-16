using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Queues;
using ContractDb.Shared.Models;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Sends messages to the indexing-requests Azure Queue.
/// </summary>
public sealed class QueueService
{
    private readonly QueueClient _queue;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public QueueService(IConfiguration config)
    {
        var queueName = config["QueueName"] ?? "indexing-requests";
        var queueOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        var accountName = config["StorageAccountName"];
        if (!string.IsNullOrEmpty(accountName))
        {
            var serviceClient = new QueueServiceClient(
                new Uri($"https://{accountName}.queue.core.windows.net"),
                new DefaultAzureCredential(),
                queueOptions);
            _queue = serviceClient.GetQueueClient(queueName);
        }
        else
        {
            var connStr = config["StorageConnectionString"]
                ?? throw new InvalidOperationException("StorageAccountName or StorageConnectionString must be configured");
            _queue = new QueueClient(connStr, queueName, queueOptions);
        }
        _queue.CreateIfNotExists();
    }

    public async Task EnqueueAsync(IndexingQueueMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _queue.SendMessageAsync(json);
    }
}
