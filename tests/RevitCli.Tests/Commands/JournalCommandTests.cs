using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

public sealed class JournalCommandTests
{
    [Fact]
    public async Task Show_PrintsRecentEntriesAndSummaries()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"Mark","value":"A"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"issue"}""",
            """{"action":"set","timestamp":"2026-04-29T12:00:00Z","param":"Comments","value":"B"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            2,
            null,
            "table",
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Showing 2 of 3", output);
        Assert.DoesNotContain("Mark", output);
        Assert.Contains("publish", output);
        Assert.Contains("pipeline=issue", output);
        Assert.Contains("param=Comments", output);
    }

    [Fact]
    public async Task Show_ActionFilterAndJsonOutput_ReturnsMatchingEntries()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"Mark"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"issue"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            20,
            "publish",
            "json",
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(2, doc.RootElement.GetProperty("entryCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("shownCount").GetInt32());
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("publish", entry.GetProperty("action").GetString());
        Assert.Equal("pipeline=issue", entry.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Show_CategoryOperatorAndUserFilters_ReturnMatchingEntries()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","param":"Mark","user":"alice","operator":"lead"}""",
            """{"action":"set","timestamp":"2026-04-29T11:00:00Z","category":"rooms","param":"Name","user":"alice","operator":"lead"}""",
            """{"action":"set","timestamp":"2026-04-29T12:00:00Z","category":"doors","param":"Comments","user":"bob","operator":"assistant"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            20,
            action: "set",
            outputFormat: "json",
            writer,
            category: "doors",
            operatorFilter: "lead",
            user: "alice");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(3, doc.RootElement.GetProperty("entryCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("shownCount").GetInt32());
        var entry = doc.RootElement.GetProperty("entries")[0];
        Assert.Equal("doors", entry.GetProperty("category").GetString());
        Assert.Equal("alice", entry.GetProperty("user").GetString());
        Assert.Equal("lead", entry.GetProperty("operator").GetString());
        Assert.Equal("category=doors, param=Mark, user=alice, operator=lead", entry.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Stats_PrintsCountsAndTimestampRange()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","param":"Mark","affected":3,"affectedElementIds":[100,101,102],"user":"alice","operator":"alice"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"issue","user":"bob"}""",
            """{"action":"set","timestamp":"2026-04-29T12:00:00Z","category":"doors","param":"Comments","affected":2,"affectedElementIds":[102,103],"user":"alice","operator":"alice"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteStatsAsync(
            sandbox.Root,
            null,
            "table",
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Entries: 3", output);
        Assert.Contains("2026-04-29T10:00:00", output);
        Assert.Contains("2026-04-29T12:00:00", output);
        Assert.Contains("Affected elements: 5", output);
        Assert.Contains("Distinct affected element IDs: 4", output);
        Assert.Contains("Affected IDs: 100, 101, 102, 103", output);
        Assert.Contains("set          2 affected=5", output);
        Assert.Contains("publish      1 affected=0", output);
        Assert.Contains("Categories:", output);
        Assert.Contains("doors        2 affected=5", output);
        Assert.Contains("Users:", output);
        Assert.Contains("alice        2 affected=5", output);
        Assert.Contains("bob          1 affected=0", output);
        Assert.Contains("Operators:", output);
        Assert.Contains("alice        2 affected=5", output);
    }

    [Fact]
    public async Task Stats_JsonOutput_IncludesActionCounts()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","affected":3,"affectedElementIds":[10,11,12],"user":"alice","operator":"lead"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","user":"bob"}""",
            """{"action":"set","timestamp":"2026-04-29T12:00:00Z","category":"rooms","affected":2,"affectedElementIds":[12,13],"user":"alice","operator":"lead"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteStatsAsync(
            sandbox.Root,
            null,
            "json",
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(3, doc.RootElement.GetProperty("entryCount").GetInt32());
        var actions = doc.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Equal("set", actions[0].GetProperty("action").GetString());
        Assert.Equal(2, actions[0].GetProperty("count").GetInt32());
        Assert.Equal(5, actions[0].GetProperty("affectedElementCount").GetInt32());
        Assert.Equal("publish", actions[1].GetProperty("action").GetString());
        Assert.Equal(1, actions[1].GetProperty("count").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("affectedElementCount").GetInt32());
        Assert.Equal(4, doc.RootElement.GetProperty("distinctAffectedElementCount").GetInt32());
        Assert.Equal(
            new[] { 10L, 11L, 12L, 13L },
            doc.RootElement.GetProperty("affectedElementIds").EnumerateArray().Select(item => item.GetInt64()).ToArray());

        var categories = doc.RootElement.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal("doors", categories[0].GetProperty("name").GetString());
        Assert.Equal(1, categories[0].GetProperty("count").GetInt32());
        Assert.Equal(3, categories[0].GetProperty("affectedElementCount").GetInt32());
        Assert.Equal("rooms", categories[1].GetProperty("name").GetString());
        Assert.Equal(1, categories[1].GetProperty("count").GetInt32());
        Assert.Equal(2, categories[1].GetProperty("affectedElementCount").GetInt32());

        var users = doc.RootElement.GetProperty("users").EnumerateArray().ToArray();
        Assert.Equal("alice", users[0].GetProperty("name").GetString());
        Assert.Equal(2, users[0].GetProperty("count").GetInt32());
        Assert.Equal(5, users[0].GetProperty("affectedElementCount").GetInt32());
        Assert.Equal("bob", users[1].GetProperty("name").GetString());
        Assert.Equal(1, users[1].GetProperty("count").GetInt32());

        var operators = doc.RootElement.GetProperty("operators").EnumerateArray().ToArray();
        Assert.Equal("lead", operators[0].GetProperty("name").GetString());
        Assert.Equal(2, operators[0].GetProperty("count").GetInt32());
        Assert.Equal(5, operators[0].GetProperty("affectedElementCount").GetInt32());
        Assert.Equal("bob", operators[1].GetProperty("name").GetString());
        Assert.Equal(1, operators[1].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Review_TableHighlightsHighImpactAndMutatingEntries()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"query","timestamp":"2026-04-29T09:00:00Z","category":"doors"}""",
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","affected":3,"affectedElementIds":[100,101,102],"operator":"alice"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"issue","operator":"bob"}""",
            """{"action":"import","timestamp":"2026-04-29T12:00:00Z","category":"rooms","affected":80,"affectedElementIds":[200,201],"operator":"alice"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteReviewAsync(
            sandbox.Root,
            null,
            limit: 20,
            highImpactThreshold: 50,
            action: null,
            category: null,
            operatorFilter: null,
            user: null,
            outputFormat: "table",
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Requires attention: true", output);
        Assert.Contains("Risk:", output);
        Assert.Contains("high-impact", output);
        Assert.Contains("write", output);
        Assert.Contains("delivery", output);
        Assert.Contains("affected=80", output);
        Assert.Contains("affected=3", output);
        Assert.Contains("Review:", output);
        Assert.Contains("line    4", output);
        Assert.Contains("import", output);
        Assert.Contains("line    2", output);
        Assert.Contains("set", output);
    }

    [Fact]
    public async Task Review_JsonOutputIncludesSchemaCountsAndEntries()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","affected":3,"affectedElementIds":[100,101,102],"operator":"alice","user":"alice"}""",
            """{"action":"import","timestamp":"2026-04-29T12:00:00Z","category":"rooms","affected":80,"affectedElementIds":[200,201],"operator":"alice","user":"bob"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteReviewAsync(
            sandbox.Root,
            null,
            limit: 20,
            highImpactThreshold: 50,
            action: null,
            category: null,
            operatorFilter: "alice",
            user: null,
            outputFormat: "json",
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("journal-review.v1", root.GetProperty("schemaVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("requiresAttention").GetBoolean());
        Assert.Equal(2, root.GetProperty("reviewedCount").GetInt32());
        Assert.Equal(83, root.GetProperty("affectedElementCount").GetInt32());
        Assert.Equal(5, root.GetProperty("distinctAffectedElementCount").GetInt32());

        var risks = root.GetProperty("risks").EnumerateArray().ToArray();
        Assert.Equal("high-impact", risks[0].GetProperty("name").GetString());
        Assert.Equal(1, risks[0].GetProperty("count").GetInt32());
        Assert.Equal("write", risks[1].GetProperty("name").GetString());
        Assert.Equal(1, risks[1].GetProperty("count").GetInt32());

        var highlighted = root.GetProperty("highlightedEntries").EnumerateArray().ToArray();
        Assert.Equal(2, highlighted.Length);
        Assert.Equal("high-impact", highlighted[0].GetProperty("risk").GetString());
        Assert.Equal("import", highlighted[0].GetProperty("action").GetString());
        Assert.Equal("write", highlighted[1].GetProperty("risk").GetString());
        Assert.Equal("set", highlighted[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task Review_MarkdownOutputPrintsTables()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","category":"doors","affected":3,"operator":"alice"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteReviewAsync(
            sandbox.Root,
            null,
            limit: 20,
            highImpactThreshold: 50,
            action: null,
            category: null,
            operatorFilter: null,
            user: null,
            outputFormat: "markdown",
            writer);

        var output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("# Journal Review", output);
        Assert.Contains("## Risk", output);
        Assert.Contains("| write | 1 | 3 |", output);
        Assert.Contains("## Review", output);
    }

    [Fact]
    public async Task Review_InvalidThresholdReturnsFailure()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal("""{"action":"set"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteReviewAsync(
            sandbox.Root,
            null,
            limit: 20,
            highImpactThreshold: 0,
            action: null,
            category: null,
            operatorFilter: null,
            user: null,
            outputFormat: "json",
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("--high-impact-threshold", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Show_InvalidJson_ReturnsFailureWithLineNumber()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z"}""",
            """{"action":""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            20,
            null,
            "table",
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("line 2", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_InvalidJson_WithJsonOutput_ReturnsErrorObject()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z"}""",
            """{"action":""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            20,
            null,
            "json",
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("line 2", doc.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Show_InvalidLimit_ReturnsFailure()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal("""{"action":"set"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            0,
            null,
            "table",
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--limit", writer.ToString());
    }

    [Fact]
    public async Task Show_InvalidLimit_WithJsonOutput_ReturnsErrorObject()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal("""{"action":"set"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteShowAsync(
            sandbox.Root,
            null,
            0,
            null,
            "json",
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("--limit", doc.RootElement.GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task SignAndVerify_RoundTripsJournalSignature()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"Mark"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"default"}""");

        var signWriter = new StringWriter();
        var signExit = await JournalCommand.ExecuteSignAsync(
            sandbox.Root,
            null,
            null,
            null,
            null,
            "table",
            signWriter);

        Assert.Equal(0, signExit);
        Assert.True(File.Exists(sandbox.SignaturePath));
        Assert.True(File.Exists(sandbox.KeyPath));
        Assert.Contains("Entries signed: 2", signWriter.ToString());

        var verifyWriter = new StringWriter();
        var verifyExit = await JournalCommand.ExecuteVerifyAsync(
            sandbox.Root,
            null,
            null,
            null,
            "table",
            verifyWriter);

        Assert.Equal(0, verifyExit);
        Assert.Contains("Journal signature valid", verifyWriter.ToString());
        Assert.Contains("Entries verified: 2", verifyWriter.ToString());
    }

    [Fact]
    public async Task Verify_ReturnsOne_WhenJournalLineIsModified()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"Mark"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"default"}""");

        Assert.Equal(0, await JournalCommand.ExecuteSignAsync(
            sandbox.Root,
            null,
            null,
            null,
            null,
            "table",
            new StringWriter()));

        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"Comments"}""",
            """{"action":"publish","timestamp":"2026-04-29T11:00:00Z","pipeline":"default"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteVerifyAsync(
            sandbox.Root,
            null,
            null,
            null,
            "table",
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("hash mismatch", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_WithUntil_AllowsLaterAppendsButRejectsEarlierInsertion()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"A"}""",
            """{"action":"set","timestamp":"2026-04-29T11:00:00Z","param":"B"}""");

        Assert.Equal(0, await JournalCommand.ExecuteSignAsync(
            sandbox.Root,
            null,
            null,
            null,
            "2026-04-29T10:30:00Z",
            "table",
            new StringWriter()));

        sandbox.AppendJournal("""{"action":"publish","timestamp":"2026-04-29T12:00:00Z","pipeline":"default"}""");
        Assert.Equal(0, await JournalCommand.ExecuteVerifyAsync(
            sandbox.Root,
            null,
            null,
            null,
            "table",
            new StringWriter()));

        sandbox.WriteJournal(
            """{"action":"set","timestamp":"2026-04-29T09:59:00Z","param":"inserted"}""",
            """{"action":"set","timestamp":"2026-04-29T10:00:00Z","param":"A"}""",
            """{"action":"set","timestamp":"2026-04-29T11:00:00Z","param":"B"}""",
            """{"action":"publish","timestamp":"2026-04-29T12:00:00Z","pipeline":"default"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteVerifyAsync(
            sandbox.Root,
            null,
            null,
            null,
            "table",
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("mismatch", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_JsonOutput_IncludesSignaturePath()
    {
        using var sandbox = new TempJournalSandbox();
        sandbox.WriteJournal("""{"action":"set","timestamp":"2026-04-29T10:00:00Z"}""");

        var writer = new StringWriter();
        var exitCode = await JournalCommand.ExecuteSignAsync(
            sandbox.Root,
            null,
            null,
            null,
            null,
            "json",
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(sandbox.SignaturePath, doc.RootElement.GetProperty("signaturePath").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("entryCount").GetInt32());
    }

    private sealed class TempJournalSandbox : IDisposable
    {
        public TempJournalSandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), $"revitcli_journal_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, ".revitcli"));
        }

        public string Root { get; }

        public string JournalPath => Path.Combine(Root, ".revitcli", "journal.jsonl");

        public string SignaturePath => JournalPath + ".sig";

        public string KeyPath => Path.Combine(Root, ".revitcli", "journal.key");

        public void WriteJournal(params string[] lines)
        {
            File.WriteAllLines(JournalPath, lines);
        }

        public void AppendJournal(string line)
        {
            File.AppendAllText(JournalPath, line + Environment.NewLine);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
