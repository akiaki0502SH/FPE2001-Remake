using System.Text.Json;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Application;

/// <summary>
/// JSON 设置服务（M08）：%APPDATA%/FPE2001-Remake/settings.json。
/// 键值字典存储；写入原子（临时文件 + 替换）。
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private const string StoreFileName = "settings.json";

    private readonly string _storePath;
    private readonly object _gate = new();
    private Dictionary<string, JsonElement> _values;

    public JsonSettingsService(string? storeDirectory = null)
    {
        var dir = storeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FPE2001-Remake");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, StoreFileName);
        _values = Load();
    }

    public ValueTask<T> GetAsync<T>(string key, T fallback, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(key, out var element))
            {
                try
                {
                    return ValueTask.FromResult(element.Deserialize<T>() ?? fallback);
                }
                catch
                {
                    return ValueTask.FromResult(fallback);
                }
            }
            return ValueTask.FromResult(fallback);
        }
    }

    public ValueTask SetAsync<T>(string key, T value, CancellationToken ct)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            Persist();
        }
        return ValueTask.CompletedTask;
    }

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return [];
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
            var temp = _storePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _storePath, overwrite: true);
        }
        catch
        {
            // 持久化失败不致命（内存值仍有效）
        }
    }
}
