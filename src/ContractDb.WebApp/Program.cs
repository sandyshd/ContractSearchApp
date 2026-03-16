using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Security.KeyVault.Secrets;
using ContractDb.Shared.Models;
using ContractDb.Shared.Services;
using ContractDb.WebApp.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// ── Razor / Blazor ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAntiforgery();

// ── Configuration: resolve secrets from Key Vault if configured ──
var kvUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrEmpty(kvUri))
{
    var secretClient = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());
    TrySetFromKeyVault(builder.Configuration, secretClient, "SearchAdminKey");
    TrySetFromKeyVault(builder.Configuration, secretClient, "SearchEndpoint");
}

// ── Services DI ──
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton<JobStoreService>();
builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<SearchContractsTool>();
builder.Services.AddSingleton<PromptExecutionService>();
builder.Services.AddSingleton<SearchAdminService>();
builder.Services.AddSingleton<RunHistoryService>();
builder.Services.AddHttpClient("SearchAdmin");

// ── Contract Chat Service (Azure OpenAI + Search) ──
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["SearchEndpoint"]
        ?? throw new InvalidOperationException("SearchEndpoint not configured");
    var adminKey = config["SearchAdminKey"]
        ?? throw new InvalidOperationException("SearchAdminKey not configured");
    var indexName = config["SearchIndexName"] ?? "contracts-index";
    return new SearchClient(new Uri(endpoint), indexName, new AzureKeyCredential(adminKey));
});

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var openAiEndpoint = config["AzureOpenAiEndpoint"]
        ?? throw new InvalidOperationException("AzureOpenAiEndpoint not configured");
    var deploymentName = config["AzureOpenAiDeployment"] ?? "gpt-4o";
    var credential = new DefaultAzureCredential();
    var client = new AzureOpenAIClient(new Uri(openAiEndpoint), credential);
    return client.GetChatClient(deploymentName);
});

builder.Services.AddSingleton<ContractChatService>();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var libPath = Path.Combine(AppContext.BaseDirectory, config["PromptLibraryPath"] ?? "PromptLibrary");
    return new PromptLibraryLoader(libPath);
});

// Allow larger uploads (100 MB)
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 104_857_600);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// ────────────────────────────────────────────
// Minimal API Endpoints
// ────────────────────────────────────────────

// POST /api/upload — accept one or more PDFs
app.MapPost("/api/upload", async (HttpRequest request,
    BlobStorageService blobService,
    JobStoreService jobStore,
    QueueService queueService) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart form data");

    var form = await request.ReadFormAsync();
    var results = new List<IndexingJob>();

    foreach (var file in form.Files)
    {
        if (file.Length == 0) continue;
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            continue;

        // Sanitize the file name
        var safeFileName = Path.GetFileName(file.FileName);

        // Upload to Blob
        using var stream = file.OpenReadStream();
        var blobPath = await blobService.UploadAsync(safeFileName, stream);

        // Create job record
        var job = new IndexingJob
        {
            JobId = Guid.NewGuid().ToString(),
            FileName = safeFileName,
            BlobPath = blobPath,
            Status = JobStatus.Uploaded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await jobStore.CreateJobAsync(job);

        // Enqueue
        var message = new IndexingQueueMessage
        {
            JobId = job.JobId,
            FileName = safeFileName,
            BlobPath = blobPath,
            UploadedAt = DateTime.UtcNow
        };
        await queueService.EnqueueAsync(message);

        results.Add(job);
    }

    return results.Count > 0
        ? Results.Ok(results)
        : Results.BadRequest("No valid PDF files uploaded");
})
.DisableAntiforgery();

// GET /api/jobs — list all jobs
app.MapGet("/api/jobs", async (JobStoreService jobStore) =>
{
    var jobs = await jobStore.GetAllJobsAsync();
    return Results.Ok(jobs);
});

// GET /api/jobs/summary
app.MapGet("/api/jobs/summary", async (JobStoreService jobStore, SearchContractsTool searchTool) =>
{
    var jobs = await jobStore.GetAllJobsAsync();
    var indexedCount = await searchTool.GetDocumentCountAsync();

    var summary = new JobsSummary
    {
        TotalJobs = jobs.Count,
        Queued = jobs.Count(j => j.Status == JobStatus.Queued),
        Uploaded = jobs.Count(j => j.Status == JobStatus.Uploaded),
        Indexing = jobs.Count(j => j.Status == JobStatus.Indexing),
        Indexed = jobs.Count(j => j.Status == JobStatus.Indexed),
        Failed = jobs.Count(j => j.Status == JobStatus.Failed),
        IndexedDocumentCount = indexedCount
    };
    return Results.Ok(summary);
});

// GET /api/jobs/{jobId}
app.MapGet("/api/jobs/{jobId}", async (string jobId, JobStoreService jobStore) =>
{
    var job = await jobStore.GetJobAsync(jobId);
    return job is not null ? Results.Ok(job) : Results.NotFound();
});

// GET /api/prompts — list prompt templates
app.MapGet("/api/prompts", async (PromptLibraryLoader loader) =>
{
    var templates = await loader.LoadTemplatesAsync();
    var groups = await loader.LoadGroupsAsync();
    return Results.Ok(new { templates, groups });
});

// POST /api/run — execute a prompt
app.MapPost("/api/run", async (PromptRunRequest req, PromptExecutionService execService) =>
{
    var response = await execService.ExecuteAsync(req);
    return Results.Ok(response);
})
.DisableAntiforgery();

// GET /api/runs — run history
app.MapGet("/api/runs", (RunHistoryService historyService) =>
{
    var runs = historyService.GetAll();
    return Results.Ok(runs);
});

// GET /api/runs/{runId}
app.MapGet("/api/runs/{runId}", (string runId, RunHistoryService historyService) =>
{
    var run = historyService.GetById(runId);
    return run is not null ? Results.Ok(run) : Results.NotFound();
});

// ── Blazor App ──
app.MapRazorComponents<ContractDb.WebApp.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Helper
static void TrySetFromKeyVault(ConfigurationManager config, SecretClient client, string name)
{
    try
    {
        var secret = client.GetSecret(name);
        config[name] = secret.Value.Value;
    }
    catch { /* Secret may not exist; fall through to config file */ }
}
