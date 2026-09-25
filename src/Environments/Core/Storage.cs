using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Environments.Core;

public static class Storage
{
    public static readonly string DataDir =
        Environment.GetEnvironmentVariable("ENVIRONMENTS_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Environments");

    static string ConfigPath => Path.Combine(DataDir, "config.json");
    static string UndoPath => Path.Combine(DataDir, "last-closed.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), Json);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception)
        {
            // A broken file must not brick the app: keep a copy and start from defaults.
            try { File.Copy(ConfigPath, ConfigPath + ".broken", true); } catch { }
        }
        var fresh = Defaults.Create();
        Save(fresh);
        return fresh;
    }

    public static void Save(Config cfg) => WriteAtomic(ConfigPath, JsonSerializer.Serialize(cfg, Json));

    public static void SaveUndo(List<UndoEntry> entries) => WriteAtomic(UndoPath, JsonSerializer.Serialize(entries, Json));

    public static List<UndoEntry> LoadUndo()
    {
        try
        {
            if (File.Exists(UndoPath))
                return JsonSerializer.Deserialize<List<UndoEntry>>(File.ReadAllText(UndoPath), Json) ?? new();
        }
        catch { }
        return new();
    }

    public static string ExportPreset(Preset preset) => JsonSerializer.Serialize(preset, Json);
    public static Preset? ImportPreset(string json) => JsonSerializer.Deserialize<Preset>(json, Json);

    static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }
}

public class UndoEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? AppId { get; set; }
}
