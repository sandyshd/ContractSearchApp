using System.Text.Json;
using ContractDb.Shared.Models;

namespace ContractDb.Shared.Services;

/// <summary>
/// Loads prompt templates and groups from JSON files in the PromptLibrary.
/// </summary>
public sealed class PromptLibraryLoader
{
    private readonly string _basePath;
    private List<PromptTemplate>? _templates;
    private List<PromptGroup>? _groups;

    public PromptLibraryLoader(string basePath)
    {
        _basePath = basePath;
    }

    public async Task<List<PromptTemplate>> LoadTemplatesAsync()
    {
        if (_templates is not null) return _templates;

        var path = Path.Combine(_basePath, "prompts.json");
        if (!File.Exists(path))
            return _templates = new List<PromptTemplate>();

        var json = await File.ReadAllTextAsync(path);
        _templates = JsonSerializer.Deserialize<List<PromptTemplate>>(json, JsonOptions) ?? new();
        return _templates;
    }

    public async Task<List<PromptGroup>> LoadGroupsAsync()
    {
        if (_groups is not null) return _groups;

        var path = Path.Combine(_basePath, "promptGroups.json");
        if (!File.Exists(path))
            return _groups = new List<PromptGroup>();

        var json = await File.ReadAllTextAsync(path);
        _groups = JsonSerializer.Deserialize<List<PromptGroup>>(json, JsonOptions) ?? new();
        return _groups;
    }

    public async Task<PromptTemplate?> GetByIdAsync(string promptId)
    {
        var templates = await LoadTemplatesAsync();
        return templates.FirstOrDefault(t =>
            string.Equals(t.Id, promptId, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}
