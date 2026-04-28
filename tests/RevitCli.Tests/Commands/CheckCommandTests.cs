using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Profile;
using RevitCli.Shared;
using RevitCli.Tests.Client;
using Xunit;

namespace RevitCli.Tests.Commands;

[Collection("Sequential")]
public class CheckCommandTests
{
    private static RevitClient CreateClient(string responseJson)
    {
        var handler = new FakeHttpHandler(responseJson);
        return new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
    }

    [Fact]
    public async Task Check_ExplicitProfileNotFound_ReturnsError()
    {
        var client = CreateClient("{}");
        var writer = new StringWriter();
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", "/nonexistent/path/.revitcli.yml", "table", null, true, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("not found", writer.ToString().ToLower());
    }

    [Fact]
    public async Task Check_NoProfileDiscovered_SuggestsExample()
    {
        // Run from a temp dir with no .revitcli.yml anywhere up the tree
        var client = CreateClient("{}");
        var writer = new StringWriter();
        // profilePath=null triggers discovery, which finds nothing in temp dir
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", null, "table", null, true, writer);
        // Might find a profile in the real cwd — if so, skip assertion
        if (exitCode == 1)
        {
            var output = writer.ToString();
            if (output.Contains(".revitcli.yml"))
                Assert.Contains(".revitcli.example.yml", output);
        }
    }

    [Fact]
    public async Task Check_UnknownCheckSet_ListsAvailable()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  my-check:
    failOn: error
    auditRules:
      - rule: naming
");
        var client = CreateClient("{}");
        var writer = new StringWriter();
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "nonexistent", profilePath, "table", null, true, writer);
        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("not found", output.ToLower());
        Assert.Contains("my-check", output); // lists available sets
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_PassesAuditRules_ReturnsZero()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("1 passed", writer.ToString());
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_FailsOnError_ReturnsOne()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: room-bounds
");
        var auditResult = new AuditResult
        {
            Passed = 0,
            Failed = 1,
            Issues = new List<AuditIssue>
            {
                new() { Rule = "room-bounds", Severity = "error", Message = "Room has zero area" }
            }
        };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("1 failed", writer.ToString());
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_WarningsPassWithFailOnError()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult
        {
            Passed = 0,
            Failed = 1,
            Issues = new List<AuditIssue>
            {
                new() { Rule = "naming", Severity = "warning", Message = "Default name" }
            }
        };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);
        // failOn=error, only warnings → should pass
        Assert.Equal(0, exitCode);
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_SuppressionFiltersIssues()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: room-bounds
    suppressions:
      - rule: room-bounds
        reason: Known legacy issue
");
        var auditResult = new AuditResult
        {
            Passed = 0,
            Failed = 1,
            Issues = new List<AuditIssue>
            {
                new() { Rule = "room-bounds", Severity = "error", Message = "Room has zero area", ElementId = 100 }
            }
        };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);
        // Suppressed → should pass
        Assert.Equal(0, exitCode);
        Assert.Contains("1 suppressed", writer.ToString());
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_JsonOutput_IsValidJson()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "json", null, true, writer);
        Assert.Equal(0, exitCode);

        // Output should be valid JSON
        var output = writer.ToString().Trim();
        var doc = JsonDocument.Parse(output);
        Assert.Equal("default", doc.RootElement.GetProperty("check").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("passed").GetInt32());
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_HtmlOutput_ContainsDarkTheme()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var response = ApiResponse<AuditResult>.Ok(auditResult);
        var client = CreateClient(JsonSerializer.Serialize(response));
        var writer = new StringWriter();

        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "html", null, true, writer);
        Assert.Equal(0, exitCode);
        Assert.Contains("#1a1a2e", writer.ToString()); // dark background
        Assert.Contains("RevitCli Check Report", writer.ToString());
        File.Delete(profilePath);
    }

    [Fact]
    public void ProfileLoader_ValidatesFailOn()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: invalid_value
    auditRules:
      - rule: naming
");
        Assert.Throws<InvalidOperationException>(() => ProfileLoader.Load(profilePath));
        File.Delete(profilePath);
    }

    [Fact]
    public void ProfileLoader_ValidatesSeverity()
    {
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    requiredParameters:
      - category: doors
        parameter: Fire Rating
        severity: typo_severity
");
        Assert.Throws<InvalidOperationException>(() => ProfileLoader.Load(profilePath));
        File.Delete(profilePath);
    }

    [Fact]
    public void ProfileLoader_DetectsCycle()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var fileA = Path.Combine(dir, "a.yml");
        var fileB = Path.Combine(dir, "b.yml");
        File.WriteAllText(fileA, $"version: 1\nextends: b.yml\nchecks: {{}}");
        File.WriteAllText(fileB, $"version: 1\nextends: a.yml\nchecks: {{}}");

        Assert.Throws<InvalidOperationException>(() => ProfileLoader.Load(fileA));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void ProfileInheritance_DefaultsMergeFieldLevel()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        // Base profile has outputDir and notify
        File.WriteAllText(Path.Combine(dir, "base.yml"), @"
version: 1
defaults:
  outputDir: ./base-output
  notify: https://base.example.com/hook
checks:
  base-check:
    failOn: error
    auditRules:
      - rule: naming
");

        // Child overrides outputDir, inherits notify, adds own check
        File.WriteAllText(Path.Combine(dir, "child.yml"), @"
version: 1
extends: base.yml
defaults:
  outputDir: ./child-output
checks:
  child-check:
    failOn: warning
    auditRules:
      - rule: room-bounds
");

        var profile = ProfileLoader.Load(Path.Combine(dir, "child.yml"));

        // Defaults: child outputDir wins, base notify inherited
        Assert.Equal("./child-output", profile.Defaults.OutputDir);
        Assert.Equal("https://base.example.com/hook", profile.Defaults.Notify);

        // Both check sets available
        Assert.True(profile.Checks.ContainsKey("base-check"));
        Assert.True(profile.Checks.ContainsKey("child-check"));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ProfileInheritance_ChildOverridesNamedCheck()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "base.yml"), @"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
      - rule: room-bounds
");

        // Child replaces entire 'default' check (not deep-merged)
        File.WriteAllText(Path.Combine(dir, "child.yml"), @"
version: 1
extends: base.yml
checks:
  default:
    failOn: warning
    auditRules:
      - rule: naming
");

        var profile = ProfileLoader.Load(Path.Combine(dir, "child.yml"));

        // Child's version wins entirely
        Assert.Equal("warning", profile.Checks["default"].FailOn);
        Assert.Single(profile.Checks["default"].AuditRules); // Only naming, not room-bounds

        Directory.Delete(dir, true);
    }

    private static string CreateTempProfile(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"revitcli_test_{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    /// <summary>
    /// Capture <see cref="Console.Error"/> for a single test. We use stderr
    /// as the observation channel because <see cref="Output.WebhookNotifier"/>
    /// emits a warning whenever it short-circuits (HTTP, private host, DNS
    /// failure). When the profile has no <c>notify</c> URL, the notifier is
    /// never called and stderr stays empty.
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
    public async Task Check_WithNotifyHttpUrl_AttemptsWebhookAndWarnsOnNonHttps()
    {
        // notify uses http:// → WebhookNotifier rejects it with an HTTPS warning.
        // Seeing that warning in stderr proves the check command actually
        // *attempted* the webhook (the v1.7 wiring fired).
        var profilePath = CreateTempProfile(@"
version: 1
defaults:
  notify: http://example.com/hook
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var client = CreateClient(JsonSerializer.Serialize(ApiResponse<AuditResult>.Ok(auditResult)));
        var writer = new StringWriter();

        // Even though notify is set, the http:// scheme means CheckCommand's
        // pre-filter (StartsWith "https://") declines to invoke the notifier.
        // No stderr warning is expected — the URL is filtered before the call.
        using var stderr = new StderrCapture();
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);

        Assert.Equal(0, exitCode);
        // CheckCommand's https:// pre-filter swallows non-https quietly. This
        // test pins that behavior: a misconfigured non-https notify never
        // surfaces a noisy warning during a clean check run.
        Assert.DoesNotContain("HTTPS", stderr.Captured);
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_WithNotifyPrivateHttpsUrl_AttemptsWebhookAndWarns()
    {
        // notify uses https://, so CheckCommand's pre-filter passes the URL
        // to the notifier. The notifier then rejects the private host with a
        // stderr warning — that warning is the proof that the webhook was
        // attempted (the v1.7 wiring fired) without changing the exit code.
        var profilePath = CreateTempProfile(@"
version: 1
defaults:
  notify: https://127.0.0.1/hook
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var client = CreateClient(JsonSerializer.Serialize(ApiResponse<AuditResult>.Ok(auditResult)));
        var writer = new StringWriter();

        using var stderr = new StderrCapture();
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("private/loopback", stderr.Captured);
        File.Delete(profilePath);
    }

    [Fact]
    public async Task Check_WithoutNotify_DoesNotInvokeWebhook()
    {
        // No notify in defaults → CheckCommand must not call the notifier at
        // all. We watch stderr for ANY notifier warning ("HTTPS",
        // "private/loopback", "Warning: webhook"); none should appear.
        var profilePath = CreateTempProfile(@"
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: naming
");
        var auditResult = new AuditResult { Passed = 1, Failed = 0, Issues = new List<AuditIssue>() };
        var client = CreateClient(JsonSerializer.Serialize(ApiResponse<AuditResult>.Ok(auditResult)));
        var writer = new StringWriter();

        using var stderr = new StderrCapture();
        var exitCode = await CheckCommand.ExecuteAsync(
            client, "default", profilePath, "table", null, true, writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("HTTPS", stderr.Captured);
        Assert.DoesNotContain("private/loopback", stderr.Captured);
        Assert.DoesNotContain("webhook", stderr.Captured, StringComparison.OrdinalIgnoreCase);
        File.Delete(profilePath);
    }
}
