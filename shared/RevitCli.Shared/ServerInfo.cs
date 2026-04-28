using System.Text.Json.Serialization;

namespace RevitCli.Shared;

public class ServerInfo
{
    /// <summary>
    /// Default port the add-in API server binds to. Single source of truth shared
    /// between the CLI client and the add-in server to prevent drift.
    /// </summary>
    public const int DefaultPort = 17839;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("revitVersion")]
    public string RevitVersion { get; set; } = "";

    [JsonPropertyName("startedAt")]
    public string StartedAt { get; set; } = "";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";
}
