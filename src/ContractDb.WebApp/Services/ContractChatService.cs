using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using OpenAI.Chat;

namespace ContractDb.WebApp.Services;

/// <summary>
/// Chat-based contract search service.
/// Fetches ALL documents from the index, processes them in batches through an LLM,
/// and consolidates results to answer the user's question accurately.
/// Uses temperature=0 for deterministic results.
/// </summary>
public sealed class ContractChatService
{
    private readonly SearchClient _searchClient;
    private readonly ChatClient _chatClient;
    private readonly ILogger<ContractChatService> _logger;
    private const int BatchSize = 3; // contracts per LLM call
    private const int MaxContentChars = 6000; // max content chars per contract sent to LLM

    public ContractChatService(
        SearchClient searchClient,
        ChatClient chatClient,
        ILogger<ContractChatService> logger)
    {
        _searchClient = searchClient;
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// Process a user's chat message: fetch all contracts, batch through LLM, consolidate.
    /// </summary>
    public async IAsyncEnumerable<ChatProgressUpdate> ProcessAsync(string userMessage, List<ChatMessageEntry> history)
    {
        yield return ChatProgressUpdate.Status("Searching all contracts in the index...");

        // 1. Fetch ALL documents from the search index
        var allDocs = await FetchAllDocumentsAsync();
        yield return ChatProgressUpdate.Status($"Found {allDocs.Count} contracts. Analyzing with AI...");

        if (allDocs.Count == 0)
        {
            yield return ChatProgressUpdate.Final("No contracts found in the index. Please upload some contracts first.");
            yield break;
        }

        // 2. Process in batches through LLM
        var batchResults = new List<string>();
        int processed = 0;

        var batches = allDocs.Chunk(BatchSize).ToList();
        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            processed += batch.Length;
            yield return ChatProgressUpdate.Status($"Analyzing batch {i + 1}/{batches.Count} ({processed}/{allDocs.Count} contracts)...");

            var batchResult = await ProcessBatchAsync(userMessage, batch, history);
            if (!string.IsNullOrWhiteSpace(batchResult))
                batchResults.Add(batchResult);
        }

        // 3. Consolidate all batch results into final answer
        yield return ChatProgressUpdate.Status("Consolidating results...");
        var finalAnswer = await ConsolidateResultsAsync(userMessage, batchResults, allDocs.Count);

        yield return ChatProgressUpdate.Final(finalAnswer);
    }

    private async Task<List<ContractDocument>> FetchAllDocumentsAsync()
    {
        var docs = new List<ContractDocument>();
        var options = new SearchOptions
        {
            Size = 50, // page size
            IncludeTotalCount = true,
            Select = { "id", "sourceFileName", "content", "lastModified" }
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", options);
        await foreach (var result in results.Value.GetResultsAsync())
        {
            var content = result.Document.GetString("content") ?? "";
            DateTime? lastModified = null;
            if (result.Document.TryGetValue("lastModified", out var lm) && lm is DateTimeOffset dto)
                lastModified = dto.UtcDateTime;

            docs.Add(new ContractDocument
            {
                Id = result.Document.GetString("id") ?? "",
                SourceFileName = result.Document.GetString("sourceFileName") ?? "",
                Content = content,
                LastModified = lastModified
            });
        }

        return docs;
    }

    private async Task<string> ProcessBatchAsync(
        string userMessage,
        ContractDocument[] batch,
        List<ChatMessageEntry> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Below are contract documents. For each contract, extract ONLY the information the user asked for.");
        sb.AppendLine("If a contract does not contain the requested information, state that clearly.");
        sb.AppendLine("Be precise and factual. Only extract data that is explicitly stated in the document.");
        sb.AppendLine();

        foreach (var doc in batch)
        {
            sb.AppendLine($"--- CONTRACT: {Path.GetFileNameWithoutExtension(doc.SourceFileName)} (File: {doc.SourceFileName}) ---");
            var content = doc.Content.Length > MaxContentChars
                ? doc.Content[..MaxContentChars] + "\n[... content truncated ...]"
                : doc.Content;
            sb.AppendLine(content);
            sb.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a contract analysis assistant. You analyze contract documents and extract specific information requested by the user. " +
                "Be precise, factual, and concise. Only report information explicitly found in the documents. " +
                "Format your response as a clear list. For each contract, provide the contract name and the requested information. " +
                "If a contract does not contain the requested information, say 'Not found in document'.")
        };

        // Add conversation history (last 4 messages for context)
        foreach (var msg in history.TakeLast(4))
        {
            if (msg.IsUser)
                messages.Add(new UserChatMessage(msg.Content));
            else
                messages.Add(new AssistantChatMessage(msg.Content));
        }

        messages.Add(new UserChatMessage(
            $"User question: {userMessage}\n\nContract documents to analyze:\n{sb}"));

        try
        {
            var options = new ChatCompletionOptions
            {
                Temperature = 0f,
                MaxOutputTokenCount = 2000
            };

            var completion = await _chatClient.CompleteChatAsync(messages, options);
            return completion.Value.Content[0].Text;
        }
        catch (ClientResultException ex) when (ex.Message.Contains("429") || ex.Message.Contains("quota"))
        {
            _logger.LogWarning("Rate limited on batch, waiting 10s before retry...");
            await Task.Delay(10_000);

            try
            {
                var options = new ChatCompletionOptions
                {
                    Temperature = 0f,
                    MaxOutputTokenCount = 2000
                };
                var completion = await _chatClient.CompleteChatAsync(messages, options);
                return completion.Value.Content[0].Text;
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Batch failed after retry");
                return $"[Error processing batch: {retryEx.Message}]";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process batch");
            return $"[Error processing batch: {ex.Message}]";
        }
    }

    private async Task<string> ConsolidateResultsAsync(
        string userMessage,
        List<string> batchResults,
        int totalContracts)
    {
        if (batchResults.Count == 0)
            return "No relevant information found across any contracts.";

        if (batchResults.Count == 1)
            return batchResults[0];

        // Consolidate multiple batch results
        var sb = new StringBuilder();
        sb.AppendLine("Below are partial results from analyzing contracts in batches. Consolidate them into a single, clean, well-formatted answer.");
        sb.AppendLine();
        for (int i = 0; i < batchResults.Count; i++)
        {
            sb.AppendLine($"--- Batch {i + 1} Results ---");
            sb.AppendLine(batchResults[i]);
            sb.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a contract analysis assistant. You are given partial results from batch processing of contracts. " +
                "Consolidate all batch results into a single, clean, well-organized response. " +
                "Remove any duplicates. Present the information in a clear format (table or numbered list). " +
                "If the user asked for specific fields (like name, date, etc.), present only those fields. " +
                $"Total contracts analyzed: {totalContracts}."),
            new UserChatMessage(
                $"Original user question: {userMessage}\n\nPartial results to consolidate:\n{sb}")
        };

        try
        {
            var options = new ChatCompletionOptions
            {
                Temperature = 0f,
                MaxOutputTokenCount = 4000
            };

            var completion = await _chatClient.CompleteChatAsync(messages, options);
            return completion.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to consolidate results");
            // Fall back to concatenating batch results
            return string.Join("\n\n---\n\n", batchResults);
        }
    }

    internal sealed class ContractDocument
    {
        public string Id { get; set; } = "";
        public string SourceFileName { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime? LastModified { get; set; }
    }
}

/// <summary>
/// Progress update sent from the chat service to the UI.
/// </summary>
public sealed class ChatProgressUpdate
{
    public string Message { get; set; } = "";
    public bool IsFinal { get; set; }

    public static ChatProgressUpdate Status(string message) => new() { Message = message, IsFinal = false };
    public static ChatProgressUpdate Final(string message) => new() { Message = message, IsFinal = true };
}

/// <summary>
/// A single message in the chat history.
/// </summary>
public sealed class ChatMessageEntry
{
    public string Content { get; set; } = "";
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
