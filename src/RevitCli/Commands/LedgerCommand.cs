using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitCli.Client;
using RevitCli.History;
using RevitCli.Journal;
using RevitCli.Output;
using RevitCli.Shared;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class LedgerCommand
{
    private const string SourceErrorMessage = "Error: --source must be 'all', 'ledger', 'journal', 'history', 'deliveries', or 'workflows'.";

    public static Command Create(RevitClient? client = null)
    {
        var command = new Command("ledger", "Append, query, validate, summarize, and timeline local RevitCli operation ledger artifacts");
        command.AddCommand(CreateAppendCommand());
        command.AddCommand(CreateReplayCommand(client));
        command.AddCommand(CreateQueryCommand());
        command.AddCommand(CreateValidateCommand());
        command.AddCommand(CreateStatsCommand());
        command.AddCommand(CreateTimelineCommand());
        return command;
    }

    private static Command CreateAppendCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var actionOpt = new Option<string?>("--action", "Operation action, e.g. issue.package or sheets.issue-meta");
        var categoryOpt = new Option<string?>("--category", "Operation category or scope");
        var operatorOpt = new Option<string?>("--operator", "Operator name; defaults to current user");
        var statusOpt = new Option<string>("--status", () => "succeeded", "Operation status: planned|succeeded|failed|blocked");
        var summaryOpt = new Option<string?>("--summary", "Short operation summary");
        var timestampOpt = new Option<string?>("--timestamp", "UTC timestamp; defaults to now");
        var modelOpt = new Option<string?>("--model", "Model identity or document name");
        var modelPathOpt = new Option<string?>("--model-path", "Model path when available");
        var revitVersionOpt = new Option<string?>("--revit-version", "Revit version when available");
        var planHashOpt = new Option<string?>("--plan-hash", "Plan hash when available");
        var artifactPathOpt = new Option<string?>("--artifact-path", "Primary artifact path for this operation");
        var receiptPathOpt = new Option<string?>("--receipt", "Receipt path for this operation");
        var receiptHashOpt = new Option<string?>("--receipt-hash", "SHA256 hash of the receipt when available");
        var rollbackPointerOpt = new Option<string?>("--rollback-pointer", "Rollback pointer or command");
        var evidenceOpt = new Option<string[]>("--evidence", "Additional evidence path; repeat for multiple paths")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var yesOpt = new Option<bool>("--yes", "Write the append-only ledger record. Without --yes this is a dry-run preview.");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("append", "Append an explicit local operation ledger record")
        {
            dirOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            statusOpt,
            summaryOpt,
            timestampOpt,
            modelOpt,
            modelPathOpt,
            revitVersionOpt,
            planHashOpt,
            artifactPathOpt,
            receiptPathOpt,
            receiptHashOpt,
            rollbackPointerOpt,
            evidenceOpt,
            yesOpt,
            outputOpt,
        };

        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteAppendAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(statusOpt)!,
                ctx.ParseResult.GetValueForOption(summaryOpt),
                ctx.ParseResult.GetValueForOption(timestampOpt),
                ctx.ParseResult.GetValueForOption(modelOpt),
                ctx.ParseResult.GetValueForOption(modelPathOpt),
                ctx.ParseResult.GetValueForOption(planHashOpt),
                ctx.ParseResult.GetValueForOption(artifactPathOpt),
                ctx.ParseResult.GetValueForOption(receiptPathOpt),
                ctx.ParseResult.GetValueForOption(receiptHashOpt),
                ctx.ParseResult.GetValueForOption(rollbackPointerOpt),
                ctx.ParseResult.GetValueForOption(evidenceOpt) ?? Array.Empty<string>(),
                ctx.ParseResult.GetValueForOption(yesOpt),
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out,
                revitVersion: ctx.ParseResult.GetValueForOption(revitVersionOpt));
        });

        return cmd;
    }

    private static Command CreateReplayCommand(RevitClient? client)
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var sourceOpt = new Option<string>("--source", () => "ledger", "Source: ledger|all|journal|history|deliveries|workflows");
        var sinceOpt = new Option<string?>("--since", "Only include operations at or after this ISO timestamp");
        var untilOpt = new Option<string?>("--until", "Only include operations at or before this ISO timestamp");
        var windowOpt = new Option<string?>("--window", "Recent window ending at now, e.g. 7d, 24h, 60m");
        var actionOpt = new Option<string?>("--action", "Only include matching action");
        var categoryOpt = new Option<string?>("--category", "Only include matching category");
        var operatorOpt = new Option<string?>("--operator", "Only include matching operator");
        var receiptStatusOpt = new Option<string>("--receipt-status", () => "all", "Receipt status: all|valid|missing|unreadable");
        var limitOpt = new Option<int>("--limit", () => 100, "Maximum operations to replay-preview");
        var applyOpt = new Option<bool>("--apply", "Apply eligible source-ledger set, export, or schedule batch-export operations to Revit");
        var yesOpt = new Option<bool>("--yes", "Approve replay apply. Required with --apply.");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("replay", "Preview a deterministic replay plan from local ledger operations")
        {
            dirOpt,
            sourceOpt,
            sinceOpt,
            untilOpt,
            windowOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            receiptStatusOpt,
            limitOpt,
            applyOpt,
            yesOpt,
            outputOpt,
        };

        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteReplayAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(sourceOpt)!,
                ctx.ParseResult.GetValueForOption(sinceOpt),
                ctx.ParseResult.GetValueForOption(untilOpt),
                ctx.ParseResult.GetValueForOption(windowOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(receiptStatusOpt)!,
                ctx.ParseResult.GetValueForOption(limitOpt),
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out,
                client: client,
                apply: ctx.ParseResult.GetValueForOption(applyOpt),
                yes: ctx.ParseResult.GetValueForOption(yesOpt));
        });

        return cmd;
    }

    private static Command CreateQueryCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var sourceOpt = new Option<string>("--source", () => "all", "Source: all|ledger|journal|history|deliveries|workflows");
        var sinceOpt = new Option<string?>("--since", "Only include operations at or after this ISO timestamp");
        var untilOpt = new Option<string?>("--until", "Only include operations at or before this ISO timestamp");
        var windowOpt = new Option<string?>("--window", "Recent window ending at now, e.g. 7d, 24h, 60m");
        var actionOpt = new Option<string?>("--action", "Only include matching action");
        var categoryOpt = new Option<string?>("--category", "Only include matching category");
        var operatorOpt = new Option<string?>("--operator", "Only include matching operator");
        var receiptStatusOpt = new Option<string>("--receipt-status", () => "all", "Receipt status: all|valid|missing|unreadable");
        var limitOpt = new Option<int>("--limit", () => 100, "Maximum operations to return");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("query", "Query local ledger, journal, history, delivery, and workflow receipt artifacts")
        {
            dirOpt,
            sourceOpt,
            sinceOpt,
            untilOpt,
            windowOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            receiptStatusOpt,
            limitOpt,
            outputOpt,
        };

        // Bindings exceed the typed SetHandler overload cap; keep CLI wiring explicit.
        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteQueryAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(sourceOpt)!,
                ctx.ParseResult.GetValueForOption(sinceOpt),
                ctx.ParseResult.GetValueForOption(untilOpt),
                ctx.ParseResult.GetValueForOption(windowOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(receiptStatusOpt)!,
                ctx.ParseResult.GetValueForOption(limitOpt),
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out);
        });

        return cmd;
    }

    private static Command CreateTimelineCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var projectOpt = new Option<string[]>("--project", "Additional project directory to include; can be repeated")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var sourceOpt = new Option<string>("--source", () => "all", "Source: all|ledger|journal|history|deliveries|workflows");
        var sinceOpt = new Option<string?>("--since", "Only include operations at or after this ISO timestamp");
        var untilOpt = new Option<string?>("--until", "Only include operations at or before this ISO timestamp");
        var windowOpt = new Option<string?>("--window", "Recent window ending at now, e.g. 7d, 24h, 60m");
        var actionOpt = new Option<string?>("--action", "Only include matching action");
        var categoryOpt = new Option<string?>("--category", "Only include matching category");
        var operatorOpt = new Option<string?>("--operator", "Only include matching operator");
        var receiptStatusOpt = new Option<string>("--receipt-status", () => "all", "Receipt status: all|valid|missing|unreadable");
        var bucketOpt = new Option<string>("--bucket", () => "day", "Timeline bucket: day|hour");
        var analyticsSnapshotOpt = new Option<string?>("--analytics-snapshot", "Persist the ledger-timeline.v1 JSON snapshot to this local path");
        var fromAnalyticsSnapshotOpt = new Option<string?>("--from-analytics-snapshot", "Read a previously persisted ledger-timeline.v1 JSON snapshot");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("timeline", "Bucket local ledger operations into a read-only project-memory timeline")
        {
            dirOpt,
            projectOpt,
            sourceOpt,
            sinceOpt,
            untilOpt,
            windowOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            receiptStatusOpt,
            bucketOpt,
            analyticsSnapshotOpt,
            fromAnalyticsSnapshotOpt,
            outputOpt,
        };

        // Bindings exceed the typed SetHandler overload cap; keep CLI wiring explicit.
        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteTimelineAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(sourceOpt)!,
                ctx.ParseResult.GetValueForOption(sinceOpt),
                ctx.ParseResult.GetValueForOption(untilOpt),
                ctx.ParseResult.GetValueForOption(windowOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(receiptStatusOpt)!,
                ctx.ParseResult.GetValueForOption(bucketOpt)!,
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out,
                projectDirectories: ctx.ParseResult.GetValueForOption(projectOpt),
                analyticsSnapshotPath: ctx.ParseResult.GetValueForOption(analyticsSnapshotOpt),
                fromAnalyticsSnapshotPath: ctx.ParseResult.GetValueForOption(fromAnalyticsSnapshotOpt));
        });

        return cmd;
    }

    private static Command CreateStatsCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var projectOpt = new Option<string[]>("--project", "Additional project directory to include; can be repeated")
        {
            AllowMultipleArgumentsPerToken = false,
        };
        var sourceOpt = new Option<string>("--source", () => "all", "Source: all|ledger|journal|history|deliveries|workflows");
        var sinceOpt = new Option<string?>("--since", "Only summarize operations at or after this ISO timestamp");
        var untilOpt = new Option<string?>("--until", "Only summarize operations at or before this ISO timestamp");
        var windowOpt = new Option<string?>("--window", "Recent window ending at now, e.g. 7d, 24h, 60m");
        var actionOpt = new Option<string?>("--action", "Only summarize matching action");
        var categoryOpt = new Option<string?>("--category", "Only summarize matching category");
        var operatorOpt = new Option<string?>("--operator", "Only summarize matching operator");
        var receiptStatusOpt = new Option<string>("--receipt-status", () => "all", "Receipt status: all|valid|missing|unreadable");
        var analyticsSnapshotOpt = new Option<string?>("--analytics-snapshot", "Persist the ledger-stats.v1 JSON snapshot to this local path");
        var fromAnalyticsSnapshotOpt = new Option<string?>("--from-analytics-snapshot", "Read a previously persisted ledger-stats.v1 JSON snapshot");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("stats", "Summarize local ledger operations and optional analytics snapshots")
        {
            dirOpt,
            projectOpt,
            sourceOpt,
            sinceOpt,
            untilOpt,
            windowOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            receiptStatusOpt,
            analyticsSnapshotOpt,
            fromAnalyticsSnapshotOpt,
            outputOpt,
        };

        // Bindings exceed the typed SetHandler overload cap; keep CLI wiring explicit.
        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteStatsAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(sourceOpt)!,
                ctx.ParseResult.GetValueForOption(sinceOpt),
                ctx.ParseResult.GetValueForOption(untilOpt),
                ctx.ParseResult.GetValueForOption(windowOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(receiptStatusOpt)!,
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out,
                projectDirectories: ctx.ParseResult.GetValueForOption(projectOpt),
                analyticsSnapshotPath: ctx.ParseResult.GetValueForOption(analyticsSnapshotOpt),
                fromAnalyticsSnapshotPath: ctx.ParseResult.GetValueForOption(fromAnalyticsSnapshotOpt));
        });

        return cmd;
    }

    private static Command CreateValidateCommand()
    {
        var dirOpt = new Option<string?>("--dir", "Project directory; defaults to current directory");
        var sourceOpt = new Option<string>("--source", () => "all", "Source: all|ledger|journal|history|deliveries|workflows");
        var sinceOpt = new Option<string?>("--since", "Only validate operations at or after this ISO timestamp");
        var untilOpt = new Option<string?>("--until", "Only validate operations at or before this ISO timestamp");
        var windowOpt = new Option<string?>("--window", "Recent window ending at now, e.g. 7d, 24h, 60m");
        var actionOpt = new Option<string?>("--action", "Only validate matching action");
        var categoryOpt = new Option<string?>("--category", "Only validate matching category");
        var operatorOpt = new Option<string?>("--operator", "Only validate matching operator");
        var receiptStatusOpt = new Option<string>("--receipt-status", () => "all", "Receipt status: all|valid|missing|unreadable");
        var failOnOpt = new Option<string>("--fail-on", () => "error", "Failure threshold: error|warning");
        var outputOpt = new Option<string>("--output", () => "table", "Output format: table|json|markdown");

        var cmd = new Command("validate", "Validate local ledger artifact references without writing")
        {
            dirOpt,
            sourceOpt,
            sinceOpt,
            untilOpt,
            windowOpt,
            actionOpt,
            categoryOpt,
            operatorOpt,
            receiptStatusOpt,
            failOnOpt,
            outputOpt,
        };

        // Bindings exceed the typed SetHandler overload cap; keep CLI wiring explicit.
        cmd.SetHandler(async ctx =>
        {
            Environment.ExitCode = await ExecuteValidateAsync(
                ctx.ParseResult.GetValueForOption(dirOpt),
                ctx.ParseResult.GetValueForOption(sourceOpt)!,
                ctx.ParseResult.GetValueForOption(sinceOpt),
                ctx.ParseResult.GetValueForOption(untilOpt),
                ctx.ParseResult.GetValueForOption(windowOpt),
                ctx.ParseResult.GetValueForOption(actionOpt),
                ctx.ParseResult.GetValueForOption(categoryOpt),
                ctx.ParseResult.GetValueForOption(operatorOpt),
                ctx.ParseResult.GetValueForOption(receiptStatusOpt)!,
                ctx.ParseResult.GetValueForOption(failOnOpt)!,
                ctx.ParseResult.GetValueForOption(outputOpt)!,
                Console.Out);
        });

        return cmd;
    }

    public static async Task<int> ExecuteQueryAsync(
        string? projectDirectory,
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        int limit,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null,
        string? commandName = null,
        IReadOnlyList<string>? commandArgs = null,
        int? affectedElementCount = null,
        IReadOnlyList<long>? affectedElementIds = null)
    {
        if (!TryNormalizeSource(source, out var normalizedSource))
        {
            await output.WriteLineAsync(SourceErrorMessage);
            return 1;
        }

        if (!TryNormalizeReceiptStatus(receiptStatus, out var normalizedReceiptStatus))
        {
            await output.WriteLineAsync("Error: --receipt-status must be 'all', 'valid', 'missing', or 'unreadable'.");
            return 1;
        }

        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (limit <= 0)
        {
            await output.WriteLineAsync("Error: --limit must be greater than 0.");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (!TryBuildWindow(since, until, window, generatedAt, out var sinceUtc, out var untilUtc, out var windowError))
        {
            await output.WriteLineAsync($"Error: {windowError}");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);

        var report = await CollectLedgerOperationsAsync(
            projectRoot,
            normalizedSource,
            generatedAt,
            new LedgerQuerySpec
            {
                Source = normalizedSource,
                SinceUtc = sinceUtc?.ToString("o", CultureInfo.InvariantCulture),
                UntilUtc = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
                Window = string.IsNullOrWhiteSpace(window) ? null : window.Trim(),
                Action = NormalizeNullable(action),
                Category = NormalizeNullable(category),
                Operator = NormalizeNullable(operatorFilter),
                ReceiptStatus = normalizedReceiptStatus,
                Limit = limit,
            });

        var filtered = report.Operations
            .Where(operation => IsInWindow(operation.Timestamp, sinceUtc, untilUtc))
            .Where(operation => Matches(operation.Action, action))
            .Where(operation => Matches(operation.Category, category))
            .Where(operation => Matches(operation.Operator, operatorFilter))
            .Where(operation => MatchesReceiptStatus(operation.ReceiptStatus, normalizedReceiptStatus))
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .Take(limit)
            .ToList();

        report.Summary = BuildSummary(report, filtered);
        report.Operations.Clear();
        report.Operations.AddRange(filtered);

        await output.WriteLineAsync(Render(report, normalizedOutput));
        return 0;
    }

    public static async Task<int> ExecuteAppendAsync(
        string? projectDirectory,
        string? action,
        string? category,
        string? operatorName,
        string status,
        string? summary,
        string? timestamp,
        string? model,
        string? modelPath,
        string? planHash,
        string? artifactPath,
        string? receiptPath,
        string? receiptHash,
        string? rollbackPointer,
        IReadOnlyList<string> evidenceLinks,
        bool yes,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null,
        string? commandName = null,
        IReadOnlyList<string>? commandArgs = null,
        int? affectedElementCount = null,
        IReadOnlyList<long>? affectedElementIds = null,
        string? revitVersion = null)
    {
        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            await output.WriteLineAsync("Error: --action is required.");
            return 1;
        }

        if (!TryNormalizeAppendStatus(status, out var normalizedStatus))
        {
            await output.WriteLineAsync("Error: --status must be 'planned', 'succeeded', 'failed', or 'blocked'.");
            return 1;
        }

        DateTimeOffset operationTimestamp;
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            operationTimestamp = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        }
        else if (!TryParseTimestamp(timestamp, out var parsedTimestamp))
        {
            await output.WriteLineAsync("Error: --timestamp must be ISO 8601 with an explicit UTC offset.");
            return 1;
        }
        else
        {
            operationTimestamp = parsedTimestamp!.Value;
        }

        if (!string.IsNullOrWhiteSpace(receiptHash) && !IsSha256Hex(receiptHash))
        {
            await output.WriteLineAsync("Error: --receipt-hash must be a 64-character SHA256 hex digest.");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var ledgerPath = GetOperationsLedgerPath(projectRoot);
        var normalizedEvidence = evidenceLinks
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => ResolveProjectPath(projectRoot, link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resolvedArtifactPath = ResolveOptionalProjectPath(projectRoot, artifactPath);
        var resolvedReceiptPath = ResolveOptionalProjectPath(projectRoot, receiptPath);
        if (!string.IsNullOrWhiteSpace(resolvedArtifactPath))
            normalizedEvidence.Add(resolvedArtifactPath!);
        if (!string.IsNullOrWhiteSpace(resolvedReceiptPath))
            normalizedEvidence.Add(resolvedReceiptPath!);
        normalizedEvidence = normalizedEvidence
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var timestampText = operationTimestamp.ToString("o", CultureInfo.InvariantCulture);
        var operatorValue = string.IsNullOrWhiteSpace(operatorName)
            ? Environment.UserName
            : operatorName.Trim();
        var record = new AppendLedgerRecord
        {
            Timestamp = timestampText,
            Command = string.IsNullOrWhiteSpace(commandName) ? "ledger append" : commandName.Trim(),
            Action = action.Trim(),
            Category = NormalizeNullable(category),
            Operator = operatorValue,
            Status = normalizedStatus,
            Summary = NormalizeNullable(summary),
            WorkingDirectory = projectRoot,
            ModelIdentity = NormalizeNullable(model),
            ModelPath = ResolveOptionalProjectPath(projectRoot, modelPath),
            RevitVersion = NormalizeNullable(revitVersion),
            Machine = Environment.MachineName,
            StartedAtUtc = timestampText,
            EndedAtUtc = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            RiskLevel = "local-write",
            DryRunRequired = true,
            ApprovalRequired = true,
            PlanHash = NormalizeNullable(planHash),
            ArtifactPath = resolvedArtifactPath,
            ReceiptPath = resolvedReceiptPath,
            ReceiptHash = NormalizeNullable(receiptHash),
            JournalPath = Path.Combine(projectRoot, ".revitcli", "journal.jsonl"),
            RollbackPointer = NormalizeNullable(rollbackPointer),
            EvidenceLinks = normalizedEvidence,
            AffectedElementCount = affectedElementCount,
            AffectedElementIds = affectedElementIds?
                .Distinct()
                .OrderBy(elementId => elementId)
                .ToList() ?? new List<long>(),
        };
        record.Args = commandArgs?.Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList() ??
            BuildAppendArgs(
                record.Action,
                record.Category,
                operatorValue,
                normalizedStatus,
                record.Summary,
                timestampText,
                record.ModelIdentity,
                record.ModelPath,
                record.RevitVersion,
                record.PlanHash,
                record.ArtifactPath,
                record.ReceiptPath,
                record.ReceiptHash,
                record.RollbackPointer,
                normalizedEvidence,
                yes);
        record.Checks = BuildAppendChecks(record, yes);
        record.Artifacts = BuildAppendArtifacts(ledgerPath, record.ArtifactPath, record.ReceiptPath);
        record.OperationId = ComputeAppendOperationId(record);

        var result = new LedgerAppendResult
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
            LedgerPath = ledgerPath,
            DryRun = !yes,
            Written = false,
            Operation = ToLedgerOperation(record, ledgerPath, line: null),
        };

        if (yes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
            File.AppendAllText(ledgerPath, JsonSerializer.Serialize(record, TerminalJsonOptions.CompactContract) + Environment.NewLine);
            result.Written = true;
        }

        await output.WriteLineAsync(RenderAppend(result, normalizedOutput));
        return 0;
    }

    public static async Task<int> ExecuteReplayAsync(
        string? projectDirectory,
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        int limit,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null,
        RevitClient? client = null,
        bool apply = false,
        bool yes = false)
    {
        if (!TryNormalizeSource(source, out var normalizedSource))
        {
            await output.WriteLineAsync(SourceErrorMessage);
            return 1;
        }

        if (!TryNormalizeReceiptStatus(receiptStatus, out var normalizedReceiptStatus))
        {
            await output.WriteLineAsync("Error: --receipt-status must be 'all', 'valid', 'missing', or 'unreadable'.");
            return 1;
        }

        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (limit <= 0)
        {
            await output.WriteLineAsync("Error: --limit must be greater than 0.");
            return 1;
        }

        if (apply && !yes)
        {
            await output.WriteLineAsync("Error: use --yes with --apply to replay ledger operations.");
            return 1;
        }

        if (apply && normalizedSource != "ledger")
        {
            await output.WriteLineAsync("Error: ledger replay --apply is limited to --source ledger.");
            return 1;
        }

        if (apply && client == null)
        {
            await output.WriteLineAsync("Error: ledger replay --apply requires a Revit client.");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (!TryBuildWindow(since, until, window, generatedAt, out var sinceUtc, out var untilUtc, out var windowError))
        {
            await output.WriteLineAsync($"Error: {windowError}");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);
        var queryReport = await CollectLedgerOperationsAsync(
            projectRoot,
            normalizedSource,
            generatedAt,
            new LedgerQuerySpec
            {
                Source = normalizedSource,
                SinceUtc = sinceUtc?.ToString("o", CultureInfo.InvariantCulture),
                UntilUtc = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
                Window = string.IsNullOrWhiteSpace(window) ? null : window.Trim(),
                Action = NormalizeNullable(action),
                Category = NormalizeNullable(category),
                Operator = NormalizeNullable(operatorFilter),
                ReceiptStatus = normalizedReceiptStatus,
                Limit = limit,
            });
        var operations = queryReport.Operations
            .Where(operation => IsInWindow(operation.Timestamp, sinceUtc, untilUtc))
            .Where(operation => Matches(operation.Action, action))
            .Where(operation => Matches(operation.Category, category))
            .Where(operation => Matches(operation.Operator, operatorFilter))
            .Where(operation => MatchesReceiptStatus(operation.ReceiptStatus, normalizedReceiptStatus))
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .Take(limit)
            .ToList();
        var replay = BuildReplayReport(projectRoot, normalizedSource, generatedAt, operations, queryReport.Issues, apply);
        if (apply)
        {
            var replayArgs = BuildReplayLedgerArgs(
                normalizedSource,
                since,
                until,
                window,
                action,
                category,
                operatorFilter,
                normalizedReceiptStatus,
                limit,
                normalizedOutput,
                apply,
                yes);
            var applyExitCode = await ApplyReplayAsync(client!, replay, replayArgs);
            await output.WriteLineAsync(RenderReplay(replay, normalizedOutput));
            return applyExitCode;
        }

        await output.WriteLineAsync(RenderReplay(replay, normalizedOutput));
        return 0;
    }

    public static async Task<int> ExecuteValidateAsync(
        string? projectDirectory,
        string source,
        string failOn,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null) =>
        await ExecuteValidateAsync(
            projectDirectory,
            source,
            since: null,
            until: null,
            window: null,
            action: null,
            category: null,
            operatorFilter: null,
            receiptStatus: "all",
            failOn,
            outputFormat,
            output,
            now);

    public static async Task<int> ExecuteValidateAsync(
        string? projectDirectory,
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        string failOn,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null)
    {
        if (!TryNormalizeSource(source, out var normalizedSource))
        {
            await output.WriteLineAsync(SourceErrorMessage);
            return 1;
        }

        if (!TryNormalizeReceiptStatus(receiptStatus, out var normalizedReceiptStatus))
        {
            await output.WriteLineAsync("Error: --receipt-status must be 'all', 'valid', 'missing', or 'unreadable'.");
            return 1;
        }

        if (!TryNormalizeFailOn(failOn, out var normalizedFailOn))
        {
            await output.WriteLineAsync("Error: --fail-on must be 'error' or 'warning'.");
            return 1;
        }

        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (!TryBuildWindow(since, until, window, generatedAt, out var sinceUtc, out var untilUtc, out var windowError))
        {
            await output.WriteLineAsync($"Error: {windowError}");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);

        var queryReport = await CollectLedgerOperationsAsync(
            projectRoot,
            normalizedSource,
            generatedAt,
            new LedgerQuerySpec
            {
                Source = normalizedSource,
                SinceUtc = sinceUtc?.ToString("o", CultureInfo.InvariantCulture),
                UntilUtc = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
                Window = string.IsNullOrWhiteSpace(window) ? null : window.Trim(),
                Action = NormalizeNullable(action),
                Category = NormalizeNullable(category),
                Operator = NormalizeNullable(operatorFilter),
                ReceiptStatus = normalizedReceiptStatus,
                Limit = int.MaxValue,
            });
        var filtered = queryReport.Operations
            .Where(operation => IsInWindow(operation.Timestamp, sinceUtc, untilUtc, keepInvalidTimestamp: true))
            .Where(operation => Matches(operation.Action, action))
            .Where(operation => Matches(operation.Category, category))
            .Where(operation => Matches(operation.Operator, operatorFilter))
            .Where(operation => MatchesReceiptStatus(operation.ReceiptStatus, normalizedReceiptStatus))
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .ToList();
        queryReport.Operations.Clear();
        queryReport.Operations.AddRange(filtered);
        queryReport.Summary = BuildSummary(queryReport, queryReport.Operations);

        var validation = BuildValidationReport(queryReport, normalizedFailOn);
        await output.WriteLineAsync(RenderValidation(validation, normalizedOutput));
        return validation.Valid ? 0 : 1;
    }

    public static async Task<int> ExecuteStatsAsync(
        string? projectDirectory,
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null,
        IReadOnlyList<string>? projectDirectories = null,
        string? analyticsSnapshotPath = null,
        string? fromAnalyticsSnapshotPath = null)
    {
        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(analyticsSnapshotPath) &&
            !string.IsNullOrWhiteSpace(fromAnalyticsSnapshotPath))
        {
            await output.WriteLineAsync("Error: --analytics-snapshot and --from-analytics-snapshot cannot be used together.");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);

        if (!string.IsNullOrWhiteSpace(fromAnalyticsSnapshotPath))
        {
            var snapshot = await TryLoadStatsSnapshotAsync(projectRoot, fromAnalyticsSnapshotPath, output);
            if (snapshot is null)
                return 1;

            await output.WriteLineAsync(RenderStats(snapshot, normalizedOutput));
            return 0;
        }

        if (!TryNormalizeSource(source, out var normalizedSource))
        {
            await output.WriteLineAsync(SourceErrorMessage);
            return 1;
        }

        if (!TryNormalizeReceiptStatus(receiptStatus, out var normalizedReceiptStatus))
        {
            await output.WriteLineAsync("Error: --receipt-status must be 'all', 'valid', 'missing', or 'unreadable'.");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (!TryBuildWindow(since, until, window, generatedAt, out var sinceUtc, out var untilUtc, out var windowError))
        {
            await output.WriteLineAsync($"Error: {windowError}");
            return 1;
        }

        var querySpec = new LedgerQuerySpec
            {
                Source = normalizedSource,
                SinceUtc = sinceUtc?.ToString("o", CultureInfo.InvariantCulture),
                UntilUtc = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
                Window = string.IsNullOrWhiteSpace(window) ? null : window.Trim(),
                Action = NormalizeNullable(action),
                Category = NormalizeNullable(category),
                Operator = NormalizeNullable(operatorFilter),
                ReceiptStatus = normalizedReceiptStatus,
                Limit = int.MaxValue,
            };
        var projectRoots = NormalizeProjectRoots(projectRoot, projectDirectories);
        var mergedReport = new LedgerQueryReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
            Query = querySpec,
        };
        var byProject = new List<LedgerStatsCount>();

        foreach (var root in projectRoots)
        {
            var queryReport = await CollectLedgerOperationsAsync(root, normalizedSource, generatedAt, querySpec);
            var filtered = queryReport.Operations
            .Where(operation => IsInWindow(operation.Timestamp, sinceUtc, untilUtc))
            .Where(operation => Matches(operation.Action, action))
            .Where(operation => Matches(operation.Category, category))
            .Where(operation => Matches(operation.Operator, operatorFilter))
            .Where(operation => MatchesReceiptStatus(operation.ReceiptStatus, normalizedReceiptStatus))
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .ToList();
            byProject.Add(new LedgerStatsCount(root, filtered.Count));
            mergedReport.Operations.AddRange(filtered);
            mergedReport.Issues.AddRange(queryReport.Issues);
        }

        mergedReport.Operations.Sort(CompareLedgerOperations);
        mergedReport.Summary = BuildSummary(mergedReport, mergedReport.Operations);

        var stats = BuildStatsReport(mergedReport, projectRoots, byProject);
        if (!string.IsNullOrWhiteSpace(analyticsSnapshotPath) &&
            !await TryWriteAnalyticsSnapshotAsync(projectRoot, analyticsSnapshotPath, stats, output))
        {
            return 1;
        }

        await output.WriteLineAsync(RenderStats(stats, normalizedOutput));
        return 0;
    }

    public static async Task<int> ExecuteTimelineAsync(
        string? projectDirectory,
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        string bucket,
        string outputFormat,
        TextWriter output,
        DateTimeOffset? now = null,
        IReadOnlyList<string>? projectDirectories = null,
        string? analyticsSnapshotPath = null,
        string? fromAnalyticsSnapshotPath = null)
    {
        if (!TryNormalizeOutput(outputFormat, out var normalizedOutput))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(analyticsSnapshotPath) &&
            !string.IsNullOrWhiteSpace(fromAnalyticsSnapshotPath))
        {
            await output.WriteLineAsync("Error: --analytics-snapshot and --from-analytics-snapshot cannot be used together.");
            return 1;
        }

        var projectRoot = string.IsNullOrWhiteSpace(projectDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirectory!);

        if (!string.IsNullOrWhiteSpace(fromAnalyticsSnapshotPath))
        {
            var snapshot = await TryLoadTimelineSnapshotAsync(projectRoot, fromAnalyticsSnapshotPath, output);
            if (snapshot is null)
                return 1;

            await output.WriteLineAsync(RenderTimeline(snapshot, normalizedOutput));
            return 0;
        }

        if (!TryNormalizeSource(source, out var normalizedSource))
        {
            await output.WriteLineAsync(SourceErrorMessage);
            return 1;
        }

        if (!TryNormalizeReceiptStatus(receiptStatus, out var normalizedReceiptStatus))
        {
            await output.WriteLineAsync("Error: --receipt-status must be 'all', 'valid', 'missing', or 'unreadable'.");
            return 1;
        }

        if (!TryNormalizeBucket(bucket, out var normalizedBucket))
        {
            await output.WriteLineAsync("Error: --bucket must be 'day' or 'hour'.");
            return 1;
        }

        var generatedAt = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (!TryBuildWindow(since, until, window, generatedAt, out var sinceUtc, out var untilUtc, out var windowError))
        {
            await output.WriteLineAsync($"Error: {windowError}");
            return 1;
        }

        var querySpec = new LedgerQuerySpec
            {
                Source = normalizedSource,
                SinceUtc = sinceUtc?.ToString("o", CultureInfo.InvariantCulture),
                UntilUtc = untilUtc?.ToString("o", CultureInfo.InvariantCulture),
                Window = string.IsNullOrWhiteSpace(window) ? null : window.Trim(),
                Action = NormalizeNullable(action),
                Category = NormalizeNullable(category),
                Operator = NormalizeNullable(operatorFilter),
                ReceiptStatus = normalizedReceiptStatus,
                Limit = int.MaxValue,
            };
        var projectRoots = NormalizeProjectRoots(projectRoot, projectDirectories);
        var mergedReport = new LedgerQueryReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
            Query = querySpec,
        };
        var byProject = new List<LedgerStatsCount>();

        foreach (var root in projectRoots)
        {
            var queryReport = await CollectLedgerOperationsAsync(root, normalizedSource, generatedAt, querySpec);
            var filtered = queryReport.Operations
                .Where(operation => IsInWindow(operation.Timestamp, sinceUtc, untilUtc, keepInvalidTimestamp: true))
                .Where(operation => Matches(operation.Action, action))
                .Where(operation => Matches(operation.Category, category))
                .Where(operation => Matches(operation.Operator, operatorFilter))
                .Where(operation => MatchesReceiptStatus(operation.ReceiptStatus, normalizedReceiptStatus))
                .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
                .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(operation => operation.Line ?? 0)
                .ToList();
            byProject.Add(new LedgerStatsCount(root, filtered.Count));
            mergedReport.Operations.AddRange(filtered);
            mergedReport.Issues.AddRange(queryReport.Issues);
        }

        mergedReport.Operations.Sort(CompareLedgerOperations);
        mergedReport.Summary = BuildSummary(mergedReport, mergedReport.Operations);

        var timeline = BuildTimelineReport(mergedReport, normalizedBucket, projectRoots, byProject);
        if (!string.IsNullOrWhiteSpace(analyticsSnapshotPath) &&
            !await TryWriteAnalyticsSnapshotAsync(projectRoot, analyticsSnapshotPath, timeline, output))
        {
            return 1;
        }

        await output.WriteLineAsync(RenderTimeline(timeline, normalizedOutput));
        return 0;
    }

    private static async Task<LedgerQueryReport> CollectLedgerOperationsAsync(
        string projectRoot,
        string normalizedSource,
        DateTimeOffset generatedAt,
        LedgerQuerySpec query)
    {
        var report = new LedgerQueryReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
            Query = query,
        };

        AddLedgerOperations(report, projectRoot, normalizedSource);
        await AddHistoryOperationsAsync(report, projectRoot, normalizedSource);
        AddJournalOperations(report, projectRoot, normalizedSource);
        AddDeliveryOperations(report, projectRoot, normalizedSource);
        AddWorkflowOperations(report, projectRoot, normalizedSource);
        return report;
    }

    private static void AddLedgerOperations(
        LedgerQueryReport report,
        string projectRoot,
        string source)
    {
        if (!IncludesSource(source, "ledger"))
            return;

        var ledgerPath = GetOperationsLedgerPath(projectRoot);
        if (!File.Exists(ledgerPath))
        {
            if (string.Equals(source, "ledger", StringComparison.OrdinalIgnoreCase))
                report.Issues.Add(new LedgerIssue("warning", "ledger", ledgerPath, null, "operations ledger not found"));
            return;
        }

        try
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(ledgerPath))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                AppendLedgerRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<AppendLedgerRecord>(line, TerminalJsonOptions.CompactContract);
                }
                catch (JsonException ex)
                {
                    report.Issues.Add(new LedgerIssue("error", "ledger", ledgerPath, lineNumber, $"failed to read operations ledger line: {ex.Message}"));
                    continue;
                }

                if (record == null)
                {
                    report.Issues.Add(new LedgerIssue("error", "ledger", ledgerPath, lineNumber, "operations ledger line is empty"));
                    continue;
                }

                report.Operations.Add(ToLedgerOperation(record, ledgerPath, lineNumber));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Issues.Add(new LedgerIssue("error", "ledger", ledgerPath, null, $"failed to read operations ledger: {ex.Message}"));
        }
    }

    private static LedgerOperation ToLedgerOperation(AppendLedgerRecord record, string ledgerPath, int? line)
    {
        var operation = new LedgerOperation
        {
            Source = "ledger",
            OperationId = record.OperationId,
            Command = record.Command,
            Args = record.Args.ToList(),
            Timestamp = record.Timestamp,
            Action = record.Action,
            Category = record.Category,
            Operator = record.Operator,
            Status = record.Status,
            ModelIdentity = record.ModelIdentity,
            ModelPath = record.ModelPath,
            RevitVersion = record.RevitVersion,
            PlanHash = record.PlanHash,
            Artifact = string.IsNullOrWhiteSpace(record.ArtifactPath) ? "operations.jsonl" : Path.GetFileName(record.ArtifactPath),
            ArtifactPath = record.ArtifactPath,
            Line = line,
            ReceiptPath = record.ReceiptPath,
            ReceiptHash = record.ReceiptHash,
            ReceiptStatus = LedgerReceiptStatus(record.ReceiptPath),
            RollbackPointer = record.RollbackPointer,
            Summary = record.Summary,
            AffectedElementCount = record.AffectedElementCount,
            AffectedElementIds = record.AffectedElementIds.ToList(),
            EvidenceLinks = record.EvidenceLinks
                .Append(ledgerPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        return operation;
    }

    private static string LedgerReceiptStatus(string? receiptPath)
    {
        if (string.IsNullOrWhiteSpace(receiptPath))
            return "none";
        return File.Exists(receiptPath) ? "valid" : "missing";
    }

    private static async Task AddHistoryOperationsAsync(
        LedgerQueryReport report,
        string projectRoot,
        string source)
    {
        if (!IncludesSource(source, "history"))
            return;

        var store = HistoryStore.ForProject(projectRoot);
        if (!Directory.Exists(store.RootDirectory))
        {
            report.Issues.Add(new LedgerIssue("warning", "history", store.RootDirectory, null, "history store not found"));
            return;
        }

        try
        {
            var entries = await store.ListAsync(includeFixBaselines: true);
            foreach (var entry in entries)
            {
                report.Operations.Add(new LedgerOperation
                {
                    Source = "history",
                    Timestamp = entry.CapturedAt,
                    Action = "history.capture",
                    Artifact = entry.Id,
                    ArtifactPath = Path.Combine(store.RootDirectory, entry.Id + ".json.gz"),
                    Category = entry.Source,
                    ReceiptStatus = "none",
                    Summary = $"{entry.Source}; elements={entry.ElementCount}",
                    EvidenceLinks = { store.IndexPath },
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            report.Issues.Add(new LedgerIssue("error", "history", store.RootDirectory, null, $"failed to read history: {ex.Message}"));
        }
    }

    private static void AddJournalOperations(
        LedgerQueryReport report,
        string projectRoot,
        string source)
    {
        if (!IncludesSource(source, "journal"))
            return;

        var journalPath = Path.Combine(projectRoot, ".revitcli", "journal.jsonl");
        if (!File.Exists(journalPath))
        {
            report.Issues.Add(new LedgerIssue("warning", "journal", journalPath, null, "journal file not found"));
            return;
        }

        try
        {
            var journal = JournalReader.Read(journalPath);
            foreach (var entry in journal.Entries)
            {
                report.Operations.Add(new LedgerOperation
                {
                    Source = "journal",
                    Timestamp = entry.Timestamp,
                    Action = entry.Action,
                    Category = entry.Category,
                    Operator = entry.Operator,
                    Artifact = "journal.jsonl",
                    ArtifactPath = journal.JournalPath,
                    Line = entry.LineNumber,
                    ReceiptStatus = "none",
                    Summary = entry.Summary,
                    AffectedElementCount = entry.AffectedElementCount,
                    AffectedElementIds = entry.AffectedElementIds.ToList(),
                    EvidenceLinks = { journal.JournalPath },
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            report.Issues.Add(new LedgerIssue("error", "journal", journalPath, null, $"failed to read journal: {ex.Message}"));
        }
    }

    private static void AddDeliveryOperations(
        LedgerQueryReport report,
        string projectRoot,
        string source)
    {
        if (!IncludesSource(source, "deliveries"))
            return;

        var manifest = DeliveryManifestReader.Read(projectRoot);
        if (!manifest.Exists)
        {
            report.Issues.Add(new LedgerIssue("warning", "deliveries", manifest.ManifestPath, null, "delivery manifest not found"));
            return;
        }

        foreach (var entry in manifest.Entries)
        {
            var operation = new LedgerOperation
            {
                Source = "deliveries",
                Timestamp = entry.Timestamp,
                Action = $"deliverables.{entry.Kind ?? "unknown"}",
                Category = entry.Pipeline ?? entry.Format,
                Artifact = entry.Kind,
                ArtifactPath = manifest.ManifestPath,
                Line = entry.LineNumber,
                ReceiptPath = entry.ResolvedReceiptPath ?? entry.ReceiptPath,
                ReceiptHash = entry.ReceiptHash,
                ReceiptStatus = DeliveryReceiptStatus(entry),
                Summary = $"success={FormatNullable(entry.Success)}; dryRun={FormatNullable(entry.DryRun)}",
                EvidenceLinks = { manifest.ManifestPath },
            };
            if (!string.IsNullOrWhiteSpace(entry.ResolvedReceiptPath))
                operation.EvidenceLinks.Add(entry.ResolvedReceiptPath!);
            AddDeliveryIssues(operation, manifest.Issues.Where(issue => issue.LineNumber == entry.LineNumber));
            report.Operations.Add(operation);
        }

        foreach (var issue in manifest.Issues.Where(issue => issue.LineNumber == null || !manifest.Entries.Any(entry => entry.LineNumber == issue.LineNumber)))
        {
            var ledgerIssue = new LedgerIssue(issue.Severity, "deliveries", manifest.ManifestPath, issue.LineNumber, issue.Message);
            report.Issues.Add(ledgerIssue);
            report.Operations.Add(new LedgerOperation
            {
                Source = "deliveries",
                Action = "deliverables.manifest-issue",
                Artifact = "manifest.jsonl",
                ArtifactPath = manifest.ManifestPath,
                Line = issue.LineNumber,
                ReceiptStatus = "unreadable",
                EvidenceLinks = { manifest.ManifestPath },
            });
        }
    }

    private static void AddWorkflowOperations(
        LedgerQueryReport report,
        string projectRoot,
        string source)
    {
        if (!IncludesSource(source, "workflows"))
            return;

        WorkflowReceiptListReport receipts;
        try
        {
            receipts = WorkflowReceiptReader.Read(projectRoot, int.MaxValue, failedOnly: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            report.Issues.Add(new LedgerIssue("error", "workflows", WorkflowReceiptReader.ResolveReceiptDirectory(projectRoot), null, $"failed to read workflow receipts: {ex.Message}"));
            return;
        }

        if (!receipts.Exists)
        {
            report.Issues.Add(new LedgerIssue("warning", "workflows", receipts.ReceiptDirectory, null, "workflow receipt directory not found"));
            return;
        }

        foreach (var receipt in receipts.Receipts)
        {
            report.Operations.Add(new LedgerOperation
            {
                Source = "workflows",
                Timestamp = FirstNonEmpty(receipt.CompletedAtUtc, receipt.StartedAtUtc),
                Action = "workflow.run",
                Category = receipt.Name,
                Operator = receipt.Operator,
                Artifact = receipt.Name,
                ArtifactPath = receipt.Path,
                ReceiptPath = receipt.Path,
                ReceiptStatus = "valid",
                Summary = $"success={receipt.Success.ToString().ToLowerInvariant()}; dryRun={receipt.DryRun.ToString().ToLowerInvariant()}; exit={receipt.ExitCode.ToString(CultureInfo.InvariantCulture)}",
                EvidenceLinks = { receipt.Path },
            });
        }

        foreach (var issue in receipts.Issues
                     .OrderBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(issue => issue.Message, StringComparer.OrdinalIgnoreCase))
        {
            var ledgerIssue = new LedgerIssue(issue.Severity, "workflows", issue.Path, null, issue.Message);
            report.Issues.Add(ledgerIssue);
            report.Operations.Add(new LedgerOperation
            {
                Source = "workflows",
                Action = "workflow.receipt-issue",
                Artifact = Path.GetFileName(issue.Path),
                ArtifactPath = issue.Path,
                ReceiptPath = issue.Path,
                ReceiptStatus = "unreadable",
                EvidenceLinks = { issue.Path },
            });
        }
    }

    private static void AddDeliveryIssues(LedgerOperation operation, IEnumerable<DeliveryManifestIssue> issues)
    {
        foreach (var issue in issues)
        {
            operation.Issues.Add(new LedgerIssue(issue.Severity, "deliveries", operation.ArtifactPath ?? "", issue.LineNumber, issue.Message));
        }
    }

    private static LedgerSummary BuildSummary(LedgerQueryReport report, IReadOnlyList<LedgerOperation> operations)
    {
        var bySource = operations
            .GroupBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LedgerSourceCount(group.Key, group.Count()))
            .ToList();

        return new LedgerSummary
        {
            TotalOperations = operations.Count,
            IssueCount = report.Issues.Count + operations.Sum(operation => operation.Issues.Count),
            BySource = bySource,
        };
    }

    private static LedgerStatsReport BuildStatsReport(
        LedgerQueryReport queryReport,
        IReadOnlyList<string>? projectDirectories = null,
        IReadOnlyList<LedgerStatsCount>? byProject = null)
    {
        var operations = queryReport.Operations
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .ToList();
        var issues = queryReport.Issues
            .Concat(operations.SelectMany(operation => operation.Issues))
            .OrderBy(issue => NormalizeSeverity(issue.Severity), StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Line ?? 0)
            .ThenBy(issue => issue.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var parsedTimestamps = operations
            .Select(operation => ParseTimestamp(operation.Timestamp))
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .OrderBy(timestamp => timestamp)
            .ToList();

        return new LedgerStatsReport
        {
            GeneratedAt = queryReport.GeneratedAt,
            ProjectDirectory = queryReport.ProjectDirectory,
            ProjectDirectories = projectDirectories?.ToList() ?? new List<string> { queryReport.ProjectDirectory },
            Query = queryReport.Query,
            Summary = new LedgerStatsSummary
            {
                OperationCount = operations.Count,
                IssueCount = issues.Count,
                ErrorIssueCount = issues.Count(issue => IsError(issue.Severity)),
                WarningIssueCount = issues.Count(issue => IsWarning(issue.Severity)),
                MissingReceiptCount = operations.Count(operation => operation.ReceiptStatus == "missing"),
                UnreadableReceiptCount = operations.Count(operation => operation.ReceiptStatus == "unreadable"),
                FirstTimestamp = parsedTimestamps.Count == 0 ? null : parsedTimestamps.First().ToString("o", CultureInfo.InvariantCulture),
                LastTimestamp = parsedTimestamps.Count == 0 ? null : parsedTimestamps.Last().ToString("o", CultureInfo.InvariantCulture),
            },
            BySource = CountBy(operations.Select(operation => operation.Source)),
            ByAction = CountBy(operations.Select(operation => operation.Action)),
            ByCategory = CountBy(operations.Select(operation => operation.Category)),
            ByOperator = CountBy(operations.Select(operation => operation.Operator)),
            ByReceiptStatus = CountBy(operations.Select(operation => operation.ReceiptStatus)),
            IssuesBySource = CountBy(issues.Select(issue => issue.Source)),
            IssuesBySeverity = CountBy(issues.Select(issue => NormalizeSeverity(issue.Severity))),
            ByProject = byProject?.OrderBy(count => count.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<LedgerStatsCount>(),
        };
    }

    private static List<string> NormalizeProjectRoots(string primaryProjectRoot, IReadOnlyList<string>? projectDirectories)
    {
        var roots = new List<string> { Path.GetFullPath(primaryProjectRoot) };
        if (projectDirectories is not null)
        {
            foreach (var projectDirectory in projectDirectories)
            {
                if (!string.IsNullOrWhiteSpace(projectDirectory))
                    roots.Add(Path.GetFullPath(projectDirectory));
            }
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<bool> TryWriteAnalyticsSnapshotAsync<TReport>(
        string projectRoot,
        string snapshotPath,
        TReport report,
        TextWriter output)
    {
        var fullPath = ResolveProjectPath(projectRoot, snapshotPath);
        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel) + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await output.WriteLineAsync($"Error: failed to write analytics snapshot '{fullPath}': {ex.Message}");
            return false;
        }
    }

    private static async Task<LedgerStatsReport?> TryLoadStatsSnapshotAsync(
        string projectRoot,
        string snapshotPath,
        TextWriter output)
    {
        var fullPath = ResolveProjectPath(projectRoot, snapshotPath);
        try
        {
            var report = JsonSerializer.Deserialize<LedgerStatsReport>(
                await File.ReadAllTextAsync(fullPath),
                TerminalJsonOptions.CompactContract);
            if (report is null ||
                !string.Equals(report.SchemaVersion, "ledger-stats.v1", StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync($"Error: analytics snapshot '{fullPath}' is not ledger-stats.v1.");
                return null;
            }

            return report;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            await output.WriteLineAsync($"Error: failed to read analytics snapshot '{fullPath}': {ex.Message}");
            return null;
        }
    }

    private static async Task<LedgerTimelineReport?> TryLoadTimelineSnapshotAsync(
        string projectRoot,
        string snapshotPath,
        TextWriter output)
    {
        var fullPath = ResolveProjectPath(projectRoot, snapshotPath);
        try
        {
            var report = JsonSerializer.Deserialize<LedgerTimelineReport>(
                await File.ReadAllTextAsync(fullPath),
                TerminalJsonOptions.CompactContract);
            if (report is null ||
                !string.Equals(report.SchemaVersion, "ledger-timeline.v1", StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync($"Error: analytics snapshot '{fullPath}' is not ledger-timeline.v1.");
                return null;
            }

            return report;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            await output.WriteLineAsync($"Error: failed to read analytics snapshot '{fullPath}': {ex.Message}");
            return null;
        }
    }

    private static int CompareLedgerOperations(LedgerOperation left, LedgerOperation right)
    {
        var timestamp = Nullable.Compare(ParseTimestamp(left.Timestamp), ParseTimestamp(right.Timestamp));
        if (timestamp != 0)
            return timestamp;

        var source = string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0)
            return source;

        var artifact = string.Compare(left.ArtifactPath ?? "", right.ArtifactPath ?? "", StringComparison.OrdinalIgnoreCase);
        if (artifact != 0)
            return artifact;

        return Nullable.Compare(left.Line, right.Line);
    }

    private static List<LedgerStatsCount> CountBy(IEnumerable<string?> values) =>
        values
            .Select(value => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LedgerStatsCount(group.Key, group.Count()))
            .ToList();

    private static LedgerReplayReport BuildReplayReport(
        string projectRoot,
        string source,
        DateTimeOffset generatedAt,
        IReadOnlyList<LedgerOperation> operations,
        IReadOnlyList<LedgerIssue> issues,
        bool applyRequested = false)
    {
        var steps = operations
            .Select((operation, index) =>
            {
                var command = string.IsNullOrWhiteSpace(operation.Command)
                    ? $"ledger operation {operation.Action}"
                    : operation.Command;
                string? blockReason = null;
                var canApply = applyRequested && TryBuildReplayApplyRequest(operation, out blockReason);
                return new LedgerReplayStep
                {
                    Index = index + 1,
                    OperationId = operation.OperationId,
                    Source = operation.Source,
                    Timestamp = operation.Timestamp,
                    Action = operation.Action,
                    Category = operation.Category,
                    Operator = operation.Operator,
                    Status = operation.Status,
                    Command = command,
                    Args = operation.Args,
                    ReceiptStatus = operation.ReceiptStatus,
                    ReceiptPath = operation.ReceiptPath,
                    RollbackPointer = operation.RollbackPointer,
                    ArtifactPath = operation.ArtifactPath,
                    AffectedElementCount = operation.AffectedElementCount,
                    AffectedElementIds = operation.AffectedElementIds.ToList(),
                    ReplayMode = applyRequested ? "apply" : "preview",
                    CanApply = canApply,
                    BlockReason = canApply
                        ? null
                        : applyRequested
                            ? blockReason
                            : "ledger replay is preview-only by default; use --apply --yes for eligible source-ledger set, export, or schedule batch-export records",
                };
            })
            .ToList();
        var applicableStepCount = steps.Count(step => step.CanApply);

        return new LedgerReplayReport
        {
            GeneratedAt = generatedAt.ToString("o", CultureInfo.InvariantCulture),
            ProjectDirectory = projectRoot,
            Source = source,
            DryRun = !applyRequested,
            ApplySupported = applicableStepCount > 0,
            Summary = new LedgerReplaySummary
            {
                StepCount = steps.Count,
                ApplicableStepCount = applicableStepCount,
                BlockedStepCount = steps.Count - applicableStepCount,
                IssueCount = issues.Count,
            },
            Steps = steps,
            Issues = issues.ToList(),
        };
    }

    private static bool TryBuildReplayApplyRequest(LedgerOperation operation, out string? blockReason)
    {
        if (!string.Equals(operation.Source, "ledger", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "replay apply is limited to source-ledger records";
            return false;
        }

        if (string.Equals(operation.Command, "set", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Action, "set", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildSetReplayRequest(operation, out _, out blockReason);
        }

        if (string.Equals(operation.Command, "export", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Action, "export", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildExportReplayRequest(operation, out _, out blockReason);
        }

        if (string.Equals(operation.Command, "schedules", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Action, "schedules.batch-export", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildScheduleBatchExportReplayRequest(operation, out _, out blockReason);
        }

        blockReason = "only ledger records produced by set, export, or schedules.batch-export are eligible for replay apply";
        return false;
    }

    private static async Task<int> ApplyReplayAsync(
        RevitClient client,
        LedgerReplayReport replay,
        IReadOnlyList<string> replayArgs)
    {
        if (replay.Steps.Count == 0)
        {
            replay.Issues.Add(new LedgerIssue("error", "ledger", "", null, "No ledger replay steps matched the requested filters."));
            replay.Summary.IssueCount = replay.Issues.Count;
            return 1;
        }

        var blocked = replay.Steps.Where(step => !step.CanApply).ToList();
        if (blocked.Count > 0)
        {
            replay.Issues.Add(new LedgerIssue(
                "error",
                "ledger",
                "",
                null,
                "Replay apply refused because one or more selected steps are not eligible set, export, or schedule batch-export ledger records."));
            replay.Summary.IssueCount = replay.Issues.Count;
            return 1;
        }

        var failures = 0;
        foreach (var step in replay.Steps)
        {
            if (TryBuildSetReplayRequest(step, out var setRequest, out var setBlockReason))
            {
                var result = await client.SetParameterAsync(setRequest!);
                if (!result.Success)
                {
                    step.ApplyStatus = "failed";
                    step.Error = result.Error ?? "Unknown set failure.";
                    failures++;
                    continue;
                }

                step.ApplyStatus = "applied";
                step.AffectedElementCount = result.Data?.Affected ?? step.AffectedElementCount;
                continue;
            }

            if (TryBuildExportReplayRequest(step, out var exportRequest, out var exportBlockReason))
            {
                var result = await client.ExportAsync(exportRequest!);
                if (!result.Success)
                {
                    step.ApplyStatus = "failed";
                    step.Error = result.Error ?? "Unknown export failure.";
                    failures++;
                    continue;
                }

                var progress = result.Data;
                if (progress == null || !string.Equals(progress.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    step.ApplyStatus = "failed";
                    step.Error = progress?.Message ?? $"Export replay returned status '{progress?.Status ?? "unknown"}'.";
                    failures++;
                    continue;
                }

                step.ApplyStatus = "applied";
                continue;
            }

            if (TryBuildScheduleBatchExportReplayRequest(step, out var scheduleRequest, out var scheduleBlockReason))
            {
                var error = await ApplyScheduleBatchExportReplayAsync(client, scheduleRequest!);
                if (error != null)
                {
                    step.ApplyStatus = "failed";
                    step.Error = error;
                    failures++;
                    continue;
                }

                step.ApplyStatus = "applied";
                continue;
            }

            var blockReason = setBlockReason ?? exportBlockReason ?? scheduleBlockReason ?? "step is not eligible for replay apply";
            step.ApplyStatus = "blocked";
            step.Error = blockReason;
            failures++;
        }

        replay.Summary.AppliedStepCount = replay.Steps.Count(step => step.ApplyStatus == "applied");
        replay.Summary.FailedStepCount = failures;
        if (failures > 0)
        {
            replay.Issues.Add(new LedgerIssue("error", "ledger", "", null, "Replay apply failed for one or more steps."));
            replay.Summary.IssueCount = replay.Issues.Count;
            return 1;
        }

        var status = await TryGetStatusForReplayLedgerAsync(client);
        return await TryAppendReplayApplyOperationAsync(replay, replayArgs, status) ? 0 : 1;
    }

    private static async Task<bool> TryAppendReplayApplyOperationAsync(
        LedgerReplayReport replay,
        IReadOnlyList<string> replayArgs,
        StatusInfo? status)
    {
        try
        {
            var output = new StringWriter();
            var affectedElementIds = replay.Steps
                .Where(step => string.Equals(step.ApplyStatus, "applied", StringComparison.OrdinalIgnoreCase))
                .SelectMany(step => step.AffectedElementIds)
                .Distinct()
                .OrderBy(elementId => elementId)
                .ToList();
            var categories = replay.Steps
                .Select(step => step.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var exitCode = await ExecuteAppendAsync(
                replay.ProjectDirectory,
                action: "ledger.replay.apply",
                category: categories.Count == 1 ? categories[0] : null,
                operatorName: null,
                status: "succeeded",
                summary: $"Applied {replay.Summary.AppliedStepCount} ledger replay step(s)",
                timestamp: null,
                model: NormalizeNullable(status?.DocumentName),
                modelPath: NormalizeNullable(status?.DocumentPath),
                planHash: null,
                artifactPath: null,
                receiptPath: null,
                receiptHash: null,
                rollbackPointer: null,
                evidenceLinks: Array.Empty<string>(),
                yes: true,
                outputFormat: "json",
                output,
                now: null,
                commandName: "ledger",
                commandArgs: replayArgs,
                affectedElementCount: affectedElementIds.Count,
                affectedElementIds: affectedElementIds,
                revitVersion: NormalizeNullable(status?.RevitVersion));
            if (exitCode != 0)
            {
                replay.Issues.Add(new LedgerIssue(
                    "error",
                    "ledger",
                    "",
                    null,
                    $"Replay apply succeeded, but operation ledger append failed: {output.ToString().Trim()}"));
                replay.Summary.IssueCount = replay.Issues.Count;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
        {
            replay.Issues.Add(new LedgerIssue(
                "error",
                "ledger",
                "",
                null,
                $"Replay apply succeeded, but operation ledger append failed: {ex.Message}"));
            replay.Summary.IssueCount = replay.Issues.Count;
            return false;
        }
    }

    private static async Task<StatusInfo?> TryGetStatusForReplayLedgerAsync(RevitClient client)
    {
        try
        {
            var response = await client.GetStatusAsync();
            return response.Success ? response.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.Error.WriteLine($"[RevitCli] Status read failed (replay ledger model identity incomplete): {ex.Message}");
            return null;
        }
    }

    private static List<string> BuildReplayLedgerArgs(
        string source,
        string? since,
        string? until,
        string? window,
        string? action,
        string? category,
        string? operatorFilter,
        string receiptStatus,
        int limit,
        string outputFormat,
        bool apply,
        bool yes)
    {
        var args = new List<string> { "ledger", "replay", "--source", source };
        AddArg(args, "--since", since);
        AddArg(args, "--until", until);
        AddArg(args, "--window", window);
        AddArg(args, "--action", action);
        AddArg(args, "--category", category);
        AddArg(args, "--operator", operatorFilter);
        AddArg(args, "--receipt-status", receiptStatus);
        AddArg(args, "--limit", limit.ToString(CultureInfo.InvariantCulture));
        AddArg(args, "--output", outputFormat);
        if (apply)
            args.Add("--apply");
        if (yes)
            args.Add("--yes");
        return args;
    }

    private static bool TryBuildSetReplayRequest(LedgerOperation operation, out SetRequest? request, out string? blockReason)
    {
        request = null;
        if (!string.Equals(operation.Source, "ledger", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "replay apply is limited to source-ledger records";
            return false;
        }

        if (!string.Equals(operation.Command, "set", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(operation.Action, "set", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only ledger records produced by set are eligible for replay apply";
            return false;
        }

        if (!string.Equals(operation.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only succeeded set ledger records are eligible for replay apply";
            return false;
        }

        return TryBuildSetReplayRequest(
            operation.Args,
            operation.AffectedElementIds,
            out request,
            out blockReason);
    }

    private static bool TryBuildSetReplayRequest(LedgerReplayStep step, out SetRequest? request, out string? blockReason)
    {
        request = null;
        if (!step.CanApply)
        {
            blockReason = step.BlockReason ?? "step is not eligible for replay apply";
            return false;
        }

        return TryBuildSetReplayRequest(step.Args, step.AffectedElementIds, out request, out blockReason);
    }

    private static bool TryBuildSetReplayRequest(
        IReadOnlyList<string> args,
        IReadOnlyList<long> affectedElementIds,
        out SetRequest? request,
        out string? blockReason)
    {
        request = null;
        blockReason = null;
        if (args.Count == 0 || !string.Equals(args[0], "set", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "recorded command args do not start with set";
            return false;
        }

        if (!args.Any(arg => string.Equals(arg, "--yes", StringComparison.OrdinalIgnoreCase)))
        {
            blockReason = "recorded set command was not approved with --yes";
            return false;
        }

        string? param = null;
        string? value = null;
        var clearValue = false;
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--param", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded set command is missing --param value";
                    return false;
                }
                param = args[++i];
            }
            else if (string.Equals(arg, "--value", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded set command is missing --value value";
                    return false;
                }
                value = args[++i];
            }
            else if (string.Equals(arg, "--clear-value", StringComparison.OrdinalIgnoreCase))
            {
                clearValue = true;
                value = "";
            }
        }

        if (string.IsNullOrWhiteSpace(param))
        {
            blockReason = "recorded set command is missing --param";
            return false;
        }

        if (value == null)
        {
            blockReason = "recorded set command is missing --value or --clear-value";
            return false;
        }

        var frozenIds = affectedElementIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        if (frozenIds.Count == 0)
        {
            blockReason = "recorded set command has no frozen affected element ids";
            return false;
        }

        request = new SetRequest
        {
            ElementIds = frozenIds,
            Param = param,
            Value = clearValue ? "" : value,
            DryRun = false,
        };
        return true;
    }

    private static bool TryBuildExportReplayRequest(LedgerOperation operation, out ExportRequest? request, out string? blockReason)
    {
        request = null;
        if (!string.Equals(operation.Source, "ledger", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "replay apply is limited to source-ledger records";
            return false;
        }

        if (!string.Equals(operation.Command, "export", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(operation.Action, "export", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only ledger records produced by set or export are eligible for replay apply";
            return false;
        }

        if (!string.Equals(operation.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only succeeded export ledger records are eligible for replay apply";
            return false;
        }

        if (string.IsNullOrWhiteSpace(operation.ReceiptPath) ||
            !string.Equals(operation.ReceiptStatus, "valid", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only valid receipt-backed export ledger records are eligible for replay apply";
            return false;
        }

        return TryBuildExportReplayRequest(operation.Args, out request, out blockReason);
    }

    private static bool TryBuildExportReplayRequest(LedgerReplayStep step, out ExportRequest? request, out string? blockReason)
    {
        request = null;
        if (!step.CanApply)
        {
            blockReason = step.BlockReason ?? "step is not eligible for replay apply";
            return false;
        }

        return TryBuildExportReplayRequest(step.Args, out request, out blockReason);
    }

    private static bool TryBuildExportReplayRequest(
        IReadOnlyList<string> args,
        out ExportRequest? request,
        out string? blockReason)
    {
        request = null;
        blockReason = null;
        if (args.Count == 0 || !string.Equals(args[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "recorded command args do not start with export";
            return false;
        }

        string? format = null;
        string? outputDir = null;
        var sheets = new List<string>();
        var views = new List<string>();
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded export command is missing --format value";
                    return false;
                }
                format = args[++i];
            }
            else if (string.Equals(arg, "--output-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded export command is missing --output-dir value";
                    return false;
                }
                outputDir = args[++i];
            }
            else if (string.Equals(arg, "--sheets", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded export command is missing --sheets value";
                    return false;
                }
                sheets.Add(args[++i]);
            }
            else if (string.Equals(arg, "--views", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded export command is missing --views value";
                    return false;
                }
                views.Add(args[++i]);
            }
            else if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "recorded export command was a dry-run";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            blockReason = "recorded export command is missing --format";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            blockReason = "recorded export command is missing --output-dir";
            return false;
        }

        request = new ExportRequest
        {
            Format = format,
            OutputDir = outputDir,
            Sheets = sheets,
            Views = views,
            DryRun = false,
        };
        return true;
    }

    private static bool TryBuildScheduleBatchExportReplayRequest(
        LedgerOperation operation,
        out ScheduleBatchExportReplayRequest? request,
        out string? blockReason)
    {
        request = null;
        if (!string.Equals(operation.Source, "ledger", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "replay apply is limited to source-ledger records";
            return false;
        }

        if (!string.Equals(operation.Command, "schedules", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(operation.Action, "schedules.batch-export", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only ledger records produced by set, export, or schedules.batch-export are eligible for replay apply";
            return false;
        }

        if (!string.Equals(operation.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "only succeeded schedules.batch-export ledger records are eligible for replay apply";
            return false;
        }

        return TryBuildScheduleBatchExportReplayRequest(operation.Args, operation.ArtifactPath, out request, out blockReason);
    }

    private static bool TryBuildScheduleBatchExportReplayRequest(
        LedgerReplayStep step,
        out ScheduleBatchExportReplayRequest? request,
        out string? blockReason)
    {
        request = null;
        if (!step.CanApply)
        {
            blockReason = step.BlockReason ?? "step is not eligible for replay apply";
            return false;
        }

        return TryBuildScheduleBatchExportReplayRequest(step.Args, step.ArtifactPath, out request, out blockReason);
    }

    private static bool TryBuildScheduleBatchExportReplayRequest(
        IReadOnlyList<string> args,
        string? artifactPath,
        out ScheduleBatchExportReplayRequest? request,
        out string? blockReason)
    {
        request = null;
        blockReason = null;
        if (args.Count < 2 ||
            !string.Equals(args[0], "schedules", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(args[1], "batch-export", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "recorded command args do not start with schedules batch-export";
            return false;
        }

        string? set = null;
        string? outputDir = null;
        string? format = null;
        string? manifestPath = null;
        for (var i = 2; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--set", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded schedules batch-export command is missing --set value";
                    return false;
                }
                set = args[++i];
            }
            else if (string.Equals(arg, "--output-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded schedules batch-export command is missing --output-dir value";
                    return false;
                }
                outputDir = args[++i];
            }
            else if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded schedules batch-export command is missing --format value";
                    return false;
                }
                format = args[++i];
            }
            else if (string.Equals(arg, "--manifest", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    blockReason = "recorded schedules batch-export command is missing --manifest value";
                    return false;
                }
                manifestPath = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(set))
        {
            blockReason = "recorded schedules batch-export command is missing --set";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            blockReason = "recorded schedules batch-export command is missing --output-dir";
            return false;
        }

        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            blockReason = "recorded schedules batch-export command must use --format csv";
            return false;
        }

        manifestPath = NormalizeNullable(manifestPath) ?? NormalizeNullable(artifactPath);
        if (manifestPath == null)
        {
            blockReason = "recorded schedules batch-export command is missing --manifest";
            return false;
        }

        var fullOutputDir = Path.GetFullPath(outputDir);
        if (!TryReadScheduleBatchExportManifest(manifestPath, fullOutputDir, out var entries, out blockReason))
            return false;

        request = new ScheduleBatchExportReplayRequest(set, fullOutputDir, "csv", Path.GetFullPath(manifestPath), entries);
        return true;
    }

    private static bool TryReadScheduleBatchExportManifest(
        string manifestPath,
        string outputDir,
        out IReadOnlyList<ScheduleBatchExportReplayEntry> entries,
        out string? blockReason)
    {
        entries = Array.Empty<ScheduleBatchExportReplayEntry>();
        blockReason = null;
        try
        {
            var fullPath = Path.GetFullPath(manifestPath);
            if (!File.Exists(fullPath))
            {
                blockReason = "recorded schedules batch-export manifest is missing";
                return false;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(fullPath));
            var root = json.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) ||
                !string.Equals(schema.GetString(), "schedule-export-manifest.v1", StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "recorded schedules batch-export manifest has an unsupported schema";
                return false;
            }

            if (!root.TryGetProperty("entries", out var entriesElement) ||
                entriesElement.ValueKind != JsonValueKind.Array)
            {
                blockReason = "recorded schedules batch-export manifest has no entries";
                return false;
            }

            var parsed = new List<ScheduleBatchExportReplayEntry>();
            foreach (var entry in entriesElement.EnumerateArray())
            {
                if (entry.TryGetProperty("success", out var success) &&
                    success.ValueKind == JsonValueKind.False)
                {
                    continue;
                }

                var scheduleName = entry.TryGetProperty("scheduleName", out var nameElement) ? nameElement.GetString() : null;
                var category = entry.TryGetProperty("category", out var categoryElement) ? categoryElement.GetString() : null;
                var outputPath = entry.TryGetProperty("outputPath", out var outputPathElement) ? outputPathElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(scheduleName) || string.IsNullOrWhiteSpace(outputPath))
                {
                    blockReason = "recorded schedules batch-export manifest has an incomplete entry";
                    return false;
                }

                var fullOutputPath = Path.GetFullPath(outputPath);
                if (!IsPathInsideDirectory(fullOutputPath, outputDir))
                {
                    blockReason = "recorded schedules batch-export manifest output is outside --output-dir";
                    return false;
                }

                parsed.Add(new ScheduleBatchExportReplayEntry(scheduleName, category, fullOutputPath));
            }

            if (parsed.Count == 0)
            {
                blockReason = "recorded schedules batch-export manifest has no successful entries";
                return false;
            }

            entries = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            blockReason = $"recorded schedules batch-export manifest is unreadable: {ex.Message}";
            return false;
        }
    }

    private static async Task<string?> ApplyScheduleBatchExportReplayAsync(
        RevitClient client,
        ScheduleBatchExportReplayRequest request)
    {
        var list = await client.ListSchedulesAsync();
        if (!list.Success)
            return list.Error ?? "Schedule list failed.";

        var schedules = list.Data ?? Array.Empty<ScheduleInfo>();
        foreach (var entry in request.Entries)
        {
            var match = schedules.FirstOrDefault(schedule =>
                string.Equals(schedule.Name, entry.ScheduleName, StringComparison.OrdinalIgnoreCase));
            var exportRequest = new ScheduleExportRequest
            {
                ExistingName = match == null ? null : entry.ScheduleName,
                Category = match == null ? entry.Category : null,
            };
            var export = await client.ExportScheduleAsync(exportRequest);
            if (!export.Success)
                return export.Error ?? $"Schedule export replay failed for '{entry.ScheduleName}'.";

            var data = export.Data ?? new ScheduleData();
            var directory = Path.GetDirectoryName(entry.OutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(entry.OutputPath, FormatScheduleReplayCsv(data));
        }

        return null;
    }

    private static string FormatScheduleReplayCsv(ScheduleData data)
    {
        var lines = new List<string> { string.Join(",", data.Columns.Select(EscapeScheduleReplayCsvField)) };
        lines.AddRange(data.Rows.Select(row => string.Join(",", data.Columns.Select(column =>
            EscapeScheduleReplayCsvField(row.TryGetValue(column, out var value) ? value : "")))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeScheduleReplayCsvField(string value) =>
        value.Contains(',', StringComparison.Ordinal) ||
        value.Contains('"', StringComparison.Ordinal) ||
        value.Contains('\n', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !string.IsNullOrWhiteSpace(relative) &&
            !relative.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private sealed record ScheduleBatchExportReplayRequest(
        string Set,
        string OutputDir,
        string Format,
        string ManifestPath,
        IReadOnlyList<ScheduleBatchExportReplayEntry> Entries);

    private sealed record ScheduleBatchExportReplayEntry(
        string ScheduleName,
        string? Category,
        string OutputPath);

    private static LedgerTimelineReport BuildTimelineReport(
        LedgerQueryReport queryReport,
        string bucket,
        IReadOnlyList<string>? projectDirectories = null,
        IReadOnlyList<LedgerStatsCount>? byProject = null)
    {
        var operations = queryReport.Operations
            .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
            .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Line ?? 0)
            .ToList();
        var issues = queryReport.Issues
            .Concat(operations.SelectMany(operation => operation.Issues))
            .ToList();
        var unbucketedOperationCount = 0;
        var grouped = new SortedDictionary<DateTimeOffset, List<LedgerOperation>>();

        foreach (var operation in operations)
        {
            var parsed = ParseTimestamp(operation.Timestamp);
            if (!parsed.HasValue)
            {
                unbucketedOperationCount++;
                issues.Add(new LedgerIssue(
                    "warning",
                    operation.Source,
                    FirstNonEmpty(operation.ArtifactPath, operation.ReceiptPath),
                    operation.Line,
                    $"operation timestamp is missing or not ISO 8601 with explicit UTC offset for timeline bucket: {Format(operation.Timestamp)}"));
                continue;
            }

            var bucketStart = GetBucketStart(parsed.Value, bucket);
            if (!grouped.TryGetValue(bucketStart, out var bucketOperations))
            {
                bucketOperations = new List<LedgerOperation>();
                grouped[bucketStart] = bucketOperations;
            }

            bucketOperations.Add(operation);
        }

        var buckets = grouped
            .Select(item =>
            {
                var bucketOperations = item.Value
                    .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
                    .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
                    .ThenBy(operation => operation.Line ?? 0)
                    .ToList();
                var bucketIssues = bucketOperations.SelectMany(operation => operation.Issues).ToList();
                return new LedgerTimelineBucket
                {
                    BucketStartUtc = item.Key.ToString("o", CultureInfo.InvariantCulture),
                    BucketEndUtc = GetBucketEnd(item.Key, bucket).ToString("o", CultureInfo.InvariantCulture),
                    OperationCount = bucketOperations.Count,
                    IssueCount = bucketIssues.Count,
                    ErrorIssueCount = bucketIssues.Count(issue => IsError(issue.Severity)),
                    WarningIssueCount = bucketIssues.Count(issue => IsWarning(issue.Severity)),
                    MissingReceiptCount = bucketOperations.Count(operation => operation.ReceiptStatus == "missing"),
                    UnreadableReceiptCount = bucketOperations.Count(operation => operation.ReceiptStatus == "unreadable"),
                    BySource = CountBy(bucketOperations.Select(operation => operation.Source)),
                    ByAction = CountBy(bucketOperations.Select(operation => operation.Action)),
                    ByCategory = CountBy(bucketOperations.Select(operation => operation.Category)),
                    ByOperator = CountBy(bucketOperations.Select(operation => operation.Operator)),
                    ByReceiptStatus = CountBy(bucketOperations.Select(operation => operation.ReceiptStatus)),
                    IssuesBySeverity = CountBy(bucketIssues.Select(issue => NormalizeSeverity(issue.Severity))),
                };
            })
            .ToList();

        return new LedgerTimelineReport
        {
            GeneratedAt = queryReport.GeneratedAt,
            ProjectDirectory = queryReport.ProjectDirectory,
            ProjectDirectories = projectDirectories?.ToList() ?? new List<string> { queryReport.ProjectDirectory },
            Query = new LedgerTimelineQuerySpec
            {
                Source = queryReport.Query.Source,
                SinceUtc = queryReport.Query.SinceUtc,
                UntilUtc = queryReport.Query.UntilUtc,
                Window = queryReport.Query.Window,
                Action = queryReport.Query.Action,
                Category = queryReport.Query.Category,
                Operator = queryReport.Query.Operator,
                ReceiptStatus = queryReport.Query.ReceiptStatus,
                Bucket = bucket,
            },
            Summary = new LedgerTimelineSummary
            {
                OperationCount = operations.Count,
                BucketCount = buckets.Count,
                IssueCount = issues.Count,
                ErrorIssueCount = issues.Count(issue => IsError(issue.Severity)),
                WarningIssueCount = issues.Count(issue => IsWarning(issue.Severity)),
                MissingReceiptCount = operations.Count(operation => operation.ReceiptStatus == "missing"),
                UnreadableReceiptCount = operations.Count(operation => operation.ReceiptStatus == "unreadable"),
                UnbucketedOperationCount = unbucketedOperationCount,
                FirstBucket = buckets.Count == 0 ? null : buckets.First().BucketStartUtc,
                LastBucket = buckets.Count == 0 ? null : buckets.Last().BucketStartUtc,
            },
            ByProject = byProject?.OrderBy(count => count.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<LedgerStatsCount>(),
            IssuesBySeverity = CountBy(issues.Select(issue => NormalizeSeverity(issue.Severity))),
            Buckets = buckets,
            Issues = issues
                .OrderBy(issue => NormalizeSeverity(issue.Severity), StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Line ?? 0)
                .ThenBy(issue => issue.Message, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static DateTimeOffset GetBucketStart(DateTimeOffset timestamp, string bucket)
    {
        var utc = timestamp.ToUniversalTime();
        return bucket == "hour"
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset GetBucketEnd(DateTimeOffset bucketStart, string bucket) =>
        bucket == "hour" ? bucketStart.AddHours(1) : bucketStart.AddDays(1);

    private static string Render(LedgerQueryReport report, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderMarkdown(report),
            _ => RenderTable(report),
        };

    private static string RenderAppend(LedgerAppendResult result, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(result, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderAppendMarkdown(result),
            _ => RenderAppendTable(result),
        };

    private static string RenderAppendTable(LedgerAppendResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger append");
        sb.AppendLine($"Project: {result.ProjectDirectory}");
        sb.AppendLine($"Ledger: {result.LedgerPath}");
        sb.AppendLine($"Written: {result.Written.ToString().ToLowerInvariant()}; dryRun={result.DryRun.ToString().ToLowerInvariant()}");
        sb.AppendLine($"{Format(result.Operation.Timestamp),-28} {result.Operation.Action,-28} {Format(result.Operation.Status),-10} {Format(result.Operation.ReceiptStatus),-10} {Format(result.Operation.ArtifactPath)}");
        if (result.DryRun)
            sb.AppendLine("Re-run with --yes to append this record.");
        return sb.ToString().TrimEnd();
    }

    private static string RenderAppendMarkdown(LedgerAppendResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Append");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(result.ProjectDirectory)}`");
        sb.AppendLine($"- Ledger: `{EscapeInlineCode(result.LedgerPath)}`");
        sb.AppendLine($"- Written: `{result.Written.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Dry run: `{result.DryRun.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Action: `{EscapeInlineCode(result.Operation.Action)}`");
        sb.AppendLine($"- Status: `{EscapeInlineCode(Format(result.Operation.Status))}`");
        sb.AppendLine($"- Receipt status: `{EscapeInlineCode(result.Operation.ReceiptStatus)}`");
        if (result.DryRun)
            sb.AppendLine("- Re-run with `--yes` to append this record.");
        return sb.ToString().TrimEnd();
    }

    private static string RenderTable(LedgerQueryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger query");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        sb.AppendLine($"Operations: {report.Summary.TotalOperations}; issues={report.Summary.IssueCount}");
        foreach (var operation in report.Operations)
        {
            sb.AppendLine($"{Format(operation.Timestamp),-28} {operation.Source,-10} {operation.Action,-28} {Format(operation.ReceiptStatus),-10} {Format(operation.ArtifactPath)}");
        }

        if (report.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in report.Issues)
                sb.AppendLine($"  - {issue.Severity.ToUpperInvariant()} {issue.Source}: {issue.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderStats(LedgerStatsReport report, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderStatsMarkdown(report),
            _ => RenderStatsTable(report),
        };

    private static string RenderReplay(LedgerReplayReport report, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderReplayMarkdown(report),
            _ => RenderReplayTable(report),
        };

    private static string RenderTimeline(LedgerTimelineReport report, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderTimelineMarkdown(report),
            _ => RenderTimelineTable(report),
        };

    private static string RenderTimelineTable(LedgerTimelineReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger timeline");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        if (report.ProjectDirectories.Count > 1)
            sb.AppendLine($"Projects: {report.ProjectDirectories.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Bucket: {report.Query.Bucket}; operations={report.Summary.OperationCount}; buckets={report.Summary.BucketCount}; issues={report.Summary.IssueCount}; unbucketed={report.Summary.UnbucketedOperationCount}");
        AppendStatsGroup(sb, "By project", report.ByProject);
        AppendStatsGroup(sb, "Issues by severity", report.IssuesBySeverity);
        foreach (var bucket in report.Buckets)
        {
            sb.AppendLine($"{bucket.BucketStartUtc,-28} bucketEnd={bucket.BucketEndUtc} operations={bucket.OperationCount.ToString(CultureInfo.InvariantCulture),-4} missingReceipts={bucket.MissingReceiptCount.ToString(CultureInfo.InvariantCulture),-3} unreadableReceipts={bucket.UnreadableReceiptCount.ToString(CultureInfo.InvariantCulture),-3} sources={JoinCounts(bucket.BySource)} actions={JoinCounts(bucket.ByAction)} categories={JoinCounts(bucket.ByCategory)} operators={JoinCounts(bucket.ByOperator)} receipts={JoinCounts(bucket.ByReceiptStatus)} issues={bucket.IssueCount.ToString(CultureInfo.InvariantCulture)} issueSeverity={JoinCounts(bucket.IssuesBySeverity)}");
        }

        if (report.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in report.Issues)
                sb.AppendLine($"  - {issue.Severity.ToUpperInvariant()} {issue.Source}: {issue.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderReplayTable(LedgerReplayReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger replay preview");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        sb.AppendLine($"Source: {report.Source}; dryRun={report.DryRun.ToString().ToLowerInvariant()}; applySupported={report.ApplySupported.ToString().ToLowerInvariant()}");
        sb.AppendLine($"Steps: {report.Summary.StepCount}; blocked={report.Summary.BlockedStepCount}; issues={report.Summary.IssueCount}");
        foreach (var step in report.Steps)
        {
            sb.AppendLine($"{step.Index,3} {Format(step.Timestamp),-28} {step.Source,-10} {step.Action,-28} {step.ReplayMode,-8} apply={step.CanApply.ToString().ToLowerInvariant()}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderReplayMarkdown(LedgerReplayReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Replay Preview");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        sb.AppendLine($"- Source: `{EscapeInlineCode(report.Source)}`");
        sb.AppendLine($"- Dry run: `{report.DryRun.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Apply supported: `{report.ApplySupported.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Steps: `{report.Summary.StepCount}`");
        sb.AppendLine();
        sb.AppendLine("| # | Timestamp | Source | Action | Can apply |");
        sb.AppendLine("| -: | --- | --- | --- | --- |");
        foreach (var step in report.Steps)
        {
            sb.AppendLine($"| {step.Index.ToString(CultureInfo.InvariantCulture)} | {EscapeMarkdownText(Format(step.Timestamp))} | {EscapeMarkdownText(step.Source)} | {EscapeMarkdownText(step.Action)} | {step.CanApply.ToString().ToLowerInvariant()} |");
        }
        return sb.ToString().TrimEnd();
    }

    private static string RenderTimelineMarkdown(LedgerTimelineReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Timeline");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        if (report.ProjectDirectories.Count > 1)
            sb.AppendLine($"- Projects: `{report.ProjectDirectories.Count.ToString(CultureInfo.InvariantCulture)}`");
        sb.AppendLine($"- Bucket: `{EscapeInlineCode(report.Query.Bucket)}`");
        sb.AppendLine($"- Operations: `{report.Summary.OperationCount}`");
        sb.AppendLine($"- Buckets: `{report.Summary.BucketCount}`");
        sb.AppendLine($"- Issues: `{report.Summary.IssueCount}`");
        sb.AppendLine($"- Unbucketed operations: `{report.Summary.UnbucketedOperationCount}`");
        sb.AppendLine();
        AppendStatsMarkdownTable(sb, "By Project", report.ByProject);
        sb.AppendLine("| Bucket start UTC | Bucket end UTC | Operations | Sources | Actions | Categories | Operators | Receipt status | Issues |");
        sb.AppendLine("|---|---|---:|---|---|---|---|---|---:|");
        if (report.Buckets.Count == 0)
        {
            sb.AppendLine("| none | none | 0 | none | none | none | none | none | 0 |");
        }
        else
        {
            foreach (var bucket in report.Buckets)
            {
                sb.AppendLine($"| {EscapeTableCell(bucket.BucketStartUtc)} | {EscapeTableCell(bucket.BucketEndUtc)} | {bucket.OperationCount.ToString(CultureInfo.InvariantCulture)} | {EscapeTableCell(JoinCounts(bucket.BySource))} | {EscapeTableCell(JoinCounts(bucket.ByAction))} | {EscapeTableCell(JoinCounts(bucket.ByCategory))} | {EscapeTableCell(JoinCounts(bucket.ByOperator))} | {EscapeTableCell(JoinCounts(bucket.ByReceiptStatus))} | {bucket.IssueCount.ToString(CultureInfo.InvariantCulture)} |");
            }
        }

        sb.AppendLine();
        AppendStatsMarkdownTable(sb, "Issues By Severity", report.IssuesBySeverity);
        sb.AppendLine("## Issues");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
                sb.AppendLine($"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(issue.Source)}`: {EscapeMarkdownText(issue.Message)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string JoinCounts(IReadOnlyList<LedgerStatsCount> counts) =>
        counts.Count == 0
            ? "none"
            : string.Join(", ", counts.Select(count => $"{count.Name}={count.Count.ToString(CultureInfo.InvariantCulture)}"));

    private static string RenderStatsTable(LedgerStatsReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger stats");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        if (report.ProjectDirectories.Count > 1)
            sb.AppendLine($"Projects: {report.ProjectDirectories.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Operations: {report.Summary.OperationCount}; issues={report.Summary.IssueCount}; missingReceipts={report.Summary.MissingReceiptCount}; unreadableReceipts={report.Summary.UnreadableReceiptCount}");
        AppendStatsGroup(sb, "By project", report.ByProject);
        AppendStatsGroup(sb, "By source", report.BySource);
        AppendStatsGroup(sb, "By action", report.ByAction);
        AppendStatsGroup(sb, "By category", report.ByCategory);
        AppendStatsGroup(sb, "By operator", report.ByOperator);
        AppendStatsGroup(sb, "By receipt", report.ByReceiptStatus);
        AppendStatsGroup(sb, "Issues by source", report.IssuesBySource);
        AppendStatsGroup(sb, "Issues by severity", report.IssuesBySeverity);
        return sb.ToString().TrimEnd();
    }

    private static void AppendStatsGroup(StringBuilder sb, string title, IReadOnlyList<LedgerStatsCount> counts)
    {
        sb.AppendLine(title + ":");
        if (counts.Count == 0)
        {
            sb.AppendLine("  - none");
            return;
        }

        foreach (var count in counts)
            sb.AppendLine($"  - {count.Name}: {count.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string RenderStatsMarkdown(LedgerStatsReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Stats");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        if (report.ProjectDirectories.Count > 1)
            sb.AppendLine($"- Projects: `{report.ProjectDirectories.Count.ToString(CultureInfo.InvariantCulture)}`");
        sb.AppendLine($"- Operations: `{report.Summary.OperationCount}`");
        sb.AppendLine($"- Issues: `{report.Summary.IssueCount}`");
        sb.AppendLine($"- Missing receipts: `{report.Summary.MissingReceiptCount}`");
        sb.AppendLine($"- Unreadable receipts: `{report.Summary.UnreadableReceiptCount}`");
        sb.AppendLine();
        AppendStatsMarkdownTable(sb, "By Project", report.ByProject);
        AppendStatsMarkdownTable(sb, "By Source", report.BySource);
        AppendStatsMarkdownTable(sb, "By Action", report.ByAction);
        AppendStatsMarkdownTable(sb, "By Category", report.ByCategory);
        AppendStatsMarkdownTable(sb, "By Operator", report.ByOperator);
        AppendStatsMarkdownTable(sb, "By Receipt Status", report.ByReceiptStatus);
        AppendStatsMarkdownTable(sb, "Issues By Source", report.IssuesBySource);
        AppendStatsMarkdownTable(sb, "Issues By Severity", report.IssuesBySeverity);
        return sb.ToString().TrimEnd();
    }

    private static void AppendStatsMarkdownTable(StringBuilder sb, string title, IReadOnlyList<LedgerStatsCount> counts)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine("| Name | Count |");
        sb.AppendLine("|---|---:|");
        if (counts.Count == 0)
        {
            sb.AppendLine("| none | 0 |");
        }
        else
        {
            foreach (var count in counts)
                sb.AppendLine($"| {EscapeTableCell(count.Name)} | {count.Count.ToString(CultureInfo.InvariantCulture)} |");
        }

        sb.AppendLine();
    }

    private static string RenderMarkdown(LedgerQueryReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Query");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        sb.AppendLine($"- Operations: `{report.Summary.TotalOperations}`");
        sb.AppendLine($"- Issues: `{report.Summary.IssueCount}`");
        sb.AppendLine();
        sb.AppendLine("| Timestamp | Source | Action | Receipt | Artifact |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var operation in report.Operations)
        {
            sb.AppendLine($"| {EscapeTableCell(Format(operation.Timestamp))} | {EscapeTableCell(operation.Source)} | {EscapeTableCell(operation.Action)} | {EscapeTableCell(Format(operation.ReceiptStatus))} | {EscapeTableCell(Format(operation.ArtifactPath))} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Issues");
        if (report.Issues.Count == 0 && report.Operations.All(operation => operation.Issues.Count == 0))
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues.Concat(report.Operations.SelectMany(operation => operation.Issues)))
                sb.AppendLine($"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(issue.Source)}`: {EscapeMarkdownText(issue.Message)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static LedgerValidationReport BuildValidationReport(LedgerQueryReport queryReport, string failOn)
    {
        var validation = new LedgerValidationReport
        {
            GeneratedAt = queryReport.GeneratedAt,
            ProjectDirectory = queryReport.ProjectDirectory,
            Source = queryReport.Query.Source,
            FailOn = failOn,
            Query = queryReport.Query,
        };

        foreach (var issue in queryReport.Issues)
        {
            var severity = NormalizeSeverity(issue.Severity);
            var code = "source.issue";
            if (IsMissingSourceIssue(issue))
            {
                code = "source.missing";
                if (!string.Equals(queryReport.Query.Source, "all", StringComparison.OrdinalIgnoreCase))
                    severity = "error";
            }
            else if (IsError(severity))
            {
                code = "source.unreadable";
            }

            AddValidationIssue(
                validation,
                severity,
                code,
                issue.Source,
                issue.Path,
                issue.Line,
                issue.Message,
                operationAction: null);
        }

        foreach (var operation in queryReport.Operations
                     .OrderBy(operation => ParseTimestamp(operation.Timestamp) ?? DateTimeOffset.MinValue)
                     .ThenBy(operation => operation.Source, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(operation => operation.ArtifactPath ?? "", StringComparer.OrdinalIgnoreCase)
                     .ThenBy(operation => operation.Line ?? 0)
                     .ThenBy(operation => operation.Action, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var issue in operation.Issues)
            {
                AddValidationIssue(
                    validation,
                    issue.Severity,
                    "operation.issue",
                    issue.Source,
                    issue.Path,
                    issue.Line,
                    issue.Message,
                    operation.Action);
            }

            if (!string.IsNullOrWhiteSpace(operation.Timestamp) && ParseTimestamp(operation.Timestamp) == null)
            {
                AddValidationIssue(
                    validation,
                    "warning",
                    "timestamp.invalid",
                    operation.Source,
                    operation.ArtifactPath,
                    operation.Line,
                    $"operation timestamp is not ISO 8601 with explicit UTC offset: {operation.Timestamp}",
                    operation.Action);
            }

            if (!string.IsNullOrWhiteSpace(operation.ArtifactPath) && !PathExists(operation.ArtifactPath!))
            {
                AddValidationIssue(
                    validation,
                    "error",
                    "artifact.missing",
                    operation.Source,
                    operation.ArtifactPath,
                    operation.Line,
                    $"operation artifact does not exist: {operation.ArtifactPath}",
                    operation.Action);
            }

            foreach (var link in operation.EvidenceLinks
                         .Where(link => !string.IsNullOrWhiteSpace(link))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(link => link, StringComparer.OrdinalIgnoreCase))
            {
                if (!PathExists(link))
                {
                    AddValidationIssue(
                        validation,
                        "error",
                        "evidence.missing",
                        operation.Source,
                        link,
                        operation.Line,
                        $"evidence link does not exist: {link}",
                        operation.Action);
                }
            }

            if (operation.ReceiptStatus is "missing" or "unreadable" &&
                !string.IsNullOrWhiteSpace(operation.ReceiptStatus))
            {
                AddValidationIssue(
                    validation,
                    "error",
                    operation.ReceiptStatus == "missing" ? "receipt.missing" : "receipt.unreadable",
                    operation.Source,
                    FirstNonEmpty(operation.ReceiptPath, operation.ArtifactPath),
                    operation.Line,
                    $"operation receipt status is {operation.ReceiptStatus}",
                    operation.Action);
            }

            if (!string.IsNullOrWhiteSpace(operation.ReceiptHash) &&
                operation.Issues.Any(issue => issue.Message.Contains("receiptHash", StringComparison.OrdinalIgnoreCase)))
            {
                AddValidationIssue(
                    validation,
                    "error",
                    "receipt.hash",
                    operation.Source,
                    FirstNonEmpty(operation.ReceiptPath, operation.ArtifactPath),
                    operation.Line,
                    "operation receiptHash does not match the referenced receipt file",
                    operation.Action);
            }
        }

        var sourceIssues = validation.Issues
            .Where(issue => issue.Code is "source.issue" or "source.missing" or "source.unreadable")
            .ToArray();
        validation.Checks.Add(CheckValidation(
            "sources-readable",
            sourceIssues.Length == 0,
            sourceIssues.Any(issue => IsError(issue.Severity)) ? "error" : "warning",
            "Ledger sources are readable.",
            "One or more ledger sources are missing or failed to read."));
        validation.Checks.Add(CheckValidation(
            "artifact-links",
            validation.Issues.All(issue => issue.Code is not ("artifact.missing" or "evidence.missing")),
            "error",
            "Operation artifacts and evidence links resolve locally.",
            "One or more operation artifacts or evidence links are missing."));
        validation.Checks.Add(CheckValidation(
            "receipt-status",
            validation.Issues.All(issue => issue.Code is not ("receipt.missing" or "receipt.unreadable")),
            "error",
            "Receipt references are present and readable when required.",
            "One or more receipt references are missing or unreadable."));
        validation.Checks.Add(CheckValidation(
            "receipt-hashes",
            validation.Issues.All(issue => issue.Code != "receipt.hash"),
            "error",
            "Declared receipt hashes match referenced receipt files when present.",
            "One or more declared receipt hashes do not match referenced receipt files."));
        validation.Checks.Add(CheckValidation(
            "timestamp-format",
            validation.Issues.All(issue => issue.Code != "timestamp.invalid"),
            "warning",
            "Operation timestamps are parseable when present.",
            "One or more operation timestamps are not parseable."));

        validation.Summary = new LedgerValidationSummary
        {
            OperationCount = queryReport.Operations.Count,
            CheckCount = validation.Checks.Count,
            IssueCount = validation.Issues.Count,
            ErrorCount = validation.Issues.Count(issue => IsError(issue.Severity)),
            WarningCount = validation.Issues.Count(issue => IsWarning(issue.Severity)),
            MissingReceiptCount = queryReport.Operations.Count(operation => operation.ReceiptStatus == "missing"),
            UnreadableReceiptCount = queryReport.Operations.Count(operation => operation.ReceiptStatus == "unreadable"),
            MissingArtifactCount = validation.Issues.Count(issue => issue.Code is "artifact.missing" or "evidence.missing"),
        };
        validation.Valid = failOn == "warning"
            ? validation.Summary.ErrorCount == 0 && validation.Summary.WarningCount == 0
            : validation.Summary.ErrorCount == 0;
        return validation;
    }

    private static void AddValidationIssue(
        LedgerValidationReport report,
        string severity,
        string code,
        string source,
        string? path,
        int? line,
        string message,
        string? operationAction)
    {
        var issue = new LedgerValidationIssue(
            NormalizeSeverity(severity),
            code,
            source,
            path ?? "",
            line,
            message,
            operationAction);
        if (report.Issues.Any(existing =>
                string.Equals(existing.Severity, issue.Severity, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Code, issue.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Source, issue.Source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Path, issue.Path, StringComparison.OrdinalIgnoreCase) &&
                existing.Line == issue.Line &&
                string.Equals(existing.Message, issue.Message, StringComparison.Ordinal)))
        {
            return;
        }

        report.Issues.Add(issue);
    }

    private static LedgerValidationCheck CheckValidation(
        string id,
        bool pass,
        string severity,
        string passEvidence,
        string failEvidence) =>
        new(id, pass ? "pass" : NormalizeSeverity(severity), pass ? passEvidence : failEvidence);

    private static bool IsMissingSourceIssue(LedgerIssue issue) =>
        issue.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private static string RenderValidation(LedgerValidationReport report, string format) =>
        format switch
        {
            "json" => JsonSerializer.Serialize(report, TerminalJsonOptions.PrettyCamel),
            "markdown" => RenderValidationMarkdown(report),
            _ => RenderValidationTable(report),
        };

    private static string RenderValidationTable(LedgerValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ledger validation");
        sb.AppendLine($"Project: {report.ProjectDirectory}");
        sb.AppendLine($"Valid: {report.Valid.ToString().ToLowerInvariant()}; operations={report.Summary.OperationCount}; errors={report.Summary.ErrorCount}; warnings={report.Summary.WarningCount}");
        foreach (var check in report.Checks)
            sb.AppendLine($"{check.Status,-8} {check.Id,-18} {check.Evidence}");
        if (report.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var issue in report.Issues)
                sb.AppendLine($"  - {issue.Severity.ToUpperInvariant()} {issue.Code} {issue.Source}: {issue.Message}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderValidationMarkdown(LedgerValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# RevitCli Ledger Validation");
        sb.AppendLine();
        sb.AppendLine($"- Project: `{EscapeInlineCode(report.ProjectDirectory)}`");
        sb.AppendLine($"- Valid: `{report.Valid.ToString().ToLowerInvariant()}`");
        sb.AppendLine($"- Operations: `{report.Summary.OperationCount}`");
        sb.AppendLine($"- Errors: `{report.Summary.ErrorCount}`");
        sb.AppendLine($"- Warnings: `{report.Summary.WarningCount}`");
        sb.AppendLine();
        sb.AppendLine("| Status | Check | Evidence |");
        sb.AppendLine("|---|---|---|");
        foreach (var check in report.Checks)
            sb.AppendLine($"| `{EscapeTableCell(check.Status)}` | `{EscapeTableCell(check.Id)}` | {EscapeTableCell(check.Evidence)} |");
        sb.AppendLine();
        sb.AppendLine("## Issues");
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("- None.");
        }
        else
        {
            foreach (var issue in report.Issues)
                sb.AppendLine($"- `{EscapeInlineCode(issue.Severity.ToUpperInvariant())}` `{EscapeInlineCode(issue.Code)}` `{EscapeInlineCode(issue.Source)}`: {EscapeMarkdownText(issue.Message)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static bool TryNormalizeSource(string? source, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim().ToLowerInvariant();
        if (normalized == "delivery")
            normalized = "deliveries";
        if (normalized == "workflow")
            normalized = "workflows";
        return normalized is "all" or "ledger" or "journal" or "history" or "deliveries" or "workflows";
    }

    private static bool TryNormalizeAppendStatus(string? status, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(status) ? "succeeded" : status.Trim().ToLowerInvariant();
        return normalized is "planned" or "succeeded" or "failed" or "blocked";
    }

    private static bool TryNormalizeReceiptStatus(string? receiptStatus, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(receiptStatus) ? "all" : receiptStatus.Trim().ToLowerInvariant();
        return normalized is "all" or "valid" or "missing" or "unreadable";
    }

    private static bool TryNormalizeFailOn(string? failOn, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(failOn) ? "error" : failOn.Trim().ToLowerInvariant();
        return normalized is "error" or "warning";
    }

    private static bool TryNormalizeBucket(string? bucket, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(bucket) ? "day" : bucket.Trim().ToLowerInvariant();
        return normalized is "day" or "hour";
    }

    private static bool TryNormalizeOutput(string? output, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(output) ? "table" : output.Trim().ToLowerInvariant();
        if (normalized == "md")
            normalized = "markdown";
        return normalized is "table" or "json" or "markdown";
    }

    private static bool TryBuildWindow(
        string? since,
        string? until,
        string? window,
        DateTimeOffset now,
        out DateTimeOffset? sinceUtc,
        out DateTimeOffset? untilUtc,
        out string error)
    {
        sinceUtc = null;
        untilUtc = null;
        error = "";

        if (!string.IsNullOrWhiteSpace(since) && !TryParseTimestamp(since, out sinceUtc))
        {
            error = "--since must be an ISO 8601 timestamp with explicit UTC offset (Z or +/-HH:mm).";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(until) && !TryParseTimestamp(until, out untilUtc))
        {
            error = "--until must be an ISO 8601 timestamp with explicit UTC offset (Z or +/-HH:mm).";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(window))
        {
            try
            {
                var span = HistoryCommand.ParseWindow(window);
                untilUtc ??= now;
                sinceUtc = untilUtc.Value - span;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        if (sinceUtc.HasValue && untilUtc.HasValue && sinceUtc > untilUtc)
        {
            error = "--since must be before --until.";
            return false;
        }

        return true;
    }

    private static bool IncludesSource(string selected, string source) =>
        string.Equals(selected, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(selected, source, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        string.Equals(value, filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesReceiptStatus(string? value, string filter) =>
        string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);

    private static bool IsInWindow(string? timestamp, DateTimeOffset? since, DateTimeOffset? until, bool keepInvalidTimestamp = false)
    {
        var parsed = ParseTimestamp(timestamp);
        if (!parsed.HasValue)
            return keepInvalidTimestamp || (since == null && until == null);
        return (!since.HasValue || parsed.Value >= since.Value) &&
               (!until.HasValue || parsed.Value <= until.Value);
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset? timestamp)
    {
        timestamp = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var trimmed = value.Trim();
        if (!HasExplicitUtcOffset(trimmed))
            return false;
        if (!IsIso8601TimestampWithOffsetShape(trimmed))
            return false;
        if (!DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        timestamp = parsed.ToUniversalTime();
        return true;
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        TryParseTimestamp(value, out var timestamp) ? timestamp : null;

    private static bool HasExplicitUtcOffset(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Length < 6)
            return false;

        var offset = value[^6..];
        return (offset[0] == '+' || offset[0] == '-') &&
               char.IsDigit(offset[1]) &&
               char.IsDigit(offset[2]) &&
               offset[3] == ':' &&
               char.IsDigit(offset[4]) &&
               char.IsDigit(offset[5]);
    }

    private static bool IsIso8601TimestampWithOffsetShape(string value)
    {
        if (value.Length is not (20 or 25) && !HasFractionalIso8601TimestampWithOffsetShape(value))
            return false;

        if (value.Length >= 19 &&
            IsDateTimePrefix(value) &&
            (value[19] == 'Z' || value.Length > 19 && value[19] is '+' or '-'))
        {
            return value.Length == 20 || value.Length == 25;
        }

        return HasFractionalIso8601TimestampWithOffsetShape(value);
    }

    private static bool HasFractionalIso8601TimestampWithOffsetShape(string value)
    {
        if (value.Length < 22 || !IsDateTimePrefix(value) || value[19] != '.')
            return false;

        var offsetIndex = value.IndexOfAny(new[] { 'Z', '+', '-' }, 20);
        if (offsetIndex <= 20)
            return false;

        for (var i = 20; i < offsetIndex; i++)
        {
            if (!char.IsDigit(value[i]))
                return false;
        }

        if (value[offsetIndex] == 'Z')
            return offsetIndex == value.Length - 1;

        return offsetIndex == value.Length - 6;
    }

    private static bool IsDateTimePrefix(string value) =>
        value.Length >= 19 &&
        IsDigitRun(value, 0, 4) &&
        value[4] == '-' &&
        IsDigitRun(value, 5, 2) &&
        value[7] == '-' &&
        IsDigitRun(value, 8, 2) &&
        value[10] == 'T' &&
        IsDigitRun(value, 11, 2) &&
        value[13] == ':' &&
        IsDigitRun(value, 14, 2) &&
        value[16] == ':' &&
        IsDigitRun(value, 17, 2);

    private static bool IsDigitRun(string value, int start, int length)
    {
        if (start + length > value.Length)
            return false;
        for (var i = start; i < start + length; i++)
        {
            if (!char.IsDigit(value[i]))
                return false;
        }

        return true;
    }

    private static string DeliveryReceiptStatus(DeliveryManifestEntry entry)
    {
        if (!entry.ReceiptExists)
            return "missing";
        if (!entry.ReceiptReadable)
            return "unreadable";
        return "valid";
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatNullable(bool? value) =>
        value.HasValue ? value.Value.ToString().ToLowerInvariant() : "n/a";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string GetOperationsLedgerPath(string projectRoot) =>
        Path.Combine(projectRoot, ".revitcli", "ledger", "operations.jsonl");

    private static string? ResolveOptionalProjectPath(string projectRoot, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : ResolveProjectPath(projectRoot, path);

    private static string ResolveProjectPath(string projectRoot, string path) =>
        Path.IsPathRooted(path.Trim())
            ? Path.GetFullPath(path.Trim())
            : Path.GetFullPath(Path.Combine(projectRoot, path.Trim()));

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(c =>
            c is >= '0' and <= '9' ||
            c is >= 'a' and <= 'f' ||
            c is >= 'A' and <= 'F');

    private static List<string> BuildAppendArgs(
        string action,
        string? category,
        string operatorName,
        string status,
        string? summary,
        string timestamp,
        string? model,
        string? modelPath,
        string? revitVersion,
        string? planHash,
        string? artifactPath,
        string? receiptPath,
        string? receiptHash,
        string? rollbackPointer,
        IReadOnlyList<string> evidenceLinks,
        bool yes)
    {
        var args = new List<string> { "--action", action, "--status", status, "--timestamp", timestamp, "--operator", operatorName };
        AddArg(args, "--category", category);
        AddArg(args, "--summary", summary);
        AddArg(args, "--model", model);
        AddArg(args, "--model-path", modelPath);
        AddArg(args, "--revit-version", revitVersion);
        AddArg(args, "--plan-hash", planHash);
        AddArg(args, "--artifact-path", artifactPath);
        AddArg(args, "--receipt", receiptPath);
        AddArg(args, "--receipt-hash", receiptHash);
        AddArg(args, "--rollback-pointer", rollbackPointer);
        foreach (var evidence in evidenceLinks)
            AddArg(args, "--evidence", evidence);
        if (yes)
            args.Add("--yes");
        return args;
    }

    private static void AddArg(List<string> args, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        args.Add(name);
        args.Add(value);
    }

    private static List<AppendLedgerCheck> BuildAppendChecks(AppendLedgerRecord record, bool yes) =>
        new()
        {
            new("action-required", "pass", "Action is present."),
            new("status-valid", "pass", $"Status is {record.Status}."),
            new("timestamp-offset", "pass", "Timestamp has an explicit UTC offset."),
            new("approval-required", yes ? "pass" : "pending", yes ? "--yes approval was supplied." : "Dry-run preview only; --yes is required to append."),
            new("receipt-hash-format", string.IsNullOrWhiteSpace(record.ReceiptHash) ? "skipped" : "pass", string.IsNullOrWhiteSpace(record.ReceiptHash) ? "No receipt hash was supplied." : "Receipt hash is a SHA256 hex digest."),
        };

    private static List<AppendLedgerArtifact> BuildAppendArtifacts(string ledgerPath, string? artifactPath, string? receiptPath)
    {
        var artifacts = new List<AppendLedgerArtifact>
        {
            new("ledger", ledgerPath, File.Exists(ledgerPath), null),
        };
        if (!string.IsNullOrWhiteSpace(artifactPath))
            artifacts.Add(new("artifact", artifactPath, File.Exists(artifactPath), File.Exists(artifactPath) ? DeliveryManifestWriter.ComputeSha256Hex(artifactPath) : null));
        if (!string.IsNullOrWhiteSpace(receiptPath))
            artifacts.Add(new("receipt", receiptPath, File.Exists(receiptPath), File.Exists(receiptPath) ? DeliveryManifestWriter.ComputeSha256Hex(receiptPath) : null));
        return artifacts
            .OrderBy(artifact => artifact.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComputeAppendOperationId(AppendLedgerRecord record)
    {
        var material = string.Join(
            "\u001f",
            record.SchemaVersion,
            record.Timestamp,
            record.Command,
            string.Join("\u001e", record.Args),
            record.WorkingDirectory,
            record.ModelIdentity,
            record.PlanHash,
            record.ReceiptHash,
            record.Status);
        return "ledger-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
    }

    private static string NormalizeSeverity(string? severity) =>
        string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase)
            ? "error"
            : string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "info";

    private static bool IsError(string? severity) =>
        string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarning(string? severity) =>
        string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase);

    private static string Format(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "n/a" : value!;

    private static string EscapeInlineCode(string? value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("`", "\\`", StringComparison.Ordinal);

    private static string EscapeMarkdownText(string? value) =>
        string.IsNullOrEmpty(value)
            ? ""
            : value
                .Replace("|", "\\|", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeTableCell(string? value) =>
        EscapeMarkdownText(value).Replace("`", "\\`", StringComparison.Ordinal);
}

public sealed class LedgerQueryReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-query.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("query")]
    public LedgerQuerySpec Query { get; set; } = new();

    [JsonPropertyName("summary")]
    public LedgerSummary Summary { get; set; } = new();

    [JsonPropertyName("operations")]
    public List<LedgerOperation> Operations { get; } = new();

    [JsonPropertyName("issues")]
    public List<LedgerIssue> Issues { get; } = new();
}

public sealed class LedgerQuerySpec
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "all";

    [JsonPropertyName("sinceUtc")]
    public string? SinceUtc { get; set; }

    [JsonPropertyName("untilUtc")]
    public string? UntilUtc { get; set; }

    [JsonPropertyName("window")]
    public string? Window { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("receiptStatus")]
    public string ReceiptStatus { get; set; } = "all";

    [JsonPropertyName("limit")]
    public int Limit { get; set; }
}

public sealed class LedgerSummary
{
    [JsonPropertyName("totalOperations")]
    public int TotalOperations { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("bySource")]
    public List<LedgerSourceCount> BySource { get; set; } = new();
}

public sealed record LedgerSourceCount(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("count")] int Count);

public sealed class LedgerStatsReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-stats.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("projectDirectories")]
    public List<string> ProjectDirectories { get; set; } = new();

    [JsonPropertyName("query")]
    public LedgerQuerySpec Query { get; set; } = new();

    [JsonPropertyName("summary")]
    public LedgerStatsSummary Summary { get; set; } = new();

    [JsonPropertyName("bySource")]
    public List<LedgerStatsCount> BySource { get; set; } = new();

    [JsonPropertyName("byProject")]
    public List<LedgerStatsCount> ByProject { get; set; } = new();

    [JsonPropertyName("byAction")]
    public List<LedgerStatsCount> ByAction { get; set; } = new();

    [JsonPropertyName("byCategory")]
    public List<LedgerStatsCount> ByCategory { get; set; } = new();

    [JsonPropertyName("byOperator")]
    public List<LedgerStatsCount> ByOperator { get; set; } = new();

    [JsonPropertyName("byReceiptStatus")]
    public List<LedgerStatsCount> ByReceiptStatus { get; set; } = new();

    [JsonPropertyName("issuesBySource")]
    public List<LedgerStatsCount> IssuesBySource { get; set; } = new();

    [JsonPropertyName("issuesBySeverity")]
    public List<LedgerStatsCount> IssuesBySeverity { get; set; } = new();
}

public sealed class LedgerStatsSummary
{
    [JsonPropertyName("operationCount")]
    public int OperationCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("errorIssueCount")]
    public int ErrorIssueCount { get; set; }

    [JsonPropertyName("warningIssueCount")]
    public int WarningIssueCount { get; set; }

    [JsonPropertyName("missingReceiptCount")]
    public int MissingReceiptCount { get; set; }

    [JsonPropertyName("unreadableReceiptCount")]
    public int UnreadableReceiptCount { get; set; }

    [JsonPropertyName("firstTimestamp")]
    public string? FirstTimestamp { get; set; }

    [JsonPropertyName("lastTimestamp")]
    public string? LastTimestamp { get; set; }
}

public sealed record LedgerStatsCount(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count);

public sealed class LedgerReplayReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-replay.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "ledger";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; } = true;

    [JsonPropertyName("applySupported")]
    public bool ApplySupported { get; set; }

    [JsonPropertyName("summary")]
    public LedgerReplaySummary Summary { get; set; } = new();

    [JsonPropertyName("steps")]
    public List<LedgerReplayStep> Steps { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<LedgerIssue> Issues { get; set; } = new();
}

public sealed class LedgerReplaySummary
{
    [JsonPropertyName("stepCount")]
    public int StepCount { get; set; }

    [JsonPropertyName("applicableStepCount")]
    public int ApplicableStepCount { get; set; }

    [JsonPropertyName("blockedStepCount")]
    public int BlockedStepCount { get; set; }

    [JsonPropertyName("appliedStepCount")]
    public int AppliedStepCount { get; set; }

    [JsonPropertyName("failedStepCount")]
    public int FailedStepCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }
}

public sealed class LedgerReplayStep
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("receiptStatus")]
    public string ReceiptStatus { get; set; } = "none";

    [JsonPropertyName("receiptPath")]
    public string? ReceiptPath { get; set; }

    [JsonPropertyName("rollbackPointer")]
    public string? RollbackPointer { get; set; }

    [JsonPropertyName("artifactPath")]
    public string? ArtifactPath { get; set; }

    [JsonPropertyName("affectedElementCount")]
    public int? AffectedElementCount { get; set; }

    [JsonPropertyName("affectedElementIds")]
    public List<long> AffectedElementIds { get; set; } = new();

    [JsonPropertyName("replayMode")]
    public string ReplayMode { get; set; } = "preview";

    [JsonPropertyName("canApply")]
    public bool CanApply { get; set; }

    [JsonPropertyName("blockReason")]
    public string? BlockReason { get; set; }

    [JsonPropertyName("applyStatus")]
    public string? ApplyStatus { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class LedgerTimelineReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-timeline.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("projectDirectories")]
    public List<string> ProjectDirectories { get; set; } = new();

    [JsonPropertyName("query")]
    public LedgerTimelineQuerySpec Query { get; set; } = new();

    [JsonPropertyName("summary")]
    public LedgerTimelineSummary Summary { get; set; } = new();

    [JsonPropertyName("buckets")]
    public List<LedgerTimelineBucket> Buckets { get; set; } = new();

    [JsonPropertyName("byProject")]
    public List<LedgerStatsCount> ByProject { get; set; } = new();

    [JsonPropertyName("issuesBySeverity")]
    public List<LedgerStatsCount> IssuesBySeverity { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<LedgerIssue> Issues { get; set; } = new();
}

public sealed class LedgerTimelineQuerySpec
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "all";

    [JsonPropertyName("sinceUtc")]
    public string? SinceUtc { get; set; }

    [JsonPropertyName("untilUtc")]
    public string? UntilUtc { get; set; }

    [JsonPropertyName("window")]
    public string? Window { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("receiptStatus")]
    public string ReceiptStatus { get; set; } = "all";

    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = "day";
}

public sealed class LedgerTimelineSummary
{
    [JsonPropertyName("operationCount")]
    public int OperationCount { get; set; }

    [JsonPropertyName("bucketCount")]
    public int BucketCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("errorIssueCount")]
    public int ErrorIssueCount { get; set; }

    [JsonPropertyName("warningIssueCount")]
    public int WarningIssueCount { get; set; }

    [JsonPropertyName("missingReceiptCount")]
    public int MissingReceiptCount { get; set; }

    [JsonPropertyName("unreadableReceiptCount")]
    public int UnreadableReceiptCount { get; set; }

    [JsonPropertyName("unbucketedOperationCount")]
    public int UnbucketedOperationCount { get; set; }

    [JsonPropertyName("firstBucket")]
    public string? FirstBucket { get; set; }

    [JsonPropertyName("lastBucket")]
    public string? LastBucket { get; set; }
}

public sealed class LedgerTimelineBucket
{
    [JsonPropertyName("bucketStartUtc")]
    public string BucketStartUtc { get; set; } = "";

    [JsonPropertyName("bucketEndUtc")]
    public string BucketEndUtc { get; set; } = "";

    [JsonPropertyName("operationCount")]
    public int OperationCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("errorIssueCount")]
    public int ErrorIssueCount { get; set; }

    [JsonPropertyName("warningIssueCount")]
    public int WarningIssueCount { get; set; }

    [JsonPropertyName("missingReceiptCount")]
    public int MissingReceiptCount { get; set; }

    [JsonPropertyName("unreadableReceiptCount")]
    public int UnreadableReceiptCount { get; set; }

    [JsonPropertyName("bySource")]
    public List<LedgerStatsCount> BySource { get; set; } = new();

    [JsonPropertyName("byAction")]
    public List<LedgerStatsCount> ByAction { get; set; } = new();

    [JsonPropertyName("byCategory")]
    public List<LedgerStatsCount> ByCategory { get; set; } = new();

    [JsonPropertyName("byOperator")]
    public List<LedgerStatsCount> ByOperator { get; set; } = new();

    [JsonPropertyName("byReceiptStatus")]
    public List<LedgerStatsCount> ByReceiptStatus { get; set; } = new();

    [JsonPropertyName("issuesBySeverity")]
    public List<LedgerStatsCount> IssuesBySeverity { get; set; } = new();
}

public sealed class LedgerOperation
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("modelIdentity")]
    public string? ModelIdentity { get; set; }

    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    [JsonPropertyName("revitVersion")]
    public string? RevitVersion { get; set; }

    [JsonPropertyName("planHash")]
    public string? PlanHash { get; set; }

    [JsonPropertyName("artifact")]
    public string? Artifact { get; set; }

    [JsonPropertyName("artifactPath")]
    public string? ArtifactPath { get; set; }

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("receiptPath")]
    public string? ReceiptPath { get; set; }

    [JsonPropertyName("receiptHash")]
    public string? ReceiptHash { get; set; }

    [JsonPropertyName("receiptStatus")]
    public string ReceiptStatus { get; set; } = "none";

    [JsonPropertyName("rollbackPointer")]
    public string? RollbackPointer { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("affectedElementCount")]
    public int? AffectedElementCount { get; set; }

    [JsonPropertyName("affectedElementIds")]
    public List<long> AffectedElementIds { get; set; } = new();

    [JsonPropertyName("evidenceLinks")]
    public List<string> EvidenceLinks { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<LedgerIssue> Issues { get; set; } = new();
}

public sealed class LedgerAppendResult
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-append.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("ledgerPath")]
    public string LedgerPath { get; set; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    [JsonPropertyName("written")]
    public bool Written { get; set; }

    [JsonPropertyName("operation")]
    public LedgerOperation Operation { get; set; } = new();
}

public sealed class AppendLedgerRecord
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-operation.v1";

    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "succeeded";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("modelIdentity")]
    public string? ModelIdentity { get; set; }

    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    [JsonPropertyName("revitVersion")]
    public string? RevitVersion { get; set; }

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = "";

    [JsonPropertyName("startedAtUtc")]
    public string StartedAtUtc { get; set; } = "";

    [JsonPropertyName("endedAtUtc")]
    public string EndedAtUtc { get; set; } = "";

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "";

    [JsonPropertyName("dryRunRequired")]
    public bool DryRunRequired { get; set; } = true;

    [JsonPropertyName("approvalRequired")]
    public bool ApprovalRequired { get; set; } = true;

    [JsonPropertyName("planPath")]
    public string? PlanPath { get; set; }

    [JsonPropertyName("planHash")]
    public string? PlanHash { get; set; }

    [JsonPropertyName("artifactPath")]
    public string? ArtifactPath { get; set; }

    [JsonPropertyName("receiptPath")]
    public string? ReceiptPath { get; set; }

    [JsonPropertyName("receiptHash")]
    public string? ReceiptHash { get; set; }

    [JsonPropertyName("journalPath")]
    public string? JournalPath { get; set; }

    [JsonPropertyName("rollbackPointer")]
    public string? RollbackPointer { get; set; }

    [JsonPropertyName("affectedElementCount")]
    public int? AffectedElementCount { get; set; }

    [JsonPropertyName("affectedElementIds")]
    public List<long> AffectedElementIds { get; set; } = new();

    [JsonPropertyName("checks")]
    public List<AppendLedgerCheck> Checks { get; set; } = new();

    [JsonPropertyName("artifacts")]
    public List<AppendLedgerArtifact> Artifacts { get; set; } = new();

    [JsonPropertyName("evidenceLinks")]
    public List<string> EvidenceLinks { get; set; } = new();
}

public sealed record AppendLedgerCheck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message);

public sealed record AppendLedgerArtifact(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("sha256")] string? Sha256);

public sealed record LedgerIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("message")] string Message);

public sealed class LedgerValidationReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "ledger-validate.v1";

    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; set; } = "";

    [JsonPropertyName("projectDirectory")]
    public string ProjectDirectory { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "all";

    [JsonPropertyName("failOn")]
    public string FailOn { get; set; } = "error";

    [JsonPropertyName("query")]
    public LedgerQuerySpec Query { get; set; } = new();

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("summary")]
    public LedgerValidationSummary Summary { get; set; } = new();

    [JsonPropertyName("checks")]
    public List<LedgerValidationCheck> Checks { get; } = new();

    [JsonPropertyName("issues")]
    public List<LedgerValidationIssue> Issues { get; } = new();
}

public sealed class LedgerValidationSummary
{
    [JsonPropertyName("operationCount")]
    public int OperationCount { get; set; }

    [JsonPropertyName("checkCount")]
    public int CheckCount { get; set; }

    [JsonPropertyName("issueCount")]
    public int IssueCount { get; set; }

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("missingReceiptCount")]
    public int MissingReceiptCount { get; set; }

    [JsonPropertyName("unreadableReceiptCount")]
    public int UnreadableReceiptCount { get; set; }

    [JsonPropertyName("missingArtifactCount")]
    public int MissingArtifactCount { get; set; }
}

public sealed record LedgerValidationCheck(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("evidence")] string Evidence);

public sealed record LedgerValidationIssue(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("line")] int? Line,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("operationAction")] string? OperationAction);
