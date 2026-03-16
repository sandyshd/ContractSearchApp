using ContractDb.Shared.Models;

namespace ContractDb.WebApp.Services;

/// <summary>
/// In-memory run history store. Replace with Table Storage for production persistence.
/// </summary>
public sealed class RunHistoryService
{
    private readonly List<PromptRunResponse> _runs = new();
    private readonly object _lock = new();

    public void Add(PromptRunResponse run)
    {
        lock (_lock)
        {
            _runs.Add(run);
            // Keep only last 200 runs in memory
            if (_runs.Count > 200)
                _runs.RemoveAt(0);
        }
    }

    public List<PromptRunResponse> GetAll()
    {
        lock (_lock)
        {
            return _runs.OrderByDescending(r => r.ExecutedAt).ToList();
        }
    }

    public PromptRunResponse? GetById(string runId)
    {
        lock (_lock)
        {
            return _runs.FirstOrDefault(r =>
                string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
