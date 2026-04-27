using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RevitCli.Output;

public static class WebhookNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Convenience overload that builds the canonical "check" event payload and
    /// delegates to <see cref="NotifyAsync(string, object)"/>. Field shape:
    /// <code>
    /// {
    ///   event: "check",
    ///   name: <checkName>,
    ///   passed: <displayPassed>,
    ///   failed: <displayFailed>,
    ///   suppressed: <suppressedCount>,
    ///   severityFailed: <bool>,   // true when CheckCommand would exit non-zero
    ///   timestamp: <ISO8601 UTC>,
    ///   profilePath: <path?>      // null when no profile was discovered/specified
    /// }
    /// </code>
    /// All HTTPS / private-host enforcement and best-effort error handling come
    /// from <see cref="NotifyAsync(string, object)"/> — failures here NEVER
    /// throw and never change the caller's exit code.
    /// </summary>
    public static Task NotifyCheckAsync(
        string url,
        string checkName,
        int passed,
        int failed,
        int suppressed,
        bool severityFailed,
        string? profilePath)
    {
        var payload = new
        {
            @event = "check",
            name = checkName,
            passed,
            failed,
            suppressed,
            severityFailed,
            timestamp = DateTime.UtcNow.ToString("o"),
            profilePath,
        };
        return NotifyAsync(url, payload);
    }

    public static async Task NotifyAsync(string url, object payload)
    {
        try
        {
            // Security: only allow HTTPS URLs (block localhost/private by default)
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync(
                    "Warning: webhook URL must use HTTPS. Skipping notification.");
                return;
            }

            var uri = new Uri(url);
            if (IsPrivateHost(uri.Host))
            {
                await Console.Error.WriteLineAsync(
                    "Warning: webhook URL points to a private/loopback address. Skipping notification.");
                return;
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
                await Console.Error.WriteLineAsync(
                    $"Warning: webhook notification failed ({response.StatusCode})");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Warning: webhook notification failed: {ex.Message}");
        }
    }

    private static bool IsPrivateHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ip))
            return IsPrivateAddress(ip);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                if (IsPrivateAddress(addr))
                    return true;
            }
        }
        catch { }

        return false;
    }

    private static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (unique local), ::1 (loopback already covered above)
            var bytes = ip.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            // 127.0.0.0/8
            if (bytes[0] == 127)
                return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
        }

        return false;
    }
}
