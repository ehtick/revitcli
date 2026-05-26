using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Commands;
using RevitCli.Fix;
using RevitCli.Plans;
using RevitCli.Shared;
using Xunit;

namespace RevitCli.Tests.Commands;

public class RollbackCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Execute_MissingBaseline_ReturnsOne()
    {
        var client = MakeClient(new QueueHttpHandler());
        var writer = new StringWriter();
        var missingBaseline = Path.Combine(Path.GetTempPath(), $"revitcli-missing-baseline-{Guid.NewGuid():N}.json");

        var exitCode = await RollbackCommand.ExecuteAsync(
            client, missingBaseline, dryRun: true, yes: false, maxChanges: 50, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("baseline", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_MissingJournal_ReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);

            var client = MakeClient(new QueueHttpHandler());
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("journal", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_MaxChangesZero_ReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 101,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "OLD-101",
                    NewValue = "NEW-101"
                }
            });

            var client = MakeClient(new QueueHttpHandler());
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: true, yes: false, maxChanges: 0, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("max-changes", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_MalformedJournalAction_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 0,
                    Category = "doors",
                    Parameter = "   ",
                    OldValue = "OLD-101",
                    NewValue = "NEW-101"
                }
            });

            var handler = new QueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("invalid", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_NullJournalAction_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournalJson(
                baselinePath,
                $$"""
                {
                  "schemaVersion": 1,
                  "action": "fix",
                  "checkName": "default",
                  "baselinePath": {{JsonSerializer.Serialize(baselinePath)}},
                  "startedAt": "2026-04-26T00:00:00Z",
                  "user": "tester",
                  "actions": [null]
                }
                """);

            var handler = new QueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("invalid", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_JournalBaselinePathMismatch_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            var otherBaselinePath = Path.Combine(tempDir, "other-baseline.json");
            WriteBaseline(baselinePath);
            WriteJournalJson(
                baselinePath,
                $$"""
                {
                  "schemaVersion": 1,
                  "action": "fix",
                  "checkName": "default",
                  "baselinePath": {{JsonSerializer.Serialize(otherBaselinePath)}},
                  "startedAt": "2026-04-26T00:00:00Z",
                  "user": "tester",
                  "actions": [
                    {
                      "elementId": 101,
                      "category": "doors",
                      "parameter": "Mark",
                      "oldValue": "OLD-101",
                      "newValue": "NEW-101"
                    }
                  ]
                }
                """);

            var handler = new QueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("baseline", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_DryRun_PreflightsReverseActions_AndDoesNotApply()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 101,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "OLD-101",
                    NewValue = "NEW-101"
                },
                new FixAction
                {
                    ElementId = 202,
                    Category = "walls",
                    Parameter = "Fire Rating",
                    OldValue = "",
                    NewValue = "2h"
                }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 101, Name = "Door 101", OldValue = "NEW-101", NewValue = "OLD-101" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 202, Name = "Wall 202", OldValue = "2h", NewValue = "" }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: true, yes: false, maxChanges: 50, writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("[101]", output);
            Assert.Contains("Mark", output);
            Assert.Contains("NEW-101", output);
            Assert.Contains("OLD-101", output);
            Assert.Contains("Dry run", output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[2]);
            Assert.DoesNotContain("\"dryRun\":false", string.Join('\n', handler.RequestBodies));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_WritesOldValues_WhenCurrentMatchesNewValue()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 303,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "RESTORE-ME",
                    NewValue = "APPLIED-VALUE"
                }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "APPLIED-VALUE", NewValue = "RESTORE-ME" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "RESTORE-ME", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.Contains("\"value\":\"RESTORE-ME\"", handler.RequestBodies[1]);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[2]);
            Assert.Contains("\"value\":\"RESTORE-ME\"", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_AcceptsRelativeJournalBaselinePath_WhenCalledWithAbsoluteBaselineFromDifferentCwd()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, ".revitcli", "baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            WriteBaseline(baselinePath);
            WriteJournalJson(
                baselinePath,
                $$"""
                {
                  "schemaVersion": 1,
                  "action": "fix",
                  "checkName": "default",
                  "baselinePath": {{JsonSerializer.Serialize(Path.Combine(".revitcli", "baseline.json"))}},
                  "startedAt": "2026-04-26T00:00:00Z",
                  "user": "tester",
                  "actions": [
                    {
                      "elementId": 303,
                      "category": "doors",
                      "parameter": "Mark",
                      "oldValue": "RESTORE-ME",
                      "newValue": "APPLIED-VALUE"
                    }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "APPLIED-VALUE", NewValue = "RESTORE-ME" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "RESTORE-ME", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, handler.RequestBodies.Count);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_RejectsBareRelativeJournalBaselinePath_ForNestedBaseline()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, ".revitcli", "baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            WriteBaseline(baselinePath);
            WriteJournalJson(
                baselinePath,
                """
                {
                  "schemaVersion": 1,
                  "action": "fix",
                  "checkName": "default",
                  "baselinePath": "baseline.json",
                  "startedAt": "2026-04-26T00:00:00Z",
                  "user": "tester",
                  "actions": [
                    {
                      "elementId": 303,
                      "category": "doors",
                      "parameter": "Mark",
                      "oldValue": "RESTORE-ME",
                      "newValue": "APPLIED-VALUE"
                    }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("baseline", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_RejectsRelativeJournalBaselinePathThatEscapesDefaultDirectory()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, ".revitcli", "baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            WriteBaseline(baselinePath);
            WriteJournalJson(
                baselinePath,
                """
                {
                  "schemaVersion": 1,
                  "action": "fix",
                  "checkName": "default",
                  "baselinePath": ".revitcli/../baseline.json",
                  "startedAt": "2026-04-26T00:00:00Z",
                  "user": "tester",
                  "actions": [
                    {
                      "elementId": 303,
                      "category": "doors",
                      "parameter": "Mark",
                      "oldValue": "RESTORE-ME",
                      "newValue": "APPLIED-VALUE"
                    }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("baseline", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_RejectsCurrentDocumentPathMismatch_AndDoesNotSetParameters()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath, documentPath: @"C:\models\expected.rvt");
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 303,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "RESTORE-ME",
                    NewValue = "APPLIED-VALUE"
                }
            });

            var handler = new RecordingQueueHttpHandler();
            handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
            {
                RevitVersion = "2026",
                RevitYear = 2026,
                DocumentName = "actual.rvt",
                DocumentPath = @"C:\models\actual.rvt"
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("document", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Single(handler.Requests);
            Assert.Equal("/api/status", handler.Requests[0]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_EmptyPreview_ReturnsOne_AndDoesNotApply()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 404,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "RESTORE-ME",
                    NewValue = "APPLIED-VALUE"
                }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>()
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("preview", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PreviewMissingMatchingElement_ReturnsOne_AndDoesNotApply()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction
                {
                    ElementId = 404,
                    Category = "doors",
                    Parameter = "Mark",
                    OldValue = "RESTORE-ME",
                    NewValue = "APPLIED-VALUE"
                }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 999, Name = "Door 404", OldValue = "SOMEONE-ELSE-EDITED", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("matching", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_ContinuesAfterSinglePreviewFailure_AndReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction { ElementId = 1, Category = "doors", Parameter = "Mark", OldValue = "A", NewValue = "B" },
                new FixAction { ElementId = 2, Category = "doors", Parameter = "Mark", OldValue = "C", NewValue = "D" }
            });

            var handler = new RecordingQueueHttpHandler();
            handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
            {
                RevitVersion = "2026",
                RevitYear = 2026,
                DocumentName = "test",
                DocumentPath = "test.rvt"
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Fail("preview failed"));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 2, Name = "Door 2", OldValue = "D", NewValue = "C" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 2, Name = "Door 2", OldValue = "C", NewValue = "C" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            var output = writer.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("restored 1", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 error", output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(4, handler.RequestBodies.Count);
            Assert.Contains("\"elementId\":2", handler.RequestBodies[2]);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[3]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PreviewApiFailure_ReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction { ElementId = 1, Category = "doors", Parameter = "Mark", OldValue = "A", NewValue = "B" }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Fail("preview failed"));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("preview", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, handler.RequestBodies.Count);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ApplyApiFailure_ReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction { ElementId = 1, Category = "doors", Parameter = "Mark", OldValue = "A", NewValue = "B" }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 1, Name = "Door 1", OldValue = "B", NewValue = "A" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Fail("apply failed"));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("apply", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_Apply_ContinuesAfterSingleApplyFailure_AndReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction { ElementId = 1, Category = "doors", Parameter = "Mark", OldValue = "A", NewValue = "B" },
                new FixAction { ElementId = 2, Category = "doors", Parameter = "Mark", OldValue = "C", NewValue = "D" }
            });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 1, Name = "Door 1", OldValue = "B", NewValue = "A" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Fail("apply failed"));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 2, Name = "Door 2", OldValue = "D", NewValue = "C" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 2, Name = "Door 2", OldValue = "C", NewValue = "C" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: true, maxChanges: 50, writer);

            var output = writer.ToString();
            Assert.Equal(1, exitCode);
            Assert.Contains("restored 1", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 error", output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(5, handler.RequestBodies.Count);
            Assert.Contains("\"elementId\":2", handler.RequestBodies[3]);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[4]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ApplyWithoutYes_ReturnsOne()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var baselinePath = Path.Combine(tempDir, "baseline.json");
            WriteBaseline(baselinePath);
            WriteJournal(baselinePath, new[]
            {
                new FixAction { ElementId = 1, Category = "doors", Parameter = "Mark", OldValue = "A", NewValue = "B" }
            });

            var client = MakeClient(new QueueHttpHandler());
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, baselinePath, dryRun: false, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("--yes", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_SetPlanReceiptDryRun_UsesLegacyPreviewFallback_AndDoesNotApply()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "set",
                param: "Mark",
                actions: new List<PlanReceiptRollbackAction>(),
                preview: new List<SetPreviewItem>
                {
                    new() { Id = 100, Name = "Door 100", OldValue = "OLD-100", NewValue = "NEW-100" }
                });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 100, Name = "Door 100", OldValue = "NEW-100", NewValue = "OLD-100" }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("[100]", output);
            Assert.Contains("NEW-100", output);
            Assert.Contains("OLD-100", output);
            Assert.Contains("Dry run", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Safe apply command after review", output);
            Assert.Contains(receiptPath, output);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", string.Join('\n', handler.RequestBodies));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptDryRun_EmitsShellSafeApplyCommand()
    {
        var rootDir = CreateTempDirectory();
        var tempDir = Path.Combine(rootDir, "danger $(touch hacked)' dir");
        Directory.CreateDirectory(tempDir);
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "APPLIED-VALUE", NewValue = "RESTORE-ME" }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Safe apply command after review", output);
            Assert.Contains("'\"'\"'", output);
            Assert.Contains("$(touch hacked)", output);
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_SetPlanReceiptApply_RestoresOldValue_WhenCurrentMatchesReceiptNewValue()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "set",
                actions: new List<PlanReceiptRollbackAction>
                {
                    new()
                    {
                        ElementId = 303,
                        Param = "Mark",
                        OldValue = "RESTORE-ME",
                        NewValue = "APPLIED-VALUE",
                        Source = "set"
                    }
                });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "APPLIED-VALUE", NewValue = "RESTORE-ME" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "RESTORE-ME", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.Contains("\"param\":\"Mark\"", handler.RequestBodies[1]);
            Assert.Contains("\"value\":\"RESTORE-ME\"", handler.RequestBodies[1]);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[2]);
            Assert.Contains("\"value\":\"RESTORE-ME\"", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ImportPlanReceiptApply_UsesPerParameterRollbackActions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "import",
                actions: new List<PlanReceiptRollbackAction>
                {
                    new()
                    {
                        ElementId = 404,
                        Param = "Lock",
                        OldValue = "OLD-LOCK",
                        NewValue = "YALE-500",
                        Source = "import"
                    }
                });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 404, Name = "Door 404", OldValue = "YALE-500", NewValue = "OLD-LOCK" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 404, Name = "Door 404", OldValue = "OLD-LOCK", NewValue = "OLD-LOCK" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"param\":\"Lock\"", handler.RequestBodies[1]);
            Assert.Contains("\"value\":\"OLD-LOCK\"", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_SheetIssuePlanReceiptApply_UsesPerParameterRollbackActions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "sheet-issue",
                actions: new List<PlanReceiptRollbackAction>
                {
                    new()
                    {
                        ElementId = 10,
                        Param = "Sheet Issue Date",
                        OldValue = "2026-05-01",
                        NewValue = "2026-05-20",
                        Source = "sheet-issue"
                    }
                });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 10, Name = "A-101", OldValue = "2026-05-20", NewValue = "2026-05-01" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 10, Name = "A-101", OldValue = "2026-05-01", NewValue = "2026-05-01" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"param\":\"Sheet Issue Date\"", handler.RequestBodies[1]);
            Assert.Contains("\"value\":\"2026-05-01\"", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_SheetRenumberPlanReceiptApply_UsesPerParameterRollbackActions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "sheet-renumber",
                actions: new List<PlanReceiptRollbackAction>
                {
                    new()
                    {
                        ElementId = 10,
                        Param = "Sheet Number",
                        OldValue = "TMP-001",
                        NewValue = "A-101",
                        Source = "sheet-renumber"
                    }
                });

            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 10, Name = "A-101", OldValue = "A-101", NewValue = "TMP-001" }
                }
            }));
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 10, Name = "TMP-001", OldValue = "TMP-001", NewValue = "TMP-001" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("restored 1", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"param\":\"Sheet Number\"", handler.RequestBodies[1]);
            Assert.Contains("\"value\":\"TMP-001\"", handler.RequestBodies[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_LinkRepairPlanReceiptApply_UsesLinkRepairRollbackActions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WriteLinkRepairReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/links/repair", ApiResponse<LinkRepairResult>.Ok(new LinkRepairResult
            {
                Affected = 1,
                Preview =
                {
                    new LinkRepairOperation
                    {
                        LinkId = 4101,
                        LinkTypeId = 4201,
                        LinkName = "Structural Model",
                        TypeName = "Structural Model.rvt",
                        OldPath = @"D:\coordination\new-struct.rvt",
                        NewPath = @"D:\coordination\old-struct.rvt",
                        OldLoaded = true,
                        NewLoaded = false
                    }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Restored 1 link repair", writer.ToString());
            Assert.Contains("\"oldPath\":\"D:\\\\coordination\\\\new-struct.rvt\"", handler.RequestBodies[1]);
            Assert.Contains("\"newPath\":\"D:\\\\coordination\\\\old-struct.rvt\"", handler.RequestBodies[1]);
            Assert.Contains("\"dryRun\":false", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_LinkRepairPlanReceiptApply_ShowsManualRecovery_WhenOriginalSourceMissing()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WriteLinkRepairReceipt(tempDir, oldPathExists: false);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/links/repair", ApiResponse<LinkRepairResult>.Ok(new LinkRepairResult
            {
                Failures =
                {
                    new CoordinationRepairFailure
                    {
                        Id = 4201,
                        Name = "Structural Model",
                        Code = "link-validation-failed",
                        Message = @"New link path does not exist: 'D:\coordination\old-struct.rvt'."
                    }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("Manual recovery required", writer.ToString());
            Assert.Contains(@"D:\coordination\old-struct.rvt", writer.ToString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ModelMapPlanReceiptDryRun_UsesModelMapRollbackActions()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WriteModelMapReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/model/map/fix", ApiResponse<ModelMapFixResult>.Ok(new ModelMapFixResult
            {
                Affected = 1,
                Preview =
                {
                    new ModelMapFixOperation
                    {
                        ElementId = 5101,
                        ElementName = "Room 101",
                        Category = "Rooms",
                        Field = "workset",
                        OldValue = "Architecture",
                        NewValue = "Interior"
                    }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Dry run: 1 model map rollback", writer.ToString());
            Assert.Contains("\"oldValue\":\"Architecture\"", handler.RequestBodies[1]);
            Assert.Contains("\"newValue\":\"Interior\"", handler.RequestBodies[1]);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ModelMapPlanReceiptDryRun_AllowsClearedPhaseDemolishedRollback()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WriteModelMapReceipt(
                tempDir,
                field: "phaseDemolished",
                oldValue: null,
                newValue: "Existing");
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/model/map/fix", ApiResponse<ModelMapFixResult>.Ok(new ModelMapFixResult
            {
                Affected = 1,
                Preview =
                {
                    new ModelMapFixOperation
                    {
                        ElementId = 5101,
                        ElementName = "Room 101",
                        Category = "Rooms",
                        Field = "phaseDemolished",
                        OldValue = "Existing",
                        NewValue = ""
                    }
                }
            }));
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"field\":\"phaseDemolished\"", handler.RequestBodies[1]);
            Assert.Contains("\"oldValue\":\"Existing\"", handler.RequestBodies[1]);
            Assert.Contains("\"newValue\":\"\"", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptApplyWithoutYes_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("--yes", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptRespectsMaxChanges_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                actions: new List<PlanReceiptRollbackAction>
                {
                    new() { ElementId = 1, Param = "Mark", OldValue = "A", NewValue = "B", Source = "set" },
                    new() { ElementId = 2, Param = "Mark", OldValue = "C", NewValue = "D", Source = "set" }
                });

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 1, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("max-changes", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_UnsupportedPlanReceipt_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, "unsupported.receipt.json");
            File.WriteAllText(
                receiptPath,
                """
                {
                  "schemaVersion": "plan-receipt.v9",
                  "operation": "set",
                  "rollbackActions": [
                    { "elementId": 1, "param": "Mark", "oldValue": "A", "newValue": "B", "source": "set" }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("unsupported plan receipt", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanLikeReceiptWithoutSchema_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, "missing-schema.receipt.json");
            File.WriteAllText(
                receiptPath,
                """
                {
                  "action": "plan.apply",
                  "operation": "set",
                  "rollbackActions": [
                    { "elementId": 1, "param": "Mark", "oldValue": "A", "newValue": "B", "source": "set" }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("missing schemaVersion", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_UnsupportedPlanReceiptOperation_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, "unsupported-operation.receipt.json");
            File.WriteAllText(
                receiptPath,
                """
                {
                  "schemaVersion": "plan-receipt.v1",
                  "action": "plan.apply",
                  "operation": "rogue",
                  "rollbackActions": [
                    { "elementId": 1, "param": "Mark", "oldValue": "A", "newValue": "B", "source": "rogue" }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("unsupported plan receipt operation", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptNullRollbackAction_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, "null-action.receipt.json");
            File.WriteAllText(
                receiptPath,
                """
                {
                  "schemaVersion": "plan-receipt.v1",
                  "action": "plan.apply",
                  "operation": "set",
                  "modelPath": "test.rvt",
                  "documentName": "test",
                  "documentVersion": "2026",
                  "rollbackActions": [ null ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("rollback action at index 0", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("entry is null", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("set")]
    [InlineData("import")]
    [InlineData("sheet-issue")]
    [InlineData("sheet-renumber")]
    [InlineData("room-numbering")]
    [InlineData("mark-assignment")]
    public async Task Execute_PlanReceiptMalformedRollbackAction_ReturnsOne_AndDoesNotCallApi(string operation)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, $"{operation}-malformed.receipt.json");
            File.WriteAllText(
                receiptPath,
                $$"""
                {
                  "schemaVersion": "plan-receipt.v1",
                  "action": "plan.apply",
                  "operation": "{{operation}}",
                  "modelPath": "test.rvt",
                  "documentName": "test",
                  "documentVersion": "2026",
                  "rollbackActions": [
                    { "elementId": 0, "param": "", "oldValue": "RESTORE-ME", "newValue": "APPLIED-VALUE", "source": "{{operation}}" }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            var output = writer.ToString();
            Assert.Contains("rollback action at index 0", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ElementId", output);
            Assert.Contains("Parameter", output);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptRollbackActionMissingOldValue_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = Path.Combine(tempDir, "missing-old-value.receipt.json");
            File.WriteAllText(
                receiptPath,
                """
                {
                  "schemaVersion": "plan-receipt.v1",
                  "action": "plan.apply",
                  "operation": "set",
                  "modelPath": "test.rvt",
                  "documentName": "test",
                  "documentVersion": "2026",
                  "rollbackActions": [
                    { "elementId": 1, "param": "Mark", "newValue": "APPLIED-VALUE", "source": "set" }
                  ]
                }
                """);

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("OldValue is required", writer.ToString());
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptPlanHashMismatch_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);
            File.AppendAllText(Path.Combine(tempDir, "set.plan.json"), "tampered");

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("hash mismatch", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_ImportPlanReceiptWithoutRollbackActions_ReturnsOne_AndDoesNotCallApi()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: "import",
                actions: new List<PlanReceiptRollbackAction>(),
                preview: new List<SetPreviewItem>());

            var handler = new RecordingQueueHttpHandler();
            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("rollback actions", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptApply_MissingDocumentIdentityReturnsOne_AndDoesNotWrite()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                modelPath: null,
                documentName: null,
                documentVersion: null);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("document identity", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "/api/status" }, handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptApply_DocumentMismatchReturnsOne_AndDoesNotWrite()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
            {
                RevitVersion = "2026",
                RevitYear = 2026,
                DocumentName = "other",
                DocumentPath = "other.rvt"
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("does not match", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { "/api/status" }, handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("set", "Mark")]
    [InlineData("import", "Lock")]
    [InlineData("sheet-issue", "Sheet Issue Date")]
    [InlineData("sheet-renumber", "Sheet Number")]
    [InlineData("room-numbering", "Number")]
    [InlineData("mark-assignment", "Mark")]
    public async Task Execute_PlanReceiptApply_CurrentValueConflictReturnsOneWithoutApplying(
        string operation,
        string param)
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(
                tempDir,
                operation: operation,
                param: param,
                actions: new List<PlanReceiptRollbackAction>
                {
                    new()
                    {
                        ElementId = 303,
                        Param = param,
                        OldValue = "RESTORE-ME",
                        NewValue = "APPLIED-VALUE",
                        Source = operation
                    }
                });
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "SOMEONE-ELSE-EDITED", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            var output = writer.ToString();
            Assert.Contains("conflict", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[303]", output);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", string.Join('\n', handler.RequestBodies));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptApply_ConflictReturnsOneWithoutApplying()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "SOMEONE-ELSE-EDITED", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: false, yes: true, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            Assert.Contains("conflict", writer.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_PlanReceiptDryRun_ConflictReturnsOneWithoutApplying()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var receiptPath = WritePlanReceipt(tempDir);
            var handler = new RecordingQueueHttpHandler();
            EnqueueMatchingStatus(handler);
            handler.Enqueue("/api/elements/set", ApiResponse<SetResult>.Ok(new SetResult
            {
                Affected = 1,
                Preview = new List<SetPreviewItem>
                {
                    new() { Id = 303, Name = "Door 303", OldValue = "SOMEONE-ELSE-EDITED", NewValue = "RESTORE-ME" }
                }
            }));

            var client = MakeClient(handler);
            var writer = new StringWriter();

            var exitCode = await RollbackCommand.ExecuteAsync(
                client, receiptPath, dryRun: true, yes: false, maxChanges: 50, writer);

            Assert.Equal(1, exitCode);
            var output = writer.ToString();
            Assert.Contains("conflict", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Dry run", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Safe apply command withheld", output);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.Contains("\"dryRun\":true", handler.RequestBodies[1]);
            Assert.DoesNotContain("\"dryRun\":false", handler.RequestBodies[1]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static RevitClient MakeClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:17839") });

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"revitcli-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteBaseline(string baselinePath, string documentPath = "test.rvt")
    {
        var snapshot = new ModelSnapshot
        {
            SchemaVersion = 1,
            TakenAt = "2026-04-26T00:00:00Z",
            Revit = new SnapshotRevit
            {
                Version = "2026",
                Document = "test",
                DocumentPath = documentPath
            },
            Categories = new Dictionary<string, List<SnapshotElement>>(),
            Summary = new SnapshotSummary()
        };

        File.WriteAllText(baselinePath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private static void WriteJournal(string baselinePath, IEnumerable<FixAction> actions)
    {
        var journal = new FixJournal
        {
            BaselinePath = baselinePath,
            Actions = new List<FixAction>(actions)
        };

        FixJournalStore.SaveForBaseline(baselinePath, journal);
    }

    private static void WriteJournalJson(string baselinePath, string json)
    {
        var journalPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(baselinePath))!,
            Path.GetFileNameWithoutExtension(baselinePath) + ".fixjournal.json");

        File.WriteAllText(journalPath, json);
    }

    private static string WritePlanReceipt(
        string tempDir,
        string operation = "set",
        string param = "Mark",
        List<PlanReceiptRollbackAction>? actions = null,
        List<SetPreviewItem>? preview = null,
        string? modelPath = "test.rvt",
        string? documentName = "test",
        string? documentVersion = "2026")
    {
        var receiptPath = Path.Combine(tempDir, $"{operation}.receipt.json");
        var planPath = Path.Combine(tempDir, $"{operation}.plan.json");
        File.WriteAllText(planPath, $$"""{ "schemaVersion": 1, "type": "{{operation}}" }""");
        var receipt = new PlanReceipt
        {
            Operation = operation,
            PlanPath = planPath,
            PlanHash = ComputeSha256Hex(planPath),
            ModelPath = modelPath,
            DocumentName = documentName,
            DocumentVersion = documentVersion,
            Affected = actions?.Count ?? preview?.Count ?? 1,
            Param = param,
            RollbackActions = actions ?? new List<PlanReceiptRollbackAction>
            {
                new()
                {
                    ElementId = 303,
                    Param = param,
                    OldValue = "RESTORE-ME",
                    NewValue = "APPLIED-VALUE",
                    Source = operation
                }
            },
            Preview = preview ?? new List<SetPreviewItem>()
        };

        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        return receiptPath;
    }

    private static string WriteLinkRepairReceipt(string tempDir, bool oldPathExists = true)
    {
        var receiptPath = Path.Combine(tempDir, "link-repair.receipt.json");
        var planPath = Path.Combine(tempDir, "link-repair.plan.json");
        File.WriteAllText(planPath, """{ "schemaVersion": "link-repair-plan.v1", "type": "link-repair" }""");
        var receipt = new PlanReceipt
        {
            Operation = "link-repair",
            PlanPath = planPath,
            PlanHash = ComputeSha256Hex(planPath),
            ModelPath = "test.rvt",
            DocumentName = "test",
            DocumentVersion = "2026",
            Affected = 1,
            LinkRepairActions =
            {
                new PlanReceiptLinkRepairAction
                {
                    LinkId = 4101,
                    LinkTypeId = 4201,
                    LinkName = "Structural Model",
                    TypeName = "Structural Model.rvt",
                    OldPath = @"D:\coordination\old-struct.rvt",
                    NewPath = @"D:\coordination\new-struct.rvt",
                    OldLoaded = false,
                    NewLoaded = true,
                    OldPathExists = oldPathExists,
                    NewPathExists = true
                }
            }
        };

        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        return receiptPath;
    }

    private static string WriteModelMapReceipt(
        string tempDir,
        string field = "workset",
        string? oldValue = "Interior",
        string newValue = "Architecture")
    {
        var receiptPath = Path.Combine(tempDir, "model-map-fix.receipt.json");
        var planPath = Path.Combine(tempDir, "model-map-fix.plan.json");
        File.WriteAllText(planPath, """{ "schemaVersion": "model-map-fix-plan.v1", "type": "model-map-fix" }""");
        var receipt = new PlanReceipt
        {
            Operation = "model-map-fix",
            PlanPath = planPath,
            PlanHash = ComputeSha256Hex(planPath),
            ModelPath = "test.rvt",
            DocumentName = "test",
            DocumentVersion = "2026",
            Affected = 1,
            ModelMapActions =
            {
                new PlanReceiptModelMapAction
                {
                    ElementId = 5101,
                    ElementName = "Room 101",
                    Category = "Rooms",
                    Field = field,
                    OldValue = oldValue,
                    NewValue = newValue
                }
            }
        };

        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions));
        return receiptPath;
    }

    private static void EnqueueMatchingStatus(RecordingQueueHttpHandler handler)
    {
        handler.Enqueue("/api/status", ApiResponse<StatusInfo>.Ok(new StatusInfo
        {
            RevitVersion = "2026",
            RevitYear = 2026,
            DocumentName = "test",
            DocumentPath = "test.rvt"
        }));
    }

    private static string ComputeSha256Hex(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

internal sealed class RecordingQueueHttpHandler : HttpMessageHandler
{
    private readonly Queue<(string Path, string Json)> _responses = new();

    public List<string> Requests { get; } = new();

    public List<string> RequestBodies { get; } = new();

    public void Enqueue<T>(string path, ApiResponse<T> response)
    {
        _responses.Enqueue((path, JsonSerializer.Serialize(response)));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsolutePath);
        RequestBodies.Add(request.Content == null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var next = _responses.Dequeue();
        Assert.Equal(next.Path, request.RequestUri.AbsolutePath);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(next.Json, Encoding.UTF8, "application/json")
        };
    }
}
