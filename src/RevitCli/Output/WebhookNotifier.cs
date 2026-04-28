using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCli.Output;

public static class WebhookNotifier
{
    // Defense-in-depth against SSRF / DNS-rebinding / redirect bypass:
    //
    // 1. AllowAutoRedirect = false. A `302 Location: http://127.0.0.1/`
    //    would otherwise drag HttpClient straight into the loopback
    //    interface, undoing every IP check we run on the declared host.
    // 2. ConnectCallback re-resolves the hostname at connect time and
    //    rejects any address inside private/loopback/link-local/multicast
    //    ranges. This catches the rebinding pattern where a hostile DNS
    //    server returns a public IP at validation time and a private IP
    //    moments later when HttpClient does its own resolution. The
    //    second resolution is the one that matters and must be checked.
    // 3. ConnectTimeout caps how long a slow / sinkholed name server can
    //    stall the CLI when a webhook is misconfigured.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = ValidatedConnectAsync,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static async ValueTask<Stream> ValidatedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }

        if (addresses.Length == 0)
            throw new IOException($"DNS returned no addresses for webhook host '{host}'.");

        // Every candidate must be public. One private address in the set is
        // enough to abort — partial trust is not acceptable for an SSRF
        // surface, and the runtime may pick any address from the list.
        foreach (var addr in addresses)
        {
            if (IsPrivateAddress(addr))
                throw new IOException(
                    $"Webhook host '{host}' resolved to private/loopback/link-local address {addr}; refused.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

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
            // Parse first so the scheme check operates on the canonical
            // Uri.Scheme rather than a raw string prefix — `https.evil.com`
            // would pass a naive StartsWith("https") test, but Uri parses
            // it as scheme="http" + authority="evil.com:443" or similar.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                await Console.Error.WriteLineAsync(
                    "Warning: webhook URL is not a valid absolute URL. Skipping notification.");
                return;
            }

            if (!string.Equals(uri.Scheme, "https", StringComparison.Ordinal))
            {
                await Console.Error.WriteLineAsync(
                    "Warning: webhook URL must use HTTPS. Skipping notification.");
                return;
            }

            // Pre-check: reject obvious private hosts up front with a friendly
            // message. The ConnectCallback below also enforces this — that's
            // the actual bypass-proof check, since this one races against DNS
            // rebinding — but the early return saves the round-trip on the
            // happy path of a misconfigured but stable target.
            if (IsLikelyPrivateHost(uri.Host))
            {
                await Console.Error.WriteLineAsync(
                    "Warning: webhook URL points to a private/loopback address. Skipping notification.");
                return;
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(uri, content);

            if (!response.IsSuccessStatusCode)
                await Console.Error.WriteLineAsync(
                    $"Warning: webhook notification failed ({response.StatusCode})");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Warning: webhook notification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Best-effort pre-check for private hosts. Authoritative enforcement
    /// happens inside <see cref="ValidatedConnectAsync"/>; this is the
    /// fast-path / friendly-message variant.
    /// </summary>
    private static bool IsLikelyPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

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
        catch
        {
            // DNS unresolvable — let ConnectCallback decide; the actual
            // request will fail there with a clearer error.
        }

        return false;
    }

    internal static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        // IPv4-mapped IPv6 (`::ffff:a.b.c.d`) — collapse to the underlying
        // IPv4 address before classifying. Without this, `https://[::ffff:127.0.0.1]/`
        // bypasses both the IPv4 ranges below and the fc00::/7 ULA test.
        if (ip.IsIPv4MappedToIPv6)
            return IsPrivateAddress(ip.MapToIPv4());

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            // fc00::/7 unique local (`fc..` and `fd..`).
            if ((bytes[0] & 0xFE) == 0xFC)
                return true;
            // fe80::/10 link-local (`fe80..fe9f` first byte, top two bits of
            // second byte == 10).
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                return true;
            // ff00::/8 multicast.
            if (bytes[0] == 0xFF)
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
            // 127.0.0.0/8 loopback
            if (bytes[0] == 127)
                return true;
            // 169.254.0.0/16 link-local
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            // 0.0.0.0/8 — "this host"; some kernels route 0.0.0.0 to loopback.
            if (bytes[0] == 0)
                return true;
            // 224.0.0.0/4 multicast (224..239 first octet).
            if (bytes[0] >= 224 && bytes[0] <= 239)
                return true;
        }

        return false;
    }
}
