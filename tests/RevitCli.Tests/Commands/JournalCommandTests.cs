using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Commands;
using Xunit;

namespace RevitCli.Tests.Commands;

public sealed class JournalCommandTests
{
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
