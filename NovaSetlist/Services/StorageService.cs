using System.IO;
using System.Text.Json;
using NovaSetlist.Models;

namespace NovaSetlist.Services;

/// <summary>Persists the sheet cache and the in-progress service to %APPDATA%\NovaSetlist.</summary>
public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NovaSetlist");

    private string CachePath => Path.Combine(_dir, "cache.json");
    private string CurrentPath => Path.Combine(_dir, "current.json");
    private string WindowPath => Path.Combine(_dir, "window.json");

    public CacheData? LoadCache() => Load<CacheData>(CachePath);
    public void SaveCache(CacheData cache) => Save(CachePath, cache);

    public ServiceSet? LoadCurrent() => Load<ServiceSet>(CurrentPath);
    public void SaveCurrent(ServiceSet set) => Save(CurrentPath, set);

    public WindowPlacement? LoadWindow() => Load<WindowPlacement>(WindowPath);
    public void SaveWindow(WindowPlacement placement) => Save(WindowPath, placement);

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return null; // corrupt/unreadable file — behave like a fresh start rather than crash
        }
    }

    private void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }
        catch
        {
            // Saving is best-effort; a failed write must never take the UI down.
        }
    }
}
