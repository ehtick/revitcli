using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class FixCommandTests
{
    [Fact]
    public async Task Fix_DryRun_PrintsPlanAndDoesNotCallSet()
    {
        var profilePath = WriteProfile("""
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: required-parameter
fixes:
  - rule: required-parameter
    category: doors
    parameter: Mark
    strategy: setParam
    value: "D-{element.id}"
""");
        try
        {
            var handler = new QueueHttpHandler();
            handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult
            {
                Passed = 0,
                Failed = 1,
                Issues = new List<AuditIssue>
                {
                    new()
                    {
                        Rule = "required-parameter",
                        Severity = "warning",
                        Message = "Missing Mark",
                        ElementId = 10,
                        Category = "doors",
                        Parameter = "Mark",
                        CurrentValue = "",
                        Source = "structured"
                    }
                }
            }));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await FixCommand.ExecuteAsync(
                client, "default", profilePath, Array.Empty<string>(), null,
                dryRun: true, apply: false, yes: false, allowInferred: false,
                maxChanges: 50, baselineOutput: null, noSnapshot: false, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Fix plan", writer.ToString());
            Assert.Contains("D-10", writer.ToString());
            Assert.DoesNotContain(handler.Requests, r => r.EndsWith("/api/elements/set", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(profilePath);
        }
    }

    [Fact]
    public async Task Fix_ApplyWithoutAllowInferred_BlocksBeforeSnapshot()
    {
        var profilePath = WriteProfile("""
version: 1
checks:
  default:
    failOn: error
""");
        try
        {
            var handler = new QueueHttpHandler();
            handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult
            {
                Issues = new List<AuditIssue>
                {
                    new()
                    {
                        Rule = "required-parameter",
                        Severity = "warning",
                        Message = "Missing Mark",
                        ElementId = 10,
                        Category = "doors",
                        Parameter = "Mark",
                        CurrentValue = "",
                        Source = "structured"
                    }
                }
            }));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await FixCommand.ExecuteAsync(
                client, "default", profilePath, Array.Empty<string>(), null,
                dryRun: false, apply: true, yes: true, allowInferred: false,
                maxChanges: 50, baselineOutput: null, noSnapshot: false, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("allow-inferred", writer.ToString());
            Assert.DoesNotContain(handler.Requests, r => r.EndsWith("/api/snapshot", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(profilePath);
        }
    }

    [Fact]
    public async Task Fix_DryRun_RulesCanBeCommaSeparatedAndRepeated()
    {
        var profilePath = WriteProfile("""
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: required-parameter
      - rule: naming
fixes:
  - rule: required-parameter
    category: doors
    parameter: Mark
    strategy: setParam
    value: "D-{element.id}"
""");
        try
        {
            var handler = new QueueHttpHandler();
            handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult
            {
                Passed = 0,
                Failed = 1,
                Issues = new List<AuditIssue>
                {
                    new()
                    {
                        Rule = "required-parameter",
                        Severity = "warning",
                        Message = "Missing Mark",
                        ElementId = 10,
                        Category = "doors",
                        Parameter = "Mark",
                        CurrentValue = "",
                        Source = "structured"
                    },
                }
            }));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await FixCommand.ExecuteAsync(
                client, "default", profilePath, new[] { " required-parameter ", " naming,required-parameter" }, null,
                dryRun: false, apply: false, yes: false, allowInferred: false,
                maxChanges: 50, baselineOutput: null, noSnapshot: false, writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Fix plan", output);
            Assert.Contains("1 action", output);
            Assert.Contains("required-parameter", output);
        }
        finally
        {
            File.Delete(profilePath);
        }
    }

    [Fact]
    public async Task Fix_Apply_FailsWhenBaselineCannotBeSavedAndSkipsSet()
    {
        var profilePath = WriteProfile("""
version: 1
checks:
  default:
    failOn: error
    auditRules:
      - rule: required-parameter
fixes:
  - rule: required-parameter
    category: doors
    parameter: Mark
    strategy: setParam
    value: "D-{element.id}"
""");
        // Use a path whose parent is an existing FILE (not a directory). FixCommand calls
        // Directory.CreateDirectory(parent) before WriteAllTextAsync, and that throws
        // IOException on both Windows and Linux when the parent path is occupied by a
        // file. The previous "<>" filename trick was Windows-only (Linux allows those
        // characters in filenames).
        var blockerFile = Path.Combine(Path.GetTempPath(), $"revitcli_fix_blocker_{Guid.NewGuid():N}");
        File.WriteAllText(blockerFile, "blocker");
        var invalidBaseline = Path.Combine(blockerFile, $"baseline_{Guid.NewGuid():N}.json");
        try
        {
            var handler = new QueueHttpHandler();
            handler.Enqueue("/api/audit", ApiResponse<AuditResult>.Ok(new AuditResult
            {
                Passed = 0,
                Failed = 1,
                Issues = new List<AuditIssue>
                {
                    new()
                    {
                        Rule = "required-parameter",
                        Severity = "warning",
                        Message = "Missing Mark",
                        ElementId = 10,
                        Category = "doors",
                        Parameter = "Mark",
                        CurrentValue = "",
                        Source = "structured"
                    }
                }
            }));
            handler.Enqueue("/api/snapshot", ApiResponse<ModelSnapshot>.Ok(new ModelSnapshot()));
            var client = new RevitClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });
            var writer = new StringWriter();

            var exitCode = await FixCommand.ExecuteAsync(
                client, "default", profilePath, Array.Empty<string>(), null,
                dryRun: false, apply: true, yes: true, allowInferred: true,
                maxChanges: 50, baselineOutput: invalidBaseline, noSnapshot: false, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("Error: failed to save baseline snapshot", writer.ToString());
            Assert.DoesNotContain(handler.Requests, r => r.EndsWith("/api/elements/set", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(profilePath);
            if (File.Exists(blockerFile)) File.Delete(blockerFile);
        }
    }

    private static string WriteProfile(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"revitcli_fix_command_{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, yaml);
        return path;
    }
}

internal sealed class QueueHttpHandler : HttpMessageHandler
{
    private readonly Queue<(string Path, string Json)> _responses = new();
    public List<string> Requests { get; } = new();

    public void Enqueue<T>(string path, ApiResponse<T> response)
    {
        _responses.Enqueue((path, JsonSerializer.Serialize(response)));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsolutePath);
        var next = _responses.Dequeue();
        Assert.Equal(next.Path, request.RequestUri.AbsolutePath);
        if (request.Content != null)
            _ = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(next.Json, Encoding.UTF8, "application/json")
        };
    }
}
