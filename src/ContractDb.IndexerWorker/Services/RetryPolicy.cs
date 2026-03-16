using Microsoft.Extensions.Logging;

namespace ContractDb.IndexerWorker.Services;

/// <summary>
/// Simple retry policy with exponential backoff for indexer operations.
/// </summary>
public sealed class RetryPolicy
{
    private readonly ILogger<RetryPolicy> _logger;

    public RetryPolicy(ILogger<RetryPolicy> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes an async action with exponential backoff retry.
    /// </summary>
    public async Task ExecuteWithRetryAsync(Func<Task> action, int maxRetries = 5, string label = "")
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "[{Label}] Attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}s...",
                    label, attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>
    /// Executes an async function returning T with exponential backoff retry.
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 5, string label = "")
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "[{Label}] Attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}s...",
                    label, attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }
}
