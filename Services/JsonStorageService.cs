using System.Text.Json;
using kanban.net.Models;

namespace kanban.net.Services;

public class JsonStorageService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _filePath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonStorageService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "kanban.json");
    }

    public async Task<KanbanStore> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                var empty = new KanbanStore();
                await SaveInternalAsync(empty);
                return empty;
            }

            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<KanbanStore>(json, _jsonOptions) ?? new KanbanStore();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(KanbanStore store)
    {
        await _lock.WaitAsync();
        try
        {
            await SaveInternalAsync(store);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(KanbanStore store)
    {
        var json = JsonSerializer.Serialize(store, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
