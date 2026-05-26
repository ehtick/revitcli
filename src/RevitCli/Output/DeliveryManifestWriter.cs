using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitCli.Output;

public static class DeliveryManifestWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string? Append(string baseDir, object entry)
    {
        try
        {
            var manifestDir = Path.Combine(baseDir, ".revitcli", "deliveries");
            Directory.CreateDirectory(manifestDir);

            var manifestPath = Path.Combine(manifestDir, "manifest.jsonl");
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            File.AppendAllText(manifestPath, line + Environment.NewLine);
            return manifestPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                    or System.Security.SecurityException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Delivery manifest write failed: {ex.Message}");
            return null;
        }
    }

    public static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
