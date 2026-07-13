using System.IO;
using System.Text.Json;

namespace NovaSetlist.Services;

public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SpreadsheetId { get; set; } = "";
    public string SongsTab { get; set; } = "Songs";
    public string LeadersTab { get; set; } = "Leaders";

    /// <summary>waveIn product name of the LTC monitor input; "" = off.</summary>
    public string TimecodeDevice { get; set; } = "";

    /// <summary>waveIn product name of the key-detection input; "" = off.</summary>
    public string KeyDetectDevice { get; set; } = "";

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>Loads appsettings.json from next to the exe; falls back to defaults if missing/invalid.</summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath));
                if (cfg is not null)
                {
                    cfg.SpreadsheetId = cfg.SpreadsheetId?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(cfg.SongsTab)) cfg.SongsTab = "Songs";
                    if (string.IsNullOrWhiteSpace(cfg.LeadersTab)) cfg.LeadersTab = "Leaders";
                    cfg.TimecodeDevice = cfg.TimecodeDevice?.Trim() ?? "";
                    cfg.KeyDetectDevice = cfg.KeyDetectDevice?.Trim() ?? "";
                    return cfg;
                }
            }
        }
        catch
        {
            // Fall through to defaults — a bad config file must not stop the app from opening.
        }
        return new AppConfig();
    }

    /// <summary>Writes appsettings.json next to the exe. Returns false if the folder isn't writable.</summary>
    public bool Save()
    {
        try
        {
            // Write-then-rename so an interrupted write can't corrupt the live file.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
