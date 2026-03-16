using Azure.Identity;
using Azure.Storage.Blobs;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Uploads files to Azure Blob Storage.
/// </summary>
public sealed class BlobStorageService
{
    private readonly BlobContainerClient _container;

    public BlobStorageService(IConfiguration config)
    {
        var containerName = config["BlobContainerName"] ?? "contracts";
        var accountName = config["StorageAccountName"];
        if (!string.IsNullOrEmpty(accountName))
        {
            var serviceClient = new BlobServiceClient(
                new Uri($"https://{accountName}.blob.core.windows.net"),
                new DefaultAzureCredential());
            _container = serviceClient.GetBlobContainerClient(containerName);
        }
        else
        {
            var connStr = config["StorageConnectionString"]
                ?? throw new InvalidOperationException("StorageAccountName or StorageConnectionString must be configured");
            _container = new BlobContainerClient(connStr, containerName);
        }
        _container.CreateIfNotExists();
    }

    /// <summary>
    /// Uploads a file and returns the blob path.
    /// </summary>
    public async Task<string> UploadAsync(string fileName, Stream content)
    {
        var blobClient = _container.GetBlobClient(fileName);
        await blobClient.UploadAsync(content, overwrite: true);
        return blobClient.Uri.AbsoluteUri;
    }
}
