using System.Text.Json;
using System.Text.Json.Serialization;
using EasyRazer.Core;

namespace EasyRazer.Config;

public static class ConfigStore
{
    public static string FilePath => Path.Combine(Paths.DataDir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options);
                if (cfg != null)
                {
                    _ = cfg.DefaultProfile; // ensures a Default profile exists
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            // Keep the broken file so the user doesn't lose it, then start fresh.
            Log.Error("Could not read config.json, starting with defaults", ex);
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch { }
        }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(Paths.DataDir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static AppConfig DeepCopy(AppConfig cfg) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(cfg, Options), Options)!;
}
