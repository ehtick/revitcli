using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RevitCli.Client;
using RevitCli.Fix;
using RevitCli.Plans;
using RevitCli.Shared;

namespace RevitCli.Commands;

public static class RollbackCommand
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static Command Create(RevitClient client)
    {
        var artifactArg = new Argument<string>("artifact", "Fix baseline snapshot or plan receipt path");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview rollback without applying");
        var yesOpt = new Option<bool>("--yes", "Confirm rollback apply in non-interactive mode");
        var maxChangesOpt = new Option<int>("--max-changes", () => 50, "Maximum number of rollback writes");

        var command = new Command("rollback", "Restore parameters from a fix baseline or plan receipt")
        {
            artifactArg,
            dryRunOpt,
            yesOpt,
            maxChangesOpt
        };

        command.SetHandler(async (artifactPath, dryRun, yes, maxChanges) =>
        {
            Environment.ExitCode = await ExecuteAsync(
                client, artifactPath, dryRun, yes, maxChanges, Console.Out);
        }, artifactArg, dryRunOpt, yesOpt, maxChangesOpt);

        return command;
    }

    public static async Task<int> ExecuteAsync(
        RevitClient client,
        string artifactPath,
        bool dryRun,
        bool yes,
        int maxChanges,
        TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            await output.WriteLineAsync("Error: rollback artifact path is required (fix baseline or plan receipt).");
            return 1;
        }

        if (maxChanges <= 0)
        {
            await output.WriteLineAsync("Error: --max-changes must be greater than 0.");
            return 1;
        }

        if (!File.Exists(artifactPath))
        {
            await output.WriteLineAsync($"Error: rollback artifact file not found: {artifactPath}");
            return 1;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(artifactPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to read rollback artifact: {ex.Message}");
            return 1;
        }

        var schemaVersion = TryReadStringSchemaVersion(json);
        if (schemaVersion != null &&
            schemaVersion.StartsWith("plan-receipt.", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecutePlanReceiptRollbackAsync(
                client,
                artifactPath,
                json,
                dryRun,
                yes,
                maxChanges,
                output);
        }

        ModelSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ModelSnapshot>(json, ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: failed to parse baseline snapshot: {ex.Message}");
            return 1;
        }

        if (snapshot == null)
        {
            await output.WriteLineAsync($"Error: invalid baseline snapshot: {artifactPath}");
            return 1;
        }

        FixJournal journal;
        try
        {
            journal = FixJournalStore.LoadForBaseline(artifactPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }

        if (!JournalMatchesBaseline(journal, artifactPath, out var journalError))
        {
            await output.WriteLineAsync($"Error: {journalError}");
            return 1;
        }

        var actions = journal.Actions?.ToList() ?? new List<FixAction>();

        if (!await TryValidateActionsAsync(actions, output))
        {
            return 1;
        }

        var rollbackWrites = actions
            .Select(action => new RollbackWrite(
                action.ElementId,
                action.Parameter,
                action.OldValue ?? string.Empty,
                action.NewValue ?? string.Empty,
                "fix"))
            .ToList();

        return await ExecuteRollbackWritesAsync(
            client,
            rollbackWrites,
            dryRun,
            yes,
            maxChanges,
            "rollback journal",
            output,
            () => TryValidateCurrentDocumentAsync(client, snapshot, output));
    }

    private static async Task<int> ExecutePlanReceiptRollbackAsync(
        RevitClient client,
        string receiptPath,
        string json,
        bool dryRun,
        bool yes,
        int maxChanges,
        TextWriter output)
    {
        PlanReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<PlanReceipt>(json, ReadOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            await output.WriteLineAsync($"Error: failed to parse plan receipt: {ex.Message}");
            return 1;
        }

        if (receipt == null || !string.Equals(receipt.SchemaVersion, "plan-receipt.v1", StringComparison.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync($"Error: unsupported plan receipt: {receiptPath}");
            return 1;
        }

        if (string.Equals(receipt.Operation, "fix", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(receipt.BaselinePath))
            {
                await output.WriteLineAsync("Error: fix plan receipt does not include a rollback baseline path.");
                return 1;
            }

            await output.WriteLineAsync($"Using fix baseline from receipt: {receipt.BaselinePath}");
            return await ExecuteAsync(client, receipt.BaselinePath, dryRun, yes, maxChanges, output);
        }

        var rollbackWrites = BuildReceiptRollbackWrites(receipt).ToList();
        if (rollbackWrites.Count == 0)
        {
            await output.WriteLineAsync("Error: plan receipt does not include rollback actions.");
            return 1;
        }

        return await ExecuteRollbackWritesAsync(
            client,
            rollbackWrites,
            dryRun,
            yes,
            maxChanges,
            "plan receipt",
            output,
            () => TryValidateCurrentDocumentAsync(
                client,
                receipt.ModelPath,
                receipt.DocumentName,
                requireIdentity: false,
                output: output));
    }

    private static string? TryReadStringSchemaVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion) &&
                schemaVersion.ValueKind == JsonValueKind.String)
            {
                return schemaVersion.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static IEnumerable<RollbackWrite> BuildReceiptRollbackWrites(PlanReceipt receipt)
    {
        if (receipt.RollbackActions is { Count: > 0 })
        {
            return receipt.RollbackActions.Select(action => action == null
                ? new RollbackWrite(0, string.Empty, string.Empty, string.Empty, receipt.Operation)
                : new RollbackWrite(
                    action.ElementId,
                    action.Param,
                    action.OldValue ?? string.Empty,
                    action.NewValue ?? string.Empty,
                    string.IsNullOrWhiteSpace(action.Source) ? receipt.Operation : action.Source));
        }

        if (!string.Equals(receipt.Operation, "set", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(receipt.Param))
        {
            return Array.Empty<RollbackWrite>();
        }

        return (receipt.Preview ?? new List<SetPreviewItem>()).Select(item => item == null
            ? new RollbackWrite(0, receipt.Param, string.Empty, string.Empty, "set")
            : new RollbackWrite(
                item.Id,
                receipt.Param,
                item.OldValue ?? string.Empty,
                item.NewValue ?? string.Empty,
                "set"));
    }

    private static async Task<int> ExecuteRollbackWritesAsync(
        RevitClient client,
        IReadOnlyList<RollbackWrite> actions,
        bool dryRun,
        bool yes,
        int maxChanges,
        string artifactName,
        TextWriter output,
        Func<Task<bool>> validateCurrentDocument)
    {
        if (!await TryValidateRollbackWritesAsync(actions, artifactName, output))
        {
            return 1;
        }

        if (actions.Count > maxChanges)
        {
            await output.WriteLineAsync($"Error: {artifactName} has {actions.Count} action(s), exceeds --max-changes {maxChanges}.");
            return 1;
        }

        if (!dryRun && !yes)
        {
            await output.WriteLineAsync("Error: use --yes to apply rollback changes.");
            return 1;
        }

        if (!await validateCurrentDocument())
        {
            return 1;
        }

        foreach (var action in actions)
        {
            await output.WriteLineAsync(
                $"[{action.ElementId}] {action.Parameter}: \"{action.NewValue}\" -> \"{action.OldValue}\"");
        }

        var restoredCount = 0;
        var conflictCount = 0;
        var errorCount = 0;

        foreach (var action in actions)
        {
            ApiResponse<SetResult>? previewResult;
            try
            {
                previewResult = await client.SetParameterAsync(new SetRequest
                {
                    ElementId = action.ElementId,
                    Param = action.Parameter,
                    Value = action.OldValue,
                    DryRun = true
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                await output.WriteLineAsync(
                    $"Error: failed to preview rollback for element {action.ElementId}: {ex.Message}");
                continue;
            }

            if (previewResult == null || !previewResult.Success || previewResult.Data == null)
            {
                errorCount++;
                await output.WriteLineAsync(
                    $"Error: failed to preview rollback for element {action.ElementId}: {previewResult?.Error}");
                continue;
            }

            var previewItem = previewResult.Data.Preview?.FirstOrDefault(item => item != null && item.Id == action.ElementId);
            if (previewItem == null)
            {
                errorCount++;
                await output.WriteLineAsync(
                    $"Error: preview response did not include a matching item for element {action.ElementId}.");
                continue;
            }

            var currentValue = previewItem.OldValue ?? string.Empty;
            if (!string.Equals(currentValue, action.NewValue, StringComparison.Ordinal))
            {
                conflictCount++;
                await output.WriteLineAsync(
                    $"Conflict: element {action.ElementId} parameter {action.Parameter} changed from \"{action.NewValue}\" to \"{currentValue}\"; skipping.");
                continue;
            }

            if (dryRun)
            {
                continue;
            }

            ApiResponse<SetResult>? applyResult;
            try
            {
                applyResult = await client.SetParameterAsync(new SetRequest
                {
                    ElementId = action.ElementId,
                    Param = action.Parameter,
                    Value = action.OldValue,
                    DryRun = false
                });
            }
            catch (Exception ex)
            {
                errorCount++;
                await output.WriteLineAsync(
                    $"Error: failed to apply rollback for element {action.ElementId}: {ex.Message}");
                continue;
            }

            if (applyResult == null || !applyResult.Success || applyResult.Data == null)
            {
                errorCount++;
                await output.WriteLineAsync(
                    $"Error: failed to apply rollback for element {action.ElementId}: {applyResult?.Error}");
                continue;
            }

            restoredCount += applyResult.Data.Affected;
        }

        if (dryRun)
        {
            await output.WriteLineAsync(
                $"Dry run: {actions.Count} rollback action(s); {conflictCount} conflict(s); {errorCount} error(s).");
            return conflictCount == 0 && errorCount == 0 ? 0 : 1;
        }

        await output.WriteLineAsync(
            $"Restored {restoredCount} element parameter(s); {conflictCount} conflict(s); {errorCount} error(s).");
        return conflictCount == 0 && errorCount == 0 ? 0 : 1;
    }

    private static async Task<bool> TryValidateRollbackWritesAsync(
        IReadOnlyList<RollbackWrite> actions,
        string artifactName,
        TextWriter output)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.ElementId <= 0 || string.IsNullOrWhiteSpace(action.Parameter))
            {
                var issues = new List<string>();
                if (action.ElementId <= 0)
                {
                    issues.Add("ElementId must be > 0");
                }

                if (string.IsNullOrWhiteSpace(action.Parameter))
                {
                    issues.Add("Parameter is required");
                }

                await output.WriteLineAsync(
                    $"Error: invalid {artifactName} action at index {i}: {string.Join(", ", issues)}.");
                return false;
            }
        }

        return true;
    }

    private static bool JournalMatchesBaseline(FixJournal journal, string baselinePath, out string error)
    {
        error = "";
        if (journal == null)
        {
            error = "invalid fix journal.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(journal.BaselinePath))
        {
            error = "fix journal does not identify its baseline path.";
            return false;
        }

        try
        {
            var expected = Path.GetFullPath(baselinePath);
            if (!GetBaselinePathCandidates(journal.BaselinePath, expected)
                .Any(actual => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"fix journal baseline path '{journal.BaselinePath}' does not match '{baselinePath}'.";
                return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"invalid fix journal baseline path: {ex.Message}";
            return false;
        }

        return true;
    }

    private static IEnumerable<string> GetBaselinePathCandidates(string recordedBaselinePath, string expectedBaselinePath)
    {
        var candidates = new List<string>();
        if (Path.IsPathRooted(recordedBaselinePath))
        {
            candidates.Add(Path.GetFullPath(recordedBaselinePath));
            return candidates;
        }

        var expectedDirectory = Path.GetDirectoryName(expectedBaselinePath);
        if (!string.IsNullOrWhiteSpace(expectedDirectory))
        {
            var expectedDirectoryName = Path.GetFileName(expectedDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (string.Equals(expectedDirectoryName, ".revitcli", StringComparison.OrdinalIgnoreCase))
            {
                var expectedParent = Directory.GetParent(expectedDirectory);
                if (expectedParent != null)
                {
                    candidates.Add(Path.GetFullPath(Path.Combine(expectedParent.FullName, recordedBaselinePath)));
                }
            }
        }

        return candidates;
    }

    private static async Task<bool> TryValidateCurrentDocumentAsync(
        RevitClient client,
        ModelSnapshot snapshot,
        TextWriter output)
    {
        return await TryValidateCurrentDocumentAsync(
            client,
            snapshot.Revit?.DocumentPath,
            snapshot.Revit?.Document,
            requireIdentity: true,
            output);
    }

    private static async Task<bool> TryValidateCurrentDocumentAsync(
        RevitClient client,
        string? expectedDocumentPath,
        string? expectedDocumentName,
        bool requireIdentity,
        TextWriter output)
    {
        ApiResponse<StatusInfo>? status;
        try
        {
            status = await client.GetStatusAsync();
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"Error: failed to validate current document: {ex.Message}");
            return false;
        }

        if (status == null || !status.Success || status.Data == null)
        {
            await output.WriteLineAsync($"Error: failed to validate current document: {status?.Error}");
            return false;
        }

        var baselineDocumentPath = expectedDocumentPath;
        var currentDocumentPath = status.Data.DocumentPath;
        if (!string.IsNullOrWhiteSpace(baselineDocumentPath))
        {
            if (string.IsNullOrWhiteSpace(currentDocumentPath))
            {
                await output.WriteLineAsync(
                    $"Error: current document path is empty; expected baseline document '{baselineDocumentPath}'.");
                return false;
            }

            if (!DocumentPathsEqual(baselineDocumentPath, currentDocumentPath))
            {
                await output.WriteLineAsync(
                    $"Error: current document '{currentDocumentPath}' does not match baseline document '{baselineDocumentPath}'.");
                return false;
            }

            return true;
        }

        var baselineDocument = expectedDocumentName;
        var currentDocument = status.Data.DocumentName;
        if (!string.IsNullOrWhiteSpace(baselineDocument))
        {
            if (!string.Equals(baselineDocument.Trim(), currentDocument?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync(
                    $"Error: current document '{currentDocument}' does not match baseline document '{baselineDocument}'.");
                return false;
            }

            return true;
        }

        if (requireIdentity)
        {
            await output.WriteLineAsync("Error: baseline snapshot does not include a document identity.");
            return false;
        }

        return true;
    }

    private static bool DocumentPathsEqual(string expected, string actual)
    {
        return string.Equals(
            NormalizeDocumentPath(expected),
            NormalizeDocumentPath(actual),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDocumentPath(string path)
    {
        var trimmed = path.Trim();
        try
        {
            if (Path.IsPathRooted(trimmed))
            {
                trimmed = Path.GetFullPath(trimmed);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Fall back to the raw value; invalid paths will still compare unequal.
        }

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static async Task<bool> TryValidateActionsAsync(IReadOnlyList<FixAction> actions, TextWriter output)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action == null)
            {
                await output.WriteLineAsync($"Error: invalid rollback journal action at index {i}: entry is null.");
                return false;
            }

            if (action.ElementId <= 0 || string.IsNullOrWhiteSpace(action.Parameter) || action.NewValue == null)
            {
                var issues = new List<string>();
                if (action.ElementId <= 0)
                {
                    issues.Add("ElementId must be > 0");
                }

                if (string.IsNullOrWhiteSpace(action.Parameter))
                {
                    issues.Add("Parameter is required");
                }

                if (action.NewValue == null)
                {
                    issues.Add("NewValue is required");
                }

                await output.WriteLineAsync(
                    $"Error: invalid rollback journal action at index {i}: {string.Join(", ", issues)}.");
                return false;
            }
        }

        return true;
    }

    private sealed record RollbackWrite(
        long ElementId,
        string Parameter,
        string OldValue,
        string NewValue,
        string Source);
}
