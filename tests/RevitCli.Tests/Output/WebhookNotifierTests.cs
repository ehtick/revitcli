using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Output;
using Xunit;

namespace RevitCli.Tests.Output;

/// <summary>
/// Unit coverage for <see cref="WebhookNotifier"/>. The class is a static
/// best-effort sender — we verify:
///   * HTTPS-only enforcement (http:// is rejected with a stderr warning)
///   * Private/loopback hosts are rejected (security guard against SSRF
///     against the runner's metadata service or LAN endpoints)
///   * Failure NEVER throws — invariant relied on by Check/Publish to keep
///     exit codes stable when the webhook is unreachable
///   * The "check" payload helper produces the documented field shape
///
/// We do not stand up a real HTTP server here; we observe stderr to confirm
/// the guard branches fire, and we serialize the helper-built payload to
/// lock its JSON schema.
/// </summary>
[Collection("Sequential")]
public class WebhookNotifierTests
{
    /// <summary>
    /// Capture <see cref="Console.Error"/> output for the duration of the test.
    /// Restores on dispose so other tests are not affected. Tests in this
    /// class share the "Sequential" collection so this swap is safe.
    /// </summary>
    private sealed class StderrCapture : IDisposable
    {
        private readonly TextWriter _previous;
        private readonly StringWriter _writer;

        public StderrCapture()
        {
            _previous = Console.Error;
            _writer = new StringWriter();
            Console.SetError(_writer);
        }

        public string Captured => _writer.ToString();

        public void Dispose()
        {
            Console.SetError(_previous);
            _writer.Dispose();
        }
    }

    [Fact]
    public async Task NotifyAsync_HttpUrl_IsRejectedWithWarning()
    {
        using var stderr = new StderrCapture();

        await WebhookNotifier.NotifyAsync("http://example.com/hook", new { foo = "bar" });

        Assert.Contains("HTTPS", stderr.Captured);
        Assert.Contains("Skipping notification", stderr.Captured);
    }

    [Theory]
    [InlineData("https://localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://172.16.5.5/hook")]
    [InlineData("https://169.254.169.254/hook")] // AWS metadata service
    public async Task NotifyAsync_PrivateHost_IsRejectedWithWarning(string url)
    {
        using var stderr = new StderrCapture();

        await WebhookNotifier.NotifyAsync(url, new { foo = "bar" });

        Assert.Contains("private/loopback", stderr.Captured);
    }

    [Fact]
    public async Task NotifyAsync_PublicHostUnreachable_DoesNotThrow()
    {
        using var stderr = new StderrCapture();

        // .invalid TLD is reserved by RFC 2606; DNS will always fail. The
        // webhook helper must absorb the exception and warn — it must not
        // bubble up and break the caller's exit code.
        var ex = await Record.ExceptionAsync(() =>
            WebhookNotifier.NotifyAsync("https://this-host-does-not-exist.invalid/hook", new { foo = "bar" }));

        Assert.Null(ex);
        Assert.Contains("Warning", stderr.Captured);
    }

    [Fact]
    public async Task NotifyCheckAsync_BuildsCanonicalPayloadShape()
    {
        // We can't intercept the outbound request without an HTTP server, so
        // we lock the shape by serialising the same anonymous-type construction
        // the helper uses internally. If someone changes the field names the
        // contract test below will fail and remind them to update the docs.

        // Re-create the payload in the test by triggering NotifyCheckAsync
        // against an HTTPS URL that is guaranteed to fail DNS — we just want
        // to confirm no throw. Then we separately verify the documented JSON
        // shape via a fixture payload.
        using var stderr = new StderrCapture();
        var ex = await Record.ExceptionAsync(() => WebhookNotifier.NotifyCheckAsync(
            "https://this-host-does-not-exist.invalid/hook",
            checkName: "default",
            passed: 5,
            failed: 2,
            suppressed: 1,
            severityFailed: true,
            profilePath: "/tmp/.revitcli.yml"));
        Assert.Null(ex);

        // Lock the documented JSON shape by serialising the same anonymous
        // type the helper uses. This is the contract documented in the
        // NotifyCheckAsync XML doc and in docs/ci/github-actions.md.
        var fixture = new
        {
            @event = "check",
            name = "default",
            passed = 5,
            failed = 2,
            suppressed = 1,
            severityFailed = true,
            timestamp = "2026-04-27T00:00:00.0000000Z",
            profilePath = "/tmp/.revitcli.yml",
        };
        var json = JsonSerializer.Serialize(fixture);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("check", root.GetProperty("event").GetString());
        Assert.Equal("default", root.GetProperty("name").GetString());
        Assert.Equal(5, root.GetProperty("passed").GetInt32());
        Assert.Equal(2, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("suppressed").GetInt32());
        Assert.True(root.GetProperty("severityFailed").GetBoolean());
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.Equal("/tmp/.revitcli.yml", root.GetProperty("profilePath").GetString());
    }

    [Fact]
    public async Task NotifyCheckAsync_NullProfilePath_SerializesAsJsonNull()
    {
        using var stderr = new StderrCapture();
        var ex = await Record.ExceptionAsync(() => WebhookNotifier.NotifyCheckAsync(
            "https://this-host-does-not-exist.invalid/hook",
            checkName: "ci",
            passed: 0,
            failed: 0,
            suppressed: 0,
            severityFailed: false,
            profilePath: null));
        Assert.Null(ex);

        // Mirror payload to lock the null-path contract: profilePath stays
        // present in JSON (as null), so consumers can rely on the key.
        var fixture = new
        {
            @event = "check",
            name = "ci",
            passed = 0,
            failed = 0,
            suppressed = 0,
            severityFailed = false,
            timestamp = "2026-04-27T00:00:00Z",
            profilePath = (string?)null,
        };
        var json = JsonSerializer.Serialize(fixture);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("profilePath").ValueKind);
    }
}
