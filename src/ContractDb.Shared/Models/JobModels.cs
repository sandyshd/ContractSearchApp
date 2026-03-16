namespace ContractDb.Shared.Models;

/// <summary>
/// Represents an indexing job tracked in Azure Table Storage.
/// </summary>
public sealed class IndexingJob
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public string Status { get; set; } = JobStatus.Queued;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Constants for job status values.
/// </summary>
public static class JobStatus
{
    public const string Queued = "Queued";
    public const string Uploaded = "Uploaded";
    public const string Indexing = "Indexing";
    public const string Indexed = "Indexed";
    public const string Failed = "Failed";
}

/// <summary>
/// Summary of all indexing jobs.
/// </summary>
public sealed class JobsSummary
{
    public int TotalJobs { get; set; }
    public int Queued { get; set; }
    public int Uploaded { get; set; }
    public int Indexing { get; set; }
    public int Indexed { get; set; }
    public int Failed { get; set; }
    public long IndexedDocumentCount { get; set; }
}

/// <summary>
/// Message placed on the indexing-requests queue.
/// </summary>
public sealed class IndexingQueueMessage
{
    public string JobId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
