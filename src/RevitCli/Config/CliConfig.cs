using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Shared;

namespace RevitCli.Config;

public class CliConfig
{
    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = $"http://127.0.0.1:{ServerInfo.DefaultPort}";

    [JsonPropertyName("defaultOutput")]
    public string DefaultOutput { get; set; } = "table";

    [JsonPropertyName("exportDir")]
    public string ExportDir { get; set; } = ".";

    [JsonPropertyName("revit2024InstallDir")]
    public string? Revit2024InstallDir { get; set; }

    [JsonPropertyName("revit2025InstallDir")]
    public string? Revit2025InstallDir { get; set; }

    [JsonPropertyName("revit2026InstallDir")]
    public string? Revit2026InstallDir { get; set; }

    public string? GetRevitInstallDir(int year)
    {
        return year switch
        {
            2024 => Revit2024InstallDir,
            2025 => Revit2025InstallDir,
            2026 => Revit2026InstallDir,
            _ => null
        };
    }

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcli");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    /// <summary>Path where the add-in writes its discovered port.</summary>
    public static string ServerInfoPath => Path.Combine(ConfigDir, "server.json");

    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException
                                    or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Console.Error.WriteLine($"[RevitCli] Could not read config.json — using defaults. ({ex.Message})");
            return new CliConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
