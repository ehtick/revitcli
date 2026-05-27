using System.CommandLine;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using RevitCli.Client;
using RevitCli.Config;
using RevitCli.History;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Shared;
using RevitCli.Standards;
using RevitCli.Team;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class WorkbenchCommand
{
    private static readonly string[] V60SaasContradictions =
    {
        "uses SaaS",
        "may use SaaS",
        "requires SaaS",
        "depends on SaaS",
        "calls SaaS",
    };

    private static readonly string[] V60McpContradictions =
    {
        "uses MCP",
        "may use MCP",
        "requires MCP",
        "depends on MCP",
    };

    private static readonly string[] V60LlmContradictions =
    {
        "uses built-in LLM",
        "may use built-in LLM",
        "requires built-in LLM",
        "built-in LLM parser is required",
    };

    private static readonly string[] V60DashboardCentralContradictions =
    {
        "uses dashboard-central",
        "may use dashboard-central",
        "requires dashboard-central",
    };

    private static readonly string[] V60DatabaseContradictions =
    {
        "uses database",
        "may use database",
        "requires database",
        "database-backed",
    };

    private static readonly WorkbenchCommandContract[] CommandContracts =
    {
        new(
            "status",
            "Check whether the Revit add-in is online and which model is active.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: false,
            "none",
            "none",
            "revitcli status --output json",
            "0 when the add-in answers; 1 when the connection or response fails."),
        new(
            "doctor",
            "Diagnose install paths, server discovery, and live Revit connectivity.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: false,
            "none",
            "none",
            "revitcli doctor --output json",
            "0 when checks pass; 1 when setup, version, or connection checks fail."),
        new(
            "inspect",
            "Discover categories, parameters, schedules, sheets, local workflows, and saved plans before work.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "none",
            "revitcli inspect sheets --issues-only --output markdown",
            "0 for successful discovery; 1 for invalid options or failed API calls."),
        new(
            "query",
            "Read model elements for review or downstream terminal filters.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: false,
            "none",
            "none",
            "revitcli query doors --output json",
            "0 for successful query; 1 for invalid filters, output, or failed API calls."),
        new(
            "examples",
            "Discover prompt-to-command recipes for recurring architect tasks.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "none",
            "revitcli examples workflow --output json",
            "0 when recipe topics are found; 1 for unknown topics or invalid output formats."),
        new(
            "workbench",
            "Show and verify the local v4 terminal workbench command contract.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "none",
            "revitcli workbench verify --output json",
            "0 when contract verification passes; 1 for invalid output or failed readiness checks."),
        new(
            "release",
            "Verify local release readiness and strict roadmap gates from the terminal.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "none",
            "revitcli release verify --strict --output json",
            "0 when release gates pass; 1 when required docs, workflows, smoke disclosures, or strict gates fail."),
        new(
            "check",
            "Run profile-driven model checks as a gate before issue work.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: false,
            "none",
            "saved check reports when configured",
            "revitcli check issue --output json",
            "Exit policy follows the profile failOn setting; invalid profiles exit 1."),
        new(
            "score",
            "Track live and history-based model health from terminal snapshots.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "none",
            "revitcli score --history 30d --output json",
            "0 when live or history score renders; 1 for invalid windows, history paths, output, or failed live audit."),
        new(
            "sheets",
            "Verify sheet numbering and plan sheet issue metadata or renumber updates.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required for issue-meta and renumber",
            "sheet-issue-plan.v1 or sheet-renumber-plan.v1 dry-run plan; plan-receipt.v1 after plan apply",
            "revitcli sheets issue-meta --selector all --issue-code R03 --issue-date 2026-05-20 --plan-output .revitcli/plans/sheet-issue.json --dry-run --output json",
            "0 when rules pass or sheet changes are planned; non-zero when validation, sheet-index requirements, or planning fail."),
        new(
            "rooms",
            "Plan reviewed room numbering updates from deterministic rules.",
            "write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before apply",
            "room-numbering-plan.v1 dry-run plan; plan-receipt.v1 after plan apply",
            "revitcli rooms renumber --rule .revitcli/numbering/rooms.yml --scope all --plan-output .revitcli/plans/room-numbering.json --dry-run --output json",
            "0 when room number changes are planned; 1 for invalid rules or API failures; 2 when the plan has no actions."),
        new(
            "marks",
            "Plan and verify door/window Mark numbering updates.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before assign apply",
            "mark-assignment-plan.v1 dry-run plan; mark-verify-report.v1 for read-only verification; plan-receipt.v1 after plan apply",
            "revitcli marks assign --category doors --rule .revitcli/numbering/doors.yml --plan-output .revitcli/plans/door-marks.json --dry-run --output json",
            "0 when mark changes are planned or verify has no errors; 1 for invalid options/API failures; 2 for no-op plans or verify errors."),
        new(
            "schedules",
            "Ensure versioned schedule specs, batch-export schedule sets, and compare exported schedule CSVs.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before ensure writes",
            "schedule-ensure-plan.v1 dry-run plan; schedule-export-manifest.v1 export trace",
            "revitcli schedules ensure --spec .revitcli/schedules/issue.yml --plan-output .revitcli/plans/schedule-ensure.json --dry-run --output json",
            "0 when ensure/export/compare succeeds without diffs; 1 for invalid options/API failures; 2 for no-op plans, export issues, or schedule diffs."),
        new(
            "views",
            "Audit view standards and create reviewed plans for template assignment or cloned view sets.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before template or clone apply",
            "view-template-plan.v1 or view-clone-plan.v1 dry-run plan; future view-mutation receipts after apply",
            "revitcli views audit --rules .revitcli/views/standards.yml --templates --browser --output markdown",
            "0 when audit/plans succeed; 1 for invalid options/API failures; 2 for audit errors or no-op plans."),
        new(
            "links",
            "Audit Revit link paths, load status, and coordinate fingerprints; plan path/load repairs.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before repair writes",
            "link-audit-report.v1 read-only report; link-repair-plan.v1 dry-run plan; plan-receipt.v1 after approved apply with link rollback payloads",
            "revitcli links audit --rules .revitcli/links/rules.yml --check paths,loaded,coordinates --output markdown",
            "0 when audit/plans succeed; 1 for invalid options/API failures or blocked repair plans; 2 for audit errors or no-op repair plans."),
        new(
            "model",
            "Audit and plan workset/phase model mapping fixes with write precheck evidence.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before map-fix writes",
            "model-map-report.v1 read-only report; model-map-fix-plan.v1 dry-run plan; plan-receipt.v1 after approved apply with model rollback payloads",
            "revitcli model map-check --against .revitcli/model-mapping.yml --worksets --phases --output json",
            "0 when map checks/plans succeed; 1 for invalid options/API failures or blocked fix plans; 2 for map-check errors or no-op fix plans."),
        new(
            "schedule",
            "List/export schedule data and preview schedule creation before reviewed writes.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before create",
            ".revitcli/receipts/schedule-create-*.json",
            "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json",
            "0 for successful list/export/create dry-run or create; 1 for invalid options, failed API calls, or receipt failures."),
        new(
            "export",
            "Export sheets or views after a dry-run target review.",
            "export",
            SupportsJson: true,
            SupportsMarkdown: false,
            "required for JSON plan review",
            "<outputDir>/.revitcli/receipts/export-*.json",
            "revitcli export --format pdf --sheets \"A1*\" --dry-run --output json",
            "0 when dry-run/export succeeds; 1 for invalid output, failed validation, or export failure."),
        new(
            "publish",
            "Run profile publish pipelines with preflight and delivery receipts.",
            "export",
            SupportsJson: true,
            SupportsMarkdown: false,
            "required for JSON plan review",
            ".revitcli/receipts/<pipeline>-*.json",
            "revitcli publish issue --dry-run --output json",
            "0 when dry-run/publish succeeds; 1 for profile, check, diff, or export failures."),
        new(
            "set",
            "Preview parameter writes or save a reviewed mutation plan.",
            "write",
            SupportsJson: false,
            SupportsMarkdown: false,
            "required before direct apply",
            "plan receipts after plan apply",
            "revitcli set doors --param \"Fire Rating\" --value \"60min\" --dry-run",
            "0 for successful preview/plan/apply; 1 for invalid inputs or refused unsafe plans."),
        new(
            "import",
            "Convert CSV parameter updates into dry-run groups or saved plans.",
            "write",
            SupportsJson: false,
            SupportsMarkdown: false,
            "required before apply",
            "plan receipts after plan apply",
            "revitcli import doors.csv --category doors --match-by Mark --dry-run",
            "0 for successful preview/plan/apply; 1 for CSV, matching, or safety failures."),
        new(
            "plan",
            "Review and apply saved mutation plans with rollback receipts.",
            "write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before apply",
            "plan-receipt.v1 sidecars",
            "revitcli plan show .revitcli/plans/doors.json --output json",
            "0 when show/apply succeeds; 1 when validation, thresholds, or apply calls fail."),
        new(
            "rollback",
            "Restore values from fix baselines or plan receipts after review.",
            "write",
            SupportsJson: false,
            SupportsMarkdown: false,
            "required before apply",
            "uses fix baselines or plan-receipt.v1 sidecars",
            "revitcli rollback .revitcli/plans/doors.json.receipt.json --dry-run",
            "0 when preview/apply succeeds; 1 for unsupported artifacts or conflict checks."),
        new(
            "workflow",
            "Validate, simulate, review, index, and run reusable terminal workflows.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before real workflow runs",
            ".revitcli/workflows/receipts/*.json",
            "revitcli workflow registry --output json",
            "0 when validation/simulation/run succeeds; workflow run receipts record per-step exit codes."),
        new(
            "deliverables",
            "Review manifests, verify receipts, and bundle delivery evidence.",
            "local-write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "available for bundles",
            "delivery-bundle-receipt.v1 sidecars",
            "revitcli deliverables verify --output json",
            "0 when manifest and receipt checks pass; non-zero for missing or invalid delivery evidence."),
        new(
            "issue",
            "Run issue preflight, diff review, and traceable delivery packaging.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before package write",
            "issue-package-receipt.v1 after approved package",
            "revitcli issue preflight --profile .revitcli/issue.yml --output markdown",
            "0 when issue preflight/diff/package succeeds; 1 for invalid profiles or package evidence; 2 for preflight fail-on thresholds."),
        new(
            "standards",
            "Install and validate portable office standards packs.",
            "local-write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "available for install",
            "none",
            "revitcli standards install profiles/office-standard --dry-run --output markdown",
            "0 when install/validation succeeds; non-zero for incompatible packs or missing required files."),
        new(
            "family",
            "Review family inventory, validate rules, and preview cleanup.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: false,
            "required before purge apply",
            "family-purge-report.v1 when --report is used",
            "revitcli family purge --dry-run --report .revitcli/reports/family-purge.json",
            "0 when list/validate/purge succeeds; non-zero follows validation severity and purge safety gates."),
        new(
            "history",
            "Capture, list, prune, diff, and trend local model snapshots.",
            "local-write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "available for prune",
            ".revitcli/history snapshots",
            "revitcli history list --limit 5",
            "0 when history operations succeed; non-zero for missing snapshots or refused prune operations."),
        new(
            "journal",
            "Inspect, review, sign, and verify local operation history.",
            "local-write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            ".revitcli/journal.jsonl.sig for sign",
            "revitcli journal review --output json",
            "0 when review/sign/verify succeeds; non-zero when signatures or journal integrity fail."),
        new(
            "report",
            "Summarize local history, journals, knowledge, and weekly review evidence.",
            "read-only",
            SupportsJson: true,
            SupportsMarkdown: true,
            "none",
            "optional report files",
            "revitcli report knowledge --output json",
            "0 when local report generation succeeds; 1 for invalid paths or output options."),
        new(
            "ledger",
            "Append, query, validate, summarize, and timeline local operation ledger artifacts across journal, history, deliveries, workflow receipts, and local operations JSONL records.",
            "local-write",
            SupportsJson: true,
            SupportsMarkdown: true,
            "--yes required for append writes",
            ".revitcli/ledger/operations.jsonl",
            "revitcli ledger append --action issue.package --output json",
            "0 when local artifacts are appended, replay-previewed, queried, or validated; 1 for invalid filters, failed validation thresholds, invalid append input, or output options.")
    };

    private static readonly WorkbenchReceiptContract[] ReceiptContracts =
    {
        new(
            "export-receipt.v1",
            "export",
            "revitcli export",
            "Successful real exports; dry-runs do not write receipts.",
            "<outputDir>/.revitcli/receipts/export-*.json",
            "revitcli export --format pdf --sheets \"A1*\" --dry-run --output json",
            "revitcli deliverables verify --output json"),
        new(
            "publish-receipt.v1",
            "publish",
            "revitcli publish",
            "Successful real publish runs; dry-runs do not write receipts.",
            ".revitcli/receipts/<pipeline>-*.json",
            "revitcli publish issue --dry-run --output json",
            "revitcli deliverables verify --output json"),
        new(
            "plan-receipt.v1",
            "plan.apply",
            "revitcli plan apply",
            "Successful plan applies after approval.",
            "<plan-file>.receipt.json",
            "revitcli plan apply <plan-file> --dry-run",
            "revitcli rollback <plan-file>.receipt.json --dry-run"),
        new(
            "workflow-run-receipt.v1",
            "workflow.run",
            "revitcli workflow run",
            "Real workflow runs after validation and approval; receipts include run and step durations.",
            ".revitcli/workflows/receipts/*.json",
            "revitcli workflow run <workflow.yml> --dry-run --output markdown",
            "revitcli workflow receipts --output json"),
        new(
            "delivery-bundle-receipt.v1",
            "deliverables.bundle",
            "revitcli deliverables bundle",
            "Successful delivery bundle writes.",
            "<bundle-path>.receipt.json",
            "revitcli deliverables bundle --dry-run --output markdown",
            "revitcli deliverables verify --output json"),
        new(
            "issue-package-receipt.v1",
            "issue.package",
            "revitcli issue package",
            "Successful issue package writes; dry-runs do not write bundles or receipts.",
            ".revitcli/receipts/issue-package-*.json",
            "revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --dry-run --output markdown",
            "revitcli issue preflight --profile .revitcli/issue.yml --output markdown"),
        new(
            "schedule-create-receipt.v1",
            "schedule.create",
            "revitcli schedule create",
            "Successful real schedule create runs; dry-runs do not write receipts.",
            ".revitcli/receipts/schedule-create-*.json",
            "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json",
            "revitcli journal review --output json")
    };

    private static readonly WorkbenchExtensionPoint[] ExtensionPoints =
    {
        new(
            "project-profile",
            "Project profile rules, publish pipelines, defaults, and governed output paths.",
            ".revitcli.yml",
            "revitcli profile validate",
            "revitcli profile show --resolve --output json",
            "local config only",
            "Profiles are terminal configuration files; validate before check, publish, or plan apply."),
        new(
            "workflow-yaml",
            "Reusable architect workflows with explicit read-only, dry-run, and mutating step modes.",
            ".revitcli/workflows/*.yml",
            "revitcli workflow validate .revitcli/workflows/<workflow>.yml",
            "revitcli workflow simulate .revitcli/workflows/<workflow>.yml --output markdown",
            "workflow run writes receipts only after approved real runs",
            "Workflow YAML is the main extension point for repeated terminal work."),
        new(
            "standards-pack",
            "Portable office standards packs with required profiles, workflows, outputs, schedules, and family rules.",
            "profiles/office-standard/.revitcli/standards.yml",
            "revitcli standards validate --manifest .revitcli/standards.yml --dir profiles/office-standard --output json",
            "revitcli standards install profiles/office-standard --dry-run --output markdown",
            "install copies governed local files after dry-run review",
            "Standards packs serve terminal validation and bootstrap, not a remote package service."),
        new(
            "family-rules",
            "Custom family validation rule files for office-specific family review.",
            ".revitcli/family-rules/*.yml",
            "revitcli family validate --rules-from .revitcli/family-rules --output json",
            "revitcli family purge --dry-run --report .revitcli/reports/family-purge.json",
            "purge stays dry-run unless --apply --yes is provided",
            "Family extensions are review rules and purge safety gates, not geometry editing."),
        new(
            "codex-recipes",
            "Prompt-to-command recipe documentation for visible CLI paths.",
            "docs/templates/codex-recipes/*.md",
            "revitcli examples recipes --output markdown",
            "revitcli examples workbench --output json",
            "documentation only",
            "Recipes never execute commands or add a prompt interpreter inside RevitCli.")
    };

    private static readonly WorkbenchOutputContract[] OutputContracts =
    {
        new(
            "workbench-contract",
            "workbench contract",
            SupportsTable: true,
            "workbench-contract.v1",
            SupportsMarkdown: true,
            "Stable command vocabulary, risk, dry-run, receipt, command paths, and exit-code notes."),
        new(
            "workbench-verification",
            "workbench verify",
            SupportsTable: true,
            "workbench-verification.v1",
            SupportsMarkdown: true,
            "Readiness checks for callable paths, non-goals, receipts, workflows, outputs, and exit codes."),
        new(
            "workbench-verification-v2",
            "workbench verify --contract workbench-contract.v2",
            SupportsTable: true,
            "workbench-verify-report.v2",
            SupportsMarkdown: true,
            "v5 issue-closure readiness checks for callable paths, package traceability, hidden mutation gates, RC boundary disclosure, and dashboard optionality."),
        new(
            "workbench-receipts",
            "workbench receipts",
            SupportsTable: true,
            "workbench-receipts.v1",
            SupportsMarkdown: true,
            "Receipt schema, path, dry-run, and review command index."),
        new(
            "workbench-paths",
            "workbench paths",
            SupportsTable: true,
            "workbench-paths.v1",
            SupportsMarkdown: true,
            "Flat callable path index for Codex CLI."),
        new(
            "workbench-exit-codes",
            "workbench exits",
            SupportsTable: true,
            "workbench-exit-codes.v1",
            SupportsMarkdown: true,
            "Predictable success/failure semantics for contract commands."),
        new(
            "workbench-extensions",
            "workbench extensions",
            SupportsTable: true,
            "workbench-extensions.v1",
            SupportsMarkdown: true,
            "Terminal-first extension points and validation or preview commands."),
        new(
            "workbench-outputs",
            "workbench outputs",
            SupportsTable: true,
            "workbench-outputs.v1",
            SupportsMarkdown: true,
            "Readable table, compact JSON schema, and Markdown output contract index."),
        new(
            "workbench-safeguards",
            "workbench safeguards",
            SupportsTable: true,
            "workbench-safeguards.v1",
            SupportsMarkdown: true,
            "Dry-run, approval, receipt, and review commands for risky terminal paths."),
        new(
            "workbench-project",
            "workbench project",
            SupportsTable: true,
            "workbench-project.v1",
            SupportsMarkdown: true,
            "Local project artifact inventory for profiles, workflows, history, journal, deliveries, receipts, plans, and reports."),
        new(
            "workbench-handoff",
            "workbench handoff",
            SupportsTable: true,
            "workbench-handoff.v1",
            SupportsMarkdown: true,
            "One-command terminal handoff summary with verification status, project artifact counts, readiness actions, and next commands."),
        new(
            "schedule-create",
            "schedule create",
            SupportsTable: true,
            "schedule-create.v1",
            SupportsMarkdown: true,
            "Dry-run and result envelope for reviewed schedule creation with receipt path after writes."),
        new(
            "schedule-ensure-plan",
            "schedules ensure",
            SupportsTable: true,
            "schedule-ensure-plan.v1",
            SupportsMarkdown: true,
            "Dry-run plan for missing or drifted schedules from schedule-spec.v1 YAML with old structure baselines."),
        new(
            "schedule-export-manifest",
            "schedules batch-export",
            SupportsTable: true,
            "schedule-export-manifest.v1",
            SupportsMarkdown: true,
            "Traceable schedule CSV export manifest with schedule ids, output paths, row counts, and issues."),
        new(
            "schedule-diff-report",
            "schedules compare",
            SupportsTable: true,
            "schedule-diff-report.v1",
            SupportsMarkdown: true,
            "Read-only CSV directory comparison report keyed by schedule columns."),
        new(
            "view-standards-report",
            "views audit",
            SupportsTable: true,
            "view-standards-report.v1",
            SupportsMarkdown: true,
            "Read-only view naming, template, and browser organization standards report."),
        new(
            "view-template-plan",
            "views template-apply",
            SupportsTable: true,
            "view-template-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for view template assignment with old/new template ids."),
        new(
            "view-clone-plan",
            "views clone-set",
            SupportsTable: true,
            "view-clone-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for cloned view names with source view ids and rollback guards."),
        new(
            "link-audit-report",
            "links audit",
            SupportsTable: true,
            "link-audit-report.v1",
            SupportsMarkdown: true,
            "Read-only Revit link path, loaded-state, and coordinate fingerprint audit report."),
        new(
            "link-repair-plan",
            "links repair",
            SupportsTable: true,
            "link-repair-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for link path/load repairs with old/new path existence and timestamp evidence."),
        new(
            "model-map-report",
            "model map-check",
            SupportsTable: true,
            "model-map-report.v1",
            SupportsMarkdown: true,
            "Read-only workset and phase mapping report for coordination hygiene."),
        new(
            "model-map-fix-plan",
            "model map-fix",
            SupportsTable: true,
            "model-map-fix-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for workset/phase fixes with write precheck evidence."),
        new(
            "sheet-issue-plan",
            "sheets issue-meta",
            SupportsTable: true,
            "sheet-issue-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for sheet issue metadata updates with skipped-parameter evidence."),
        new(
            "sheet-renumber-plan",
            "sheets renumber",
            SupportsTable: true,
            "sheet-renumber-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for sheet number updates with duplicate-target and skipped-sheet evidence."),
        new(
            "room-numbering-plan",
            "rooms renumber",
            SupportsTable: true,
            "room-numbering-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for room number updates with deterministic ordering and collision evidence."),
        new(
            "mark-assignment-plan",
            "marks assign",
            SupportsTable: true,
            "mark-assignment-plan.v1",
            SupportsMarkdown: true,
            "Frozen dry-run plan for door/window Mark updates with deterministic sorting and collision evidence."),
        new(
            "mark-verify-report",
            "marks verify",
            SupportsTable: true,
            "mark-verify-report.v1",
            SupportsMarkdown: true,
            "Read-only duplicate, missing, and rule-conformance report for door/window Marks."),
        new(
            "delivery-plan",
            "deliverables plan",
            SupportsTable: true,
            "delivery-plan.v1",
            SupportsMarkdown: true,
            "Read-only export delivery plan from profile publish pipelines with baseline and risk evidence."),
        new(
            "issue-preflight-report",
            "issue preflight",
            SupportsTable: true,
            "issue-preflight-report.v1",
            SupportsMarkdown: true,
            "Issue readiness report with explicit hidden-mutation checks and handoff commands."),
        new(
            "issue-diff-report",
            "issue diff",
            SupportsTable: true,
            "issue-diff-report.v1",
            SupportsMarkdown: true,
            "Issue-scoped snapshot diff report with anomaly/notable/routine review groups."),
        new(
            "issue-package-receipt",
            "issue package",
            SupportsTable: true,
            "issue-package-receipt.v1",
            SupportsMarkdown: true,
            "Issue package receipt with manifest path, child receipts, bundle hash, and optional journal signature path."),
        new(
            "workbench-contract-v2",
            "workbench contract --contract workbench-contract.v2",
            SupportsTable: true,
            "workbench-contract.v2",
            SupportsMarkdown: true,
            "v5 workbench contract compatibility marker for issue closure command and receipt schemas."),
        new(
            "inspect-workflows",
            "inspect workflows",
            SupportsTable: true,
            "inspect-workflows.v1",
            SupportsMarkdown: true,
            "Read-only local workflow YAML discovery with validate, simulate, review, dry-run, approval, and receipt commands."),
        new(
            "inspect-plans",
            "inspect plans",
            SupportsTable: true,
            "inspect-plans.v1",
            SupportsMarkdown: true,
            "Read-only saved mutation plan discovery with show, dry-run apply, approved apply, receipt, and rollback-preview commands."),
        new(
            "example-recipes",
            "examples <topic>",
            SupportsTable: true,
            "example-recipes.v1",
            SupportsMarkdown: true,
            "Prompt-to-command recipe documentation for visible CLI paths."),
        new(
            "workflow-review",
            "workflow review <file>",
            SupportsTable: true,
            "workflow-review.v1",
            SupportsMarkdown: true,
            "Pre-run workbench handoff, approval gates, project artifact readiness, dry-run commands, acceptance evidence, and post-run receipt triage commands."),
        new(
            "workflow-registry",
            "workflow registry",
            SupportsTable: true,
            "workflow-registry.v1",
            SupportsMarkdown: true,
            "Read-only local workflow registry index with inputs, outputs, read/write scope, risk level, dry-run commands, approval commands, rollback support, receipt schemas, and acceptance evidence."),
        new(
            "workflow-receipts",
            "workflow receipts",
            SupportsTable: true,
            "workflow-receipts.v1",
            SupportsMarkdown: true,
            "Local workflow-run receipt triage with duration and filters."),
        new(
            "model-health-history",
            "score --history <duration>",
            SupportsTable: true,
            "model-health-history.v1",
            SupportsMarkdown: true,
            "Local model-health trend from history snapshots."),
        new(
            "knowledge-report",
            "report knowledge",
            SupportsTable: true,
            "knowledge-report.v1",
            SupportsMarkdown: true,
            "Reusable local project knowledge from history, journal, workflow, delivery, standards, and reports."),
        new(
            "ledger-append",
            "ledger append",
            SupportsTable: true,
            "ledger-append.v1",
            SupportsMarkdown: true,
            "Append-only local operations ledger writer with dry-run default and explicit --yes approval for .revitcli/ledger/operations.jsonl."),
        new(
            "ledger-query",
            "ledger query",
            SupportsTable: true,
            "ledger-query.v1",
            SupportsMarkdown: true,
            "Local operations ledger query output across journal, history, delivery, and workflow receipt artifacts."),
        new(
            "ledger-replay",
            "ledger replay",
            SupportsTable: true,
            "ledger-replay.v1",
            SupportsMarkdown: true,
            "Default local operations ledger replay preview plus bounded --apply --yes support for approved set, export, and schedule batch-export records."),
        new(
            "ledger-validate",
            "ledger validate",
            SupportsTable: true,
            "ledger-validate.v1",
            SupportsMarkdown: true,
            "Read-only local operations ledger validation for source readability, artifact links, receipt status, and timestamp format."),
        new(
            "ledger-stats",
            "ledger stats",
            SupportsTable: true,
            "ledger-stats.v1",
            SupportsMarkdown: true,
            "Read-only local operations ledger project-memory summary for sources, actions, categories, operators, receipt status, and issues."),
        new(
            "ledger-timeline",
            "ledger timeline",
            SupportsTable: true,
            "ledger-timeline.v1",
            SupportsMarkdown: true,
            "Read-only local operations ledger project-memory timeline with bucket, source, action, category, operator, receipt status, issue severity, and unbucketed timestamp evidence."),
        new(
            "ledger-analytics",
            "ledger analytics",
            SupportsTable: true,
            "ledger-analytics-bundle.v1",
            SupportsMarkdown: true,
            "Local operations ledger analytics bundle that writes stats and timeline snapshot evidence without a service or database runtime.")
    };

    private static readonly WorkbenchSafeguardContract[] SafeguardContracts =
    {
        new(
            "export",
            "export",
            "export",
            "revitcli export --format pdf --sheets \"A1*\" --dry-run --output json",
            "revitcli export --format pdf --sheets \"A1*\" --output-dir <dir>",
            "<outputDir>/.revitcli/receipts/export-*.json",
            "revitcli deliverables verify --output json",
            "Dry-run validates export targets before files are written."),
        new(
            "publish",
            "publish",
            "export",
            "revitcli publish issue --dry-run --output json",
            "revitcli publish issue --yes",
            ".revitcli/receipts/<pipeline>-*.json",
            "revitcli deliverables verify --output json",
            "Publish should pass profile checks and dry-run review before real export."),
        new(
            "set-plan",
            "set",
            "write",
            "revitcli set doors --param \"Fire Rating\" --value \"60min\" --dry-run",
            "revitcli set doors --param \"Fire Rating\" --value \"60min\" --plan-output .revitcli/plans/doors.json",
            "plan-receipt.v1 after plan apply",
            "revitcli plan show .revitcli/plans/doors.json --output markdown",
            "Prefer saving a reviewed plan over direct parameter writes."),
        new(
            "import-plan",
            "import",
            "write",
            "revitcli import doors.csv --category doors --match-by Mark --dry-run",
            "revitcli import doors.csv --category doors --match-by Mark --plan-output .revitcli/plans/doors-import.json",
            "plan-receipt.v1 after plan apply",
            "revitcli plan show .revitcli/plans/doors-import.json --output markdown",
            "CSV writes should become a saved plan before apply."),
        new(
            "plan-apply",
            "plan apply",
            "write",
            "revitcli plan apply <plan-file> --dry-run",
            "revitcli plan apply <plan-file> --yes",
            "<plan-file>.receipt.json",
            "revitcli rollback <plan-file>.receipt.json --dry-run",
            "Plan apply receipts provide rollback actions and model context when available."),
        new(
            "rollback",
            "rollback",
            "write",
            "revitcli rollback <artifact> --dry-run",
            "revitcli rollback <artifact> --yes",
            "uses fix baselines or plan-receipt.v1 sidecars",
            "revitcli journal review --output markdown",
            "Rollback previews current-value conflicts before restoring values."),
        new(
            "workflow-run",
            "workflow run",
            "mixed",
            "revitcli workflow run <workflow.yml> --dry-run --output markdown",
            "revitcli workflow run <workflow.yml> --yes",
            ".revitcli/workflows/receipts/*.json",
            "revitcli workflow receipts --output markdown",
            "Workflow runs require validate/simulate/review before approved mutating steps."),
        new(
            "deliverables-bundle",
            "deliverables bundle",
            "local-write",
            "revitcli deliverables bundle --dry-run --output markdown",
            "revitcli deliverables bundle --bundle-path deliverables/review-package.zip",
            "delivery-bundle-receipt.v1 sidecars",
            "revitcli deliverables verify --output json",
            "Bundle dry-runs preview receipts and output files before writing the zip."),
        new(
            "issue-package",
            "issue package",
            "export",
            "revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --dry-run --output markdown",
            "revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue-package.zip --include-receipts true",
            "issue-package-receipt.v1",
            "revitcli issue preflight --profile .revitcli/issue.yml --output markdown",
            "Issue package dry-runs must expose planned files, child receipts, bundle path, and hidden-mutation checks before writing."),
        new(
            "standards-install",
            "standards install",
            "local-write",
            "revitcli standards install profiles/office-standard --dry-run --output markdown",
            "revitcli standards install profiles/office-standard --force",
            "none",
            "revitcli standards validate --output json",
            "Standards install copies governed local files only after dry-run review."),
        new(
            "family-purge",
            "family purge",
            "mixed",
            "revitcli family purge --dry-run --report .revitcli/reports/family-purge.json",
            "revitcli family purge --apply --yes",
            "family-purge-report.v1 when --report is used",
            "revitcli family validate --output json",
            "Family purge defaults to dry-run and applies only with explicit approval."),
        new(
            "history-prune",
            "history prune",
            "local-write",
            "revitcli history prune --keep 30d --dry-run",
            "revitcli history prune --keep 30d --apply",
            ".revitcli/history snapshots",
            "revitcli history list --limit 5",
            "History pruning previews deleted snapshots before applying."),
        new(
            "schedule-create",
            "schedule create",
            "mixed",
            "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json",
            "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --output json",
            ".revitcli/receipts/schedule-create-*.json",
            "revitcli journal review --output json",
            "Schedule creation must be dry-run reviewed before writing a ViewSchedule."),
        new(
            "schedules-ensure",
            "schedules ensure",
            "write",
            "revitcli schedules ensure --spec .revitcli/schedules/issue.yml --plan-output .revitcli/plans/schedule-ensure.json --dry-run --output json",
            "revitcli plan show .revitcli/plans/schedule-ensure.json --output markdown",
            "future .revitcli/receipts/schedule-ensure-*.json with old schedule structure baselines",
            "revitcli schedules compare --from exports/baseline --to exports/current --output markdown",
            "Schedule structure writes must start from a dry-run plan with old fields/filter/sort baseline evidence."),
        new(
            "views-template-apply",
            "views template-apply",
            "write",
            "revitcli views template-apply --selector \"Level*\" --template \"Architectural Plan\" --plan-output .revitcli/plans/view-template.json --dry-run --output json",
            "revitcli plan show .revitcli/plans/view-template.json --output markdown",
            "future view-mutation-receipt.v1 after approved apply",
            "revitcli views audit --rules .revitcli/views/standards.yml --templates --output markdown",
            "View template writes must start from a frozen dry-run plan with source view ids and old/new template ids."),
        new(
            "views-clone-set",
            "views clone-set",
            "write",
            "revitcli views clone-set --from-set \"Level*\" --to-prefix \"T-\" --naming-rule \"{prefix}{name}\" --plan-output .revitcli/plans/view-clone.json --dry-run --output json",
            "revitcli plan show .revitcli/plans/view-clone.json --output markdown",
            "future view-mutation-receipt.v1 after approved apply",
            "revitcli views audit --rules .revitcli/views/standards.yml --browser --output markdown",
            "View clone writes must fail on name collisions and guard rollback deletes for views placed on sheets."),
        new(
            "links-repair",
            "links repair",
            "write",
            "revitcli links repair --map .revitcli/links/paths.yml --plan-output .revitcli/plans/link-repair.json --dry-run --output json",
            "revitcli plan show .revitcli/plans/link-repair.json --output markdown",
            "plan-receipt.v1 with old/new path and load-state rollback evidence",
            "revitcli links audit --rules .revitcli/links/rules.yml --output markdown",
            "Link repair plans may change only path/load state; coordinate move, rotate, or align is outside the contract."),
        new(
            "model-map-fix",
            "model map-fix",
            "write",
            "revitcli model map-fix --against .revitcli/model-mapping.yml --plan-output .revitcli/plans/model-map-fix.json --dry-run --output json",
            "revitcli plan show .revitcli/plans/model-map-fix.json --output markdown",
            "plan-receipt.v1 with old/new phase or workset rollback values",
            "revitcli model map-check --against .revitcli/model-mapping.yml --worksets --phases --output markdown",
            "Model map fixes must prove target element writability before approved apply."),
        new(
            "sheet-issue-meta",
            "sheets issue-meta",
            "mixed",
            "revitcli sheets issue-meta --selector all --issue-code R03 --issue-date 2026-05-20 --plan-output .revitcli/plans/sheet-issue.json --dry-run --output json",
            "revitcli plan apply .revitcli/plans/sheet-issue.json --yes --max-changes 250",
            "sheet-issue-plan.v1 plus plan-receipt.v1 after apply",
            "revitcli plan show .revitcli/plans/sheet-issue.json --output markdown",
            "Sheet issue metadata writes are gated behind a frozen dry-run plan with skipped-parameter evidence."),
        new(
            "sheet-renumber",
            "sheets renumber",
            "mixed",
            "revitcli sheets renumber --rule .revitcli/sheets/numbering.yml --selector all --plan-output .revitcli/plans/sheet-renumber.json --dry-run --output json",
            "revitcli plan apply .revitcli/plans/sheet-renumber.json --yes --max-changes 250",
            "sheet-renumber-plan.v1 plus plan-receipt.v1 after apply",
            "revitcli plan show .revitcli/plans/sheet-renumber.json --output markdown",
            "Sheet renumber writes are gated behind a frozen dry-run plan and stale-number validation."),
        new(
            "rooms-renumber",
            "rooms renumber",
            "write",
            "revitcli rooms renumber --rule .revitcli/numbering/rooms.yml --scope all --plan-output .revitcli/plans/room-numbering.json --dry-run --output json",
            "revitcli plan apply .revitcli/plans/room-numbering.json --yes --max-changes 500",
            "room-numbering-plan.v1 plus plan-receipt.v1 after apply",
            "revitcli plan show .revitcli/plans/room-numbering.json --output markdown",
            "Room renumber writes are gated behind a frozen dry-run plan, duplicate-target checks, and stale-number validation."),
        new(
            "marks-assign",
            "marks assign",
            "write",
            "revitcli marks assign --category doors --rule .revitcli/numbering/doors.yml --plan-output .revitcli/plans/door-marks.json --dry-run --output json",
            "revitcli plan apply .revitcli/plans/door-marks.json --yes --max-changes 500",
            "mark-assignment-plan.v1 plus plan-receipt.v1 after apply",
            "revitcli plan show .revitcli/plans/door-marks.json --output markdown",
            "Mark assignment writes are gated behind a frozen dry-run plan, duplicate-target checks, and stale-value validation.")
    };

    public static Command Create()
    {
        var contract = new Command("contract", "Show stable terminal workbench contract for Codex CLI");
        var contractOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var contractSchemaOpt = new Option<string?>("--contract", "Contract schema to emit: workbench-contract.v1 or workbench-contract.v2");
        contract.AddOption(contractOutputOpt);
        contract.AddOption(contractSchemaOpt);
        contract.SetHandler(async (outputFormat, contractSchema) =>
        {
            Environment.ExitCode = await ExecuteContractAsync(Console.Out, outputFormat, contractSchema);
        }, contractOutputOpt, contractSchemaOpt);

        var verify = new Command("verify", "Verify terminal workbench contract and recipe readiness");
        var verifyDirOpt = new Option<string?>("--dir", "Project directory for project-inventory readiness (default: current directory)");
        var verifyOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        var verifyContractOpt = new Option<string?>("--contract", "Contract schema to verify: workbench-contract.v1 or workbench-contract.v2");
        verify.AddOption(verifyDirOpt);
        verify.AddOption(verifyOutputOpt);
        verify.AddOption(verifyContractOpt);
        verify.SetHandler(async (projectDirectory, outputFormat, contractSchema) =>
        {
            Environment.ExitCode = await ExecuteVerifyAsync(Console.Out, outputFormat, projectDirectory, contractSchema);
        }, verifyDirOpt, verifyOutputOpt, verifyContractOpt);

        var receipts = new Command("receipts", "Show stable receipt schemas, paths, dry-runs, and review commands");
        var receiptsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        receipts.AddOption(receiptsOutputOpt);
        receipts.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecuteReceiptsAsync(Console.Out, outputFormat);
        }, receiptsOutputOpt);

        var paths = new Command("paths", "Show flat callable command paths for Codex CLI");
        var pathsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        paths.AddOption(pathsOutputOpt);
        paths.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecutePathsAsync(Console.Out, outputFormat);
        }, pathsOutputOpt);

        var exits = new Command("exits", "Show predictable exit-code notes for Codex-callable commands");
        var exitsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        exits.AddOption(exitsOutputOpt);
        exits.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecuteExitsAsync(Console.Out, outputFormat);
        }, exitsOutputOpt);

        var extensions = new Command("extensions", "Show terminal-first extension points and validation commands");
        var extensionsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        extensions.AddOption(extensionsOutputOpt);
        extensions.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecuteExtensionsAsync(Console.Out, outputFormat);
        }, extensionsOutputOpt);

        var outputs = new Command("outputs", "Show readable table and compact JSON output contracts");
        var outputsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        outputs.AddOption(outputsOutputOpt);
        outputs.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecuteOutputsAsync(Console.Out, outputFormat);
        }, outputsOutputOpt);

        var safeguards = new Command("safeguards", "Show dry-run, approval, receipt, and review paths for risky commands");
        var safeguardsOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        safeguards.AddOption(safeguardsOutputOpt);
        safeguards.SetHandler(async outputFormat =>
        {
            Environment.ExitCode = await ExecuteSafeguardsAsync(Console.Out, outputFormat);
        }, safeguardsOutputOpt);

        var project = new Command("project", "Inspect local workbench project artifacts");
        var projectDirOpt = new Option<string?>("--dir", "Project directory (default: current directory)");
        var projectOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        project.AddOption(projectDirOpt);
        project.AddOption(projectOutputOpt);
        project.SetHandler(async (projectDirectory, outputFormat) =>
        {
            Environment.ExitCode = await ExecuteProjectAsync(projectDirectory, outputFormat, Console.Out);
        }, projectDirOpt, projectOutputOpt);

        var handoff = new Command("handoff", "Print a local terminal handoff summary for Codex CLI");
        var handoffDirOpt = new Option<string?>("--dir", "Project directory (default: current directory)");
        var handoffOutputOpt = new Option<string>("--output", () => "table", "Output format: table, json, markdown");
        handoff.AddOption(handoffDirOpt);
        handoff.AddOption(handoffOutputOpt);
        handoff.SetHandler(async (projectDirectory, outputFormat) =>
        {
            Environment.ExitCode = await ExecuteHandoffAsync(projectDirectory, outputFormat, Console.Out);
        }, handoffDirOpt, handoffOutputOpt);

        return new Command("workbench", "Show stable terminal workbench contract for Codex CLI")
        {
            contract,
            verify,
            receipts,
            paths,
            exits,
            extensions,
            outputs,
            safeguards,
            project,
            handoff
        };
    }

    public static async Task<int> ExecuteContractAsync(TextWriter output, string outputFormat, string? contractSchema = null)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!TryNormalizeWorkbenchContract(contractSchema, output, out var normalizedContract, out _))
            return 1;

        var contract = CreateContract(normalizedContract);
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(contract, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteMarkdownAsync(output, contract);
                break;
            default:
                await WriteTableAsync(output, contract);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteVerifyAsync(
        TextWriter output,
        string outputFormat,
        string? projectDirectory = null,
        string? contractSchema = null)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        if (!TryNormalizeWorkbenchContract(contractSchema, output, out var normalizedContract, out var verificationSchema))
            return 1;

        string root;
        try
        {
            root = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await output.WriteLineAsync($"Error: invalid --dir: {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(root))
        {
            await output.WriteLineAsync($"Error: project directory not found: {root}");
            return 1;
        }

        var verification = CreateVerification(root, normalizedContract, verificationSchema);
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(verification, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteVerificationMarkdownAsync(output, verification);
                break;
            default:
                await WriteVerificationTableAsync(output, verification);
                break;
        }

        return verification.Success ? 0 : 1;
    }

    public static async Task<int> ExecuteReceiptsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var receipts = CreateReceiptIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(receipts, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteReceiptsMarkdownAsync(output, receipts);
                break;
            default:
                await WriteReceiptsTableAsync(output, receipts);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecutePathsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var paths = CreatePathIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(paths, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WritePathsMarkdownAsync(output, paths);
                break;
            default:
                await WritePathsTableAsync(output, paths);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteExitsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var exits = CreateExitCodeIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(exits, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteExitCodesMarkdownAsync(output, exits);
                break;
            default:
                await WriteExitCodesTableAsync(output, exits);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteExtensionsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var extensions = CreateExtensionIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(extensions, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteExtensionsMarkdownAsync(output, extensions);
                break;
            default:
                await WriteExtensionsTableAsync(output, extensions);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteOutputsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var outputs = CreateOutputIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(outputs, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteOutputsMarkdownAsync(output, outputs);
                break;
            default:
                await WriteOutputsTableAsync(output, outputs);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteSafeguardsAsync(TextWriter output, string outputFormat)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        var safeguards = CreateSafeguardIndex();
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(safeguards, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteSafeguardsMarkdownAsync(output, safeguards);
                break;
            default:
                await WriteSafeguardsTableAsync(output, safeguards);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteProjectAsync(
        string? projectDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        string root;
        try
        {
            root = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await output.WriteLineAsync($"Error: invalid --dir: {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(root))
        {
            await output.WriteLineAsync($"Error: project directory not found: {root}");
            return 1;
        }

        var inventory = CreateProjectInventory(root);
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(inventory, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteProjectMarkdownAsync(output, inventory);
                break;
            default:
                await WriteProjectTableAsync(output, inventory);
                break;
        }

        return 0;
    }

    public static async Task<int> ExecuteHandoffAsync(
        string? projectDirectory,
        string outputFormat,
        TextWriter output)
    {
        if (!TerminalOutputFormat.TryNormalize(outputFormat, out var normalized, "table", "json", "markdown"))
        {
            await output.WriteLineAsync("Error: --output must be 'table', 'json', or 'markdown'.");
            return 1;
        }

        string root;
        try
        {
            root = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(projectDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await output.WriteLineAsync($"Error: invalid --dir: {ex.Message}");
            return 1;
        }

        if (!Directory.Exists(root))
        {
            await output.WriteLineAsync($"Error: project directory not found: {root}");
            return 1;
        }

        var handoff = CreateHandoffReport(root);
        switch (normalized)
        {
            case "json":
                await output.WriteLineAsync(JsonSerializer.Serialize(handoff, TerminalJsonOptions.CompactContract));
                break;
            case "markdown":
                await WriteHandoffMarkdownAsync(output, handoff);
                break;
            default:
                await WriteHandoffTableAsync(output, handoff);
                break;
        }

        return handoff.Success ? 0 : 1;
    }

    private static WorkbenchContract CreateContract(string schemaVersion = "workbench-contract.v1") =>
        new(
            schemaVersion,
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Stable local command surface that Codex CLI can call through visible terminal commands.",
            CommandContracts);

    private static WorkbenchReceiptIndex CreateReceiptIndex() =>
        new(
            "workbench-receipts.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Stable receipt schemas, write triggers, path patterns, dry-run commands, and review commands.",
            ReceiptContracts);

    private static WorkbenchExtensionIndex CreateExtensionIndex() =>
        new(
            "workbench-extensions.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Terminal-first extension points and their validation or dry-run commands.",
            ExtensionPoints);

    private static WorkbenchOutputIndex CreateOutputIndex() =>
        new(
            "workbench-outputs.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Readable table, compact JSON schema, and Markdown output contracts for key terminal paths.",
            OutputContracts);

    private static WorkbenchSafeguardIndex CreateSafeguardIndex() =>
        new(
            "workbench-safeguards.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Dry-run, approval, receipt, and review paths for risky terminal commands.",
            SafeguardContracts);

    private static WorkbenchHandoffReport CreateHandoffReport(string projectDirectory)
    {
        var verification = CreateVerification(projectDirectory);
        var project = CreateProjectInventory(projectDirectory);
        var readinessActions = CreateReadinessActions(projectDirectory, project);
        var commands = CreateHandoffCommands(projectDirectory);
        var notes = new[]
        {
            "Start with read-only workbench commands before live Revit operations.",
            "Use dry-run and approval flags before any write, export, bundle, prune, or workflow run.",
            "This handoff has no dashboard, cloud sync, MCP, or embedded language-runtime dependency."
        };

        return new WorkbenchHandoffReport(
            "workbench-handoff.v1",
            DateTimeOffset.UtcNow,
            projectDirectory,
            verification.Success,
            verification.CheckCount,
            verification.IssueCount,
            project.ArtifactCount,
            project.PresentCount,
            project.MissingCount,
            project.EmptyCount,
            verification.Checks,
            readinessActions,
            commands,
            notes);
    }

    private static IReadOnlyList<WorkbenchHandoffCommand> CreateHandoffCommands(string projectDirectory)
    {
        var workingDirectory = Path.GetFullPath(projectDirectory);
        WorkbenchHandoffCommand Command(string phase, string commandLine, string purpose) =>
            new(phase, commandLine, workingDirectory, purpose);

        return new[]
        {
            Command(
                "verify",
                $"revitcli workbench verify{ProjectDirOption(projectDirectory)} --output json",
                "Confirm the local command contract, output schemas, safeguards, receipts, and non-goals."),
            Command(
                "project",
                $"revitcli workbench project{ProjectDirOption(projectDirectory)} --output json",
                "Inspect local profiles, standards, workflows, receipts, history, journal, deliveries, plans, and reports."),
            Command(
                "paths",
                "revitcli workbench paths --output json",
                "Choose concrete callable command paths without scraping help text."),
            Command(
                "receipts",
                "revitcli workbench receipts --output json",
                "Check which write/export paths produce reviewable receipts."),
            Command(
                "safeguards",
                "revitcli workbench safeguards --output json",
                "Review dry-run, approval, receipt, and review commands for risky paths."),
            Command(
                "schedule-create",
                "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json",
                "Preview ViewSchedule writes through the schedule-create.v1 contract before any real create."),
            Command(
                "outputs",
                "revitcli workbench outputs --output json",
                "See the table, JSON schema, and Markdown output contracts available to scripts."),
            Command(
                "examples",
                "revitcli examples workbench --output markdown",
                "Open copy-paste workbench recipes for recurring architect tasks."),
            Command(
                "workflow-discovery",
                $"revitcli inspect workflows{ProjectDirOption(projectDirectory)} --output markdown",
                "Discover local workflow YAML files and next validate/simulate/review/dry-run/receipt commands."),
            Command(
                "plan-discovery",
                $"revitcli inspect plans{ProjectDirOption(projectDirectory)} --output markdown",
                "Discover saved mutation plans and next show/dry-run/apply/rollback-preview commands."),
            Command(
                "workflow-review",
                $"revitcli workflow review .revitcli/workflows/pre-issue.yml{ProjectDirOption(projectDirectory)} --output markdown",
                "Review approval gates and acceptance evidence before any workflow run.")
        };
    }

    private static IReadOnlyList<WorkbenchReadinessAction> CreateReadinessActions(
        string projectDirectory,
        WorkbenchProjectInventory project)
    {
        var actions = new List<WorkbenchReadinessAction>();
        foreach (var artifact in project.Artifacts.Where(artifact =>
            !string.Equals(artifact.Status, "present", StringComparison.OrdinalIgnoreCase)))
        {
            switch (artifact.Name)
            {
                case "profile":
                    actions.Add(new(
                        "bootstrap-profile",
                        artifact.Name,
                        artifact.Status,
                        "revitcli init architectural",
                        projectDirectory,
                        "Create a starter profile before profile-driven checks, publish pipelines, and safety defaults."));
                    break;
                case "standards":
                    actions.Add(new(
                        "review-standards",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli standards validate{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Review local standards readiness before standards-guided workflows."));
                    break;
                case "workflows":
                    actions.Add(new(
                        "bootstrap-workflows",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli workflow init all{ProjectDirOption(projectDirectory)}",
                        projectDirectory,
                        "Install built-in delivery workflows before workflow review or workflow run."));
                    break;
                case "workflow-receipts":
                    actions.Add(new(
                        "review-workflow-receipts",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli workflow receipts{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Check whether workflow-run evidence exists for failed, recent, or slow workflow triage."));
                    break;
                case "history":
                    actions.Add(new(
                        "initialize-history",
                        artifact.Name,
                        artifact.Status,
                        "revitcli history init --dir .revitcli/history",
                        projectDirectory,
                        "Initialize local model history before trend, diff, and model-health review."));
                    break;
                case "journal":
                    actions.Add(new(
                        "review-journal",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli journal review{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Review operation journal evidence before drafting handoff notes."));
                    break;
                case "delivery-manifest":
                    actions.Add(new(
                        "verify-delivery-manifest",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli deliverables verify{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Verify delivery manifest readiness and missing receipt evidence."));
                    break;
                case "delivery-receipts":
                    actions.Add(new(
                        "verify-delivery-receipts",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli deliverables verify{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Verify delivery receipts before packaging or reviewing handoff evidence."));
                    break;
                case "plans":
                    actions.Add(new(
                        "review-saved-plans",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli inspect plans{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Review saved mutation plans before plan apply; create one with --plan-output when none exist."));
                    break;
                case "reports":
                    actions.Add(new(
                        "draft-knowledge-report",
                        artifact.Name,
                        artifact.Status,
                        $"revitcli report knowledge{ProjectDirOption(projectDirectory)} --output markdown",
                        projectDirectory,
                        "Draft local knowledge and evidence notes from available history, journal, standards, and workflow signals."));
                    break;
            }
        }

        return actions;
    }

    private static bool IsReadinessActionableArtifact(string name) =>
        name is
            "profile" or
            "standards" or
            "workflows" or
            "workflow-receipts" or
            "history" or
            "journal" or
            "delivery-manifest" or
            "delivery-receipts" or
            "plans" or
            "reports";

    private static WorkbenchProjectInventory CreateProjectInventory(string projectDirectory)
    {
        var artifacts = new[]
        {
            FileArtifact(
                projectDirectory,
                "profile",
                "file",
                ".revitcli.yml",
                "revitcli profile validate",
                "Project profile for checks, publish pipelines, and safety defaults."),
            FileArtifact(
                projectDirectory,
                "standards",
                "file",
                Path.Combine(".revitcli", "standards.yml"),
                "revitcli standards validate --output json",
                "Installed local standards manifest."),
            DirectoryArtifact(
                projectDirectory,
                "workflows",
                "directory",
                Path.Combine(".revitcli", "workflows"),
                new[] { "*.yml", "*.yaml" },
                "revitcli workflow validate",
                "Reusable workflow YAML files."),
            DirectoryArtifact(
                projectDirectory,
                "workflow-receipts",
                "directory",
                Path.Combine(".revitcli", "workflows", "receipts"),
                new[] { "*.json" },
                "revitcli workflow receipts --output json",
                "Saved workflow-run receipts."),
            DirectoryArtifact(
                projectDirectory,
                "history",
                "directory",
                Path.Combine(".revitcli", "history"),
                new[] { "snapshot-*.json.gz" },
                "revitcli history list --limit 5",
                "Local model snapshot timeline."),
            FileArtifact(
                projectDirectory,
                "journal",
                "file",
                Path.Combine(".revitcli", "journal.jsonl"),
                "revitcli journal review --output json",
                "Local operation journal."),
            FileArtifact(
                projectDirectory,
                "delivery-manifest",
                "file",
                Path.Combine(".revitcli", "deliveries", "manifest.jsonl"),
                "revitcli deliverables list --output json",
                "Delivery manifest entries for exported outputs."),
            DirectoryArtifact(
                projectDirectory,
                "delivery-receipts",
                "directory",
                Path.Combine(".revitcli", "receipts"),
                new[] { "*.json" },
                "revitcli deliverables verify --output json",
                "Publish and delivery receipts."),
            DirectoryArtifact(
                projectDirectory,
                "plans",
                "directory",
                Path.Combine(".revitcli", "plans"),
                new[] { "*.json" },
                "revitcli plan show <plan-file> --output markdown",
                "Saved mutation plans."),
            DirectoryArtifact(
                projectDirectory,
                "reports",
                "directory",
                Path.Combine(".revitcli", "reports"),
                new[] { "*.*" },
                "revitcli report knowledge --output markdown",
                "Saved local reports and review handoffs.")
        };

        return new WorkbenchProjectInventory(
            "workbench-project.v1",
            DateTimeOffset.UtcNow,
            projectDirectory,
            artifacts);
    }

    private static WorkbenchProjectArtifact FileArtifact(
        string projectDirectory,
        string name,
        string kind,
        string relativePath,
        string reviewCommand,
        string notes)
    {
        var path = Path.Combine(projectDirectory, relativePath);
        if (!File.Exists(path))
        {
            return new(name, kind, NormalizePath(relativePath), "missing", 0, reviewCommand, notes);
        }

        var count = relativePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? CountNonEmptyLines(path)
            : 1;
        var status = count == 0 ? "empty" : "present";
        return new(name, kind, NormalizePath(relativePath), status, count, reviewCommand, notes);
    }

    private static WorkbenchProjectArtifact DirectoryArtifact(
        string projectDirectory,
        string name,
        string kind,
        string relativePath,
        IReadOnlyList<string> patterns,
        string reviewCommand,
        string notes)
    {
        var path = Path.Combine(projectDirectory, relativePath);
        if (!Directory.Exists(path))
        {
            return new(name, kind, NormalizePath(relativePath), "missing", 0, reviewCommand, notes);
        }

        var count = patterns
            .SelectMany(pattern => Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new(name, kind, NormalizePath(relativePath), count == 0 ? "empty" : "present", count, reviewCommand, notes);
    }

    private static int CountNonEmptyLines(string path)
    {
        try
        {
            return File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return 0;
        }
    }

    private static bool SheetPlanReceiptShapeReady(WorkbenchSafeguardIndex safeguardIndex)
    {
        var sheetSafeguards = safeguardIndex.Safeguards
            .Where(safeguard =>
                string.Equals(safeguard.Name, "sheet-issue-meta", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(safeguard.Name, "sheet-renumber", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sheetSafeguards.Length != 2 ||
            sheetSafeguards.Any(safeguard =>
                !safeguard.Receipt.Contains("plan-receipt.v1", StringComparison.OrdinalIgnoreCase) ||
                !safeguard.ReviewCommand.Contains("plan show", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return SerializedSheetReceiptShapeReady("sheet-issue", "Sheet Issue Date", "2026-05-01", "2026-05-20") &&
               SerializedSheetReceiptShapeReady("sheet-renumber", "Sheet Number", "TMP-001", "A-101");
    }

    private static bool NumberingPlanReceiptShapeReady(WorkbenchSafeguardIndex safeguardIndex)
    {
        var numberingSafeguards = safeguardIndex.Safeguards
            .Where(safeguard =>
                string.Equals(safeguard.Name, "rooms-renumber", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(safeguard.Name, "marks-assign", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (numberingSafeguards.Length != 2 ||
            numberingSafeguards.Any(safeguard =>
                !safeguard.Receipt.Contains("plan-receipt.v1", StringComparison.OrdinalIgnoreCase) ||
                !safeguard.ReviewCommand.Contains("plan show", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return SerializedNumberingReceiptShapeReady(
                   "room-numbering",
                   "Number",
                   ".revitcli/numbering/rooms.yml",
                   Array.Empty<string>()) &&
               SerializedNumberingReceiptShapeReady(
                   "mark-assignment",
                   "Mark",
                   ".revitcli/numbering/doors.yml",
                   new[] { "level", "zone", "type", "location" });
    }

    private static bool SerializedSheetReceiptShapeReady(
        string operation,
        string param,
        string oldValue,
        string newValue)
    {
        var receipt = new PlanReceipt
        {
            Operation = operation,
            PlanPath = $".revitcli/plans/{operation}.json",
            ModelPath = "D:/models/tower.rvt",
            DocumentName = "tower.rvt",
            DocumentVersion = "2026",
            Affected = 1,
            AffectedElementIds = new List<long> { 10 },
            RequiresRollback = true,
            PlanActionCount = 1,
            SkippedCount = 0,
            Param = param,
            RollbackActions = new List<PlanReceiptRollbackAction>
            {
                new()
                {
                    ElementId = 10,
                    Param = param,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Source = operation
                }
            }
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(receipt, SetPlanFileStore.JsonOptions));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "plan-receipt.v1" ||
            root.GetProperty("operation").GetString() != operation ||
            string.IsNullOrWhiteSpace(root.GetProperty("modelPath").GetString()) ||
            string.IsNullOrWhiteSpace(root.GetProperty("documentName").GetString()) ||
            string.IsNullOrWhiteSpace(root.GetProperty("documentVersion").GetString()) ||
            !root.GetProperty("requiresRollback").GetBoolean() ||
            root.GetProperty("planActionCount").GetInt32() != 1 ||
            root.GetProperty("skippedCount").GetInt32() != 0 ||
            root.GetProperty("affectedElementIds").EnumerateArray().All(id => id.GetInt64() != 10))
        {
            return false;
        }

        var rollback = root.GetProperty("rollbackActions").EnumerateArray().SingleOrDefault();
        return rollback.ValueKind == JsonValueKind.Object &&
               rollback.GetProperty("elementId").GetInt64() == 10 &&
               rollback.GetProperty("param").GetString() == param &&
               rollback.GetProperty("oldValue").GetString() == oldValue &&
               rollback.GetProperty("newValue").GetString() == newValue &&
               rollback.GetProperty("source").GetString() == operation;
    }

    private static bool SerializedNumberingReceiptShapeReady(
        string operation,
        string param,
        string rulePath,
        IReadOnlyList<string> sort)
    {
        var receipt = new PlanReceipt
        {
            Operation = operation,
            PlanPath = $".revitcli/plans/{operation}.json",
            ModelPath = "D:/models/tower.rvt",
            DocumentName = "tower.rvt",
            DocumentVersion = "2026",
            RulePath = rulePath,
            Sort = sort.ToList(),
            PlanActionCount = 1,
            SkippedCount = 0,
            Affected = 1,
            AffectedElementIds = new List<long> { 20 },
            RequiresRollback = true,
            Param = param,
            RollbackActions = new List<PlanReceiptRollbackAction>
            {
                new()
                {
                    ElementId = 20,
                    Param = param,
                    OldValue = "OLD",
                    NewValue = "NEW",
                    Source = operation
                }
            }
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(receipt, SetPlanFileStore.JsonOptions));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "plan-receipt.v1" ||
            root.GetProperty("operation").GetString() != operation ||
            root.GetProperty("rulePath").GetString() != rulePath ||
            root.GetProperty("planActionCount").GetInt32() != 1 ||
            root.GetProperty("skippedCount").GetInt32() != 0 ||
            !root.GetProperty("requiresRollback").GetBoolean() ||
            root.GetProperty("affectedElementIds").EnumerateArray().All(id => id.GetInt64() != 20))
        {
            return false;
        }

        var actualSort = root.GetProperty("sort").EnumerateArray()
            .Select(item => item.GetString() ?? "")
            .ToArray();
        if (!actualSort.SequenceEqual(sort))
        {
            return false;
        }

        var rollback = root.GetProperty("rollbackActions").EnumerateArray().SingleOrDefault();
        return rollback.ValueKind == JsonValueKind.Object &&
               rollback.GetProperty("elementId").GetInt64() == 20 &&
               rollback.GetProperty("param").GetString() == param &&
               rollback.GetProperty("oldValue").GetString() == "OLD" &&
               rollback.GetProperty("newValue").GetString() == "NEW" &&
               rollback.GetProperty("source").GetString() == operation;
    }

    private static WorkbenchPathIndex CreatePathIndex()
    {
        var paths = CommandContracts
            .SelectMany(command => command.CommandPaths.Select(path => new WorkbenchCallablePath(
                path,
                $"revitcli {path}",
                command.Name,
                command.Risk,
                command.SupportsJson,
                command.SupportsMarkdown,
                command.DryRun,
                command.Receipt,
                command.RecommendedFirstCommand,
                command.ExitCodeNotes)))
            .OrderBy(path => path.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkbenchPathIndex(
            "workbench-paths.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Flat callable command paths with risk, output, dry-run, receipt, and exit-code notes.",
            paths);
    }

    private static WorkbenchExitCodeIndex CreateExitCodeIndex()
    {
        var commands = CommandContracts
            .Select(command => new WorkbenchExitCodeContract(
                command.Name,
                command.CommandPaths,
                command.Risk,
                new[] { "0" },
                new[] { "1", "non-zero" },
                command.ExitCodeNotes,
                command.RecommendedFirstCommand))
            .OrderBy(command => command.Command, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WorkbenchExitCodeIndex(
            "workbench-exit-codes.v1",
            DateTimeOffset.UtcNow,
            "RevitCli Architect Terminal BIM Workbench",
            "Predictable exit-code notes for Codex-callable terminal commands.",
            commands);
    }

    private static WorkbenchVerification CreateVerification(
        string projectDirectory,
        string contractSchema = "workbench-contract.v1",
        string verificationSchema = "workbench-verification.v1")
    {
        var checks = BuildVerificationChecks(projectDirectory).ToArray();
        var issueCount = checks.Count(check => check.Status != "pass");

        return new WorkbenchVerification(
            verificationSchema,
            contractSchema,
            DateTimeOffset.UtcNow,
            projectDirectory,
            issueCount == 0,
            checks.Length,
            issueCount,
            CommandContracts.Length,
            ReceiptContracts.Length,
            ExamplesCommand.TopicNames.Length,
            checks);
    }

    private static IEnumerable<WorkbenchCheckResult> BuildVerificationChecks(string projectDirectory)
    {
        var rootCommands = CliCommandCatalog.TopLevelCommandNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contractCommands = CommandContracts.Select(command => command.Name).ToArray();
        var contractCommandSet = contractCommands.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var coreCommands = new[]
        {
            "inspect", "query", "plan", "publish", "report", "standards",
            "family", "history", "journal"
        };
        var missingCore = coreCommands
            .Where(command => !contractCommandSet.Contains(command))
            .ToArray();
        yield return Check(
            "core-command-contract",
            missingCore.Length == 0,
            missingCore.Length == 0
                ? $"Core v4 commands covered: {string.Join(", ", coreCommands)}"
                : $"Missing core commands: {string.Join(", ", missingCore)}");

        var missingRoot = contractCommands
            .Where(command => !rootCommands.Contains(command))
            .ToArray();
        yield return Check(
            "contract-root-alignment",
            missingRoot.Length == 0,
            missingRoot.Length == 0
                ? "Every workbench contract command exists in the public top-level command catalog."
                : $"Contract commands missing from root catalog: {string.Join(", ", missingRoot)}");

        var commandPaths = CommandContracts
            .SelectMany(command => command.CommandPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredCommandPaths = new[]
        {
            "workbench contract",
            "workbench contract --contract workbench-contract.v2",
            "workbench verify",
            "workbench verify --contract workbench-contract.v2",
            "workbench receipts",
            "workbench paths",
            "workbench exits",
            "workbench extensions",
            "workbench outputs",
            "workbench safeguards",
            "workbench project",
            "workbench handoff",
            "release verify --strict",
            "examples workbench",
            "examples workflow",
            "score --history",
            "inspect workflows",
            "schedules ensure",
            "schedules batch-export",
            "schedules compare",
            "views audit",
            "views template-apply",
            "views clone-set",
            "links audit",
            "links repair",
            "model map-check",
            "model map-fix",
            "schedule create",
            "sheets renumber",
            "rooms renumber",
            "marks assign",
            "marks verify",
            "workflow review",
            "workflow run",
            "plan apply",
            "deliverables bundle",
            "issue preflight",
            "issue diff",
            "issue package",
            "standards validate",
            "family purge",
            "journal review",
            "report knowledge",
            "ledger append",
            "ledger replay",
            "ledger query",
            "ledger validate",
            "ledger stats",
            "ledger timeline"
        };
        var missingCommandPaths = requiredCommandPaths
            .Where(path => !commandPaths.Contains(path))
            .ToArray();
        yield return Check(
            "callable-command-paths",
            missingCommandPaths.Length == 0,
            missingCommandPaths.Length == 0
                ? $"Callable command paths covered: {requiredCommandPaths.Length}"
                : $"Missing callable command paths: {string.Join(", ", missingCommandPaths)}");

        var manualOnlyCommandPaths = Array.Empty<string>();
        var exposedManualOnlyPaths = manualOnlyCommandPaths
            .Where(path => commandPaths.Contains(path))
            .ToArray();
        yield return Check(
            "manual-only-path-exclusion",
            exposedManualOnlyPaths.Length == 0,
            exposedManualOnlyPaths.Length == 0
                ? "Manual-only command paths without dry-run receipts are excluded from the Codex callable path index."
                : $"Manual-only command paths exposed: {string.Join(", ", exposedManualOnlyPaths)}");

        var exposesMcp = contractCommandSet.Contains("mcp") ||
                         rootCommands.Contains("mcp") ||
                         commandPaths.Any(path => path.StartsWith("mcp", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "mcp-public-exclusion",
            !exposesMcp,
            exposesMcp
                ? "MCP appeared in the public workbench contract or top-level catalog."
                : "MCP remains excluded from the public terminal workbench contract.");

        var legacyMcpReady = IsLegacyMcpHiddenAndDeprecated(out var legacyMcpEvidence);
        yield return Check(
            "legacy-mcp-hidden",
            legacyMcpReady,
            legacyMcpEvidence);

        var publicContractSurface = contractCommands
            .Concat(commandPaths)
            .ToArray();
        var llmRuntimeTerms = new[] { "llm", "openai", "agent", "chat", "prompt" };
        var exposedLlmRuntimeTerms = publicContractSurface
            .Where(path => llmRuntimeTerms.Any(term =>
                path.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        yield return Check(
            "llm-runtime-exclusion",
            exposedLlmRuntimeTerms.Length == 0,
            exposedLlmRuntimeTerms.Length == 0
                ? "No LLM runtime, prompt layer, agent, or chat command is exposed in the workbench contract."
                : $"LLM-like command paths exposed: {string.Join(", ", exposedLlmRuntimeTerms)}");

        var exposesDashboardDependency = publicContractSurface
            .Any(path => path.StartsWith("dashboard", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "dashboard-dependency-exclusion",
            !exposesDashboardDependency,
            exposesDashboardDependency
                ? "Dashboard command paths appeared in the terminal workbench contract."
                : "Dashboard remains outside the Codex-callable terminal workbench contract.");

        var cloudSyncTerms = new[] { "cloud", "saas", "acc", "sync" };
        var exposedCloudSyncTerms = publicContractSurface
            .Where(path => cloudSyncTerms.Any(term =>
                path.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        yield return Check(
            "cloud-sync-exclusion",
            exposedCloudSyncTerms.Length == 0,
            exposedCloudSyncTerms.Length == 0
                ? "No SaaS, cloud sync, or ACC command path is exposed in the workbench contract."
                : $"Cloud-sync command paths exposed: {string.Join(", ", exposedCloudSyncTerms)}");

        var jsonCommandCount = CommandContracts.Count(command => command.SupportsJson);
        yield return Check(
            "machine-readable-command-surface",
            jsonCommandCount >= 12,
            $"{jsonCommandCount} contract commands expose JSON output for Codex CLI.");

        var recipeFormats = ExamplesCommand.OutputFormats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredRecipeTopics = new[] { "workflow", "deliverables", "standards", "family" };
        var recipeTopics = ExamplesCommand.TopicNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingRecipeTopics = requiredRecipeTopics
            .Where(topic => !recipeTopics.Contains(topic))
            .ToArray();
        var recipesReady = recipeFormats.Contains("json") &&
                           recipeFormats.Contains("markdown") &&
                           missingRecipeTopics.Length == 0;
        yield return Check(
            "example-recipe-surface",
            recipesReady,
            recipesReady
                ? "Example recipes expose JSON/Markdown and cover workflow, deliverables, standards, and family tasks."
                : $"Recipe surface incomplete; missing topics: {string.Join(", ", missingRecipeTopics)}");

        var scoreContract = CommandContracts.FirstOrDefault(command =>
            string.Equals(command.Name, "score", StringComparison.OrdinalIgnoreCase));
        var scoreFormats = ScoreCommand.OutputFormats.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modelHealthReady = scoreContract is { SupportsJson: true, SupportsMarkdown: true } &&
                               scoreFormats.Contains("json") &&
                               scoreFormats.Contains("markdown") &&
                               commandPaths.Contains("score --history");
        yield return Check(
            "model-health-terminal-surface",
            modelHealthReady,
            modelHealthReady
                ? $"Model health is covered through {ScoreCommand.HistorySchemaVersion} JSON/Markdown history output."
                : "Model health is missing score contract, JSON/Markdown formats, or score --history callable path.");

        var riskyWithoutSafety = CommandContracts
            .Where(command => command.Risk is "write" or "export" or "mixed" or "local-write")
            .Where(command =>
                string.Equals(command.DryRun, "none", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(command.Receipt, "none", StringComparison.OrdinalIgnoreCase))
            .Select(command => command.Name)
            .ToArray();
        yield return Check(
            "risky-command-safety",
            riskyWithoutSafety.Length == 0,
            riskyWithoutSafety.Length == 0
                ? "Risky commands advertise dry-run and/or receipt evidence."
                : $"Risky commands missing dry-run or receipt evidence: {string.Join(", ", riskyWithoutSafety)}");

        var requiredReceiptSchemas = new[]
        {
            "export-receipt.v1",
            "publish-receipt.v1",
            "plan-receipt.v1",
            "workflow-run-receipt.v1",
            "delivery-bundle-receipt.v1",
            "issue-package-receipt.v1",
            "schedule-create-receipt.v1"
        };
        var receiptSchemas = ReceiptContracts
            .Select(receipt => receipt.SchemaVersion)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingReceiptSchemas = requiredReceiptSchemas
            .Where(schema => !receiptSchemas.Contains(schema))
            .ToArray();
        yield return Check(
            "receipt-index-surface",
            missingReceiptSchemas.Length == 0,
            missingReceiptSchemas.Length == 0
                ? $"Receipt index covers {requiredReceiptSchemas.Length} write/export evidence schemas."
                : $"Receipt index missing schemas: {string.Join(", ", missingReceiptSchemas)}");

        var extensionIndex = CreateExtensionIndex();
        var requiredExtensionPoints = new[]
        {
            "project-profile", "workflow-yaml", "standards-pack", "family-rules"
        };
        var extensionPointNames = extensionIndex.Extensions
            .Select(extension => extension.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingExtensionPoints = requiredExtensionPoints
            .Where(extension => !extensionPointNames.Contains(extension))
            .ToArray();
        var extensionCommandsStayTerminal =
            extensionIndex.Extensions.All(extension =>
                !extension.ValidationCommand.Contains("mcp", StringComparison.OrdinalIgnoreCase) &&
                !extension.DryRunCommand.Contains("mcp", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "extension-point-surface",
            missingExtensionPoints.Length == 0 && extensionCommandsStayTerminal,
            missingExtensionPoints.Length == 0 && extensionCommandsStayTerminal
                ? $"Extension index covers {extensionIndex.ExtensionCount} terminal-first extension points."
                : $"Extension point surface incomplete; missing: {string.Join(", ", missingExtensionPoints)}");

        var outputIndex = CreateOutputIndex();
        var requiredOutputSchemas = new[]
        {
            "workbench-contract.v1",
            "workbench-verification.v1",
            "workbench-verify-report.v2",
            "workbench-paths.v1",
            "workbench-receipts.v1",
            "workbench-exit-codes.v1",
            "workbench-extensions.v1",
            "workbench-outputs.v1",
            "workbench-safeguards.v1",
            "workbench-project.v1",
            "workbench-handoff.v1",
            "inspect-workflows.v1",
            "inspect-plans.v1",
            "schedule-ensure-plan.v1",
            "schedule-export-manifest.v1",
            "schedule-diff-report.v1",
            "view-standards-report.v1",
            "view-template-plan.v1",
            "view-clone-plan.v1",
            "link-audit-report.v1",
            "link-repair-plan.v1",
            "model-map-report.v1",
            "model-map-fix-plan.v1",
            "issue-preflight-report.v1",
            "issue-diff-report.v1",
            "issue-package-receipt.v1",
            "workbench-contract.v2",
            "schedule-create.v1",
            "workflow-review.v1",
            "workflow-registry.v1",
            "workflow-receipts.v1",
            "example-recipes.v1",
            "model-health-history.v1",
            "knowledge-report.v1",
            "ledger-query.v1",
            "ledger-validate.v1",
            "ledger-stats.v1",
            "ledger-timeline.v1"
        };
        var outputSchemas = outputIndex.Outputs
            .Select(output => output.JsonSchema)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingOutputSchemas = requiredOutputSchemas
            .Where(schema => !outputSchemas.Contains(schema))
            .ToArray();
        var outputFormatsReady = outputIndex.Outputs.All(output =>
            output.SupportsTable &&
            output.SupportsMarkdown &&
            !string.IsNullOrWhiteSpace(output.JsonSchema));
        yield return Check(
            "output-contract-surface",
            missingOutputSchemas.Length == 0 && outputFormatsReady,
            missingOutputSchemas.Length == 0 && outputFormatsReady
                ? $"Output index covers {outputIndex.OutputCount} table/JSON/Markdown contracts."
                : $"Output contract surface incomplete; missing schemas: {string.Join(", ", missingOutputSchemas)}");

        var completionIssues = new List<string>();
        AddMissingCompletionValues(
            completionIssues,
            "workbench subcommands",
            CompletionsCommand.WorkbenchCompletionSubcommands,
            new[] { "contract", "verify", "receipts", "paths", "exits", "extensions", "outputs", "safeguards", "project", "handoff" });
        AddMissingCompletionValues(
            completionIssues,
            "workbench options",
            CompletionsCommand.WorkbenchCompletionOptions,
            new[] { "--dir", "--output", "--contract" });
        AddMissingCompletionValues(
            completionIssues,
            "workbench output formats",
            CompletionsCommand.WorkbenchCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "inspect subcommands",
            CompletionsCommand.InspectCompletionSubcommands,
            new[] { "categories", "params", "schedules", "sheets", "workflows", "plans" });
        AddMissingCompletionValues(
            completionIssues,
            "inspect options",
            CompletionsCommand.InspectCompletionOptions,
            new[]
            {
                "--output", "--dir", "--include-empty", "--category", "--name",
                "--writable-only", "--missing-only", "--ready-only", "--empty-only",
                "--sheets", "--issues-only"
            });
        AddMissingCompletionValues(
            completionIssues,
            "inspect output formats",
            CompletionsCommand.InspectCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "inspect output formats",
            CompletionsCommand.InspectCompletionOutputFormats,
            new[] { "csv", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "workflow subcommands",
            CompletionsCommand.WorkflowCompletionSubcommands,
            new[] { "validate", "simulate", "review", "registry", "run", "suggest", "examples", "receipts" });
        AddMissingCompletionValues(
            completionIssues,
            "workflow receipt options",
            CompletionsCommand.WorkflowCompletionOptions,
            new[] { "--limit", "--failed-only", "--name", "--min-duration-ms", "--sort", "--window" });
        AddMissingCompletionValues(
            completionIssues,
            "workflow run options",
            CompletionsCommand.WorkflowCompletionOptions,
            new[] { "--dry-run", "--yes", "--continue-on-error", "--timeout-ms" });
        AddMissingCompletionValues(
            completionIssues,
            "workflow report output formats",
            CompletionsCommand.WorkflowReportCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "workflow report output formats",
            CompletionsCommand.WorkflowReportCompletionOutputFormats,
            new[] { "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "workflow suggest output formats",
            CompletionsCommand.WorkflowSuggestCompletionOutputFormats,
            new[] { "table", "json", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "schedules subcommands",
            CompletionsCommand.SchedulesCompletionSubcommands,
            new[] { "ensure", "batch-export", "compare" });
        AddMissingCompletionValues(
            completionIssues,
            "schedules options",
            CompletionsCommand.SchedulesCompletionOptions,
            new[] { "--spec", "--plan-output", "--dry-run", "--mode", "--set", "--output-dir", "--format", "--manifest", "--from", "--to", "--keys", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "schedules output formats",
            CompletionsCommand.SchedulesCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "schedules output formats",
            CompletionsCommand.SchedulesCompletionOutputFormats,
            new[] { "csv", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "schedules modes",
            CompletionsCommand.SchedulesCompletionModes,
            new[] { "create-only", "sync-fields" });
        AddMissingCompletionValues(
            completionIssues,
            "views subcommands",
            CompletionsCommand.ViewsCompletionSubcommands,
            new[] { "audit", "template-apply", "clone-set" });
        AddMissingCompletionValues(
            completionIssues,
            "views options",
            CompletionsCommand.ViewsCompletionOptions,
            new[] { "--rules", "--templates", "--browser", "--selector", "--template", "--plan-output", "--dry-run", "--exclude", "--from-set", "--to-prefix", "--naming-rule", "--include-sheets", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "views output formats",
            CompletionsCommand.ViewsCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "views output formats",
            CompletionsCommand.ViewsCompletionOutputFormats,
            new[] { "csv", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "views exclude values",
            CompletionsCommand.ViewsCompletionExcludeValues,
            new[] { "locked" });
        AddMissingCompletionValues(
            completionIssues,
            "links subcommands",
            CompletionsCommand.LinksCompletionSubcommands,
            new[] { "audit", "repair" });
        AddMissingCompletionValues(
            completionIssues,
            "links options",
            CompletionsCommand.LinksCompletionOptions,
            new[] { "--rules", "--check", "--map", "--plan-output", "--dry-run", "--max-changes", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "links output formats",
            CompletionsCommand.LinksCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "links checks",
            CompletionsCommand.LinksCompletionCheckValues,
            new[] { "paths", "loaded", "coordinates" });
        AddMissingCompletionValues(
            completionIssues,
            "model subcommands",
            CompletionsCommand.ModelCompletionSubcommands,
            new[] { "map-check", "map-fix" });
        AddMissingCompletionValues(
            completionIssues,
            "model options",
            CompletionsCommand.ModelCompletionOptions,
            new[] { "--against", "--worksets", "--phases", "--plan-output", "--scope", "--dry-run", "--max-changes", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "model output formats",
            CompletionsCommand.ModelCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "model scope values",
            CompletionsCommand.ModelCompletionScopeValues,
            new[] { "rooms", "doors", "walls", "all" });
        AddMissingCompletionValues(
            completionIssues,
            "schedule subcommands",
            CompletionsCommand.ScheduleCompletionSubcommands,
            new[] { "list", "export", "create" });
        AddMissingCompletionValues(
            completionIssues,
            "schedule create options",
            CompletionsCommand.ScheduleCompletionOptions,
            new[] { "--dry-run", "--receipt-dir" });
        AddMissingCompletionValues(
            completionIssues,
            "schedule list output formats",
            CompletionsCommand.ScheduleListCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "schedule list output formats",
            CompletionsCommand.ScheduleListCompletionOutputFormats,
            new[] { "csv" });
        AddMissingCompletionValues(
            completionIssues,
            "schedule export output formats",
            CompletionsCommand.ScheduleExportCompletionOutputFormats,
            new[] { "table", "json", "csv", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "schedule create output formats",
            CompletionsCommand.ScheduleCreateCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "schedule create output formats",
            CompletionsCommand.ScheduleCreateCompletionOutputFormats,
            new[] { "csv" });
        AddMissingCompletionValues(
            completionIssues,
            "rooms subcommands",
            CompletionsCommand.RoomsCompletionSubcommands,
            new[] { "renumber" });
        AddMissingCompletionValues(
            completionIssues,
            "rooms options",
            CompletionsCommand.RoomsCompletionOptions,
            new[] { "--rule", "--plan-output", "--scope", "--dry-run", "--max-changes", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "rooms output formats",
            CompletionsCommand.RoomsCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "rooms output formats",
            CompletionsCommand.RoomsCompletionOutputFormats,
            new[] { "csv", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "marks subcommands",
            CompletionsCommand.MarksCompletionSubcommands,
            new[] { "assign", "verify" });
        AddMissingCompletionValues(
            completionIssues,
            "marks options",
            CompletionsCommand.MarksCompletionOptions,
            new[] { "--category", "--rule", "--plan-output", "--sort", "--dry-run", "--max-changes", "--against", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "marks output formats",
            CompletionsCommand.MarksCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "marks output formats",
            CompletionsCommand.MarksCompletionOutputFormats,
            new[] { "csv", "yaml" });
        AddMissingCompletionValues(
            completionIssues,
            "issue subcommands",
            CompletionsCommand.IssueCompletionSubcommands,
            new[] { "preflight", "diff", "package" });
        AddMissingCompletionValues(
            completionIssues,
            "issue options",
            CompletionsCommand.IssueCompletionOptions,
            new[] { "--profile", "--output", "--fail-on", "--from", "--to", "--review", "--report", "--max-rows", "--bundle-path", "--dry-run", "--sign-journal", "--include-receipts" });
        AddMissingCompletionValues(
            completionIssues,
            "issue output formats",
            CompletionsCommand.IssueCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger subcommands",
            CompletionsCommand.LedgerCompletionSubcommands,
            new[] { "query", "validate", "stats", "timeline" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger options",
            CompletionsCommand.LedgerCompletionOptions,
            new[] { "--dir", "--source", "--since", "--until", "--window", "--action", "--category", "--operator", "--receipt-status", "--limit", "--fail-on", "--bucket", "--output" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger output formats",
            CompletionsCommand.LedgerCompletionOutputFormats,
            new[] { "table", "json", "markdown" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger sources",
            CompletionsCommand.LedgerCompletionSources,
            new[] { "all", "journal", "history", "deliveries", "workflows" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger receipt statuses",
            CompletionsCommand.LedgerCompletionReceiptStatuses,
            new[] { "all", "valid", "missing", "unreadable" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger fail-on values",
            CompletionsCommand.LedgerCompletionFailOnValues,
            new[] { "error", "warning" });
        AddMissingCompletionValues(
            completionIssues,
            "ledger bucket values",
            CompletionsCommand.LedgerCompletionBucketValues,
            new[] { "day", "hour" });
        AddUnexpectedCompletionValues(
            completionIssues,
            "ledger output formats",
            CompletionsCommand.LedgerCompletionOutputFormats,
            new[] { "csv", "yaml" });
        yield return Check(
            "completion-surface",
            completionIssues.Count == 0,
            completionIssues.Count == 0
                ? "Shell completions cover inspect, workbench, workflow, schedules, views, links, model, schedule, rooms, marks, issue, and ledger subcommands, options, and output-format contracts."
                : $"Completion surface incomplete: {string.Join("; ", completionIssues)}");

        var projectInventory = CreateProjectInventory(projectDirectory);
        var requiredProjectArtifacts = new[]
        {
            "profile", "standards", "workflows", "workflow-receipts", "history",
            "journal", "delivery-manifest", "delivery-receipts", "plans", "reports"
        };
        var projectArtifactNames = projectInventory.Artifacts
            .Select(artifact => artifact.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingProjectArtifacts = requiredProjectArtifacts
            .Where(artifact => !projectArtifactNames.Contains(artifact))
            .ToArray();
        var projectInventoryReady =
            string.Equals(projectInventory.SchemaVersion, "workbench-project.v1", StringComparison.OrdinalIgnoreCase) &&
            missingProjectArtifacts.Length == 0 &&
            projectInventory.Artifacts.All(artifact =>
                !string.IsNullOrWhiteSpace(artifact.RelativePath) &&
                !string.IsNullOrWhiteSpace(artifact.ReviewCommand));
        yield return Check(
            "project-inventory-surface",
            projectInventoryReady,
            projectInventoryReady
                ? $"Project inventory covers {projectInventory.ArtifactCount} local artifacts with review commands."
                : $"Project inventory surface incomplete; missing artifacts: {string.Join(", ", missingProjectArtifacts)}");

        var readinessActions = CreateReadinessActions(projectDirectory, projectInventory);
        var actionableIncompleteArtifactCount = projectInventory.Artifacts.Count(artifact =>
            IsReadinessActionableArtifact(artifact.Name) &&
            !string.Equals(artifact.Status, "present", StringComparison.OrdinalIgnoreCase));
        var readinessActionsReady =
            readinessActions.All(action =>
                !string.IsNullOrWhiteSpace(action.Phase) &&
                !string.IsNullOrWhiteSpace(action.Artifact) &&
                !string.IsNullOrWhiteSpace(action.CommandLine) &&
                !string.IsNullOrWhiteSpace(action.WorkingDirectory) &&
                !string.IsNullOrWhiteSpace(action.Reason)) &&
            (actionableIncompleteArtifactCount == 0 || readinessActions.Count > 0);
        yield return Check(
            "handoff-readiness-actions",
            readinessActionsReady,
            readinessActionsReady
                ? $"Workbench handoff maps {readinessActions.Count} actionable missing or empty project artifacts to next actions."
                : "Workbench handoff readiness actions are incomplete for actionable missing or empty project artifacts.");

        var handoffCommands = CreateHandoffCommands(projectDirectory);
        var requiredHandoffPhases = new[]
        {
            "verify",
            "project",
            "paths",
            "receipts",
            "safeguards",
            "schedule-create",
            "outputs",
            "examples",
            "workflow-discovery",
            "plan-discovery",
            "workflow-review"
        };
        var handoffPhases = handoffCommands
            .Select(command => command.Phase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingHandoffPhases = requiredHandoffPhases
            .Where(phase => !handoffPhases.Contains(phase))
            .ToArray();
        var handoffCommandsReady =
            missingHandoffPhases.Length == 0 &&
            handoffCommands.All(command =>
                !string.IsNullOrWhiteSpace(command.CommandLine) &&
                !string.IsNullOrWhiteSpace(command.WorkingDirectory) &&
                !string.IsNullOrWhiteSpace(command.Purpose)) &&
            handoffCommands.Any(command =>
                string.Equals(command.Phase, "plan-discovery", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("inspect plans", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("--output markdown", StringComparison.OrdinalIgnoreCase)) &&
            handoffCommands.Any(command =>
                string.Equals(command.Phase, "workflow-discovery", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("inspect workflows", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("--output markdown", StringComparison.OrdinalIgnoreCase)) &&
            handoffCommands.Any(command =>
                string.Equals(command.Phase, "schedule-create", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                command.CommandLine.Contains("--output json", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "handoff-command-surface",
            handoffCommandsReady,
            handoffCommandsReady
                ? $"Workbench handoff recommends {handoffCommands.Count} command phases including workflow-discovery, plan-discovery, and schedule-create."
                : $"Workbench handoff command surface incomplete; missing phases: {string.Join(", ", missingHandoffPhases)}");

        var safeguardIndex = CreateSafeguardIndex();
        var requiredSafeguards = new[]
        {
            "export", "publish", "plan-apply", "rollback", "workflow-run", "deliverables-bundle", "issue-package", "schedule-create", "schedules-ensure", "views-template-apply", "views-clone-set", "links-repair", "model-map-fix", "sheet-issue-meta", "sheet-renumber", "rooms-renumber", "marks-assign"
        };
        var safeguardNames = safeguardIndex.Safeguards
            .Select(safeguard => safeguard.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSafeguards = requiredSafeguards
            .Where(safeguard => !safeguardNames.Contains(safeguard))
            .ToArray();
        var dryRunCommandsReady = safeguardIndex.Safeguards.All(safeguard =>
            safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) ||
            safeguard.DryRunCommand.Contains("plan-output", StringComparison.OrdinalIgnoreCase) ||
            safeguard.DryRunCommand.Contains("simulate", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "safeguard-surface",
            missingSafeguards.Length == 0 && dryRunCommandsReady,
            missingSafeguards.Length == 0 && dryRunCommandsReady
                ? $"Safeguard index covers {safeguardIndex.SafeguardCount} dry-run/approval paths."
                : $"Safeguard surface incomplete; missing: {string.Join(", ", missingSafeguards)}");

        var scheduleCreateReady =
            commandPaths.Contains("schedule create") &&
            receiptSchemas.Contains("schedule-create-receipt.v1") &&
            outputSchemas.Contains("schedule-create.v1") &&
            safeguardNames.Contains("schedule-create") &&
            safeguardIndex.Safeguards.Any(safeguard =>
                string.Equals(safeguard.Name, "schedule-create", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                safeguard.Receipt.Contains("schedule-create", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "schedule-create-safety",
            scheduleCreateReady,
            scheduleCreateReady
                ? "Schedule create is callable only with dry-run, output, receipt, and safeguard contracts indexed."
                : "Schedule create is missing callable path, receipt schema, output schema, or safeguard coverage.");

        var scheduleSpecSchemaReady =
            typeof(SchedulesCommand.ScheduleSpecSet).GetProperty(nameof(SchedulesCommand.ScheduleSpecSet.SchemaVersion)) != null &&
            typeof(SchedulesCommand.ScheduleSpecSet).GetProperty(nameof(SchedulesCommand.ScheduleSpecSet.Schedules)) != null &&
            typeof(SchedulesCommand.ScheduleSpec).GetProperty(nameof(SchedulesCommand.ScheduleSpec.Fields)) != null &&
            typeof(SchedulesCommand.ScheduleSpec).GetProperty(nameof(SchedulesCommand.ScheduleSpec.Filter)) != null &&
            typeof(SchedulesCommand.ScheduleSpec).GetProperty(nameof(SchedulesCommand.ScheduleSpec.Sort)) != null &&
            typeof(SchedulesCommand.ScheduleSpec).GetProperty(nameof(SchedulesCommand.ScheduleSpec.KeyColumns)) != null;
        yield return Check(
            "schedule-spec-schema",
            scheduleSpecSchemaReady,
            scheduleSpecSchemaReady
                ? "schedule-spec.v1 exposes fields, filters, sort, and key-column contracts for validation."
                : "schedule-spec.v1 is missing fields, filters, sort, or key-column contracts.");

        var traceabilityRuntime = BuildTraceabilityRuntimeCheck();
        var scheduleExportTraceableReady =
            commandPaths.Contains("schedules batch-export") &&
            outputSchemas.Contains("schedule-export-manifest.v1") &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.Profile)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.ManifestPath)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.Command)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.ModelPath)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.DocumentName)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifest).GetProperty(nameof(SchedulesCommand.ScheduleExportManifest.DocumentVersion)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.ScheduleId)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.OutputPath)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.Bytes)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.Sha256)) != null &&
            traceabilityRuntime.ScheduleExport;
        yield return Check(
            "schedule-export-traceable",
            scheduleExportTraceableReady,
            scheduleExportTraceableReady
                ? $"Schedule batch exports write schedule-export-manifest.v1 entries with schedule ids, CSV paths, byte counts, SHA256 evidence, profile, command, and model/document identity when available; {traceabilityRuntime.Evidence}."
                : $"Schedule batch export is missing callable path, manifest schema, profile, command, model/document identity fields, schedule id, output path, byte-count, or populated SHA256 evidence. {traceabilityRuntime.Evidence}");

        var scheduleDiffTraceableReady =
            commandPaths.Contains("schedules compare") &&
            outputSchemas.Contains("schedule-diff-report.v1") &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.BeforePath)) != null &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.AfterPath)) != null &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.BeforeSha256)) != null &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.AfterSha256)) != null &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.BeforeBytes)) != null &&
            typeof(SchedulesCommand.ScheduleFileDiff).GetProperty(nameof(SchedulesCommand.ScheduleFileDiff.AfterBytes)) != null &&
            traceabilityRuntime.ScheduleDiff;
        yield return Check(
            "schedule-diff-traceable",
            scheduleDiffTraceableReady,
            scheduleDiffTraceableReady
                ? $"Schedule diff reports carry before/after file paths, bytes, and SHA256 evidence; {traceabilityRuntime.Evidence}."
                : $"Schedule diff reports are missing callable path, schema, before/after paths, bytes, or populated SHA256 evidence. {traceabilityRuntime.Evidence}");

        var scheduleEnsureRollbackReady =
            commandPaths.Contains("schedules ensure") &&
            outputSchemas.Contains("schedule-ensure-plan.v1") &&
            safeguardNames.Contains("schedules-ensure") &&
            typeof(SchedulesCommand.ScheduleEnsurePlan).GetProperty(nameof(SchedulesCommand.ScheduleEnsurePlan.Baselines)) != null &&
            typeof(SchedulesCommand.ScheduleEnsureBaseline).GetProperty(nameof(SchedulesCommand.ScheduleEnsureBaseline.FieldCount)) != null &&
            typeof(SchedulesCommand.ScheduleEnsureBaseline).GetProperty(nameof(SchedulesCommand.ScheduleEnsureBaseline.Fields)) != null &&
            typeof(SchedulesCommand.ScheduleEnsureBaseline).GetProperty(nameof(SchedulesCommand.ScheduleEnsureBaseline.Filter)) != null &&
            typeof(SchedulesCommand.ScheduleEnsureBaseline).GetProperty(nameof(SchedulesCommand.ScheduleEnsureBaseline.Sort)) != null;
        yield return Check(
            "schedule-ensure-rollback",
            scheduleEnsureRollbackReady,
            scheduleEnsureRollbackReady
                ? "Schedule ensure plans include old schedule baselines before any future structure write path."
                : "Schedule ensure is missing callable path, output schema, safeguard, or baseline shape.");

        var viewMutationPlanIdsFrozen =
            commandPaths.Contains("views template-apply") &&
            outputSchemas.Contains("view-template-plan.v1") &&
            safeguardNames.Contains("views-template-apply") &&
            typeof(ViewsCommand.ViewTemplatePlanAction).GetProperty(nameof(ViewsCommand.ViewTemplatePlanAction.ViewId)) != null &&
            typeof(ViewsCommand.ViewTemplatePlanAction).GetProperty(nameof(ViewsCommand.ViewTemplatePlanAction.OldTemplateId)) != null &&
            typeof(ViewsCommand.ViewTemplatePlanAction).GetProperty(nameof(ViewsCommand.ViewTemplatePlanAction.NewTemplateId)) != null;
        yield return Check(
            "view-mutation-plan-ids-frozen",
            viewMutationPlanIdsFrozen,
            viewMutationPlanIdsFrozen
                ? "View template plans freeze source view ids and old/new template ids."
                : "View template planning is missing callable path, schema, safeguard, or frozen id fields.");

        var viewCloneNoNameCollision =
            commandPaths.Contains("views clone-set") &&
            outputSchemas.Contains("view-clone-plan.v1") &&
            safeguardNames.Contains("views-clone-set") &&
            typeof(ViewsCommand.ViewClonePlan).GetProperty(nameof(ViewsCommand.ViewClonePlan.Issues)) != null &&
            typeof(ViewsCommand.ViewClonePlanAction).GetProperty(nameof(ViewsCommand.ViewClonePlanAction.TargetName)) != null;
        yield return Check(
            "view-clone-no-name-collision",
            viewCloneNoNameCollision,
            viewCloneNoNameCollision
                ? "View clone plans expose target names and collision issues before writes."
                : "View clone planning is missing callable path, schema, safeguard, target names, or issue reporting.");

        var viewRollbackGuardsPlacedViews =
            typeof(ViewsCommand.ViewClonePlanAction).GetProperty(nameof(ViewsCommand.ViewClonePlanAction.SourceIsPlacedOnSheet)) != null &&
            typeof(ViewsCommand.ViewClonePlanAction).GetProperty(nameof(ViewsCommand.ViewClonePlanAction.RollbackGuard)) != null;
        yield return Check(
            "view-rollback-guards-placed-views",
            viewRollbackGuardsPlacedViews,
            viewRollbackGuardsPlacedViews
                ? "View clone plans carry placed-view evidence and rollback guard text before cloned views can be deleted."
                : "View clone rollback guards are missing placed-view evidence or rollback guard fields.");

        var linkRepairJsonEvidence = LinksCommand.VerifyLinkRepairPlanJsonIsPathLoadOnly();
        var linkRepairNoCoordinateMove =
            commandPaths.Contains("links repair") &&
            outputSchemas.Contains("link-repair-plan.v1") &&
            safeguardNames.Contains("links-repair") &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldLoaded)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewLoaded)) != null &&
            linkRepairJsonEvidence.Success;
        yield return Check(
            "linkRepairNoCoordinateMove",
            linkRepairNoCoordinateMove,
            linkRepairNoCoordinateMove
                ? $"Link repair plans expose only old/new path and load-state changes; {linkRepairJsonEvidence.Evidence}"
                : $"Link repair planning is missing callable path, schema, safeguard, path/load fields, or it exposes coordinate mutation fields. {linkRepairJsonEvidence.Evidence}");

        var modelMapWritableProbe =
            commandPaths.Contains("model map-fix") &&
            outputSchemas.Contains("model-map-fix-plan.v1") &&
            safeguardNames.Contains("model-map-fix") &&
            typeof(ModelCommand.ModelMapFixAction).GetProperty(nameof(ModelCommand.ModelMapFixAction.CanWrite)) != null &&
            typeof(ModelCommand.ModelMapFixAction).GetProperty(nameof(ModelCommand.ModelMapFixAction.WritableProbe)) != null &&
            typeof(ModelCommand.ModelMapFixAction).GetProperty(nameof(ModelCommand.ModelMapFixAction.UnwritableReason)) != null &&
            ModelCommand.ResolveWritableProbe(canWrite: true, targetExists: true) &&
            !ModelCommand.ResolveWritableProbe(canWrite: false, targetExists: true) &&
            !ModelCommand.ResolveWritableProbe(canWrite: true, targetExists: false);
        yield return Check(
            "modelMapWritableProbe",
            modelMapWritableProbe,
            modelMapWritableProbe
                ? "Model map-fix plans include positive writable/access probe evidence and blocked-reason evidence before future writes."
                : "Model map-fix is missing callable path, schema, safeguard, writable probe semantics, or write precheck fields.");

        var coordinationReceiptPaths =
            safeguardNames.Contains("links-repair") &&
            safeguardNames.Contains("model-map-fix") &&
            typeof(PlanReceipt).GetProperty(nameof(PlanReceipt.LinkRepairActions)) != null &&
            typeof(PlanReceipt).GetProperty(nameof(PlanReceipt.ModelMapActions)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.OldPath)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.NewPath)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.OldLoaded)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.NewLoaded)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.OldPathExists)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.NewPathExists)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.OldPathLastWriteTimeUtc)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.NewPathLastWriteTimeUtc)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.OldPathSizeBytes)) != null &&
            typeof(PlanReceiptLinkRepairAction).GetProperty(nameof(PlanReceiptLinkRepairAction.NewPathSizeBytes)) != null &&
            typeof(PlanReceiptModelMapAction).GetProperty(nameof(PlanReceiptModelMapAction.Field)) != null &&
            typeof(PlanReceiptModelMapAction).GetProperty(nameof(PlanReceiptModelMapAction.OldValue)) != null &&
            typeof(PlanReceiptModelMapAction).GetProperty(nameof(PlanReceiptModelMapAction.NewValue)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldPathExists)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPathExists)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPathLastWriteTimeUtc)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPathSizeBytes)) != null;
        yield return Check(
            "coordinationReceiptPaths",
            coordinationReceiptPaths,
            coordinationReceiptPaths
                ? "Coordination plan receipts include link path/load rollback evidence and model map old/new values."
                : "Coordination plan receipts are missing link path/load evidence or model map old/new value fields.");

        yield return BuildV55ViewCoordinationHygieneGateCheck(
            projectDirectory,
            commandPaths,
            outputSchemas,
            safeguardNames,
            viewMutationPlanIdsFrozen,
            viewCloneNoNameCollision,
            viewRollbackGuardsPlacedViews,
            linkRepairNoCoordinateMove,
            modelMapWritableProbe,
            coordinationReceiptPaths);

        var contractV2Compat =
            commandPaths.Contains("issue preflight") &&
            commandPaths.Contains("issue diff") &&
            commandPaths.Contains("issue package") &&
            commandPaths.Contains("workbench contract --contract workbench-contract.v2") &&
            outputSchemas.Contains("workbench-contract.v2") &&
            outputSchemas.Contains("workbench-verify-report.v2") &&
            receiptSchemas.Contains("issue-package-receipt.v1");
        yield return Check(
            "contractV2Compat",
            contractV2Compat,
            contractV2Compat
                ? "Workbench v2 compatibility declares issue command paths, issue package receipts, and v2 contract markers while retaining v1 surfaces."
                : "Workbench v2 compatibility is missing issue command paths, issue package receipt schema, or v2 contract markers.");

        var issueNoHiddenMutation =
            commandPaths.Contains("issue preflight") &&
            outputSchemas.Contains("issue-preflight-report.v1") &&
            typeof(IssueCommand.IssuePreflightReport).GetProperty(nameof(IssueCommand.IssuePreflightReport.NoHiddenMutation)) != null &&
            typeof(IssueCommand.IssueContractIssue).GetProperty(nameof(IssueCommand.IssueContractIssue.Code)) != null &&
            typeof(IssueCommand.IssueContractIssue).GetProperty(nameof(IssueCommand.IssueContractIssue.Command)) != null;
        yield return Check(
            "issueNoHiddenMutation",
            issueNoHiddenMutation,
            issueNoHiddenMutation
                ? "Issue preflight exposes hidden-mutation checks so package flows cannot hide model writes."
                : "Issue preflight is missing command path, output schema, or hidden-mutation issue fields.");

        var issuePackageTraceability =
            commandPaths.Contains("issue package") &&
            outputSchemas.Contains("issue-package-receipt.v1") &&
            receiptSchemas.Contains("issue-package-receipt.v1") &&
            safeguardNames.Contains("issue-package") &&
            typeof(IssueCommand.IssuePackageReport).GetProperty(nameof(IssueCommand.IssuePackageReport.ManifestPath)) != null &&
            typeof(IssueCommand.IssuePackageReport).GetProperty(nameof(IssueCommand.IssuePackageReport.BundleHash)) != null &&
            typeof(IssueCommand.IssuePackageReport).GetProperty(nameof(IssueCommand.IssuePackageReport.JournalSignaturePath)) != null &&
            typeof(IssueCommand.IssuePackageReport).GetProperty(nameof(IssueCommand.IssuePackageReport.Files)) != null &&
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.ArchivePath)) != null &&
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.SourcePath)) != null &&
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.Bytes)) != null &&
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.Sha256)) != null &&
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.LineNumber)) != null &&
            traceabilityRuntime.DeliverablesBundle &&
            traceabilityRuntime.IssuePackage;
        yield return Check(
            "issuePackageTraceability",
            issuePackageTraceability,
            issuePackageTraceability
                ? $"Issue package receipts trace manifest, child files, file hashes, bundle hash, and optional journal signature evidence; {traceabilityRuntime.Evidence}."
                : $"Issue package is missing receipt schema, safeguard, manifest, bundle hash, journal signature, file hash, manifest-line trace fields, or runtime package hash evidence. {traceabilityRuntime.Evidence}");

        var faultInjectionRuntime = BuildFaultInjectionRuntimeCheck();
        var faultInjectionReady =
            faultInjectionRuntime.MissingProfile &&
            faultInjectionRuntime.ScheduleFaults &&
            faultInjectionRuntime.DeliveryFaults &&
            faultInjectionRuntime.BundlePathFault &&
            faultInjectionRuntime.PackageCleanup;
        yield return Check(
            "v5FaultInjectionCoverage",
            faultInjectionReady,
            faultInjectionReady
                ? $"v5 fault-injection runtime checks cover missing profiles, missing schedule exports, stale compare baselines, missing manifest fields, missing receipts, tampered receipts/manifests, bundle path failures, and package cleanup; {faultInjectionRuntime.Evidence}."
                : $"v5 fault-injection runtime checks are incomplete for missing profiles, missing schedule exports, stale compare baselines, missing manifest fields, missing receipts, tampered receipts/manifests, bundle path failures, or package cleanup. {faultInjectionRuntime.Evidence}");

        yield return BuildV5RealSmokeDisclosureCheck(projectDirectory);
        yield return BuildV5RcBoundaryDisclosureCheck(projectDirectory);
        yield return BuildV51SheetReleasePilotGateCheck(projectDirectory);
        yield return BuildV52SchedulePackagePilotGateCheck(projectDirectory);
        yield return BuildV53NumberingControlledApplyPilotGateCheck(projectDirectory);
        yield return BuildV54StandardsRuntimePackGateCheck(projectDirectory);
        yield return BuildV56TeamPilotPackGateCheck(projectDirectory, commandPaths, safeguardNames);
        yield return BuildV60LocalBimOpsContractGateCheck(projectDirectory, commandPaths, outputSchemas, receiptSchemas, safeguardNames);

        var dashboardOptional =
            !CommandContracts.Any(command => string.Equals(command.Name, "dashboard", StringComparison.OrdinalIgnoreCase)) &&
            !commandPaths.Any(path => path.StartsWith("dashboard", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "dashboardOptional",
            dashboardOptional,
            dashboardOptional
                ? "Dashboard remains optional and outside the CLI issue closure contract."
                : "Dashboard is incorrectly required by the issue closure workbench contract.");

        var sheetIssueDryRunReady =
            commandPaths.Contains("sheets issue-meta") &&
            outputSchemas.Contains("sheet-issue-plan.v1") &&
            safeguardNames.Contains("sheet-issue-meta") &&
            safeguardIndex.Safeguards.Any(safeguard =>
                string.Equals(safeguard.Name, "sheet-issue-meta", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--plan-output", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "sheet-issue-dry-run-required",
            sheetIssueDryRunReady,
            sheetIssueDryRunReady
                ? "Sheet issue metadata updates are indexed only through dry-run plan-output review."
                : "Sheet issue metadata planning is missing callable path, output schema, or dry-run plan-output safeguard coverage.");

        var sheetRenumberDryRunReady =
            commandPaths.Contains("sheets renumber") &&
            outputSchemas.Contains("sheet-renumber-plan.v1") &&
            safeguardNames.Contains("sheet-renumber") &&
            safeguardIndex.Safeguards.Any(safeguard =>
                string.Equals(safeguard.Name, "sheet-renumber", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--plan-output", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "sheet-renumber-dry-run-required",
            sheetRenumberDryRunReady,
            sheetRenumberDryRunReady
                ? "Sheet renumber updates are indexed only through dry-run plan-output review."
                : "Sheet renumber planning is missing callable path, output schema, or dry-run plan-output safeguard coverage.");

        var roomRenumberDryRunReady =
            commandPaths.Contains("rooms renumber") &&
            outputSchemas.Contains("room-numbering-plan.v1") &&
            safeguardNames.Contains("rooms-renumber") &&
            safeguardIndex.Safeguards.Any(safeguard =>
                string.Equals(safeguard.Name, "rooms-renumber", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--plan-output", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "room-renumber-dry-run-required",
            roomRenumberDryRunReady,
            roomRenumberDryRunReady
                ? "Room renumber updates are indexed only through dry-run plan-output review."
                : "Room renumber planning is missing callable path, output schema, or dry-run plan-output safeguard coverage.");

        var markAssignDryRunReady =
            commandPaths.Contains("marks assign") &&
            commandPaths.Contains("marks verify") &&
            outputSchemas.Contains("mark-assignment-plan.v1") &&
            outputSchemas.Contains("mark-verify-report.v1") &&
            safeguardNames.Contains("marks-assign") &&
            safeguardIndex.Safeguards.Any(safeguard =>
                string.Equals(safeguard.Name, "marks-assign", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                safeguard.DryRunCommand.Contains("--plan-output", StringComparison.OrdinalIgnoreCase));
        yield return Check(
            "mark-assignment-dry-run-required",
            markAssignDryRunReady,
            markAssignDryRunReady
                ? "Door/window Mark assignments are indexed through dry-run plan-output review plus read-only verify."
                : "Mark assignment planning is missing callable paths, output schemas, or dry-run plan-output safeguard coverage.");

        var sheetReceiptRollbackShapeReady = SheetPlanReceiptShapeReady(safeguardIndex);
        yield return Check(
            "sheet-receipt-rollback-shape",
            sheetReceiptRollbackShapeReady,
            sheetReceiptRollbackShapeReady
                ? "Sheet plan receipts expose rollback actions, affected ids, plan counts, rollback requirement, and model/document context."
                : "Sheet plan receipts are missing rollback actions, affected ids, plan counts, model context, rollback requirement, or review commands.");

        var numberingReceiptRollbackShapeReady = NumberingPlanReceiptShapeReady(safeguardIndex);
        yield return Check(
            "numbering-receipt-rollback-shape",
            numberingReceiptRollbackShapeReady,
            numberingReceiptRollbackShapeReady
                ? "Room and Mark plan receipts expose rule provenance, deterministic ordering evidence, rollback actions, affected ids, and model/document context."
                : "Room or Mark plan receipts are missing rule provenance, ordering evidence, rollback actions, affected ids, model context, or review commands.");

        var workflowDurationReady =
            typeof(WorkflowRunReport).GetProperty(nameof(WorkflowRunReport.DurationMs)) != null &&
            typeof(WorkflowRunReport).GetProperty(nameof(WorkflowRunReport.TimeoutMs)) != null &&
            typeof(WorkflowRunStepResult).GetProperty(nameof(WorkflowRunStepResult.DurationMs)) != null &&
            typeof(WorkflowRunStepResult).GetProperty(nameof(WorkflowRunStepResult.TimedOut)) != null &&
            typeof(WorkflowReceiptSummary).GetProperty(nameof(WorkflowReceiptSummary.DurationMs)) != null;
        yield return Check(
            "workflow-duration-telemetry",
            workflowDurationReady,
            workflowDurationReady
                ? "Workflow run receipts expose durationMs plus timeoutMs/timedOut for long-running terminal review."
                : "Workflow run duration telemetry is missing duration, timeout, timed-out, or receipt summary contracts.");

        var workflowReceiptTriageReady =
            typeof(WorkflowReceiptListReport).GetProperty(nameof(WorkflowReceiptListReport.NameFilter)) != null &&
            typeof(WorkflowReceiptListReport).GetProperty(nameof(WorkflowReceiptListReport.MinDurationMs)) != null &&
            typeof(WorkflowReceiptListReport).GetProperty(nameof(WorkflowReceiptListReport.Sort)) != null &&
            typeof(WorkflowReceiptListReport).GetProperty(nameof(WorkflowReceiptListReport.Window)) != null &&
            typeof(WorkflowReceiptListReport).GetProperty(nameof(WorkflowReceiptListReport.SinceUtc)) != null &&
            typeof(WorkflowReceiptSummary).GetProperty(nameof(WorkflowReceiptSummary.DurationMs)) != null;
        yield return Check(
            "workflow-receipt-triage",
            workflowReceiptTriageReady,
            workflowReceiptTriageReady
                ? "Workflow receipt review exposes name, minimum-duration, sort, and recent-window contracts for local triage."
                : "Workflow receipt triage is missing name, minimum-duration, sort, window, since, or duration contracts.");

        var workflowDiscoveryReady =
            commandPaths.Contains("inspect workflows") &&
            outputSchemas.Contains("inspect-workflows.v1") &&
            OutputContracts.Any(output =>
                string.Equals(output.CommandPath, "inspect workflows", StringComparison.OrdinalIgnoreCase) &&
                output.SupportsMarkdown);
        yield return Check(
            "workflow-discovery-surface",
            workflowDiscoveryReady,
            workflowDiscoveryReady
                ? "Inspect workflows exposes local workflow YAML discovery with JSON/Markdown handoff commands."
                : "Inspect workflows is missing from command paths, output schemas, or Markdown-capable output contracts.");

        var planDiscoveryReady =
            commandPaths.Contains("inspect plans") &&
            outputSchemas.Contains("inspect-plans.v1") &&
            OutputContracts.Any(output =>
                string.Equals(output.CommandPath, "inspect plans", StringComparison.OrdinalIgnoreCase) &&
                output.SupportsMarkdown);
        yield return Check(
            "plan-discovery-surface",
            planDiscoveryReady,
            planDiscoveryReady
                ? "Inspect plans exposes saved mutation plan discovery with JSON/Markdown dry-run and rollback handoff commands."
                : "Inspect plans is missing from command paths, output schemas, or Markdown-capable output contracts.");

        var requiredWorkflowTemplates = new[] { "pre-issue", "weekly-health", "export-package", "family-cleanup" };
        var workflowTemplateIssues = GetBuiltInWorkflowTemplateIssues(requiredWorkflowTemplates);
        yield return Check(
            "workflow-template-surface",
            workflowTemplateIssues.Count == 0,
            workflowTemplateIssues.Count == 0
                ? $"Built-in workflow templates validate without issues: {string.Join(", ", requiredWorkflowTemplates)}."
                : $"Built-in workflow template surface incomplete: {string.Join("; ", workflowTemplateIssues)}");

        var workflowReviewReportType = typeof(WorkflowCommand)
            .GetNestedType("WorkflowReviewReport", BindingFlags.NonPublic);
        var workflowReviewOutput = OutputContracts.FirstOrDefault(output =>
            string.Equals(output.JsonSchema, "workflow-review.v1", StringComparison.OrdinalIgnoreCase));
        var workflowReviewHandoffReady =
            workflowReviewReportType?.GetProperty("PreRunHandoffCommands") != null &&
            workflowReviewReportType?.GetProperty("ProjectDirectory") != null &&
            workflowReviewReportType?.GetProperty("ArtifactReadiness") != null &&
            workflowReviewReportType?.GetProperty("PostRunReceiptCommands") != null &&
            workflowReviewOutput is not null &&
            workflowReviewOutput.Notes.Contains("receipt", StringComparison.OrdinalIgnoreCase) &&
            workflowReviewOutput.Notes.Contains("artifact readiness", StringComparison.OrdinalIgnoreCase);
        yield return Check(
            "workflow-review-handoff",
            workflowReviewHandoffReady,
            workflowReviewHandoffReady
                ? "Workflow review exposes pre-run workbench handoff, project artifact readiness, and post-run receipt triage commands in the workflow-review.v1 contract."
                : "Workflow review is missing pre-run workbench handoff commands, project artifact readiness, post-run receipt triage commands, or output-contract notes.");

        var workflowRegistryReportType = typeof(WorkflowCommand)
            .GetNestedType("WorkflowRegistryReport", BindingFlags.NonPublic);
        var workflowRegistryEntryType = typeof(WorkflowCommand)
            .GetNestedType("WorkflowRegistryEntry", BindingFlags.NonPublic);
        var workflowRegistryOutput = OutputContracts.FirstOrDefault(output =>
            string.Equals(output.JsonSchema, "workflow-registry.v1", StringComparison.OrdinalIgnoreCase));
        var workflowRegistryReady =
            commandPaths.Contains("workflow registry") &&
            outputSchemas.Contains("workflow-registry.v1") &&
            workflowRegistryOutput is not null &&
            workflowRegistryOutput.SupportsMarkdown &&
            workflowRegistryOutput.Notes.Contains("inputs", StringComparison.OrdinalIgnoreCase) &&
            workflowRegistryOutput.Notes.Contains("read/write scope", StringComparison.OrdinalIgnoreCase) &&
            workflowRegistryOutput.Notes.Contains("risk level", StringComparison.OrdinalIgnoreCase) &&
            workflowRegistryOutput.Notes.Contains("receipt", StringComparison.OrdinalIgnoreCase) &&
            workflowRegistryReportType?.GetProperty("SchemaVersion") != null &&
            workflowRegistryReportType?.GetProperty("WorkflowCount") != null &&
            workflowRegistryReportType?.GetProperty("RollbackSupportedWorkflowCount") != null &&
            workflowRegistryEntryType?.GetProperty("Inputs") != null &&
            workflowRegistryEntryType?.GetProperty("Outputs") != null &&
            workflowRegistryEntryType?.GetProperty("ReadWriteScope") != null &&
            workflowRegistryEntryType?.GetProperty("RiskLevel") != null &&
            workflowRegistryEntryType?.GetProperty("DryRunCommands") != null &&
            workflowRegistryEntryType?.GetProperty("ApprovalCommands") != null &&
            workflowRegistryEntryType?.GetProperty("RollbackSupport") != null &&
            workflowRegistryEntryType?.GetProperty("ReceiptSchemas") != null &&
            workflowRegistryEntryType?.GetProperty("AcceptanceEvidence") != null;
        yield return Check(
            "workflow-registry-contract",
            workflowRegistryReady,
            workflowRegistryReady
                ? "Workflow registry exposes workflow-registry.v1 with inputs, outputs, read/write scope, risk level, dry-run commands, approval commands, rollback support, receipt schemas, and acceptance evidence."
                : "Workflow registry is missing its command path, output schema, Markdown contract, or required registry fields.");

        var commandsWithoutExitNotes = CommandContracts
            .Where(command => string.IsNullOrWhiteSpace(command.ExitCodeNotes))
            .Select(command => command.Name)
            .ToArray();
        yield return Check(
            "exit-code-notes",
            commandsWithoutExitNotes.Length == 0,
            commandsWithoutExitNotes.Length == 0
                ? "Every contract command has exit-code notes."
                : $"Commands missing exit-code notes: {string.Join(", ", commandsWithoutExitNotes)}");

        var exitIndex = CreateExitCodeIndex();
        var exitCommands = exitIndex.Commands
            .Select(command => command.Command)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingExitIndexCommands = contractCommands
            .Where(command => !exitCommands.Contains(command))
            .ToArray();
        yield return Check(
            "exit-code-index-surface",
            missingExitIndexCommands.Length == 0 && exitIndex.Commands.All(command => command.SuccessExitCodes.Count > 0),
            missingExitIndexCommands.Length == 0
                ? $"Exit-code index covers {exitIndex.CommandCount} contract commands."
                : $"Exit-code index missing commands: {string.Join(", ", missingExitIndexCommands)}");
    }

    private static WorkbenchCheckResult BuildV5RealSmokeDisclosureCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v5RealSmokeDisclosure",
                true,
                "Project-local v5.0 docs are not present, so this workbench run verifies command contracts only and does not claim live Revit issue-closure readiness for the verified --dir.");
        }

        var smokeRoot = Path.Combine(docsRoot, "smoke", "v5.0");
        var gapReportPath = Path.Combine(smokeRoot, "gap-report.md");
        var requiredYears = new[] { "2024", "2025", "2026" };
        var liveEvidenceYears = requiredYears
            .Where(year => File.Exists(Path.Combine(smokeRoot, $"revit-{year}-issue-closure.md")))
            .ToArray();
        var readOnlyDryRunYears = requiredYears
            .Where(year => File.Exists(Path.Combine(smokeRoot, $"revit-{year}-readonly-dryrun.md")))
            .ToArray();
        var missingYears = requiredYears
            .Where(year => !liveEvidenceYears.Contains(year, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var gapReportText = TryReadText(gapReportPath);
        var undisclosedYears = missingYears
            .Where(year => !GapReportDisclosesMissingSmoke(gapReportText, year))
            .ToArray();

        var liveText = liveEvidenceYears.Length == 0
            ? "no issue-closure write evidence files"
            : $"issue-closure write evidence files for Revit {string.Join(", Revit ", liveEvidenceYears)}";
        var readOnlyText = readOnlyDryRunYears.Length == 0
            ? "no read-only dry-run evidence files"
            : $"read-only dry-run evidence files for Revit {string.Join(", Revit ", readOnlyDryRunYears)}";
        var missingText = missingYears.Length == 0
            ? "no missing Revit years"
            : $"not-live-verified rows for Revit {string.Join(", Revit ", missingYears)}";
        return Check(
            "v5RealSmokeDisclosure",
            undisclosedYears.Length == 0,
            undisclosedYears.Length == 0
                ? $"v5.0 issue-closure smoke readiness is separated from command-surface checks: {liveText}; {readOnlyText}; {missingText} in docs/smoke/v5.0/gap-report.md."
                : $"v5.0 issue-closure smoke evidence is missing and not disclosed for Revit {string.Join(", Revit ", undisclosedYears)}; add evidence files or update docs/smoke/v5.0/gap-report.md.");
    }

    private static WorkbenchCheckResult BuildV5RcBoundaryDisclosureCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v5RcBoundaryDisclosure",
                true,
                "Project-local v5.0 RC docs are not present, so this workbench run verifies command contracts only and does not claim v5.0 RC readiness for the verified --dir.");
        }

        var path = Path.Combine(docsRoot, "v5-rc-readiness.md");
        var text = TryReadText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Check(
                "v5RcBoundaryDisclosure",
                false,
                "docs/v5-rc-readiness.md is missing or unreadable; v5.0 RC boundaries are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "Current status:",
            "Claimed live Revit years",
            "Stable P0 Commands",
            "Experimental / Deferred Commands",
            "not live verified",
            "v5RealSmokeDisclosure",
            "issuePackageTraceability",
            "v5FaultInjectionCoverage",
            "release verify --strict",
            "MCP, SaaS, or built-in LLM parser",
        };
        var missing = requiredPhrases
            .Where(phrase => !text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Check(
            "v5RcBoundaryDisclosure",
            missing.Length == 0,
            missing.Length == 0
                ? "v5.0 RC readiness doc discloses current status, claimed live Revit years, stable P0 scope, experimental boundaries, live-smoke gaps, strict release gate, and v5 workbench evidence checks."
                : $"docs/v5-rc-readiness.md is missing RC boundary disclosures: {string.Join(", ", missing)}.");
    }

    private static WorkbenchCheckResult BuildV51SheetReleasePilotGateCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v51SheetReleasePilotGate",
                true,
                "Project-local v5.1 docs are not present, so this workbench run verifies sheet release command contracts only and does not claim v5.1 pilot readiness for the verified --dir.");
        }

        var path = Path.Combine(docsRoot, "smoke", "v5.1", "gap-report.md");
        var text = TryReadText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Check(
                "v51SheetReleasePilotGate",
                false,
                "docs/smoke/v5.1/gap-report.md is missing or unreadable; v5.1 sheet release pilot gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.1 sheet release control",
            "production pilot gated",
            "100 sheet",
            "300 sheet",
            "1000 sheet",
            "not live verified",
            "Revit 2026",
            "dry-run/plan/receipt/rollback",
            "journal verify",
            "Post-rollback evidence",
        };
        var missing = requiredPhrases
            .Where(phrase => !text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Check(
            "v51SheetReleasePilotGate",
            missing.Length == 0,
            missing.Length == 0
                ? "v5.1 sheet release control is disclosed as production pilot gated, with 100/300/1000 sheet live fixture gaps kept separate from portable command hardening."
                : $"docs/smoke/v5.1/gap-report.md is missing sheet release pilot gate disclosures: {string.Join(", ", missing)}.");
    }

    private static WorkbenchCheckResult BuildV52SchedulePackagePilotGateCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v52SchedulePackagePilotGate",
                true,
                "Project-local v5.2 docs are not present, so this workbench run verifies schedule/package command contracts only and does not claim v5.2 pilot readiness for the verified --dir.");
        }

        var path = Path.Combine(docsRoot, "smoke", "v5.2", "gap-report.md");
        var text = TryReadText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Check(
                "v52SchedulePackagePilotGate",
                false,
                "docs/smoke/v5.2/gap-report.md is missing or unreadable; v5.2 schedule/package pilot gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.2 schedule deliverable closure",
            "schedule/package-only",
            "explicit go-forward decision",
            "not live verified",
            "schedules batch-export",
            "schedules compare",
            "deliverables bundle",
            "issue package",
            "journal verify",
        };
        var missing = requiredPhrases
            .Where(phrase => !text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Check(
            "v52SchedulePackagePilotGate",
            missing.Length == 0,
            missing.Length == 0
                ? "v5.2 schedule/package closure is disclosed as pilot-gated, with live smoke gaps kept separate from portable schedule/package hardening."
                : $"docs/smoke/v5.2/gap-report.md is missing schedule/package pilot gate disclosures: {string.Join(", ", missing)}.");
    }

    private static WorkbenchCheckResult BuildV53NumberingControlledApplyPilotGateCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v53NumberingControlledApplyPilotGate",
                true,
                "Project-local v5.3 docs are not present, so this workbench run verifies numbering command contracts only and does not claim v5.3 pilot readiness for the verified --dir.");
        }

        var path = Path.Combine(docsRoot, "smoke", "v5.3", "gap-report.md");
        var text = TryReadText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Check(
                "v53NumberingControlledApplyPilotGate",
                false,
                "docs/smoke/v5.3/gap-report.md is missing or unreadable; v5.3 numbering controlled-apply pilot gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.3 numbering controlled apply",
            "explicit go-forward decision",
            "reserved numbers",
            "hold numbers",
            "duplicate-target failure",
            "not live verified",
            "plan apply",
            "rollback",
            "journal verify",
        };
        var missing = requiredPhrases
            .Where(phrase => !text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Check(
            "v53NumberingControlledApplyPilotGate",
            missing.Length == 0,
            missing.Length == 0
                ? "v5.3 numbering controlled apply is disclosed as pilot-gated, with reserved/hold rule hardening and live Revit smoke gaps kept separate."
                : $"docs/smoke/v5.3/gap-report.md is missing numbering controlled-apply pilot gate disclosures: {string.Join(", ", missing)}.");
    }

    private static WorkbenchCheckResult BuildV54StandardsRuntimePackGateCheck(string projectDirectory)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v54StandardsRuntimePackGate",
                true,
                "Project-local v5.4 docs are not present, so this workbench run verifies standards command contracts only and does not claim v5.4 runtime pack readiness for the verified --dir.");
        }

        var gapPath = Path.Combine(docsRoot, "smoke", "v5.4", "gap-report.md");
        var gapText = TryReadText(gapPath);
        if (string.IsNullOrWhiteSpace(gapText))
        {
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                "docs/smoke/v5.4/gap-report.md is missing or unreadable; v5.4 standards runtime pack gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.4 Standards Runtime Pack",
            "profiles/office-standard",
            "sheet map",
            "numbering rules",
            "release/workbench gates",
            "not benchmarked",
            "not live verified",
            "SaaS",
            "MCP",
            "LLM",
        };
        var missing = requiredPhrases
            .Where(phrase => !gapText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missing.Length > 0)
        {
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                $"docs/smoke/v5.4/gap-report.md is missing standards runtime pack disclosures: {string.Join(", ", missing)}.");
        }

        var repoRoot = Directory.GetParent(docsRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                "Could not resolve repository root for profiles/office-standard validation.");
        }

        var packRoot = Path.Combine(repoRoot, "profiles", "office-standard");
        var manifestPath = Path.Combine(packRoot, ".revitcli", "standards.yml");
        if (!File.Exists(manifestPath))
        {
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                "profiles/office-standard/.revitcli/standards.yml is missing.");
        }

        var validation = StandardsValidator.Validate(manifestPath, packRoot);
        if (!validation.Valid)
        {
            var preview = string.Join("; ", validation.Issues.Take(3).Select(issue => $"{issue.Path}: {issue.Message}"));
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                $"profiles/office-standard failed standards validation: {preview}");
        }

        var installSmoke = StandardsRuntimePackSmoke.Run(packRoot);
        if (!installSmoke.Success)
        {
            return Check(
                "v54StandardsRuntimePackGate",
                false,
                $"profiles/office-standard failed standards install dry-run/apply smoke: {installSmoke.Evidence}");
        }

        var manifestText = TryReadText(manifestPath);
        var manifestHasRuntimeFiles =
            manifestText.Contains("sheetMaps", StringComparison.OrdinalIgnoreCase) &&
            manifestText.Contains("numberingRules", StringComparison.OrdinalIgnoreCase);

        return Check(
            "v54StandardsRuntimePackGate",
            manifestHasRuntimeFiles,
            manifestHasRuntimeFiles
                ? $"v5.4 standards runtime pack profiles/office-standard validates offline and {installSmoke.Evidence}, covering profile, workflow, output path, schedule template, sheet map, numbering rule, and family-rule requirements."
                : "profiles/office-standard manifest is missing sheetMaps or numberingRules runtime file requirements.");
    }

    private static WorkbenchCheckResult BuildV55ViewCoordinationHygieneGateCheck(
        string projectDirectory,
        IReadOnlySet<string> commandPaths,
        IReadOnlySet<string> outputSchemas,
        IReadOnlySet<string> safeguardNames,
        bool viewMutationPlanIdsFrozen,
        bool viewCloneNoNameCollision,
        bool viewRollbackGuardsPlacedViews,
        bool linkRepairNoCoordinateMove,
        bool modelMapWritableProbe,
        bool coordinationReceiptPaths)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v55ViewCoordinationHygieneGate",
                true,
                "Project-local v5.5 docs are not present, so this workbench run does not claim v5.5 release readiness for the verified --dir; release roots must provide docs/smoke/v5.5/gap-report.md.");
        }

        var gapPath = Path.Combine(docsRoot, "smoke", "v5.5", "gap-report.md");
        var gapText = TryReadText(gapPath);
        if (string.IsNullOrWhiteSpace(gapText))
        {
            return Check(
                "v55ViewCoordinationHygieneGate",
                false,
                "docs/smoke/v5.5/gap-report.md is missing or unreadable; v5.5 view/coordination hygiene gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.5 View and Coordination Hygiene",
            "audit-first",
            "views audit",
            "views template-apply",
            "views clone-set",
            "placed-view rollback guard",
            "links audit",
            "links repair",
            "no coordinate moves",
            "model map-check",
            "model map-fix",
            "write-precheck",
            "worksharing locks",
            "not live verified",
            "journal verify",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missing = requiredPhrases
            .Where(phrase => !gapText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missing.Length > 0)
        {
            return Check(
                "v55ViewCoordinationHygieneGate",
                false,
                $"docs/smoke/v5.5/gap-report.md is missing view/coordination hygiene disclosures: {string.Join(", ", missing)}.");
        }

        var boundaryIssues = FindBoundaryEvidenceIssues(
            gapText,
            ("SaaS", V60SaasContradictions),
            ("MCP", V60McpContradictions),
            ("built-in LLM", V60LlmContradictions),
            ("dashboard-central", V60DashboardCentralContradictions));
        if (boundaryIssues.Length > 0)
        {
            return Check(
                "v55ViewCoordinationHygieneGate",
                false,
                $"docs/smoke/v5.5/gap-report.md has contradictory or bare non-goal boundary disclosures: {string.Join(", ", boundaryIssues)}.");
        }

        var surfaceReady =
            commandPaths.Contains("views audit") &&
            commandPaths.Contains("views template-apply") &&
            commandPaths.Contains("views clone-set") &&
            commandPaths.Contains("links audit") &&
            commandPaths.Contains("links repair") &&
            commandPaths.Contains("model map-check") &&
            commandPaths.Contains("model map-fix") &&
            outputSchemas.Contains("view-standards-report.v1") &&
            outputSchemas.Contains("view-template-plan.v1") &&
            outputSchemas.Contains("view-clone-plan.v1") &&
            outputSchemas.Contains("link-audit-report.v1") &&
            outputSchemas.Contains("link-repair-plan.v1") &&
            outputSchemas.Contains("model-map-report.v1") &&
            outputSchemas.Contains("model-map-fix-plan.v1") &&
            safeguardNames.Contains("views-template-apply") &&
            safeguardNames.Contains("views-clone-set") &&
            safeguardNames.Contains("links-repair") &&
            safeguardNames.Contains("model-map-fix") &&
            viewMutationPlanIdsFrozen &&
            viewCloneNoNameCollision &&
            viewRollbackGuardsPlacedViews &&
            linkRepairNoCoordinateMove &&
            modelMapWritableProbe &&
            coordinationReceiptPaths;

        return Check(
            "v55ViewCoordinationHygieneGate",
            surfaceReady,
            surfaceReady
                ? "v5.5 view/coordination hygiene is audit-first: view plans freeze ids and placed-view rollback guards, link repair remains path/load only with no coordinate moves, and model map-fix carries writable probe plus receipt rollback evidence while live worksharing gaps stay disclosed."
                : "v5.5 view/coordination hygiene is missing command paths, output schemas, safeguards, placed-view guards, no-coordinate link repair, model map writable probe, or coordination receipt evidence.");
    }

    private static WorkbenchCheckResult BuildV56TeamPilotPackGateCheck(
        string projectDirectory,
        IReadOnlySet<string> commandPaths,
        IReadOnlySet<string> safeguardNames)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v56TeamPilotPackGate",
                true,
                "Project-local v5.6 docs are not present, so this workbench run verifies command contracts only and does not claim team pilot readiness for the verified --dir.");
        }

        var gapPath = Path.Combine(docsRoot, "smoke", "v5.6", "gap-report.md");
        var gapText = TryReadText(gapPath);
        if (string.IsNullOrWhiteSpace(gapText))
        {
            return Check(
                "v56TeamPilotPackGate",
                false,
                "docs/smoke/v5.6/gap-report.md is missing or unreadable; v5.6 team pilot pack gates are not disclosed.");
        }

        var requiredPhrases = new[]
        {
            "v5.6 Team Pilot Pack",
            "installer",
            "doctor",
            "policy files",
            "receipt retention",
            "training",
            "supportable error reports",
            "office pilots",
            "not live verified",
            "local-first",
            "terminal-first",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missing = requiredPhrases
            .Where(phrase => !gapText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missing.Length > 0)
        {
            return Check(
                "v56TeamPilotPackGate",
                false,
                $"docs/smoke/v5.6/gap-report.md is missing team pilot disclosures: {string.Join(", ", missing)}.");
        }

        var boundaryIssues = FindBoundaryEvidenceIssues(
            gapText,
            ("SaaS", V60SaasContradictions),
            ("MCP", V60McpContradictions),
            ("built-in LLM", V60LlmContradictions),
            ("dashboard-central", V60DashboardCentralContradictions));
        if (boundaryIssues.Length > 0)
        {
            return Check(
                "v56TeamPilotPackGate",
                false,
                $"docs/smoke/v5.6/gap-report.md has contradictory or bare non-goal boundary disclosures: {string.Join(", ", boundaryIssues)}.");
        }

        var policyPath = Path.Combine(projectDirectory, "profiles", "team-pilot", ".revitcli", "team-policy.yml");
        var policy = TeamPolicyValidator.Validate(policyPath, projectDirectory);
        if (!policy.Valid)
        {
            var preview = string.Join("; ", policy.Issues.Take(3).Select(issue => $"{issue.Code}: {issue.Message}"));
            return Check(
                "v56TeamPilotPackGate",
                false,
                $"profiles/team-pilot/.revitcli/team-policy.yml failed team policy validation: {preview}");
        }

        var surfaceReady =
            commandPaths.Contains("doctor") &&
            commandPaths.Contains("workbench verify --contract workbench-contract.v2") &&
            commandPaths.Contains("release verify --strict") &&
            commandPaths.Contains("standards validate") &&
            commandPaths.Contains("journal verify") &&
            safeguardNames.Contains("history-prune");

        return Check(
            "v56TeamPilotPackGate",
            surfaceReady,
            surfaceReady
                ? "v5.6 team pilot pack has local policy validation for installer/doctor/release gates, receipt retention, support evidence, and history-prune retention review without SaaS, MCP, dashboard-central, or built-in LLM runtime behavior."
                : "v5.6 team pilot pack is missing doctor/workbench/release/standards/journal command paths or history-prune retention safeguards.");
    }

    private static WorkbenchCheckResult BuildV60LocalBimOpsContractGateCheck(
        string projectDirectory,
        IReadOnlySet<string> commandPaths,
        IReadOnlySet<string> outputSchemas,
        IReadOnlySet<string> receiptSchemas,
        IReadOnlySet<string> safeguardNames)
    {
        var docsRoot = GetProjectDocsRootForReleaseClaims(projectDirectory);
        if (docsRoot == null)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                true,
                "Project-local v6.0 docs are not present, so this workbench run verifies command contracts only and does not claim Local BIMOps Workbench readiness for the verified --dir.");
        }

        var contractPath = Path.Combine(docsRoot, "v6-local-bimops-contract.md");
        var contractText = TryReadText(contractPath);
        if (string.IsNullOrWhiteSpace(contractText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/v6-local-bimops-contract.md is missing or unreadable; v6.0 Local BIMOps contract gates are not disclosed.");
        }

        var gapPath = Path.Combine(docsRoot, "smoke", "v6.0", "gap-report.md");
        var gapText = TryReadText(gapPath);
        if (string.IsNullOrWhiteSpace(gapText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/gap-report.md is missing or unreadable; v6.0 Local BIMOps gaps are not disclosed.");
        }

        var contractPhrases = new[]
        {
            "v6.0 Local BIMOps Workbench Contract",
            "BIM Release OS",
            "Revit Model Operations Ledger",
            "terminal-first",
            "local-first",
            "deterministic",
            "dry-run first",
            "explicit approval",
            "planHash",
            "receiptHash",
            "journalPath",
            "rollbackPointer",
            "checks",
            "artifacts",
            "deterministic receipt",
            "rollback preconditions",
            "current-value conflict",
            "audit trail",
            "journal verify",
            "standards runtime",
            "project memory",
            "workflow registry",
            "workflow-registry.v1",
            "ledger append",
            "ledger replay",
            "ledger query",
            "ledger validate",
            "ledger stats",
            "ledger timeline",
            "ledger analytics",
            "release pilot validate",
            "release pilot register",
            "completedOfficePilotCountBefore",
            "completedOfficePilotCountAfter",
            "register nextActions",
            "release pilot status",
            "missingEvidence",
            "missingEvidenceSummary",
            "evidenceCompleteOfficePilotCount",
            "remainingEvidenceCompleteOfficePilotCount",
            "productionSupportReviewPath",
            "release pilot claim",
            "claimBlockers",
            "nextActions",
            "ledger-append.v1",
            "ledger-replay.v1",
            "ledger-query.v1",
            "ledger-validate.v1",
            "ledger-stats.v1",
            "ledger-timeline.v1",
            "ledger-analytics-bundle.v1",
            "no SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
            "database",
        };
        var missingContract = contractPhrases
            .Where(phrase => !contractText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingContract.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/v6-local-bimops-contract.md is missing Local BIMOps contract disclosures: {string.Join(", ", missingContract)}.");
        }

        var contractBoundaryIssues = FindBoundaryEvidenceIssues(
            contractText,
            ("SaaS", V60SaasContradictions),
            ("MCP", V60McpContradictions),
            ("built-in LLM", V60LlmContradictions),
            ("dashboard-central", V60DashboardCentralContradictions),
            ("database", V60DatabaseContradictions));
        if (contractBoundaryIssues.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/v6-local-bimops-contract.md has contradictory or bare non-goal boundary disclosures: {string.Join(", ", contractBoundaryIssues)}.");
        }

        var gapPhrases = new[]
        {
            "v6.0 Local BIMOps Workbench",
            "contract baseline",
            "operations ledger",
            "not live verified",
            "ledger replay",
            "live Revit ledger integration",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
            "real Revit pilots",
            "office rollout pilots",
            "--support-review",
            "productionSupportReviewPath",
            "pilot evidence packet",
            "local controlled pilot packet",
            "read-only ledger query",
            "ledger replay preview",
            "read-only ledger validate",
            "read-only ledger stats",
            "read-only ledger timeline",
            "append-only ledger runtime",
            "read-only workflow registry",
            "read-only standards validate",
            "dry-run issue package",
            "read-only deliverables verify",
            @"scripts\install-current-source-revit2026.ps1",
            "--require-current-source",
            "currentSourceDriftKind",
            "install-required",
            "restart-required",
            "stagedAddinCommit",
            "stagedAddinPath",
        };
        var missingGap = gapPhrases
            .Where(phrase => !gapText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .Concat(MissingSemanticEvidence(
                gapText,
                "journal verify JSON/table validity/root-hash parity",
                "journal verify",
                "JSON/table",
                "validity",
                "root-hash",
                "parity"))
            .Concat(MissingSemanticEvidence(
                gapText,
                "history-list.v1 JSON count consistency and table row-order parity",
                "history-list.v1",
                "JSON count consistency",
                "table",
                "row-order parity"))
            .ToArray();
        if (missingGap.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/gap-report.md is missing Local BIMOps gap disclosures: {string.Join(", ", missingGap)}.");
        }

        var gapBoundaryIssues = FindBoundaryEvidenceIssues(
            gapText,
            ("SaaS", V60SaasContradictions),
            ("MCP", V60McpContradictions),
            ("built-in LLM", V60LlmContradictions),
            ("dashboard-central", V60DashboardCentralContradictions),
            ("database", V60DatabaseContradictions));
        if (gapBoundaryIssues.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/gap-report.md has contradictory or bare non-goal boundary disclosures: {string.Join(", ", gapBoundaryIssues)}.");
        }

        var pilotEvidencePath = Path.Combine(docsRoot, "smoke", "v6.0", "pilot-evidence-template.md");
        var pilotEvidenceText = TryReadText(pilotEvidencePath);
        if (string.IsNullOrWhiteSpace(pilotEvidenceText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/pilot-evidence-template.md is missing or unreadable; v6.0 office rollout pilot evidence intake is not disclosed.");
        }

        var pilotEvidencePhrases = new[]
        {
            "v6.0 Office Rollout Pilot Evidence Packet",
            "controlled project-copy pilots",
            "Pilot identifier",
            "release pilot scaffold",
            "scaffold `nextActions`",
            "release pilot validate",
            "validate `nextActions`",
            "release pilot register",
            "register nextActions",
            "release pilot status",
            "missingEvidence",
            "missingEvidenceSummary",
            "evidenceCompleteOfficePilotCount",
            "remainingEvidenceCompleteOfficePilotCount",
            "release pilot claim",
            "claimBlockers",
            "nextActions",
            "--support-review",
            "productionSupportReviewPath",
            "Support review creation is deferred until the completed pilot threshold",
            "doctor --check-version 2026 --output json",
            "`status --output json`",
            "workbench verify --contract workbench-contract.v2",
            "release verify --strict --output json",
            "ledger query --source ledger --output json",
            "ledger validate --source ledger --output json",
            "ledger stats --source ledger --analytics-snapshot",
            "ledger timeline --source ledger --analytics-snapshot",
            "journal verify --output json",
            "Rollback result",
            "no production support claim",
            "Minimum office pilots: 2-3 completed office pilots",
            "BIM manager signoff",
            "Project-copy owner signoff",
            "Support ticket review",
            "Multi-user rollout postmortem",
        };
        var missingPilotEvidence = pilotEvidencePhrases
            .Where(phrase => !pilotEvidenceText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingPilotEvidence.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/pilot-evidence-template.md is missing office rollout pilot evidence disclosures: {string.Join(", ", missingPilotEvidence)}.");
        }

        var pilotEvidenceBoundaryIssues = FindBoundaryEvidenceIssues(
            pilotEvidenceText,
            ("SaaS", V60SaasContradictions),
            ("MCP", V60McpContradictions),
            ("built-in LLM", V60LlmContradictions),
            ("dashboard-central", V60DashboardCentralContradictions),
            ("database", V60DatabaseContradictions));
        if (pilotEvidenceBoundaryIssues.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/pilot-evidence-template.md has contradictory or bare non-goal boundary disclosures: {string.Join(", ", pilotEvidenceBoundaryIssues)}.");
        }

        var officeRolloutStatusPath = Path.Combine(docsRoot, "smoke", "v6.0", "office-rollout-status.json");
        var officeRolloutStatusIssue = ValidateV60OfficeRolloutStatus(officeRolloutStatusPath);
        if (officeRolloutStatusIssue is not null)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                officeRolloutStatusIssue);
        }

        var standardsRuntimePath = Path.Combine(docsRoot, "smoke", "v6.0", "standards-runtime.md");
        var standardsRuntimeText = TryReadText(standardsRuntimePath);
        if (string.IsNullOrWhiteSpace(standardsRuntimeText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/standards-runtime.md is missing or unreadable; v6.0 standards runtime smoke behavior is not disclosed.");
        }

        var standardsRuntimePhrases = new[]
        {
            "standards validate --output json",
            "standards runtime",
            "table",
            "summary",
            "Markdown",
            "detail",
            "parity",
            "final file-tree snapshot evidence",
            "read-only",
            "does not start Revit",
            "does not write model data",
            "database",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missingStandardsRuntime = standardsRuntimePhrases
            .Where(phrase => !standardsRuntimeText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingStandardsRuntime.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/standards-runtime.md is missing standards runtime smoke disclosures: {string.Join(", ", missingStandardsRuntime)}.");
        }

        var issueSpinePath = Path.Combine(docsRoot, "smoke", "v6.0", "issue-spine.md");
        var issueSpineText = TryReadText(issueSpinePath);
        if (string.IsNullOrWhiteSpace(issueSpineText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/issue-spine.md is missing or unreadable; v6.0 issue command spine smoke behavior is not disclosed.");
        }

        var issueSpinePhrases = new[]
        {
            "issue preflight --profile",
            "issue package --profile",
            "dry-run first",
            "hidden mutation guards",
            "issue-package-receipt.v1",
            "table",
            "summary",
            "Markdown",
            "detail",
            "parity",
            "dry-run no-write evidence",
            "does not start Revit",
            "database",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missingIssueSpine = issueSpinePhrases
            .Where(phrase => !issueSpineText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingIssueSpine.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/issue-spine.md is missing issue command spine smoke disclosures: {string.Join(", ", missingIssueSpine)}.");
        }

        var deliverablesVerifyPath = Path.Combine(docsRoot, "smoke", "v6.0", "deliverables-verify.md");
        var deliverablesVerifyText = TryReadText(deliverablesVerifyPath);
        if (string.IsNullOrWhiteSpace(deliverablesVerifyText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/deliverables-verify.md is missing or unreadable; v6.0 deliverables verification smoke behavior is not disclosed.");
        }

        var deliverablesVerifyPhrases = new[]
        {
            "deliverables verify --output json",
            "local manifest-read",
            "readable-receipt evidence",
            "Kinds",
            "Outcomes",
            "counts",
            "table",
            "Markdown",
            "without package writes",
            "missing receipts",
            "no Revit API",
            "starting Revit",
            "database",
            "SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missingDeliverablesVerify = deliverablesVerifyPhrases
            .Where(phrase => !deliverablesVerifyText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingDeliverablesVerify.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/deliverables-verify.md is missing deliverables verification smoke disclosures: {string.Join(", ", missingDeliverablesVerify)}.");
        }

        var ledgerQueryPath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-query.md");
        var ledgerQueryText = TryReadText(ledgerQueryPath);
        if (string.IsNullOrWhiteSpace(ledgerQueryText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-query.md is missing or unreadable; v6.0 ledger query smoke behavior is not disclosed.");
        }

        var ledgerQueryPhrases = new[]
        {
            "read-only ledger query",
            "ledger-query.v1",
            "journal",
            "history",
            "delivery",
            "workflow receipt",
            "timestamp/source/path/line ordering",
            "JSON/table/Markdown output semantic parity",
            "malformed",
            "event-level no-write evidence",
            "final file-tree snapshot evidence",
            "no database",
        };
        var missingLedgerQuery = ledgerQueryPhrases
            .Where(phrase => !ledgerQueryText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerQuery.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-query.md is missing ledger query smoke disclosures: {string.Join(", ", missingLedgerQuery)}.");
        }

        var ledgerValidatePath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-validate.md");
        var ledgerValidateText = TryReadText(ledgerValidatePath);
        if (string.IsNullOrWhiteSpace(ledgerValidateText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-validate.md is missing or unreadable; v6.0 ledger validation smoke behavior is not disclosed.");
        }

        var ledgerValidatePhrases = new[]
        {
            "read-only ledger validate",
            "ledger-validate.v1",
            "source readability",
            "artifact links",
            "receipt status",
            "timestamp format",
            "explicit UTC offset",
            "time filters preserve invalid timestamp warnings",
            "validation JSON/table/Markdown semantic parity",
            "event-level no-write evidence",
            "final file-tree snapshot evidence",
            "does not write files",
            "no database",
        };
        var missingLedgerValidate = ledgerValidatePhrases
            .Where(phrase => !ledgerValidateText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerValidate.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-validate.md is missing ledger validation smoke disclosures: {string.Join(", ", missingLedgerValidate)}.");
        }

        var ledgerStatsPath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-stats.md");
        var ledgerStatsText = TryReadText(ledgerStatsPath);
        if (string.IsNullOrWhiteSpace(ledgerStatsText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-stats.md is missing or unreadable; v6.0 ledger stats smoke behavior is not disclosed.");
        }

        var ledgerStatsPhrases = new[]
        {
            "read-only ledger stats",
            "ledger-stats.v1",
            "operation counts",
            "source counts",
            "action counts",
            "category and operator counts",
            "receipt status counts",
            "issue source counts",
            "issue severity counts",
            "malformed journal, delivery manifest, and workflow receipt artifacts",
            "JSON/table/Markdown stats semantic parity",
            "event-level no-write evidence",
            "final file-tree snapshot evidence",
            "does not write files",
            "no database",
        };
        var missingLedgerStats = ledgerStatsPhrases
            .Where(phrase => !ledgerStatsText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerStats.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-stats.md is missing ledger stats smoke disclosures: {string.Join(", ", missingLedgerStats)}.");
        }

        var ledgerTimelinePath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-timeline.md");
        var ledgerTimelineText = TryReadText(ledgerTimelinePath);
        if (string.IsNullOrWhiteSpace(ledgerTimelineText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-timeline.md is missing or unreadable; v6.0 ledger timeline smoke behavior is not disclosed.");
        }

        var ledgerTimelinePhrases = new[]
        {
            "read-only ledger timeline",
            "ledger-timeline.v1",
            "bucket",
            "source",
            "action",
            "category counts per bucket",
            "operator counts per bucket",
            "receipt status",
            "issue severity",
            "JSON/table/Markdown timeline semantic parity",
            "unbucketed timestamp",
            "explicit UTC offset",
            "time filters preserve unbucketed timestamp warnings",
            "projectDirectories",
            "byProject",
            "event-level no-write evidence",
            "final file-tree snapshot evidence",
            "does not write files",
            "no database",
        };
        var missingLedgerTimeline = ledgerTimelinePhrases
            .Where(phrase => !ledgerTimelineText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerTimeline.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-timeline.md is missing ledger timeline smoke disclosures: {string.Join(", ", missingLedgerTimeline)}.");
        }

        var ledgerAnalyticsPath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-analytics.md");
        var ledgerAnalyticsText = TryReadText(ledgerAnalyticsPath);
        if (string.IsNullOrWhiteSpace(ledgerAnalyticsText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-analytics.md is missing or unreadable; v6.0 ledger analytics bundle behavior is not disclosed.");
        }

        var ledgerAnalyticsPhrases = new[]
        {
            "ledger analytics",
            "ledger-analytics-bundle.v1",
            "ledger-stats.v1",
            "ledger-timeline.v1",
            "JSON/table/Markdown output formats",
            "localOnly=true",
            "databaseRuntime=false",
            "networkService=false",
            "does not start Revit",
            "does not call a network service",
            "does not create a database",
        };
        var missingLedgerAnalytics = ledgerAnalyticsPhrases
            .Where(phrase => !ledgerAnalyticsText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerAnalytics.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-analytics.md is missing ledger analytics bundle smoke disclosures: {string.Join(", ", missingLedgerAnalytics)}.");
        }

        var ledgerAppendPath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-append.md");
        var ledgerAppendText = TryReadText(ledgerAppendPath);
        if (string.IsNullOrWhiteSpace(ledgerAppendText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-append.md is missing or unreadable; v6.0 ledger append runtime smoke behavior is not disclosed.");
        }

        var ledgerAppendPhrases = new[]
        {
            "append-only ledger runtime",
            "ledger append",
            "ledger-append.v1",
            "ledger-operation.v1",
            ".revitcli/ledger/operations.jsonl",
            "dry-run default",
            "--yes",
            "source ledger",
            "deterministic evidence links",
            "JSON/table/Markdown output semantic parity",
            "bounded local write evidence",
            "does not start Revit",
            "no database",
        };
        var missingLedgerAppend = ledgerAppendPhrases
            .Where(phrase => !ledgerAppendText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerAppend.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-append.md is missing ledger append runtime smoke disclosures: {string.Join(", ", missingLedgerAppend)}.");
        }

        var ledgerReplayPath = Path.Combine(docsRoot, "smoke", "v6.0", "ledger-replay.md");
        var ledgerReplayText = TryReadText(ledgerReplayPath);
        if (string.IsNullOrWhiteSpace(ledgerReplayText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/ledger-replay.md is missing or unreadable; v6.0 ledger replay preview smoke behavior is not disclosed.");
        }

        var ledgerReplayPhrases = new[]
        {
            "ledger replay",
            "ledger-replay.v1",
            "preview-only",
            "dryRun",
            "applySupported",
            "canApply",
            "source ledger",
            "JSON/table/Markdown output semantic parity",
            "does not write files",
            "does not start Revit",
            "no database",
        };
        var missingLedgerReplay = ledgerReplayPhrases
            .Where(phrase => !ledgerReplayText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingLedgerReplay.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/ledger-replay.md is missing ledger replay preview smoke disclosures: {string.Join(", ", missingLedgerReplay)}.");
        }

        var workflowRegistryPath = Path.Combine(docsRoot, "smoke", "v6.0", "workflow-registry.md");
        var workflowRegistryText = TryReadText(workflowRegistryPath);
        if (string.IsNullOrWhiteSpace(workflowRegistryText))
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                "docs/smoke/v6.0/workflow-registry.md is missing or unreadable; v6.0 workflow registry smoke behavior is not disclosed.");
        }

        var workflowRegistryPhrases = new[]
        {
            "read-only workflow registry",
            "workflow-registry.v1",
            "inputs",
            "outputs",
            "read/write scope",
            "risk level",
            "dry-run command",
            "approval command",
            "rollback support",
            "receipt schema",
            "acceptance evidence",
            "JSON/table/Markdown output semantic parity",
            "does not write files",
            "final file-tree snapshot evidence",
            "event-level no-write evidence",
            "no SaaS",
            "MCP",
            "built-in LLM",
            "dashboard-central",
        };
        var missingWorkflowRegistry = workflowRegistryPhrases
            .Where(phrase => !workflowRegistryText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (missingWorkflowRegistry.Length > 0)
        {
            return Check(
                "v60LocalBimOpsContractGate",
                false,
                $"docs/smoke/v6.0/workflow-registry.md is missing workflow registry smoke disclosures: {string.Join(", ", missingWorkflowRegistry)}.");
        }

        var commandSpineRuntimeReady = RunCommandSpineRuntimeCheck(
            out var commandSpineRuntimeEvidence,
            out var commandSpineRuntimeDetails);
        var workflowRegistryRuntimeReady = RunWorkflowRegistryRuntimeCheck(out var workflowRegistryRuntimeEvidence);
        var ledgerQueryValidateRuntimeReady = RunLedgerQueryValidateRuntimeCheck(out var ledgerQueryValidateRuntimeEvidence);
        var ledgerStatsRuntimeReady = RunLedgerStatsRuntimeCheck(out var ledgerStatsRuntimeEvidence);
        var ledgerTimelineRuntimeReady = RunLedgerTimelineRuntimeCheck(out var ledgerTimelineRuntimeEvidence);
        var ledgerAnalyticsRuntimeReady = RunLedgerAnalyticsRuntimeCheck(out var ledgerAnalyticsRuntimeEvidence);
        var ledgerAppendRuntimeReady = RunLedgerAppendRuntimeCheck(out var ledgerAppendRuntimeEvidence);
        var ledgerReplayRuntimeReady = RunLedgerReplayRuntimeCheck(out var ledgerReplayRuntimeEvidence);
        var runtimeEvidence = new WorkbenchRuntimeEvidence(
            commandSpineRuntimeReady,
            commandSpineRuntimeDetails.OutputParity,
            commandSpineRuntimeDetails.NoWrites,
            workflowRegistryRuntimeReady,
            ledgerAppendRuntimeReady,
            ledgerQueryValidateRuntimeReady,
            ledgerStatsRuntimeReady,
            ledgerTimelineRuntimeReady,
            ledgerAnalyticsRuntimeReady,
            ledgerReplayRuntimeReady,
            commandSpineRuntimeDetails.StandardsValidate,
            commandSpineRuntimeDetails.IssuePreflight,
            commandSpineRuntimeDetails.IssuePackageDryRun,
            commandSpineRuntimeDetails.DeliverablesVerify,
            commandSpineRuntimeDetails.JournalVerify,
            commandSpineRuntimeDetails.HistoryList,
            commandSpineRuntimeDetails.HistoryListCountConsistency,
            commandSpineRuntimeDetails.HistoryListRowOrder,
            commandSpineRuntimeDetails.RollbackDryRun,
            commandSpineRuntimeDetails.RollbackDryRunPreview,
            commandSpineRuntimeDetails.RollbackNoMutatingSetRequest,
            commandSpineRuntimeDetails.HistoryListEvidence,
            commandSpineRuntimeDetails.RollbackDryRunEvidence,
            commandSpineRuntimeReady &&
            workflowRegistryRuntimeReady &&
            ledgerAppendRuntimeReady &&
            ledgerQueryValidateRuntimeReady &&
            ledgerStatsRuntimeReady &&
            ledgerTimelineRuntimeReady &&
            ledgerAnalyticsRuntimeReady &&
            ledgerReplayRuntimeReady);
        var surfaceReady =
            commandPaths.Contains("doctor") &&
            commandPaths.Contains("workbench verify --contract workbench-contract.v2") &&
            commandPaths.Contains("release verify --strict") &&
            commandPaths.Contains("standards validate") &&
            commandPaths.Contains("issue preflight") &&
            commandPaths.Contains("issue package") &&
            commandPaths.Contains("deliverables verify") &&
            commandPaths.Contains("journal verify") &&
            commandPaths.Contains("history list") &&
            commandPaths.Contains("ledger append") &&
            commandPaths.Contains("ledger replay") &&
            commandPaths.Contains("ledger query") &&
            commandPaths.Contains("ledger validate") &&
            commandPaths.Contains("ledger stats") &&
            commandPaths.Contains("ledger timeline") &&
            commandPaths.Contains("ledger analytics") &&
            commandPaths.Contains("workflow registry") &&
            commandPaths.Contains("rollback") &&
            outputSchemas.Contains("workbench-verify-report.v2") &&
            outputSchemas.Contains("ledger-append.v1") &&
            outputSchemas.Contains("ledger-replay.v1") &&
            outputSchemas.Contains("ledger-query.v1") &&
            outputSchemas.Contains("ledger-validate.v1") &&
            outputSchemas.Contains("ledger-stats.v1") &&
            outputSchemas.Contains("ledger-timeline.v1") &&
            outputSchemas.Contains("ledger-analytics-bundle.v1") &&
            outputSchemas.Contains("workflow-registry.v1") &&
            receiptSchemas.Contains("plan-receipt.v1") &&
            receiptSchemas.Contains("issue-package-receipt.v1") &&
            receiptSchemas.Contains("delivery-bundle-receipt.v1") &&
            safeguardNames.Contains("plan-apply") &&
            safeguardNames.Contains("rollback") &&
            safeguardNames.Contains("issue-package") &&
            safeguardNames.Contains("deliverables-bundle") &&
            commandSpineRuntimeReady &&
            workflowRegistryRuntimeReady &&
            ledgerAppendRuntimeReady &&
            ledgerQueryValidateRuntimeReady &&
            ledgerStatsRuntimeReady &&
            ledgerTimelineRuntimeReady &&
            ledgerAnalyticsRuntimeReady &&
            ledgerReplayRuntimeReady;

        return Check(
            "v60LocalBimOpsContractGate",
            surfaceReady,
            surfaceReady
                ? $"v6.0 Local BIMOps contract baseline is docs/contract-first with staged local runtime: command spine, deterministic receipts, rollback preconditions, local audit trail, standards runtime, project memory, workflow registry, append-only ledger runtime, preview-only ledger replay, local ledger analytics bundle, and non-goals are gated without SaaS, MCP, dashboard-central, built-in LLM, database, or live Revit ledger apply; {commandSpineRuntimeEvidence}; {workflowRegistryRuntimeEvidence}; {ledgerAppendRuntimeEvidence}; {ledgerReplayRuntimeEvidence}; {ledgerQueryValidateRuntimeEvidence}; {ledgerStatsRuntimeEvidence}; {ledgerTimelineRuntimeEvidence}; {ledgerAnalyticsRuntimeEvidence}."
                : $"v6.0 Local BIMOps contract baseline is missing command spine paths, command-spine runtime evidence, ledger append/replay/query/validate/stats/timeline/analytics output schemas, workflow-registry.v1, ledger-append.v1, ledger-replay.v1, ledger-query.v1, ledger-validate.v1, ledger-stats.v1, ledger-timeline.v1 or ledger-analytics-bundle.v1 runtime evidence, receipt schemas, rollback/package safeguards, or workbench v2 output schema. Command spine runtime evidence: {commandSpineRuntimeEvidence}. Workflow registry runtime evidence: {workflowRegistryRuntimeEvidence}. Ledger append runtime evidence: {ledgerAppendRuntimeEvidence}. Ledger replay runtime evidence: {ledgerReplayRuntimeEvidence}. Ledger query/validate runtime evidence: {ledgerQueryValidateRuntimeEvidence}. Ledger stats runtime evidence: {ledgerStatsRuntimeEvidence}. Ledger timeline runtime evidence: {ledgerTimelineRuntimeEvidence}. Ledger analytics runtime evidence: {ledgerAnalyticsRuntimeEvidence}",
            runtimeEvidence);
    }

    private static bool RunCommandSpineRuntimeCheck(
        out string evidence,
        out CommandSpineRuntimeEvidence commandSpineEvidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-command-spine-check-{Guid.NewGuid():N}");
        commandSpineEvidence = CommandSpineRuntimeEvidence.None;
        try
        {
            WriteCommandSpineStandardsProject(root);
            WriteCommandSpineDeliveryEvidence(root);
            var issueProfilePath = Path.Combine(root, ".revitcli", "issue.yml");
            File.WriteAllText(issueProfilePath, """
schemaVersion: issue-profile.v1
checks:
  - name: issue exports
    command: revitcli publish issue --dry-run --output json
package:
  commands:
    - revitcli deliverables bundle --dry-run --output json
""");
            var rollbackReceiptPath = WriteCommandSpineAuditEvidence(root);

            var before = SnapshotLocalFiles(root);
            using var writeProbe = new FileWriteProbe(root);
            var standardsInvocation = InvokeRootCommandLine([
                "standards",
                "validate",
                "--dir",
                root,
                "--output",
                "json",
            ]);
            var standardsTableInvocation = InvokeRootCommandLine([
                "standards",
                "validate",
                "--dir",
                root,
                "--output",
                "table",
            ]);
            var standardsMarkdownInvocation = InvokeRootCommandLine([
                "standards",
                "validate",
                "--dir",
                root,
                "--output",
                "markdown",
            ]);
            var issuePreflightInvocation = InvokeRootCommandLine([
                "issue",
                "preflight",
                "--profile",
                issueProfilePath,
                "--output",
                "json",
                "--fail-on",
                "error",
            ]);
            var issuePreflightTableInvocation = InvokeRootCommandLine([
                "issue",
                "preflight",
                "--profile",
                issueProfilePath,
                "--output",
                "table",
                "--fail-on",
                "error",
            ]);
            var issuePreflightMarkdownInvocation = InvokeRootCommandLine([
                "issue",
                "preflight",
                "--profile",
                issueProfilePath,
                "--output",
                "markdown",
                "--fail-on",
                "error",
            ]);
            var bundlePath = Path.Combine(root, "deliverables", "issue-package.zip");
            var issuePackageInvocation = InvokeRootCommandLine([
                "issue",
                "package",
                "--profile",
                issueProfilePath,
                "--bundle-path",
                bundlePath,
                "--dry-run",
                "--include-receipts",
                "true",
                "--output",
                "json",
            ]);
            var issuePackageTableInvocation = InvokeRootCommandLine([
                "issue",
                "package",
                "--profile",
                issueProfilePath,
                "--bundle-path",
                bundlePath,
                "--dry-run",
                "--include-receipts",
                "true",
                "--output",
                "table",
            ]);
            var issuePackageMarkdownInvocation = InvokeRootCommandLine([
                "issue",
                "package",
                "--profile",
                issueProfilePath,
                "--bundle-path",
                bundlePath,
                "--dry-run",
                "--include-receipts",
                "true",
                "--output",
                "markdown",
            ]);
            var deliverablesInvocation = InvokeRootCommandLine([
                "deliverables",
                "verify",
                "--dir",
                root,
                "--output",
                "json",
            ]);
            var deliverablesTableInvocation = InvokeRootCommandLine([
                "deliverables",
                "verify",
                "--dir",
                root,
                "--output",
                "table",
            ]);
            var deliverablesMarkdownInvocation = InvokeRootCommandLine([
                "deliverables",
                "verify",
                "--dir",
                root,
                "--output",
                "markdown",
            ]);
            var journalInvocation = InvokeRootCommandLine([
                "journal",
                "verify",
                "--dir",
                root,
                "--output",
                "json",
            ]);
            var journalTableInvocation = InvokeRootCommandLine([
                "journal",
                "verify",
                "--dir",
                root,
                "--output",
                "table",
            ]);
            var historyInvocation = InvokeRootCommandLine([
                "history",
                "list",
                "--dir",
                Path.Combine(root, ".revitcli", "history"),
                "--limit",
                "5",
                "--output",
                "json",
            ]);
            var historyTableInvocation = InvokeRootCommandLine([
                "history",
                "list",
                "--dir",
                Path.Combine(root, ".revitcli", "history"),
                "--limit",
                "5",
                "--output",
                "table",
            ]);
            var rollbackHandler = new CommandSpineRollbackHandler();
            var rollbackInvocation = InvokeRootCommandLine(
            [
                "rollback",
                rollbackReceiptPath,
                "--dry-run",
                "--max-changes",
                "5",
            ],
            rollbackHandler);
            writeProbe.Drain();
            var noWrites =
                before.SequenceEqual(SnapshotLocalFiles(root)) &&
                !File.Exists(bundlePath) &&
                writeProbe.IsClean;

            if (standardsInvocation.ExitCode != 0 ||
                issuePreflightInvocation.ExitCode != 0 ||
                issuePackageInvocation.ExitCode != 0 ||
                deliverablesInvocation.ExitCode != 0 ||
                journalInvocation.ExitCode != 0 ||
                historyInvocation.ExitCode != 0 ||
                rollbackInvocation.ExitCode != 0 ||
                standardsTableInvocation.ExitCode != 0 ||
                standardsMarkdownInvocation.ExitCode != 0 ||
                issuePreflightTableInvocation.ExitCode != 0 ||
                issuePreflightMarkdownInvocation.ExitCode != 0 ||
                issuePackageTableInvocation.ExitCode != 0 ||
                issuePackageMarkdownInvocation.ExitCode != 0 ||
                deliverablesTableInvocation.ExitCode != 0 ||
                deliverablesMarkdownInvocation.ExitCode != 0 ||
                journalTableInvocation.ExitCode != 0 ||
                historyTableInvocation.ExitCode != 0)
            {
                commandSpineEvidence = new CommandSpineRuntimeEvidence(
                    standardsInvocation.ExitCode == 0,
                    issuePreflightInvocation.ExitCode == 0,
                    issuePackageInvocation.ExitCode == 0,
                    deliverablesInvocation.ExitCode == 0,
                    journalInvocation.ExitCode == 0,
                    historyInvocation.ExitCode == 0,
                    rollbackInvocation.ExitCode == 0,
                    false,
                    false,
                    false,
                    false,
                    WorkbenchHistoryListEvidence.Empty,
                    WorkbenchRollbackDryRunEvidence.Empty,
                    false,
                    noWrites);
                evidence = $"command spine runtime exited standards={standardsInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{standardsTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{standardsMarkdownInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} preflight={issuePreflightInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{issuePreflightTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{issuePreflightMarkdownInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} package={issuePackageInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{issuePackageTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{issuePackageMarkdownInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} deliverables={deliverablesInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{deliverablesTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{deliverablesMarkdownInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} journal={journalInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{journalTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} history={historyInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}/{historyTableInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)} rollback={rollbackInvocation.ExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var standardsDocument = JsonDocument.Parse(standardsInvocation.Stdout);
            using var preflightDocument = JsonDocument.Parse(issuePreflightInvocation.Stdout);
            using var packageDocument = JsonDocument.Parse(issuePackageInvocation.Stdout);
            using var deliverablesDocument = JsonDocument.Parse(deliverablesInvocation.Stdout);
            using var journalDocument = JsonDocument.Parse(journalInvocation.Stdout);
            using var historyDocument = JsonDocument.Parse(historyInvocation.Stdout);
            var standards = standardsDocument.RootElement;
            var preflight = preflightDocument.RootElement;
            var package = packageDocument.RootElement;
            var deliverables = deliverablesDocument.RootElement;
            var journal = journalDocument.RootElement;
            var history = historyDocument.RootElement;
            var standardsOutputParityReady = CommandSpineStandardsOutputParityReady(
                standards,
                standardsTableInvocation.Stdout,
                standardsMarkdownInvocation.Stdout);
            var issuePreflightOutputParityReady = CommandSpineIssuePreflightOutputParityReady(
                preflight,
                issuePreflightTableInvocation.Stdout,
                issuePreflightMarkdownInvocation.Stdout);
            var issuePackageOutputParityReady = CommandSpineIssuePackageOutputParityReady(
                package,
                issuePackageTableInvocation.Stdout,
                issuePackageMarkdownInvocation.Stdout);
            var deliverablesOutputParityReady = CommandSpineDeliverablesOutputParityReady(
                deliverables,
                deliverablesTableInvocation.Stdout,
                deliverablesMarkdownInvocation.Stdout);
            var journalOutputParityReady = CommandSpineJournalOutputParityReady(journal, journalTableInvocation.Stdout);
            var entries = history.GetProperty("entries").EnumerateArray().ToArray();
            var historyTableEvidence = AnalyzeHistoryTableRows(entries, historyTableInvocation.Stdout);
            var historyListCountConsistencyReady = CommandSpineHistoryCountConsistencyReady(history, entries);
            var historyListRowOrderReady = historyTableEvidence.IdOrderMatch;
            var historyOutputParityReady = CommandSpineHistoryOutputParityReady(
                history,
                historyTableInvocation.Stdout,
                historyListCountConsistencyReady,
                historyTableEvidence);
            var commandSpineOutputParityReady =
                standardsOutputParityReady &&
                issuePreflightOutputParityReady &&
                issuePackageOutputParityReady &&
                deliverablesOutputParityReady &&
                journalOutputParityReady &&
                historyOutputParityReady;

            var standardsReady =
                standards.GetProperty("valid").GetBoolean() &&
                standards.GetProperty("name").GetString() == "office" &&
                standards.GetProperty("issues").GetArrayLength() == 0 &&
                standardsOutputParityReady;
            var issuePreflightReady =
                preflight.GetProperty("schemaVersion").GetString() == "issue-preflight-report.v1" &&
                preflight.GetProperty("noHiddenMutation").GetBoolean() &&
                preflight.GetProperty("errorCount").GetInt32() == 0 &&
                issuePreflightOutputParityReady;
            var issuePackageDryRunReady =
                package.GetProperty("schemaVersion").GetString() == "issue-package-receipt.v1" &&
                package.GetProperty("dryRun").GetBoolean() &&
                !package.GetProperty("bundleWritten").GetBoolean() &&
                !package.GetProperty("receiptWritten").GetBoolean() &&
                package.GetProperty("receiptCount").GetInt32() >= 1 &&
                issuePackageOutputParityReady;
            var deliverablesReady =
                deliverables.GetProperty("schemaVersion").GetString() == "deliverables.v1" &&
                deliverables.GetProperty("success").GetBoolean() &&
                deliverables.GetProperty("valid").GetBoolean() &&
                deliverables.GetProperty("entryCount").GetInt32() >= 1 &&
                deliverablesOutputParityReady;
            var journalReady =
                journal.GetProperty("isValid").GetBoolean() &&
                journal.GetProperty("entryCount").GetInt32() == 2 &&
                journalOutputParityReady;
            var historyReady =
                history.GetProperty("schemaVersion").GetString() == "history-list.v1" &&
                history.GetProperty("initialized").GetBoolean() &&
                history.GetProperty("entryCount").GetInt32() == 1 &&
                historyOutputParityReady;
            var rollbackReady =
                rollbackHandler.SawDryRunSetPreview &&
                !rollbackHandler.SawMutatingSetRequest &&
                rollbackInvocation.Stdout.Contains("Dry run: 1 rollback action", StringComparison.OrdinalIgnoreCase) &&
                rollbackInvocation.Stdout.Contains("Safe apply command after review", StringComparison.OrdinalIgnoreCase);
            var rollbackDryRunEvidence = CreateRollbackDryRunEvidence(rollbackInvocation.Stdout, rollbackHandler);
            var historyListEvidence = new WorkbenchHistoryListEvidence(
                history.GetProperty("entryCount").GetInt32(),
                history.GetProperty("hiddenCount").GetInt32(),
                history.GetProperty("returnedCount").GetInt32(),
                historyTableEvidence.TableRowCount,
                historyListCountConsistencyReady,
                historyListRowOrderReady,
                historyTableEvidence.HeaderMatched);
            commandSpineEvidence = new CommandSpineRuntimeEvidence(
                standardsReady,
                issuePreflightReady,
                issuePackageDryRunReady,
                deliverablesReady,
                journalReady,
                historyReady,
                rollbackReady,
                historyListCountConsistencyReady,
                historyListRowOrderReady,
                rollbackHandler.SawDryRunSetPreview,
                !rollbackHandler.SawMutatingSetRequest,
                historyListEvidence,
                rollbackDryRunEvidence,
                commandSpineOutputParityReady,
                noWrites);
            var ok =
                commandSpineEvidence.AllReady;

            evidence = ok
                ? $"command spine runtime validates public CLI parser paths for standards validate, issue preflight, issue package dry-run, deliverables verify, journal verify, history list JSON/table outputs, and rollback dry-run with JSON schemas, table summary and Markdown detail parity for supported command-spine paths, journal verify JSON/table validity/root-hash parity, history-list.v1 JSON count consistency and table row-order parity, hidden mutation guard, dry-run no-write package evidence, readable receipt evidence, signed journal execution, history-list.v1 execution, rollback safe preview execution, rollback dry-run request enforcement, final file-tree snapshot evidence, event-level no-write evidence, and sub-command runtime evidence {commandSpineEvidence.Describe()}"
                : $"command spine runtime payload is missing required standards/issue/deliverables/journal/history/rollback evidence ({commandSpineEvidence.Describe()}, outputParity standards={standardsOutputParityReady.ToString().ToLowerInvariant()} preflight={issuePreflightOutputParityReady.ToString().ToLowerInvariant()} package={issuePackageOutputParityReady.ToString().ToLowerInvariant()} deliverables={deliverablesOutputParityReady.ToString().ToLowerInvariant()} journal={journalOutputParityReady.ToString().ToLowerInvariant()} history={historyOutputParityReady.ToString().ToLowerInvariant()}, historyListCountConsistency={historyListCountConsistencyReady.ToString().ToLowerInvariant()}, historyListRowOrder={historyListRowOrderReady.ToString().ToLowerInvariant()}, noWrites={noWrites.ToString().ToLowerInvariant()}, writes={writeProbe.Describe()}, rollbackDryRunPreview={rollbackHandler.SawDryRunSetPreview.ToString().ToLowerInvariant()}, rollbackNoMutatingSetRequest={(!rollbackHandler.SawMutatingSetRequest).ToString().ToLowerInvariant()})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            commandSpineEvidence = CommandSpineRuntimeEvidence.None;
            evidence = $"command spine runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static CommandLineInvocation InvokeRootCommandLine(string[] args, HttpMessageHandler? httpHandler = null)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var previousExitCode = Environment.ExitCode;
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var stderr = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Environment.ExitCode = 0;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var http = httpHandler == null
                ? new HttpClient()
                : new HttpClient(httpHandler);
            http.BaseAddress = new Uri("http://127.0.0.1:17839");
            var client = new RevitClient(http);
            var root = CliCommandCatalog.CreateRootCommand(
                client,
                new CliConfig(),
                includeInteractiveCommand: false,
                includeBatchCommand: false);
            var invokeExitCode = root.InvokeAsync(args).GetAwaiter().GetResult();
            var exitCode = Environment.ExitCode != 0 ? Environment.ExitCode : invokeExitCode;
            return new CommandLineInvocation(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            Environment.ExitCode = previousExitCode;
        }
    }

    private sealed record CommandLineInvocation(int ExitCode, string Stdout, string Stderr);

    private static bool CommandSpineStandardsOutputParityReady(JsonElement report, string table, string markdown)
    {
        var name = report.GetProperty("name").GetString() ?? "";
        var packVersion = report.GetProperty("packVersion").GetString() ?? "";
        var status = report.GetProperty("valid").GetBoolean() ? "OK" : "FAIL";
        return ContainsAll(table, "Standards validation", $"Name: {name}", $"Pack version: {packVersion}", $"Status: {status}", "No issues.") &&
               ContainsAll(markdown, "# Standards Validation", $"- Name: `{name}`", $"- Pack version: `{packVersion}`", $"- Status: `{status}`", "## Issues", "- None.");
    }

    private static bool CommandSpineIssuePreflightOutputParityReady(JsonElement report, string table, string markdown)
    {
        var schema = report.GetProperty("schemaVersion").GetString() ?? "";
        var checkCount = report.GetProperty("checkCount").GetInt32();
        var errorCount = report.GetProperty("errorCount").GetInt32();
        var warningCount = report.GetProperty("warningCount").GetInt32();
        var summaryReady =
            ContainsAll(
                table,
                $"Issue preflight ({schema})",
                $"checks={checkCount.ToString(CultureInfo.InvariantCulture)}",
                $"errors={errorCount.ToString(CultureInfo.InvariantCulture)}",
                $"warnings={warningCount.ToString(CultureInfo.InvariantCulture)}") &&
            ContainsAll(
                markdown,
                "# Issue Preflight",
                $"- Schema: `{schema}`",
                $"- Checks: `{checkCount.ToString(CultureInfo.InvariantCulture)}`",
                $"- Errors: `{errorCount.ToString(CultureInfo.InvariantCulture)}`",
                $"- Warnings: `{warningCount.ToString(CultureInfo.InvariantCulture)}`");
        return summaryReady &&
               JsonObjectArrayValuesAppear(report.GetProperty("checks"), markdown, "name", "status", "severity") &&
               JsonStringArrayValuesAppear(report.GetProperty("commandPaths"), markdown);
    }

    private static bool CommandSpineIssuePackageOutputParityReady(JsonElement report, string table, string markdown)
    {
        var schema = report.GetProperty("schemaVersion").GetString() ?? "";
        var fileCount = report.GetProperty("fileCount").GetInt32();
        var errorCount = report.GetProperty("errorCount").GetInt32();
        var written = report.GetProperty("bundleWritten").GetBoolean().ToString().ToLowerInvariant();
        var summaryReady =
            ContainsAll(
                table,
                $"Issue package ({schema})",
                $"files={fileCount.ToString(CultureInfo.InvariantCulture)}",
                $"errors={errorCount.ToString(CultureInfo.InvariantCulture)}",
                $"written={written}") &&
            ContainsAll(
                markdown,
                "# Issue Package",
                $"- Schema: `{schema}`",
                $"- Dry run: `{report.GetProperty("dryRun").GetBoolean().ToString().ToLowerInvariant()}`",
                $"- Files: `{fileCount.ToString(CultureInfo.InvariantCulture)}`");
        return summaryReady &&
               JsonStringArrayValuesAppear(report.GetProperty("plannedActions"), markdown) &&
               JsonObjectArrayValuesAppear(report.GetProperty("files"), markdown, "kind", "archivePath", "sourcePath") &&
               JsonStringArrayValuesAppear(report.GetProperty("commandPaths"), markdown);
    }

    private static bool CommandSpineDeliverablesOutputParityReady(JsonElement report, string table, string markdown)
    {
        var entryCount = report.GetProperty("entryCount").GetInt32();
        var stats = report.GetProperty("stats");
        return ContainsAll(
                   table,
                   "OK: Delivery manifest valid",
                   $"OK: Entries verified: {entryCount.ToString(CultureInfo.InvariantCulture)}") &&
               ContainsAll(
                   markdown,
                   "# Delivery Manifest Verification",
                   "- Status: `OK`",
                   $"- Entries verified: `{entryCount.ToString(CultureInfo.InvariantCulture)}`") &&
               JsonStatsCountsAppear(stats.GetProperty("kinds"), table) &&
               JsonStatsCountsAppear(stats.GetProperty("outcomes"), table) &&
               JsonStatsCountsAppear(stats.GetProperty("kinds"), markdown) &&
               JsonStatsCountsAppear(stats.GetProperty("outcomes"), markdown);
    }

    private static bool CommandSpineJournalOutputParityReady(JsonElement report, string table)
    {
        if (!report.GetProperty("isValid").GetBoolean() ||
            report.GetProperty("errors").GetArrayLength() != 0)
        {
            return false;
        }

        var entryCount = report.GetProperty("entryCount").GetInt32();
        var rootHash = report.GetProperty("rootHash").GetString() ?? "";
        var signaturePath = report.GetProperty("signaturePath").GetString() ?? "";
        return ContainsAll(
            table,
            $"OK: Journal signature valid: {signaturePath}",
            $"OK: Entries verified: {entryCount.ToString(CultureInfo.InvariantCulture)}",
            $"OK: Root hash: {rootHash}");
    }

    private static bool CommandSpineHistoryOutputParityReady(
        JsonElement report,
        string table,
        bool countConsistencyReady,
        HistoryTableEvidence tableEvidence)
    {
        if (report.GetProperty("schemaVersion").GetString() != "history-list.v1" ||
            string.IsNullOrWhiteSpace(report.GetProperty("historyDirectory").GetString()) ||
            !report.GetProperty("initialized").GetBoolean())
        {
            return false;
        }

        return countConsistencyReady &&
               ContainsAll(table, "capturedAt", "source", "elements") &&
               tableEvidence.HeaderMatched &&
               tableEvidence.IdOrderMatch;
    }

    private static bool CommandSpineHistoryCountConsistencyReady(JsonElement report, IReadOnlyList<JsonElement> entries)
    {
        var returnedCount = report.GetProperty("returnedCount").GetInt32();
        var hiddenCount = report.GetProperty("hiddenCount").GetInt32();
        var entryCount = report.GetProperty("entryCount").GetInt32();
        return entryCount == returnedCount + hiddenCount &&
               returnedCount == entries.Count;
    }

    private static HistoryTableEvidence AnalyzeHistoryTableRows(IReadOnlyList<JsonElement> entries, string table)
    {
        var lines = table
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .ToArray();
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("capturedAt", StringComparison.Ordinal) &&
            line.Contains("source", StringComparison.Ordinal) &&
            line.Contains("elements", StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 + entries.Count > lines.Length)
        {
            return new HistoryTableEvidence(false, 0, false);
        }

        var tableRowCount = 0;
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                break;

            tableRowCount++;
        }

        var idOrderMatch = tableRowCount == entries.Count;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = lines[headerIndex + 1 + i];
            var columns = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 4 ||
                !string.Equals(columns[0], entry.GetProperty("id").GetString() ?? "", StringComparison.Ordinal) ||
                !string.Equals(columns[1], entry.GetProperty("capturedAt").GetString() ?? "", StringComparison.Ordinal) ||
                !string.Equals(columns[2], entry.GetProperty("source").GetString() ?? "", StringComparison.Ordinal) ||
                !string.Equals(columns[3], entry.GetProperty("elementCount").GetInt32().ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                idOrderMatch = false;
                break;
            }
        }

        return new HistoryTableEvidence(true, tableRowCount, idOrderMatch);
    }

    private static WorkbenchRollbackDryRunEvidence CreateRollbackDryRunEvidence(
        string output,
        CommandSpineRollbackHandler rollbackHandler)
    {
        return new WorkbenchRollbackDryRunEvidence(
            ExtractRollbackCount(output, "Dry run: ", " rollback action"),
            ExtractRollbackCount(output, " rollback action(s); ", " conflict"),
            ExtractRollbackCount(output, " conflict(s); ", " error"),
            ExtractLineSuffix(output, "Safe apply command after review: "),
            output.Contains("Safe apply command after review", StringComparison.OrdinalIgnoreCase),
            rollbackHandler.SawDryRunSetPreview && !rollbackHandler.SawMutatingSetRequest,
            rollbackHandler.SawDryRunSetPreview,
            rollbackHandler.SawMutatingSetRequest);
    }

    private static int ExtractRollbackCount(string text, string prefix, string suffix)
    {
        var prefixIndex = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
            return -1;

        var start = prefixIndex + prefix.Length;
        var suffixIndex = text.IndexOf(suffix, start, StringComparison.OrdinalIgnoreCase);
        if (suffixIndex < 0 || suffixIndex <= start)
            return -1;

        return int.TryParse(text[start..suffixIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : -1;
    }

    private static string? ExtractLineSuffix(string text, string prefix)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var prefixIndex = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex >= 0)
                return line[(prefixIndex + prefix.Length)..].Trim();
        }

        return null;
    }

    private static bool ContainsAll(string text, params string[] values) =>
        values.All(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string[] MissingSemanticEvidence(string text, string label, params string[] terms) =>
        terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
            ? Array.Empty<string>()
            : new[] { label };

    private static string[] FindBoundaryEvidenceIssues(
        string text,
        params (string Needle, string[] Contradictions)[] boundaries)
    {
        return boundaries
            .Select(boundary =>
            {
                var evidence = FindBoundaryEvidenceLine(text, boundary.Needle);
                if (evidence is null)
                    return $"{boundary.Needle} boundary wording";

                var contradiction = boundary.Contradictions
                    .FirstOrDefault(phrase => evidence.Contains(phrase, StringComparison.OrdinalIgnoreCase));
                return contradiction is null
                    ? null
                    : $"{boundary.Needle} contradictory wording '{contradiction}'";
            })
            .Where(issue => issue is not null)
            .Select(issue => issue!)
            .ToArray();
    }

    private static string? FindBoundaryEvidenceLine(string text, string needle)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                !IsIgnoredBoundaryExampleLine(line) &&
                IsBoundaryEvidenceLine(line, needle))
            {
                return line;
            }
        }

        return null;
    }

    private static bool IsBoundaryEvidenceLine(string line, string needle)
    {
        var normalizedLine = line.Trim().ToLowerInvariant();
        var normalizedNeedle = needle.Trim().ToLowerInvariant();
        return normalizedNeedle.Contains("read-only", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("no-write", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("does not", StringComparison.Ordinal) ||
               normalizedNeedle.Contains("without", StringComparison.Ordinal) ||
               normalizedNeedle.StartsWith("no ", StringComparison.Ordinal) ||
               normalizedLine.Contains("no " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("not " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("without " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("avoids " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("excludes " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("does not introduce " + normalizedNeedle, StringComparison.Ordinal) ||
               normalizedLine.Contains("do not introduce " + normalizedNeedle, StringComparison.Ordinal) ||
               BoundaryPhraseAppearsBeforeNeedle(normalizedLine, normalizedNeedle, "does not introduce") ||
               BoundaryPhraseAppearsBeforeNeedle(normalizedLine, normalizedNeedle, "do not introduce") ||
               KeepsNeedleOut(normalizedLine, normalizedNeedle);
    }

    private static bool IsIgnoredBoundaryExampleLine(string line)
    {
        var normalizedLine = line.Trim().ToLowerInvariant();
        return normalizedLine.StartsWith("rejected example", StringComparison.Ordinal) ||
               normalizedLine.StartsWith("reviewer note", StringComparison.Ordinal) ||
               normalizedLine.Contains("rejected example wording", StringComparison.Ordinal);
    }

    private static bool KeepsNeedleOut(string normalizedLine, string normalizedNeedle)
    {
        var keepsIndex = normalizedLine.IndexOf("keeps", StringComparison.Ordinal);
        var needleIndex = normalizedLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        var outIndex = normalizedLine.IndexOf("out", StringComparison.Ordinal);
        return keepsIndex >= 0 &&
               needleIndex > keepsIndex &&
               outIndex > needleIndex;
    }

    private static bool BoundaryPhraseAppearsBeforeNeedle(string normalizedLine, string normalizedNeedle, string boundaryPhrase)
    {
        var boundaryIndex = normalizedLine.IndexOf(boundaryPhrase, StringComparison.Ordinal);
        var needleIndex = normalizedLine.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        return boundaryIndex >= 0 &&
               needleIndex > boundaryIndex;
    }

    private static bool JsonStringArrayValuesAppear(JsonElement array, string text) =>
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().All(item =>
            item.ValueKind == JsonValueKind.String &&
            text.Contains(item.GetString() ?? "", StringComparison.OrdinalIgnoreCase));

    private static bool JsonObjectArrayValuesAppear(JsonElement array, string text, params string[] propertyNames) =>
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().All(item =>
            item.ValueKind == JsonValueKind.Object &&
            propertyNames.All(propertyName =>
                !item.TryGetProperty(propertyName, out var property) ||
                property.ValueKind == JsonValueKind.Null ||
                text.Contains(property.ToString(), StringComparison.OrdinalIgnoreCase)));

    private static bool JsonStatsCountsAppear(JsonElement array, string text) =>
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().All(item =>
            item.TryGetProperty("name", out var name) &&
            item.TryGetProperty("count", out var count) &&
            TextContainsCountPair(text, name.GetString() ?? "", count.GetInt32()));

    private static bool TextContainsCountPair(string text, string name, int count)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var expectedCount = count.ToString(CultureInfo.InvariantCulture);
        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line =>
                line.Contains(name, StringComparison.OrdinalIgnoreCase) &&
                line.Contains(expectedCount, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CommandSpineRuntimeEvidence(
        bool StandardsValidate,
        bool IssuePreflight,
        bool IssuePackageDryRun,
        bool DeliverablesVerify,
        bool JournalVerify,
        bool HistoryList,
        bool RollbackDryRun,
        bool HistoryListCountConsistency,
        bool HistoryListRowOrder,
        bool RollbackDryRunPreview,
        bool RollbackNoMutatingSetRequest,
        WorkbenchHistoryListEvidence HistoryListEvidence,
        WorkbenchRollbackDryRunEvidence RollbackDryRunEvidence,
        bool OutputParity,
        bool NoWrites)
    {
        public static CommandSpineRuntimeEvidence None { get; } = new(
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            WorkbenchHistoryListEvidence.Empty,
            WorkbenchRollbackDryRunEvidence.Empty,
            false,
            false);

        public bool AllReady =>
            StandardsValidate &&
            IssuePreflight &&
            IssuePackageDryRun &&
            DeliverablesVerify &&
            JournalVerify &&
            HistoryList &&
            RollbackDryRun &&
            HistoryListCountConsistency &&
            HistoryListRowOrder &&
            RollbackDryRunPreview &&
            RollbackNoMutatingSetRequest &&
            OutputParity &&
            NoWrites;

        public string Describe() =>
            $"standardsValidate={Format(StandardsValidate)}, issuePreflight={Format(IssuePreflight)}, issuePackageDryRun={Format(IssuePackageDryRun)}, deliverablesVerify={Format(DeliverablesVerify)}, journalVerify={Format(JournalVerify)}, historyList={Format(HistoryList)}, historyListCountConsistency={Format(HistoryListCountConsistency)}, historyListRowOrder={Format(HistoryListRowOrder)}, rollbackDryRun={Format(RollbackDryRun)}, rollbackDryRunPreview={Format(RollbackDryRunPreview)}, rollbackNoMutatingSetRequest={Format(RollbackNoMutatingSetRequest)}, outputParity={Format(OutputParity)}, noWrites={Format(NoWrites)}";

        private static string Format(bool value) => value.ToString().ToLowerInvariant();
    }

    private sealed record HistoryTableEvidence(bool HeaderMatched, int TableRowCount, bool IdOrderMatch);

    public sealed record WorkbenchHistoryListEvidence(
        int JsonEntryCount,
        int JsonHiddenCount,
        int JsonReturnedCount,
        int TableRowCount,
        bool CountConsistency,
        bool IdOrderMatch,
        bool HeaderMatched)
    {
        public static WorkbenchHistoryListEvidence Empty { get; } = new(0, 0, 0, 0, false, false, false);
    }

    public sealed record WorkbenchRollbackDryRunEvidence(
        int ActionCount,
        int ConflictCount,
        int ErrorCount,
        string? SafeApplyCommand,
        bool SafeApplyEmitted,
        bool DryRunPreviewOnly,
        bool SawDryRunSetPreview,
        bool SawMutatingSetRequest)
    {
        public static WorkbenchRollbackDryRunEvidence Empty { get; } = new(0, 0, 0, null, false, false, false, false);
    }

    private sealed class FileWriteProbe : IDisposable
    {
        private readonly string _root;
        private readonly FileSystemWatcher _watcher;
        private readonly object _gate = new();
        private readonly List<string> _events = new();
        private readonly string _calibrationRelativePath;
        private bool _calibrating;
        private bool _calibrated;
        private int _calibrationEventCount;
        private string? _calibrationError;

        public FileWriteProbe(string root)
        {
            _root = Path.GetFullPath(root);
            _calibrationRelativePath = $".revitcli-write-probe-{Guid.NewGuid():N}.tmp";
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.CreationTime |
                    NotifyFilters.Size,
            };
            _watcher.Created += (_, args) => Record("created", args.FullPath);
            _watcher.Changed += (_, args) => Record("changed", args.FullPath);
            _watcher.Deleted += (_, args) => Record("deleted", args.FullPath);
            _watcher.Renamed += (_, args) => Record(
                "renamed",
                $"{FormatRelative(args.OldFullPath)}->{FormatRelative(args.FullPath)}",
                    alreadyRelative: true);
            _watcher.EnableRaisingEvents = true;
            Calibrate();
        }

        public bool IsClean
        {
            get
            {
                lock (_gate)
                    return _calibrated && _events.Count == 0;
            }
        }

        public bool CalibrationObserved
        {
            get
            {
                lock (_gate)
                    return _calibrated;
            }
        }

        public void Drain()
        {
            DrainUntilStable();
        }

        public string Describe()
        {
            lock (_gate)
            {
                if (!_calibrated)
                    return string.IsNullOrWhiteSpace(_calibrationError)
                        ? "none (probe calibration missed events)"
                        : $"none (probe calibration failed: {_calibrationError})";

                return _events.Count == 0
                    ? "none (probe calibrated)"
                    : string.Join(", ", _events.Take(5));
            }
        }

        public void Dispose()
        {
            _watcher.Dispose();
        }

        private void Record(string kind, string path, bool alreadyRelative = false)
        {
            if (!alreadyRelative &&
                string.Equals(kind, "changed", StringComparison.Ordinal) &&
                IsDirectoryPath(path))
            {
                return;
            }

            var relative = alreadyRelative ? path : FormatRelative(path);
            lock (_gate)
            {
                if (_calibrating)
                {
                    _calibrationEventCount++;
                    return;
                }

                if (string.Equals(relative, _calibrationRelativePath, StringComparison.Ordinal))
                {
                    return;
                }

                _events.Add($"{kind}:{relative}");
            }
        }

        private static bool IsDirectoryPath(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void Calibrate()
        {
            var calibrationPath = Path.Combine(_root, _calibrationRelativePath);
            try
            {
                lock (_gate)
                {
                    _calibrating = true;
                    _calibrationEventCount = 0;
                }

                File.WriteAllText(calibrationPath, "probe");
                File.AppendAllText(calibrationPath, "probe");
                File.Delete(calibrationPath);
                DrainUntilStable();
                lock (_gate)
                {
                    _calibrated = _calibrationEventCount > 0;
                    if (!_calibrated)
                        _calibrationError = "FileSystemWatcher did not observe calibration writes";
                    _events.Clear();
                }
                DrainUntilStable();
                lock (_gate)
                {
                    _calibrating = false;
                    _events.Clear();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_gate)
                {
                    _calibrationError = ex.Message;
                    _calibrated = false;
                    _calibrating = false;
                    _events.Clear();
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(calibrationPath))
                        File.Delete(calibrationPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void DrainUntilStable()
        {
            var previousCount = -1;
            var stableSamples = 0;
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(1000);
            while (DateTimeOffset.UtcNow < deadline && stableSamples < 3)
            {
                Thread.Sleep(50);
                int currentCount;
                lock (_gate)
                    currentCount = _events.Count + _calibrationEventCount;

                if (currentCount == previousCount)
                    stableSamples++;
                else
                    stableSamples = 0;

                previousCount = currentCount;
            }
        }

        private string FormatRelative(string path)
        {
            try
            {
                return Path.GetRelativePath(_root, path).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path.Replace('\\', '/');
            }
        }
    }

    private static string WriteCommandSpineAuditEvidence(string root)
    {
        var revitCliDir = Path.Combine(root, ".revitcli");
        Directory.CreateDirectory(revitCliDir);
        File.WriteAllLines(
            Path.Combine(revitCliDir, "journal.jsonl"),
            new[]
            {
                """{"action":"issue.preflight","timestamp":"2026-05-23T10:00:00Z","category":"issue","operator":"workbench-runtime","affectedElementCount":0}""",
                """{"action":"issue.package","timestamp":"2026-05-23T10:05:00Z","category":"deliverables","operator":"workbench-runtime","affectedElementCount":0}""",
            });
        var signOutput = new StringWriter(CultureInfo.InvariantCulture);
        var signExit = JournalCommand.ExecuteSignAsync(
                root,
                journal: null,
                signature: null,
                key: null,
                until: null,
                outputFormat: "json",
                signOutput)
            .GetAwaiter()
            .GetResult();
        if (signExit != 0)
        {
            throw new InvalidOperationException($"failed to sign command-spine journal: {signOutput}");
        }

        var historyStore = HistoryStore.ForProject(root);
        historyStore.InitAsync().GetAwaiter().GetResult();
        historyStore.AppendAsync(
                new ModelSnapshot
                {
                    SchemaVersion = 1,
                    TakenAt = "2026-05-23T10:10:00Z",
                    Revit = new SnapshotRevit
                    {
                        Version = "2026",
                        Document = "CommandSpine.rvt",
                        DocumentPath = "D:/models/CommandSpine.rvt",
                    },
                    Summary = new SnapshotSummary
                    {
                        ElementCounts = new Dictionary<string, int> { ["doors"] = 1 },
                    },
                },
                "command-spine-runtime",
                DateTimeOffset.Parse("2026-05-23T10:10:00Z", CultureInfo.InvariantCulture))
            .GetAwaiter()
            .GetResult();

        var planPath = Path.Combine(revitCliDir, "plans", "rollback-plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        File.WriteAllText(
            planPath,
            """
{"schemaVersion":1,"type":"set","summary":{"operation":"set","param":"Mark","value":"NEW","affected":1}}
""");
        var planHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(planPath))).ToLowerInvariant();
        var receiptPath = Path.Combine(revitCliDir, "plans", "rollback-plan.receipt.json");
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(
                new PlanReceipt
                {
                    Operation = "set",
                    Action = "plan.apply",
                    PlanPath = planPath,
                    PlanHash = planHash,
                    Command = "revitcli plan apply .revitcli/plans/rollback-plan.json --yes",
                    DryRun = false,
                    Timestamp = "2026-05-23T10:15:00Z",
                    AppliedAtUtc = "2026-05-23T10:15:00Z",
                    Operator = "workbench-runtime",
                    User = "workbench-runtime",
                    AppliedBy = "workbench-runtime",
                    Machine = "local",
                    ModelPath = "D:/models/CommandSpine.rvt",
                    DocumentName = "CommandSpine.rvt",
                    DocumentVersion = "2026",
                    Affected = 1,
                    AffectedElementIds = new List<long> { 900 },
                    RequiresRollback = true,
                    PlanActionCount = 1,
                    SkippedCount = 0,
                    Param = "Mark",
                    RollbackActions = new List<PlanReceiptRollbackAction>
                    {
                        new()
                        {
                            ElementId = 900,
                            Param = "Mark",
                            OldValue = "OLD",
                            NewValue = "NEW",
                            Source = "set",
                        },
                    },
                },
                SetPlanFileStore.JsonOptions));
        return receiptPath;
    }

    private sealed class CommandSpineRollbackHandler : HttpMessageHandler
    {
        public bool SawDryRunSetPreview { get; private set; }

        public bool SawMutatingSetRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object payload;
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/status")
            {
                payload = ApiResponse<StatusInfo>.Ok(new StatusInfo
                {
                    RevitVersion = "2026",
                    DocumentName = "CommandSpine.rvt",
                    DocumentPath = "D:/models/CommandSpine.rvt",
                });
            }
            else if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/elements/set")
            {
                var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? "{}";
                var setRequest = JsonSerializer.Deserialize<SetRequest>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SetRequest();
                if (!setRequest.DryRun)
                {
                    SawMutatingSetRequest = true;
                    payload = ApiResponse<SetResult>.Fail("rollback dry-run attempted a mutating set request");
                }
                else
                {
                    SawDryRunSetPreview = true;
                    var elementId = setRequest.ElementId ?? setRequest.ElementIds?.FirstOrDefault() ?? 900;
                    payload = ApiResponse<SetResult>.Ok(new SetResult
                    {
                        Affected = 1,
                        Preview = new List<SetPreviewItem>
                        {
                            new()
                            {
                                Id = elementId,
                                Name = "Door 900",
                                OldValue = "NEW",
                                NewValue = setRequest.Value,
                            },
                        },
                    });
                }
            }
            else
            {
                payload = ApiResponse<object>.Fail($"unexpected command-spine rollback request: {request.Method} {request.RequestUri?.AbsolutePath}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            });
        }
    }

    private static void WriteCommandSpineStandardsProject(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".revitcli", "workflows"));
        Directory.CreateDirectory(Path.Combine(root, "deliverables"));
        File.WriteAllText(Path.Combine(root, ".revitcli.yml"), """
version: 1
checks:
  default:
    failOn: error
exports:
  pdf:
    format: pdf
publish:
  issue:
    precheck: default
    presets: [pdf]
schedules:
  doors:
    category: doors
    fields: [Mark, Fire Rating]
    name: Door Schedule
""");
        File.WriteAllText(Path.Combine(root, ".revitcli", "workflows", "pre-issue.yml"), """
version: 1
name: pre-issue
steps:
  - run: revitcli check issue --output table
    mode: read-only
  - run: revitcli publish issue --dry-run
    mode: dry-run
""");
        File.WriteAllText(Path.Combine(root, ".revitcli", "standards.yml"), """
version: 1
name: office
packVersion: 2026.4.0
compatibility:
  revitCli: ">=0.1.0"
  revitYears: [2024, 2025, 2026]
  notes:
    - Portable standards pack for CLI-only validation.
required:
  profiles: [.revitcli.yml]
  workflows: [pre-issue]
  outputPaths: [deliverables]
  scheduleTemplates: [doors]
  familyRules: [name-non-empty, category-known]
""");
    }

    private static void WriteCommandSpineDeliveryEvidence(string root)
    {
        var outputDir = Path.Combine(root, "deliverables", "pdf");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "A101.pdf"), "pdf-bytes");
        var receiptPath = Path.Combine(root, ".revitcli", "receipts", "export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(
            receiptPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "export-receipt.v1",
                action = "export",
                success = true,
                dryRun = false,
                outputDir,
            }));
        var manifestPath = Path.Combine(root, ".revitcli", "deliveries", "manifest.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "delivery-manifest.v1",
                kind = "export",
                success = true,
                dryRun = false,
                format = "pdf",
                receiptPath,
                timestamp = "2026-05-23T00:00:00Z",
            }) + Environment.NewLine);
    }

    private static bool RunWorkflowRegistryRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workflow-registry-check-{Guid.NewGuid():N}");
        try
        {
            var workflowDir = Path.Combine(root, ".revitcli", "workflows");
            Directory.CreateDirectory(workflowDir);
            File.WriteAllText(Path.Combine(workflowDir, "registry-smoke.yml"), """
name: registry-smoke
description: Runtime check for workflow registry payload.
steps:
  - name: registry
    run: revitcli workflow registry --output json
    mode: read-only
  - name: package preview
    run: revitcli issue package --profile .revitcli/issue.yml --bundle-path deliverables/issue.zip --dry-run --output json
    mode: dry-run
  - name: delivery bundle preview
    run: revitcli deliverables bundle --dry-run --output json
    mode: dry-run
  - name: schedule export
    run: revitcli schedules batch-export --set issue --output-dir exports/schedules/current --format csv --manifest exports/schedules/current/manifest.json --output json
    mode: mutating
    requiresApproval: true
  - name: publish issue
    run: revitcli publish issue
    mode: mutating
    requiresApproval: true
  - name: approved plan
    run: revitcli plan apply .revitcli/plans/sheets.json --yes
    mode: mutating
    requiresApproval: true
  - name: rollback preview
    run: revitcli rollback .revitcli/receipts/plan.json --dry-run
    mode: dry-run
  - name: verify journal
    run: revitcli journal verify --output json
    mode: read-only
""");
            var output = new StringWriter();
            var tableOutput = new StringWriter();
            var markdownOutput = new StringWriter();
            var before = SnapshotLocalFiles(root);
            int exitCode;
            int tableExitCode;
            int markdownExitCode;
            bool eventNoWrites;
            string eventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                exitCode = WorkflowCommand.ExecuteRegistryAsync(
                        null,
                        root,
                        "json",
                        output,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                tableExitCode = WorkflowCommand.ExecuteRegistryAsync(
                        null,
                        root,
                        "table",
                        tableOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                markdownExitCode = WorkflowCommand.ExecuteRegistryAsync(
                        null,
                        root,
                        "markdown",
                        markdownOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                eventNoWrites = writeProbe.IsClean;
                eventWrites = writeProbe.Describe();
            }
            if (exitCode != 0)
            {
                evidence = $"workflow registry runtime exited {exitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }
            if (tableExitCode != 0 || markdownExitCode != 0)
            {
                evidence = $"workflow registry output parity runtime exited table={tableExitCode.ToString(CultureInfo.InvariantCulture)} markdown={markdownExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var document = JsonDocument.Parse(output.ToString());
            var registry = document.RootElement;
            if (registry.GetProperty("schemaVersion").GetString() != "workflow-registry.v1" ||
                !registry.GetProperty("success").GetBoolean() ||
                registry.GetProperty("workflowCount").GetInt32() != 1)
            {
                evidence = "workflow registry runtime did not emit a successful workflow-registry.v1 payload";
                return false;
            }

            var workflow = registry.GetProperty("workflows").EnumerateArray().Single();
            var finalSnapshotNoWrites = before.SequenceEqual(SnapshotLocalFiles(root));
            var noWrites =
                finalSnapshotNoWrites &&
                eventNoWrites;
            var outputParityReady = WorkflowRegistryOutputFormatParityReady(
                registry,
                tableOutput.ToString(),
                markdownOutput.ToString());
            var ok =
                noWrites &&
                outputParityReady &&
                workflow.GetProperty("name").GetString() == "registry-smoke" &&
                workflow.GetProperty("riskLevel").GetString() == "mutating" &&
                JsonArrayContains(workflow.GetProperty("readWriteScope"), "read-only") &&
                JsonArrayContains(workflow.GetProperty("readWriteScope"), "dry-run") &&
                JsonArrayContains(workflow.GetProperty("readWriteScope"), "mutating") &&
                JsonArrayContains(workflow.GetProperty("inputs"), "workflow YAML") &&
                JsonArrayContains(workflow.GetProperty("inputs"), "profile:.revitcli/issue.yml") &&
                JsonArrayContains(workflow.GetProperty("inputs"), "manifest:exports/schedules/current/manifest.json") &&
                JsonArrayContains(workflow.GetProperty("inputs"), "plan:.revitcli/plans/sheets.json") &&
                JsonArrayContains(workflow.GetProperty("inputs"), "receipt:.revitcli/receipts/plan.json") &&
                JsonArrayContains(workflow.GetProperty("outputs"), "issue package") &&
                JsonArrayContains(workflow.GetProperty("outputs"), "delivery bundle") &&
                JsonArrayContains(workflow.GetProperty("outputs"), "schedule export") &&
                JsonArrayContains(workflow.GetProperty("outputs"), "schedule-export-manifest.v1") &&
                JsonArrayContains(workflow.GetProperty("outputs"), "publish output") &&
                workflow.GetProperty("dryRunCommands").GetArrayLength() >= 2 &&
                workflow.GetProperty("approvalCommands").GetArrayLength() >= 3 &&
                workflow.GetProperty("rollbackSupport").GetBoolean() &&
                JsonArrayContains(workflow.GetProperty("receiptSchemas"), "workflow-run-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("receiptSchemas"), "plan-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("receiptSchemas"), "issue-package-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("receiptSchemas"), "delivery-bundle-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("receiptSchemas"), "publish-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("acceptanceEvidence"), "workflow receipts") &&
                JsonArrayContains(workflow.GetProperty("acceptanceEvidence"), "journal verify") &&
                JsonArrayContains(workflow.GetProperty("acceptanceEvidence"), "schedule-export-manifest.v1") &&
                JsonArrayContains(workflow.GetProperty("acceptanceEvidence"), "delivery-bundle-receipt.v1") &&
                JsonArrayContains(workflow.GetProperty("acceptanceEvidence"), "publish-receipt.v1") &&
                !workflow.GetProperty("issues").EnumerateArray().Any(issue =>
                    string.Equals(issue.GetProperty("severity").GetString(), "Error", StringComparison.OrdinalIgnoreCase));

            evidence = ok
                ? "workflow registry runtime emits workflow-registry.v1 with inputs, delivery, schedule, publish outputs, scope, risk, dry-run, approval, rollback, receipt, journal verify, schema evidence fields, JSON/table/Markdown output semantic parity, final file-tree snapshot evidence, and event-level no-write evidence"
                : $"workflow registry runtime payload is missing required workflow-registry.v1 fields, JSON/table/Markdown output semantic parity, final file-tree snapshot evidence, or event-level no-write evidence (outputParity={outputParityReady.ToString().ToLowerInvariant()}, noWrites={noWrites.ToString().ToLowerInvariant()}, events={eventWrites})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            evidence = $"workflow registry runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunLedgerAppendRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-append-check-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var receiptDir = Path.Combine(root, ".revitcli", "receipts");
            Directory.CreateDirectory(receiptDir);
            var receiptPath = Path.Combine(receiptDir, "append-smoke.json");
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
            {
                schemaVersion = "publish-receipt.v1",
                action = "issue.package",
                success = true,
                dryRun = false,
            }));
            var artifactPath = Path.Combine(root, "out", "issue-package.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            File.WriteAllText(artifactPath, "package");
            var receiptHash = DeliveryManifestWriter.ComputeSha256Hex(receiptPath);
            var now = DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture);

            var beforeDryRun = SnapshotLocalFiles(root);
            var dryRunOutput = new StringWriter();
            var dryRunMarkdownOutput = new StringWriter();
            var dryRunTableOutput = new StringWriter();
            int dryRunExitCode;
            int dryRunMarkdownExitCode;
            int dryRunTableExitCode;
            bool dryRunEventNoWrites;
            string dryRunEventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                dryRunExitCode = LedgerCommand.ExecuteAppendAsync(
                        root,
                        "issue.package",
                        "issue",
                        "alice",
                        "succeeded",
                        "package issue deliverables",
                        "2026-05-23T00:00:00Z",
                        "AppendSmoke.rvt",
                        "models/AppendSmoke.rvt",
                        "plan-append-smoke",
                        artifactPath,
                        receiptPath,
                        receiptHash,
                        "revitcli rollback append-smoke.json",
                        new[] { receiptPath, artifactPath, receiptPath },
                        yes: false,
                        outputFormat: "json",
                        dryRunOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                dryRunMarkdownExitCode = LedgerCommand.ExecuteAppendAsync(
                        root,
                        "issue.package",
                        "issue",
                        "alice",
                        "succeeded",
                        "package issue deliverables",
                        "2026-05-23T00:00:00Z",
                        "AppendSmoke.rvt",
                        "models/AppendSmoke.rvt",
                        "plan-append-smoke",
                        artifactPath,
                        receiptPath,
                        receiptHash,
                        "revitcli rollback append-smoke.json",
                        new[] { receiptPath, artifactPath, receiptPath },
                        yes: false,
                        outputFormat: "markdown",
                        dryRunMarkdownOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                dryRunTableExitCode = LedgerCommand.ExecuteAppendAsync(
                        root,
                        "issue.package",
                        "issue",
                        "alice",
                        "succeeded",
                        "package issue deliverables",
                        "2026-05-23T00:00:00Z",
                        "AppendSmoke.rvt",
                        "models/AppendSmoke.rvt",
                        "plan-append-smoke",
                        artifactPath,
                        receiptPath,
                        receiptHash,
                        "revitcli rollback append-smoke.json",
                        new[] { receiptPath, artifactPath, receiptPath },
                        yes: false,
                        outputFormat: "table",
                        dryRunTableOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                dryRunEventNoWrites = writeProbe.IsClean;
                dryRunEventWrites = writeProbe.Describe();
            }

            var ledgerPath = Path.Combine(root, ".revitcli", "ledger", "operations.jsonl");
            var dryRunNoWrites =
                beforeDryRun.SequenceEqual(SnapshotLocalFiles(root)) &&
                !File.Exists(ledgerPath) &&
                dryRunEventNoWrites;
            if (dryRunExitCode != 0 || dryRunMarkdownExitCode != 0 || dryRunTableExitCode != 0 || !dryRunNoWrites)
            {
                evidence = $"ledger append dry-run runtime failed no-write or output checks (json={dryRunExitCode.ToString(CultureInfo.InvariantCulture)}, markdown={dryRunMarkdownExitCode.ToString(CultureInfo.InvariantCulture)}, table={dryRunTableExitCode.ToString(CultureInfo.InvariantCulture)}, noWrites={dryRunNoWrites.ToString().ToLowerInvariant()}, events={dryRunEventWrites})";
                return false;
            }

            using var dryRunDocument = JsonDocument.Parse(dryRunOutput.ToString());
            var dryRun = dryRunDocument.RootElement;
            var dryRunOperation = dryRun.GetProperty("operation");
            var outputParityReady =
                dryRun.GetProperty("schemaVersion").GetString() == "ledger-append.v1" &&
                dryRun.GetProperty("dryRun").GetBoolean() &&
                !dryRun.GetProperty("written").GetBoolean() &&
                dryRunMarkdownOutput.ToString().Contains("RevitCli Ledger Append", StringComparison.OrdinalIgnoreCase) &&
                dryRunTableOutput.ToString().Contains("Ledger append", StringComparison.OrdinalIgnoreCase) &&
                dryRunMarkdownOutput.ToString().Contains("issue.package", StringComparison.OrdinalIgnoreCase) &&
                dryRunTableOutput.ToString().Contains("issue.package", StringComparison.OrdinalIgnoreCase) &&
                dryRunOperation.GetProperty("source").GetString() == "ledger" &&
                dryRunOperation.GetProperty("action").GetString() == "issue.package" &&
                dryRunOperation.GetProperty("receiptStatus").GetString() == "valid";

            var beforeAppend = SnapshotLocalFiles(root);
            var appendOutput = new StringWriter();
            var appendExitCode = LedgerCommand.ExecuteAppendAsync(
                    root,
                    "issue.package",
                    "issue",
                    "alice",
                    "succeeded",
                    "package issue deliverables",
                    "2026-05-23T00:00:00Z",
                    "AppendSmoke.rvt",
                    "models/AppendSmoke.rvt",
                    "plan-append-smoke",
                    artifactPath,
                    receiptPath,
                    receiptHash,
                    "revitcli rollback append-smoke.json",
                    new[] { receiptPath, artifactPath, receiptPath },
                    yes: true,
                    outputFormat: "json",
                    appendOutput,
                    now)
                .GetAwaiter()
                .GetResult();
            var afterAppend = SnapshotLocalFiles(root);
            var addedPaths = afterAppend.Keys.Except(beforeAppend.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var boundedWriteReady =
                appendExitCode == 0 &&
                addedPaths.SequenceEqual(new[] { ".revitcli/ledger/", ".revitcli/ledger/operations.jsonl" }, StringComparer.Ordinal) &&
                File.Exists(ledgerPath) &&
                File.ReadAllLines(ledgerPath).Length == 1;

            var queryOutput = new StringWriter();
            var validateOutput = new StringWriter();
            var afterAppendStable = SnapshotLocalFiles(root);
            var queryExitCode = LedgerCommand.ExecuteQueryAsync(
                    root,
                    "ledger",
                    since: null,
                    until: null,
                    window: null,
                    action: null,
                    category: null,
                    operatorFilter: null,
                    receiptStatus: "all",
                    limit: 10,
                    outputFormat: "json",
                    queryOutput,
                    now)
                .GetAwaiter()
                .GetResult();
            var validateExitCode = LedgerCommand.ExecuteValidateAsync(
                    root,
                    "ledger",
                    since: null,
                    until: null,
                    window: null,
                    action: null,
                    category: null,
                    operatorFilter: null,
                    receiptStatus: "all",
                    failOn: "error",
                    outputFormat: "json",
                    validateOutput,
                    now)
                .GetAwaiter()
                .GetResult();
            var readBackNoWrites = afterAppendStable.SequenceEqual(SnapshotLocalFiles(root));
            using var queryDocument = JsonDocument.Parse(queryOutput.ToString());
            using var validateDocument = JsonDocument.Parse(validateOutput.ToString());
            var operation = queryDocument.RootElement.GetProperty("operations").EnumerateArray().SingleOrDefault();
            var readBackReady =
                queryExitCode == 0 &&
                validateExitCode == 0 &&
                readBackNoWrites &&
                queryDocument.RootElement.GetProperty("schemaVersion").GetString() == "ledger-query.v1" &&
                validateDocument.RootElement.GetProperty("schemaVersion").GetString() == "ledger-validate.v1" &&
                queryDocument.RootElement.GetProperty("summary").GetProperty("totalOperations").GetInt32() == 1 &&
                validateDocument.RootElement.GetProperty("valid").GetBoolean() &&
                validateDocument.RootElement.GetProperty("summary").GetProperty("operationCount").GetInt32() == 1 &&
                operation.ValueKind == JsonValueKind.Object &&
                operation.GetProperty("source").GetString() == "ledger" &&
                operation.GetProperty("action").GetString() == "issue.package" &&
                operation.GetProperty("receiptStatus").GetString() == "valid" &&
                operation.GetProperty("receiptHash").GetString() == receiptHash;
            var evidenceSortedReady =
                operation.ValueKind == JsonValueKind.Object &&
                operation.GetProperty("evidenceLinks").EnumerateArray()
                    .Select(item => item.GetString())
                    .SequenceEqual(
                        operation.GetProperty("evidenceLinks").EnumerateArray()
                            .Select(item => item.GetString())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);

            var ok = outputParityReady && boundedWriteReady && readBackReady && evidenceSortedReady;
            evidence = ok
                ? "ledger append runtime emits ledger-append.v1, preserves dry-run default no-write behavior, writes exactly .revitcli/ledger/operations.jsonl with --yes, records ledger-operation.v1 with deterministic evidence links, and query/validate read back the source ledger record without additional writes"
                : $"ledger append runtime payload is missing output parity, bounded local write evidence, source ledger read-back, validation evidence, or deterministic evidence links (outputParity={outputParityReady.ToString().ToLowerInvariant()}, boundedWrite={boundedWriteReady.ToString().ToLowerInvariant()}, readBack={readBackReady.ToString().ToLowerInvariant()}, evidenceSorted={evidenceSortedReady.ToString().ToLowerInvariant()}, added={string.Join(",", addedPaths)})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger append runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunLedgerReplayRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-replay-check-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var now = DateTimeOffset.Parse("2026-05-23T01:00:00Z", CultureInfo.InvariantCulture);
            var appendOutput = new StringWriter();
            var appendExitCode = LedgerCommand.ExecuteAppendAsync(
                    root,
                    "issue.package",
                    "issue",
                    "alice",
                    "succeeded",
                    "package issue deliverables",
                    "2026-05-23T01:00:00Z",
                    "ReplaySmoke.rvt",
                    "models/ReplaySmoke.rvt",
                    "plan-replay-smoke",
                    "out/issue-package.zip",
                    null,
                    null,
                    "revitcli rollback replay-smoke.json",
                    new[] { "out/issue-package.zip" },
                    yes: true,
                    outputFormat: "json",
                    appendOutput,
                    now)
                .GetAwaiter()
                .GetResult();
            if (appendExitCode != 0)
            {
                evidence = $"ledger replay runtime setup append failed ({appendExitCode.ToString(CultureInfo.InvariantCulture)})";
                return false;
            }

            var beforeReplay = SnapshotLocalFiles(root);
            var jsonOutput = new StringWriter();
            var markdownOutput = new StringWriter();
            var tableOutput = new StringWriter();
            int jsonExitCode;
            int markdownExitCode;
            int tableExitCode;
            bool eventNoWrites;
            string eventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                jsonExitCode = LedgerCommand.ExecuteReplayAsync(
                        root,
                        "ledger",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 10,
                        outputFormat: "json",
                        jsonOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                markdownExitCode = LedgerCommand.ExecuteReplayAsync(
                        root,
                        "ledger",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 10,
                        outputFormat: "markdown",
                        markdownOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                tableExitCode = LedgerCommand.ExecuteReplayAsync(
                        root,
                        "ledger",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 10,
                        outputFormat: "table",
                        tableOutput,
                        now)
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                eventNoWrites = writeProbe.IsClean;
                eventWrites = writeProbe.Describe();
            }

            var replayNoWrites = beforeReplay.SequenceEqual(SnapshotLocalFiles(root)) && eventNoWrites;
            using var document = JsonDocument.Parse(jsonOutput.ToString());
            var report = document.RootElement;
            var step = report.GetProperty("steps").EnumerateArray().SingleOrDefault();
            var outputParityReady =
                jsonExitCode == 0 &&
                markdownExitCode == 0 &&
                tableExitCode == 0 &&
                report.GetProperty("schemaVersion").GetString() == "ledger-replay.v1" &&
                markdownOutput.ToString().Contains("Ledger Replay Preview", StringComparison.OrdinalIgnoreCase) &&
                tableOutput.ToString().Contains("Ledger replay preview", StringComparison.OrdinalIgnoreCase);
            var previewReady =
                report.GetProperty("source").GetString() == "ledger" &&
                report.GetProperty("dryRun").GetBoolean() &&
                !report.GetProperty("applySupported").GetBoolean() &&
                report.GetProperty("summary").GetProperty("stepCount").GetInt32() == 1 &&
                report.GetProperty("summary").GetProperty("blockedStepCount").GetInt32() == 1 &&
                step.ValueKind == JsonValueKind.Object &&
                step.GetProperty("replayMode").GetString() == "preview" &&
                !step.GetProperty("canApply").GetBoolean() &&
                step.GetProperty("blockReason").GetString()!.Contains("preview-only", StringComparison.OrdinalIgnoreCase);

            var ok = outputParityReady && previewReady && replayNoWrites;
            evidence = ok
                ? "ledger replay preview emits ledger-replay.v1 from source ledger with dryRun=true, applySupported=false, canApply=false steps, JSON/table/Markdown output semantic parity, and event-level no-write evidence"
                : $"ledger replay preview runtime payload is missing preview contract or no-write evidence (outputParity={outputParityReady.ToString().ToLowerInvariant()}, preview={previewReady.ToString().ToLowerInvariant()}, noWrites={replayNoWrites.ToString().ToLowerInvariant()}, events={eventWrites})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger replay runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunLedgerQueryValidateRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-query-validate-check-{Guid.NewGuid():N}");
        try
        {
            const string deterministicTimestamp = "2026-05-23T00:00:00Z";
            var revitCliDir = Path.Combine(root, ".revitcli");
            Directory.CreateDirectory(revitCliDir);
            var historyStore = HistoryStore.ForProject(root);
            historyStore.InitAsync().GetAwaiter().GetResult();
            historyStore.AppendAsync(
                    new ModelSnapshot
                    {
                        SchemaVersion = 1,
                        TakenAt = deterministicTimestamp,
                        Revit = new SnapshotRevit
                        {
                            Version = "2026",
                            Document = "LedgerQueryValidate.rvt",
                            DocumentPath = "C:/models/LedgerQueryValidate.rvt",
                        },
                        Summary = new SnapshotSummary
                        {
                            ElementCounts = new Dictionary<string, int> { ["walls"] = 1 },
                            SheetCount = 1,
                            ScheduleCount = 1,
                        },
                    },
                    "query-validate-baseline",
                    DateTimeOffset.Parse(deterministicTimestamp, CultureInfo.InvariantCulture))
                .GetAwaiter()
                .GetResult();

            File.WriteAllLines(
                Path.Combine(revitCliDir, "journal.jsonl"),
                new[]
                {
                    JsonSerializer.Serialize(new
                    {
                        timestamp = deterministicTimestamp,
                        action = "issue.preflight",
                        category = "issue",
                        user = "alice",
                        @operator = "alice",
                        affected = 0,
                    }),
                    JsonSerializer.Serialize(new
                    {
                        timestamp = deterministicTimestamp,
                        action = "issue.review",
                        category = "issue",
                        user = "alice",
                        @operator = "alice",
                        affected = 0,
                    }),
                });

            var receiptDir = Path.Combine(revitCliDir, "receipts");
            Directory.CreateDirectory(receiptDir);
            var deliveryReceipt = Path.Combine(receiptDir, "query-validate-delivery.json");
            File.WriteAllText(
                deliveryReceipt,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "export-receipt.v1",
                    action = "export",
                    success = true,
                    dryRun = false,
                    command = "revitcli export --output json",
                }));
            var deliveryDir = Path.Combine(revitCliDir, "deliveries");
            Directory.CreateDirectory(deliveryDir);
            File.WriteAllText(
                Path.Combine(deliveryDir, "manifest.jsonl"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "export",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath = deliveryReceipt,
                    receiptHash = DeliveryManifestWriter.ComputeSha256Hex(deliveryReceipt),
                    timestamp = deterministicTimestamp,
                }) + Environment.NewLine);

            var workflowReceiptDir = Path.Combine(revitCliDir, "workflows", "receipts");
            Directory.CreateDirectory(workflowReceiptDir);
            File.WriteAllText(
                Path.Combine(workflowReceiptDir, "z-query-validate-workflow.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "workflow-run-receipt.v1",
                    action = "workflow.run",
                    path = Path.Combine(revitCliDir, "workflows", "query-validate.yml"),
                    name = "z-query-validate",
                    command = "revitcli workflow run .revitcli/workflows/query-validate.yml --yes",
                    startedAtUtc = deterministicTimestamp,
                    completedAtUtc = deterministicTimestamp,
                    @operator = "alice",
                    machine = "workstation",
                    dryRun = false,
                    success = true,
                    canRun = true,
                    exitCode = 0,
                    issues = Array.Empty<object>(),
                    steps = Array.Empty<object>(),
                }));
            File.WriteAllText(
                Path.Combine(workflowReceiptDir, "a-query-validate-workflow.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "workflow-run-receipt.v1",
                    action = "workflow.run",
                    path = Path.Combine(revitCliDir, "workflows", "query-validate.yml"),
                    name = "a-query-validate",
                    command = "revitcli workflow run .revitcli/workflows/query-validate.yml --yes",
                    startedAtUtc = deterministicTimestamp,
                    completedAtUtc = deterministicTimestamp,
                    @operator = "alice",
                    machine = "workstation",
                    dryRun = false,
                    success = true,
                    canRun = true,
                    exitCode = 0,
                    issues = Array.Empty<object>(),
                    steps = Array.Empty<object>(),
                }));

            var queryOutput = new StringWriter();
            var queryMarkdownOutput = new StringWriter();
            var queryTableOutput = new StringWriter();
            var validateOutput = new StringWriter();
            var validateMarkdownOutput = new StringWriter();
            var validateTableOutput = new StringWriter();
            var before = SnapshotLocalFiles(root);
            SortedDictionary<string, string> afterQuery;
            int queryExitCode;
            int queryMarkdownExitCode;
            int queryTableExitCode;
            int validateExitCode;
            int validateMarkdownExitCode;
            int validateTableExitCode;
            bool eventNoWrites;
            string eventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                queryExitCode = LedgerCommand.ExecuteQueryAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 100,
                        outputFormat: "json",
                        queryOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                queryMarkdownExitCode = LedgerCommand.ExecuteQueryAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 100,
                        outputFormat: "markdown",
                        queryMarkdownOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                queryTableExitCode = LedgerCommand.ExecuteQueryAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 100,
                        outputFormat: "table",
                        queryTableOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                afterQuery = SnapshotLocalFiles(root);
                validateExitCode = LedgerCommand.ExecuteValidateAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        failOn: "error",
                        outputFormat: "json",
                        validateOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                validateMarkdownExitCode = LedgerCommand.ExecuteValidateAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        failOn: "error",
                        outputFormat: "markdown",
                        validateMarkdownOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                validateTableExitCode = LedgerCommand.ExecuteValidateAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        failOn: "error",
                        outputFormat: "table",
                        validateTableOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                eventNoWrites = writeProbe.IsClean;
                eventWrites = writeProbe.Describe();
            }

            var noWrites =
                before.SequenceEqual(afterQuery) &&
                before.SequenceEqual(SnapshotLocalFiles(root)) &&
                eventNoWrites;
            if (queryExitCode != 0 ||
                queryMarkdownExitCode != 0 ||
                queryTableExitCode != 0 ||
                validateExitCode != 0 ||
                validateMarkdownExitCode != 0 ||
                validateTableExitCode != 0)
            {
                evidence = $"ledger query/validate runtime exited query={queryExitCode.ToString(CultureInfo.InvariantCulture)} queryMarkdown={queryMarkdownExitCode.ToString(CultureInfo.InvariantCulture)} queryTable={queryTableExitCode.ToString(CultureInfo.InvariantCulture)} validate={validateExitCode.ToString(CultureInfo.InvariantCulture)} validateMarkdown={validateMarkdownExitCode.ToString(CultureInfo.InvariantCulture)} validateTable={validateTableExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var queryDocument = JsonDocument.Parse(queryOutput.ToString());
            using var validateDocument = JsonDocument.Parse(validateOutput.ToString());
            var query = queryDocument.RootElement;
            var validate = validateDocument.RootElement;
            if (query.GetProperty("schemaVersion").GetString() != "ledger-query.v1" ||
                validate.GetProperty("schemaVersion").GetString() != "ledger-validate.v1")
            {
                evidence = "ledger query/validate runtime did not emit ledger-query.v1 and ledger-validate.v1";
                return false;
            }

            var operations = query.GetProperty("operations").EnumerateArray().ToArray();
            var expectedTimestamp = DateTimeOffset.Parse(deterministicTimestamp, CultureInfo.InvariantCulture);
            var sameTimestampReady = operations
                .Select(operation => operation.GetProperty("timestamp").GetString())
                .Select(value => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed.ToUniversalTime()
                    : (DateTimeOffset?)null)
                .All(timestamp => timestamp == expectedTimestamp);
            var deterministicOrderReady =
                operations.Length == 6 &&
                OperationMatches(root, operations[0], "deliveries", "deliverables.export", ".revitcli/deliveries/manifest.jsonl", 1) &&
                OperationMatches(root, operations[1], "history", "history.capture", ".revitcli/history/", null) &&
                OperationMatches(root, operations[2], "journal", "issue.preflight", ".revitcli/journal.jsonl", 1) &&
                OperationMatches(root, operations[3], "journal", "issue.review", ".revitcli/journal.jsonl", 2) &&
                OperationMatches(root, operations[4], "workflows", "workflow.run", ".revitcli/workflows/receipts/a-query-validate-workflow.json", null) &&
                OperationMatches(root, operations[5], "workflows", "workflow.run", ".revitcli/workflows/receipts/z-query-validate-workflow.json", null);
            var outputFormatParityReady = QueryOutputFormatParityReady(
                query,
                queryMarkdownOutput.ToString(),
                queryTableOutput.ToString());
            var validateOutputFormatParityReady = ValidateOutputFormatParityReady(
                validate,
                validateMarkdownOutput.ToString(),
                validateTableOutput.ToString());
            var ok =
                noWrites &&
                sameTimestampReady &&
                query.GetProperty("summary").GetProperty("totalOperations").GetInt32() == 6 &&
                operations.Length == 6 &&
                deterministicOrderReady &&
                outputFormatParityReady &&
                validateOutputFormatParityReady &&
                operations.Any(operation => operation.GetProperty("source").GetString() == "history") &&
                operations.Any(operation => operation.GetProperty("source").GetString() == "journal") &&
                operations.Any(operation => operation.GetProperty("source").GetString() == "deliveries") &&
                operations.Any(operation => operation.GetProperty("source").GetString() == "workflows") &&
                validate.GetProperty("valid").GetBoolean() &&
                validate.GetProperty("summary").GetProperty("operationCount").GetInt32() == 6 &&
                validate.GetProperty("summary").GetProperty("errorCount").GetInt32() == 0 &&
                validate.GetProperty("checks").EnumerateArray().Any(check =>
                    check.GetProperty("id").GetString() == "sources-readable" &&
                    check.GetProperty("status").GetString() == "pass") &&
                validate.GetProperty("checks").EnumerateArray().Any(check =>
                    check.GetProperty("id").GetString() == "artifact-links" &&
                    check.GetProperty("status").GetString() == "pass") &&
                validate.GetProperty("checks").EnumerateArray().Any(check =>
                    check.GetProperty("id").GetString() == "receipt-status" &&
                    check.GetProperty("status").GetString() == "pass") &&
                validate.GetProperty("checks").EnumerateArray().Any(check =>
                    check.GetProperty("id").GetString() == "receipt-hashes" &&
                    check.GetProperty("status").GetString() == "pass") &&
                validate.GetProperty("checks").EnumerateArray().Any(check =>
                    check.GetProperty("id").GetString() == "timestamp-format" &&
                    check.GetProperty("status").GetString() == "pass");

            if (!ok)
            {
                evidence = $"ledger query/validate runtime payload is missing required read-only source, deterministic timestamp/source/path/line order, JSON/table/Markdown output semantic parity, validation JSON/table/Markdown semantic parity, validation evidence, final file-tree snapshot evidence, or event-level no-write evidence (sameTimestamp={sameTimestampReady.ToString().ToLowerInvariant()}, deterministicOrder={deterministicOrderReady.ToString().ToLowerInvariant()}, outputParity={outputFormatParityReady.ToString().ToLowerInvariant()}, validateOutputParity={validateOutputFormatParityReady.ToString().ToLowerInvariant()}, noWrites={noWrites.ToString().ToLowerInvariant()}, events={eventWrites})";
                return false;
            }

            File.AppendAllText(
                Path.Combine(revitCliDir, "journal.jsonl"),
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-05-23T00:30:00",
                    action = "issue.review",
                    category = "issue",
                    user = "alice",
                    @operator = "alice",
                    affected = 0,
                }) + Environment.NewLine);

            var filteredQueryOutput = new StringWriter();
            var filteredValidateOutput = new StringWriter();
            var semanticBefore = SnapshotLocalFiles(root);
            int filteredQueryExitCode;
            int filteredValidateExitCode;
            bool semanticEventNoWrites;
            string semanticEventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                filteredQueryExitCode = LedgerCommand.ExecuteQueryAsync(
                        root,
                        "all",
                        since: "2026-05-23T00:00:00Z",
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        limit: 100,
                        outputFormat: "json",
                        filteredQueryOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                filteredValidateExitCode = LedgerCommand.ExecuteValidateAsync(
                        root,
                        "all",
                        since: "2026-05-23T00:00:00Z",
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        failOn: "error",
                        outputFormat: "json",
                        filteredValidateOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                semanticEventNoWrites = writeProbe.IsClean;
                semanticEventWrites = writeProbe.Describe();
            }

            var semanticNoWrites =
                semanticBefore.SequenceEqual(SnapshotLocalFiles(root)) &&
                semanticEventNoWrites;
            if (filteredQueryExitCode != 0 || filteredValidateExitCode != 0)
            {
                evidence = $"ledger query/validate timestamp-filter runtime exited query={filteredQueryExitCode.ToString(CultureInfo.InvariantCulture)} validate={filteredValidateExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var filteredQueryDocument = JsonDocument.Parse(filteredQueryOutput.ToString());
            using var filteredValidateDocument = JsonDocument.Parse(filteredValidateOutput.ToString());
            var filteredQuery = filteredQueryDocument.RootElement;
            var filteredValidate = filteredValidateDocument.RootElement;
            var filteredQueryCount = filteredQuery.GetProperty("summary").GetProperty("totalOperations").GetInt32();
            var filteredValidateCount = filteredValidate.GetProperty("summary").GetProperty("operationCount").GetInt32();
            var preservesTimestampWarning =
                semanticNoWrites &&
                filteredValidateCount == filteredQueryCount + 1 &&
                filteredValidate.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("code").GetString() == "timestamp.invalid" &&
                    issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
            if (!preservesTimestampWarning)
            {
                evidence = $"ledger query/validate timestamp-filter semantics failed (queryCount={filteredQueryCount.ToString(CultureInfo.InvariantCulture)}, validateCount={filteredValidateCount.ToString(CultureInfo.InvariantCulture)}, noWrites={semanticNoWrites.ToString().ToLowerInvariant()}, events={semanticEventWrites})";
                return false;
            }

            evidence = "ledger query/validate runtime emits ledger-query.v1 and ledger-validate.v1 across journal, history, delivery, and workflow sources with deterministic timestamp/source/path/line ordering, JSON/table/Markdown output semantic parity, validation JSON/table/Markdown semantic parity, source readability, artifact link, receipt status, receipt hash, timestamp format, explicit UTC offset warning preservation under time filters, query invalid-timestamp filtering, final file-tree snapshot evidence, and event-level no-write evidence";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger query/validate runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunLedgerStatsRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-stats-check-{Guid.NewGuid():N}");
        try
        {
            var revitCliDir = Path.Combine(root, ".revitcli");
            Directory.CreateDirectory(revitCliDir);
            var historyStore = HistoryStore.ForProject(root);
            historyStore.InitAsync().GetAwaiter().GetResult();
            historyStore.AppendAsync(
                    new ModelSnapshot
                    {
                        SchemaVersion = 1,
                        TakenAt = "2026-05-22T22:15:00Z",
                        Revit = new SnapshotRevit
                        {
                            Version = "2026",
                            Document = "LedgerStats.rvt",
                            DocumentPath = "C:/models/LedgerStats.rvt",
                        },
                        Summary = new SnapshotSummary
                        {
                            ElementCounts = new Dictionary<string, int> { ["walls"] = 1 },
                            SheetCount = 1,
                            ScheduleCount = 1,
                        },
                    },
                    "stats-baseline",
                    DateTimeOffset.Parse("2026-05-22T22:15:00Z", CultureInfo.InvariantCulture))
                .GetAwaiter()
                .GetResult();

            File.WriteAllText(Path.Combine(revitCliDir, "journal.jsonl"), "{bad-json");

            var deliveryDir = Path.Combine(revitCliDir, "deliveries");
            Directory.CreateDirectory(deliveryDir);
            File.WriteAllText(Path.Combine(deliveryDir, "manifest.jsonl"), "{bad-json");

            var workflowReceiptDir = Path.Combine(revitCliDir, "workflows", "receipts");
            Directory.CreateDirectory(workflowReceiptDir);
            File.WriteAllText(Path.Combine(workflowReceiptDir, "bad-workflow.json"), "{bad-json");

            var before = SnapshotLocalFiles(root);
            var output = new StringWriter();
            var markdownOutput = new StringWriter();
            var tableOutput = new StringWriter();
            int exitCode;
            int markdownExitCode;
            int tableExitCode;
            bool eventNoWrites;
            string eventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                exitCode = LedgerCommand.ExecuteStatsAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        outputFormat: "json",
                        output,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                markdownExitCode = LedgerCommand.ExecuteStatsAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        outputFormat: "markdown",
                        markdownOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                tableExitCode = LedgerCommand.ExecuteStatsAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        outputFormat: "table",
                        tableOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                eventNoWrites = writeProbe.IsClean;
                eventWrites = writeProbe.Describe();
            }

            var finalSnapshotNoWrites = before.SequenceEqual(SnapshotLocalFiles(root));
            var noWrites =
                finalSnapshotNoWrites &&
                eventNoWrites;
            if (exitCode != 0 || markdownExitCode != 0 || tableExitCode != 0)
            {
                evidence = $"ledger stats runtime exited json={exitCode.ToString(CultureInfo.InvariantCulture)} markdown={markdownExitCode.ToString(CultureInfo.InvariantCulture)} table={tableExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var document = JsonDocument.Parse(output.ToString());
            var stats = document.RootElement;
            if (stats.GetProperty("schemaVersion").GetString() != "ledger-stats.v1")
            {
                evidence = "ledger stats runtime did not emit ledger-stats.v1";
                return false;
            }

            var summary = stats.GetProperty("summary");
            var operationCount = summary.GetProperty("operationCount").GetInt32();
            var issueCount = summary.GetProperty("issueCount").GetInt32();
            var errorIssueCount = summary.GetProperty("errorIssueCount").GetInt32();
            var warningIssueCount = summary.GetProperty("warningIssueCount").GetInt32();
            var missingReceiptCount = summary.GetProperty("missingReceiptCount").GetInt32();
            var unreadableReceiptCount = summary.GetProperty("unreadableReceiptCount").GetInt32();
            var outputFormatParityReady = StatsOutputFormatParityReady(
                stats,
                markdownOutput.ToString(),
                tableOutput.ToString());
            var ok =
                noWrites &&
                outputFormatParityReady &&
                operationCount == 3 &&
                issueCount == 3 &&
                errorIssueCount == 3 &&
                warningIssueCount == 0 &&
                missingReceiptCount == 0 &&
                unreadableReceiptCount == 2 &&
                JsonArrayCountsEqual(
                    stats.GetProperty("bySource"),
                    ("deliveries", 1),
                    ("history", 1),
                    ("workflows", 1)) &&
                JsonArrayCountsEqual(
                    stats.GetProperty("byAction"),
                    ("deliverables.manifest-issue", 1),
                    ("history.capture", 1),
                    ("workflow.receipt-issue", 1)) &&
                JsonArrayCountsEqual(
                    stats.GetProperty("byCategory"),
                    ("none", 2),
                    ("stats-baseline", 1)) &&
                JsonArrayCountsEqual(stats.GetProperty("byOperator"), ("none", 3)) &&
                JsonArrayCountsEqual(
                    stats.GetProperty("byReceiptStatus"),
                    ("none", 1),
                    ("unreadable", 2)) &&
                JsonArrayCountsEqual(
                    stats.GetProperty("issuesBySource"),
                    ("deliveries", 1),
                    ("journal", 1),
                    ("workflows", 1)) &&
                JsonArrayCountsEqual(stats.GetProperty("issuesBySeverity"), ("error", 3));

            evidence = ok
                ? "ledger stats runtime emits ledger-stats.v1 with exact counters operationCount=3 issueCount=3 errorIssueCount=3 unreadableReceiptCount=2, exact source/action/category/operator/receipt-status/issue-source/issue-severity sets, JSON/table/Markdown stats semantic parity for malformed journal, delivery, and workflow artifacts, final file-tree snapshot evidence, and event-level no-write evidence"
                : $"ledger stats runtime payload does not match exact ledger-stats.v1 parity counters or sets (operationCount={operationCount.ToString(CultureInfo.InvariantCulture)}, issueCount={issueCount.ToString(CultureInfo.InvariantCulture)}, errorIssueCount={errorIssueCount.ToString(CultureInfo.InvariantCulture)}, unreadableReceiptCount={unreadableReceiptCount.ToString(CultureInfo.InvariantCulture)}, outputParity={outputFormatParityReady.ToString().ToLowerInvariant()}, finalSnapshotNoWrites={finalSnapshotNoWrites.ToString().ToLowerInvariant()}, eventNoWrites={eventNoWrites.ToString().ToLowerInvariant()}, events={eventWrites})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger stats runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool RunLedgerTimelineRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-timeline-check-{Guid.NewGuid():N}");
        try
        {
            var revitCliDir = Path.Combine(root, ".revitcli");
            Directory.CreateDirectory(revitCliDir);
            var historyStore = HistoryStore.ForProject(root);
            historyStore.InitAsync().GetAwaiter().GetResult();
            historyStore.AppendAsync(
                    new ModelSnapshot
                    {
                        SchemaVersion = 1,
                        TakenAt = "2026-05-22T22:15:00Z",
                        Revit = new SnapshotRevit
                        {
                            Version = "2026",
                            Document = "LedgerTimeline.rvt",
                            DocumentPath = "C:/models/LedgerTimeline.rvt",
                        },
                        Summary = new SnapshotSummary
                        {
                            ElementCounts = new Dictionary<string, int> { ["walls"] = 1 },
                            SheetCount = 1,
                            ScheduleCount = 1,
                        },
                    },
                    "timeline-baseline",
                    DateTimeOffset.Parse("2026-05-22T22:15:00Z", CultureInfo.InvariantCulture))
                .GetAwaiter()
                .GetResult();

            File.WriteAllLines(
                Path.Combine(revitCliDir, "journal.jsonl"),
                new[]
                {
                    JsonSerializer.Serialize(new
                    {
                        timestamp = "2026-05-22T23:30:00Z",
                        action = "issue.preflight",
                        category = "issue",
                        user = "alice",
                        @operator = "alice",
                        affected = 0,
                    }),
                    JsonSerializer.Serialize(new
                    {
                        timestamp = "2026-05-23T00:15:00Z",
                        action = "issue.package",
                        category = "issue",
                        user = "alice",
                        @operator = "alice",
                        affected = 1,
                    }),
                    JsonSerializer.Serialize(new
                    {
                        timestamp = "not-a-date",
                        action = "issue.review",
                        category = "issue",
                        user = "alice",
                        @operator = "alice",
                        affected = 0,
                    }),
                });
            var receiptDir = Path.Combine(revitCliDir, "receipts");
            Directory.CreateDirectory(receiptDir);
            var deliveryReceipt = Path.Combine(receiptDir, "timeline-delivery.json");
            File.WriteAllText(
                deliveryReceipt,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-bundle-receipt.v1",
                    action = "deliverables.bundle",
                    success = true,
                    dryRun = false,
                    command = "revitcli deliverables bundle --output json",
                }));
            var deliveryDir = Path.Combine(revitCliDir, "deliveries");
            Directory.CreateDirectory(deliveryDir);
            File.WriteAllText(
                Path.Combine(deliveryDir, "manifest.jsonl"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "delivery-manifest.v1",
                    kind = "bundle",
                    success = true,
                    dryRun = false,
                    pipeline = "issue",
                    receiptPath = deliveryReceipt,
                    timestamp = "2026-05-23T00:45:00Z",
                }));
            var workflowReceiptDir = Path.Combine(revitCliDir, "workflows", "receipts");
            Directory.CreateDirectory(workflowReceiptDir);
            File.WriteAllText(
                Path.Combine(workflowReceiptDir, "timeline-workflow.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = "workflow-run-receipt.v1",
                    action = "workflow.run",
                    path = Path.Combine(revitCliDir, "workflows", "timeline.yml"),
                    name = "timeline",
                    command = "revitcli workflow run .revitcli/workflows/timeline.yml --yes",
                    startedAtUtc = "2026-05-23T01:00:00Z",
                    completedAtUtc = "2026-05-23T01:05:00Z",
                    @operator = "alice",
                    machine = "workstation",
                    dryRun = false,
                    success = true,
                    canRun = true,
                    exitCode = 0,
                    issues = Array.Empty<object>(),
                    steps = Array.Empty<object>(),
                }));

            var before = SnapshotLocalFiles(root);
            var output = new StringWriter();
            var markdownOutput = new StringWriter();
            var tableOutput = new StringWriter();
            int exitCode;
            int markdownExitCode;
            int tableExitCode;
            bool eventNoWrites;
            string eventWrites;
            using (var writeProbe = new FileWriteProbe(root))
            {
                exitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "json",
                        output,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                markdownExitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "markdown",
                        markdownOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                tableExitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "table",
                        tableOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                writeProbe.Drain();
                eventNoWrites = writeProbe.IsClean;
                eventWrites = writeProbe.Describe();
            }

            var finalSnapshotNoWrites = before.SequenceEqual(SnapshotLocalFiles(root));
            var noWrites =
                finalSnapshotNoWrites &&
                eventNoWrites;
            if (exitCode != 0 || markdownExitCode != 0 || tableExitCode != 0)
            {
                evidence = $"ledger timeline runtime exited json={exitCode.ToString(CultureInfo.InvariantCulture)} markdown={markdownExitCode.ToString(CultureInfo.InvariantCulture)} table={tableExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var document = JsonDocument.Parse(output.ToString());
            var timeline = document.RootElement;
            if (timeline.GetProperty("schemaVersion").GetString() != "ledger-timeline.v1")
            {
                evidence = "ledger timeline runtime did not emit ledger-timeline.v1";
                return false;
            }

            var summary = timeline.GetProperty("summary");
            var buckets = timeline.GetProperty("buckets").EnumerateArray().ToArray();
            var timelineSources = buckets
                .SelectMany(bucket => bucket.GetProperty("bySource").EnumerateArray())
                .Select(item => item.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outputFormatParityReady = TimelineOutputFormatParityReady(
                timeline,
                markdownOutput.ToString(),
                tableOutput.ToString());
            var ok =
                noWrites &&
                outputFormatParityReady &&
                timeline.GetProperty("query").GetProperty("bucket").GetString() == "day" &&
                summary.GetProperty("operationCount").GetInt32() == 6 &&
                summary.GetProperty("bucketCount").GetInt32() == 2 &&
                summary.GetProperty("unbucketedOperationCount").GetInt32() == 1 &&
                summary.GetProperty("warningIssueCount").GetInt32() >= 1 &&
                JsonArrayCountContains(timeline.GetProperty("issuesBySeverity"), "warning") &&
                buckets.Length == 2 &&
                timelineSources.SetEquals(new[] { "deliveries", "history", "journal", "workflows" }) &&
                TimelineBucketCountsEqual(
                    buckets,
                    "2026-05-22T00:00:00.0000000+00:00",
                    2,
                    bySource: new[] { ("history", 1), ("journal", 1) },
                    byAction: new[] { ("history.capture", 1), ("issue.preflight", 1) },
                    byCategory: new[] { ("issue", 1), ("timeline-baseline", 1) },
                    byOperator: new[] { ("alice", 1), ("none", 1) },
                    byReceiptStatus: new[] { ("none", 2) }) &&
                TimelineBucketCountsEqual(
                    buckets,
                    "2026-05-23T00:00:00.0000000+00:00",
                    3,
                    bySource: new[] { ("deliveries", 1), ("journal", 1), ("workflows", 1) },
                    byAction: new[] { ("deliverables.bundle", 1), ("issue.package", 1), ("workflow.run", 1) },
                    byCategory: new[] { ("issue", 2), ("timeline", 1) },
                    byOperator: new[] { ("alice", 2), ("none", 1) },
                    byReceiptStatus: new[] { ("valid", 2), ("none", 1) }) &&
                timeline.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("message").GetString()!.Contains("timeline bucket", StringComparison.OrdinalIgnoreCase));

            if (!ok)
            {
                evidence = $"ledger timeline runtime payload is missing required ledger-timeline.v1 fields, JSON/table/Markdown timeline semantic parity, final file-tree snapshot evidence, or event-level no-write evidence (outputParity={outputFormatParityReady.ToString().ToLowerInvariant()}, finalSnapshotNoWrites={finalSnapshotNoWrites.ToString().ToLowerInvariant()}, eventNoWrites={eventNoWrites.ToString().ToLowerInvariant()}, events={eventWrites}, buckets={DescribeTimelineBuckets(buckets)})";
                return false;
            }

            var sinceFilteredOutput = new StringWriter();
            var untilFilteredOutput = new StringWriter();
            var windowFilteredOutput = new StringWriter();
            int sinceFilteredExitCode;
            int untilFilteredExitCode;
            int windowFilteredExitCode;
            bool filteredEventNoWrites;
            string filteredEventWrites;
            using (var filteredWriteProbe = new FileWriteProbe(root))
            {
                sinceFilteredExitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: "2026-05-23T00:00:00Z",
                        until: null,
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "json",
                        sinceFilteredOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                untilFilteredExitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: null,
                        until: "2026-05-22T23:59:59Z",
                        window: null,
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "json",
                        untilFilteredOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                windowFilteredExitCode = LedgerCommand.ExecuteTimelineAsync(
                        root,
                        "all",
                        since: null,
                        until: null,
                        window: "1h",
                        action: null,
                        category: null,
                        operatorFilter: null,
                        receiptStatus: "all",
                        bucket: "day",
                        outputFormat: "json",
                        windowFilteredOutput,
                        DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture))
                    .GetAwaiter()
                    .GetResult();
                filteredWriteProbe.Drain();
                filteredEventNoWrites = filteredWriteProbe.IsClean;
                filteredEventWrites = filteredWriteProbe.Describe();
            }
            if (sinceFilteredExitCode != 0 || untilFilteredExitCode != 0 || windowFilteredExitCode != 0)
            {
                evidence = $"ledger timeline timestamp-filter runtime exited since={sinceFilteredExitCode.ToString(CultureInfo.InvariantCulture)} until={untilFilteredExitCode.ToString(CultureInfo.InvariantCulture)} window={windowFilteredExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            using var sinceFilteredDocument = JsonDocument.Parse(sinceFilteredOutput.ToString());
            using var untilFilteredDocument = JsonDocument.Parse(untilFilteredOutput.ToString());
            using var windowFilteredDocument = JsonDocument.Parse(windowFilteredOutput.ToString());
            var sinceFilteredTimeline = sinceFilteredDocument.RootElement;
            var untilFilteredTimeline = untilFilteredDocument.RootElement;
            var windowFilteredTimeline = windowFilteredDocument.RootElement;
            var sinceFilteredSummary = sinceFilteredTimeline.GetProperty("summary");
            var untilFilteredSummary = untilFilteredTimeline.GetProperty("summary");
            var windowFilteredSummary = windowFilteredTimeline.GetProperty("summary");
            var filteredFinalSnapshotNoWrites = before.SequenceEqual(SnapshotLocalFiles(root));
            var filteredNoWrites =
                filteredFinalSnapshotNoWrites &&
                filteredEventNoWrites;
            var preservesTimestampWarning =
                filteredNoWrites &&
                sinceFilteredTimeline.GetProperty("query").GetProperty("sinceUtc").GetString() == "2026-05-23T00:00:00.0000000+00:00" &&
                sinceFilteredSummary.GetProperty("operationCount").GetInt32() == 4 &&
                sinceFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32() == 1 &&
                sinceFilteredTimeline.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase)) &&
                untilFilteredTimeline.GetProperty("query").GetProperty("untilUtc").GetString() == "2026-05-22T23:59:59.0000000+00:00" &&
                untilFilteredSummary.GetProperty("operationCount").GetInt32() == 3 &&
                untilFilteredSummary.GetProperty("bucketCount").GetInt32() == 1 &&
                untilFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32() == 1 &&
                untilFilteredTimeline.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase)) &&
                windowFilteredTimeline.GetProperty("query").GetProperty("window").GetString() == "1h" &&
                windowFilteredSummary.GetProperty("operationCount").GetInt32() == 2 &&
                windowFilteredSummary.GetProperty("bucketCount").GetInt32() == 1 &&
                windowFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32() == 1 &&
                windowFilteredTimeline.GetProperty("issues").EnumerateArray().Any(issue =>
                    issue.GetProperty("message").GetString()!.Contains("explicit UTC offset", StringComparison.OrdinalIgnoreCase));
            if (!preservesTimestampWarning)
            {
                evidence = $"ledger timeline timestamp-filter semantics failed (sinceOperationCount={sinceFilteredSummary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, sinceUnbucketed={sinceFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, untilOperationCount={untilFilteredSummary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, untilUnbucketed={untilFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, windowOperationCount={windowFilteredSummary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, windowUnbucketed={windowFilteredSummary.GetProperty("unbucketedOperationCount").GetInt32().ToString(CultureInfo.InvariantCulture)}, finalSnapshotNoWrites={filteredFinalSnapshotNoWrites.ToString().ToLowerInvariant()}, eventNoWrites={filteredEventNoWrites.ToString().ToLowerInvariant()}, events={filteredEventWrites})";
                return false;
            }

            evidence = "ledger timeline runtime emits ledger-timeline.v1 across journal, history, delivery, and workflow sources with exact day-bucket source/action/category/operator/receipt-status counts, issue severity, JSON/table/Markdown timeline semantic parity, unbucketed timestamp, explicit UTC offset warning preservation under since/until/window time filters, final file-tree snapshot evidence, and event-level no-write evidence";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger timeline runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool JsonArrayCountsEqual(JsonElement array, params (string Name, int Count)[] expected)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return false;

        var actual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("name", out var nameElement) ||
                !item.TryGetProperty("count", out var countElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !countElement.TryGetInt32(out var count))
            {
                return false;
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || !actual.TryAdd(name, count))
                return false;
        }

        if (actual.Count != expected.Length)
            return false;

        foreach (var (name, count) in expected)
        {
            if (!actual.TryGetValue(name, out var actualCount) || actualCount != count)
                return false;
        }

        return true;
    }

    private static bool RunLedgerAnalyticsRuntimeCheck(out string evidence)
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-ledger-analytics-check-{Guid.NewGuid():N}");
        try
        {
            var historyStore = HistoryStore.ForProject(root);
            historyStore.InitAsync().GetAwaiter().GetResult();
            historyStore.AppendAsync(
                    new ModelSnapshot
                    {
                        SchemaVersion = 1,
                        TakenAt = "2026-05-22T22:15:00Z",
                        Revit = new SnapshotRevit
                        {
                            Version = "2026",
                            Document = "LedgerAnalytics.rvt",
                            DocumentPath = "C:/models/LedgerAnalytics.rvt",
                        },
                        Summary = new SnapshotSummary
                        {
                            ElementCounts = new Dictionary<string, int> { ["walls"] = 1 },
                            SheetCount = 1,
                            ScheduleCount = 1,
                        },
                    },
                    "analytics-baseline",
                    DateTimeOffset.Parse("2026-05-22T22:15:00Z", CultureInfo.InvariantCulture))
                .GetAwaiter()
                .GetResult();

            var before = SnapshotLocalFiles(root);
            var output = new StringWriter();
            var markdownOutput = new StringWriter();
            var tableOutput = new StringWriter();
            var generatedAt = DateTimeOffset.Parse("2026-05-23T00:00:00Z", CultureInfo.InvariantCulture);
            var snapshotDir = Path.Combine(".revitcli", "analytics");
            var exitCode = LedgerCommand.ExecuteAnalyticsAsync(
                    root,
                    "history",
                    since: null,
                    until: null,
                    window: null,
                    action: null,
                    category: null,
                    operatorFilter: null,
                    receiptStatus: "all",
                    bucket: "day",
                    outputDirectory: snapshotDir,
                    outputFormat: "json",
                    output,
                    generatedAt)
                .GetAwaiter()
                .GetResult();
            var markdownExitCode = LedgerCommand.ExecuteAnalyticsAsync(
                    root,
                    "history",
                    since: null,
                    until: null,
                    window: null,
                    action: null,
                    category: null,
                    operatorFilter: null,
                    receiptStatus: "all",
                    bucket: "day",
                    outputDirectory: snapshotDir,
                    outputFormat: "markdown",
                    markdownOutput,
                    generatedAt)
                .GetAwaiter()
                .GetResult();
            var tableExitCode = LedgerCommand.ExecuteAnalyticsAsync(
                    root,
                    "history",
                    since: null,
                    until: null,
                    window: null,
                    action: null,
                    category: null,
                    operatorFilter: null,
                    receiptStatus: "all",
                    bucket: "day",
                    outputDirectory: snapshotDir,
                    outputFormat: "table",
                    tableOutput,
                    generatedAt)
                .GetAwaiter()
                .GetResult();

            if (exitCode != 0 || markdownExitCode != 0 || tableExitCode != 0)
            {
                evidence = $"ledger analytics runtime exited json={exitCode.ToString(CultureInfo.InvariantCulture)} markdown={markdownExitCode.ToString(CultureInfo.InvariantCulture)} table={tableExitCode.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            var after = SnapshotLocalFiles(root);
            var addedPaths = after.Keys.Except(before.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            using var document = JsonDocument.Parse(output.ToString());
            var bundle = document.RootElement;
            if (bundle.GetProperty("schemaVersion").GetString() != "ledger-analytics-bundle.v1")
            {
                evidence = "ledger analytics runtime did not emit ledger-analytics-bundle.v1";
                return false;
            }

            var statsPath = Path.Combine(root, ".revitcli", "analytics", "ledger-stats.json");
            var timelinePath = Path.Combine(root, ".revitcli", "analytics", "ledger-timeline.json");
            using var statsDocument = JsonDocument.Parse(File.ReadAllText(statsPath));
            using var timelineDocument = JsonDocument.Parse(File.ReadAllText(timelinePath));
            var stats = statsDocument.RootElement;
            var timeline = timelineDocument.RootElement;
            var operationCount = bundle.GetProperty("statsSummary").GetProperty("operationCount").GetInt32();
            var timelineOperationCount = bundle.GetProperty("timelineSummary").GetProperty("operationCount").GetInt32();
            var outputFormatParityReady = AnalyticsBundleOutputFormatParityReady(
                bundle,
                markdownOutput.ToString(),
                tableOutput.ToString());
            var boundedWriteReady = addedPaths.SequenceEqual(
                new[]
                {
                    ".revitcli/analytics/",
                    ".revitcli/analytics/ledger-stats.json",
                    ".revitcli/analytics/ledger-timeline.json",
                },
                StringComparer.Ordinal);
            var ok =
                boundedWriteReady &&
                outputFormatParityReady &&
                operationCount == 1 &&
                timelineOperationCount == 1 &&
                bundle.GetProperty("localOnly").GetBoolean() &&
                !bundle.GetProperty("databaseRuntime").GetBoolean() &&
                !bundle.GetProperty("networkService").GetBoolean() &&
                stats.GetProperty("schemaVersion").GetString() == "ledger-stats.v1" &&
                timeline.GetProperty("schemaVersion").GetString() == "ledger-timeline.v1";

            evidence = ok
                ? "ledger analytics runtime emits ledger-analytics-bundle.v1, writes exactly local ledger-stats.v1 and ledger-timeline.v1 snapshot evidence under .revitcli/analytics, preserves JSON/table/Markdown bundle semantic parity, and declares localOnly=true with no database or network service runtime"
                : $"ledger analytics runtime payload or bounded writes failed (operationCount={operationCount.ToString(CultureInfo.InvariantCulture)}, timelineOperationCount={timelineOperationCount.ToString(CultureInfo.InvariantCulture)}, outputParity={outputFormatParityReady.ToString().ToLowerInvariant()}, boundedWrite={boundedWriteReady.ToString().ToLowerInvariant()}, added={string.Join(",", addedPaths)})";
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            evidence = $"ledger analytics runtime check failed: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool TimelineBucketCountsEqual(
        JsonElement[] buckets,
        string bucketStartUtc,
        int operationCount,
        (string Name, int Count)[] bySource,
        (string Name, int Count)[] byAction,
        (string Name, int Count)[] byCategory,
        (string Name, int Count)[] byOperator,
        (string Name, int Count)[] byReceiptStatus)
    {
        var bucket = buckets.FirstOrDefault(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("bucketStartUtc", out var start) &&
            string.Equals(start.GetString(), bucketStartUtc, StringComparison.OrdinalIgnoreCase));
        if (bucket.ValueKind != JsonValueKind.Object)
            return false;

        return bucket.GetProperty("operationCount").GetInt32() == operationCount &&
               bucket.TryGetProperty("issuesBySeverity", out var bucketSeverity) &&
               bucketSeverity.ValueKind == JsonValueKind.Array &&
               JsonArrayCountsEqual(bucket.GetProperty("bySource"), bySource) &&
               JsonArrayCountsEqual(bucket.GetProperty("byAction"), byAction) &&
               JsonArrayCountsEqual(bucket.GetProperty("byCategory"), byCategory) &&
               JsonArrayCountsEqual(bucket.GetProperty("byOperator"), byOperator) &&
               JsonArrayCountsEqual(bucket.GetProperty("byReceiptStatus"), byReceiptStatus);
    }

    private static string DescribeTimelineBuckets(JsonElement[] buckets) =>
        string.Join(
            "; ",
            buckets.Select(bucket =>
                $"{bucket.GetProperty("bucketStartUtc").GetString()} ops={bucket.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture)} source=[{DescribeCounts(bucket.GetProperty("bySource"))}] action=[{DescribeCounts(bucket.GetProperty("byAction"))}] category=[{DescribeCounts(bucket.GetProperty("byCategory"))}] operator=[{DescribeCounts(bucket.GetProperty("byOperator"))}] receipt=[{DescribeCounts(bucket.GetProperty("byReceiptStatus"))}]"));

    private static string DescribeCounts(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array
            ? string.Join(
                ",",
                array.EnumerateArray().Select(item =>
                    $"{item.GetProperty("name").GetString()}:{item.GetProperty("count").GetInt32().ToString(CultureInfo.InvariantCulture)}"))
            : "not-array";

    private static bool OperationMatches(
        string root,
        JsonElement operation,
        string source,
        string action,
        string relativeArtifactPath,
        int? line)
    {
        if (operation.GetProperty("source").GetString() != source ||
            operation.GetProperty("action").GetString() != action ||
            ReadNullableInt(operation, "line") != line)
        {
            return false;
        }

        var artifactPath = operation.GetProperty("artifactPath").GetString();
        if (string.IsNullOrWhiteSpace(artifactPath))
            return false;

        var relative = Path.GetRelativePath(root, artifactPath).Replace('\\', '/');
        return relativeArtifactPath.EndsWith("/", StringComparison.Ordinal)
            ? relative.StartsWith(relativeArtifactPath, StringComparison.OrdinalIgnoreCase) &&
              relative.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase)
            : string.Equals(relative, relativeArtifactPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool QueryOutputFormatParityReady(JsonElement query, string markdown, string table)
    {
        var totalOperations = query.GetProperty("summary").GetProperty("totalOperations").GetInt32();
        var issueCount = query.GetProperty("summary").GetProperty("issueCount").GetInt32();
        var operations = query.GetProperty("operations").EnumerateArray()
            .Select(QueryOutputProjection.FromJson)
            .ToArray();
        if (totalOperations != operations.Length ||
            !table.Contains($"Operations: {totalOperations.ToString(CultureInfo.InvariantCulture)}; issues={issueCount.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Operations: `{totalOperations.ToString(CultureInfo.InvariantCulture)}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Issues: `{issueCount.ToString(CultureInfo.InvariantCulture)}`", StringComparison.Ordinal))
        {
            return false;
        }

        var markdownRows = ParseQueryMarkdownRows(markdown);
        if (!operations.SequenceEqual(markdownRows))
            return false;

        var tableRows = ParseQueryTableRows(table, operations.Length);
        if (!operations.SequenceEqual(tableRows))
            return false;

        return true;
    }

    private static bool ValidateOutputFormatParityReady(JsonElement validate, string markdown, string table)
    {
        var summary = validate.GetProperty("summary");
        var valid = validate.GetProperty("valid").GetBoolean().ToString().ToLowerInvariant();
        var operations = summary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var errors = summary.GetProperty("errorCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var warnings = summary.GetProperty("warningCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        if (!markdown.Contains($"- Valid: `{valid}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Operations: `{operations}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Errors: `{errors}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Warnings: `{warnings}`", StringComparison.Ordinal) ||
            !table.Contains($"Valid: {valid}; operations={operations}; errors={errors}; warnings={warnings}", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var check in validate.GetProperty("checks").EnumerateArray())
        {
            var status = check.GetProperty("status").GetString() ?? "";
            var id = check.GetProperty("id").GetString() ?? "";
            var evidence = check.GetProperty("evidence").GetString() ?? "";
            if (!markdown.Contains($"| `{status}` | `{id}` | {evidence} |", StringComparison.Ordinal) ||
                !table.Contains($"{status,-8} {id,-18} {evidence}", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StatsOutputFormatParityReady(JsonElement stats, string markdown, string table)
    {
        var summary = stats.GetProperty("summary");
        var operations = summary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var issues = summary.GetProperty("issueCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var missingReceipts = summary.GetProperty("missingReceiptCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var unreadableReceipts = summary.GetProperty("unreadableReceiptCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        if (!markdown.Contains($"- Operations: `{operations}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Issues: `{issues}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Missing receipts: `{missingReceipts}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Unreadable receipts: `{unreadableReceipts}`", StringComparison.Ordinal) ||
            !table.Contains($"Operations: {operations}; issues={issues}; missingReceipts={missingReceipts}; unreadableReceipts={unreadableReceipts}", StringComparison.Ordinal))
        {
            return false;
        }

        return CountsRendered(stats.GetProperty("bySource"), markdown, table) &&
               CountsRendered(stats.GetProperty("byAction"), markdown, table) &&
               CountsRendered(stats.GetProperty("byCategory"), markdown, table) &&
               CountsRendered(stats.GetProperty("byOperator"), markdown, table) &&
               CountsRendered(stats.GetProperty("byReceiptStatus"), markdown, table) &&
               CountsRendered(stats.GetProperty("issuesBySource"), markdown, table) &&
               CountsRendered(stats.GetProperty("issuesBySeverity"), markdown, table);
    }

    private static bool AnalyticsBundleOutputFormatParityReady(JsonElement bundle, string markdown, string table)
    {
        var statsSummary = bundle.GetProperty("statsSummary");
        var timelineSummary = bundle.GetProperty("timelineSummary");
        var operations = statsSummary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var statsIssues = statsSummary.GetProperty("issueCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var timelineBuckets = timelineSummary.GetProperty("bucketCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var timelineIssues = timelineSummary.GetProperty("issueCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var localOnly = bundle.GetProperty("localOnly").GetBoolean().ToString().ToLowerInvariant();
        var databaseRuntime = bundle.GetProperty("databaseRuntime").GetBoolean().ToString().ToLowerInvariant();
        var networkService = bundle.GetProperty("networkService").GetBoolean().ToString().ToLowerInvariant();

        return markdown.Contains($"- Operations: `{operations}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Stats issues: `{statsIssues}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Timeline buckets: `{timelineBuckets}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Timeline issues: `{timelineIssues}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Local only: `{localOnly}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Database runtime: `{databaseRuntime}`", StringComparison.Ordinal) &&
               markdown.Contains($"- Network service: `{networkService}`", StringComparison.Ordinal) &&
               table.Contains($"Operations: {operations}; statsIssues={statsIssues}; timelineBuckets={timelineBuckets}; timelineIssues={timelineIssues}", StringComparison.Ordinal) &&
               table.Contains($"Local only: {localOnly}; databaseRuntime={databaseRuntime}; networkService={networkService}", StringComparison.Ordinal);
    }

    private static bool TimelineOutputFormatParityReady(JsonElement timeline, string markdown, string table)
    {
        var query = timeline.GetProperty("query");
        var summary = timeline.GetProperty("summary");
        var bucketName = query.GetProperty("bucket").GetString() ?? "";
        var operations = summary.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var bucketCount = summary.GetProperty("bucketCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var issues = summary.GetProperty("issueCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var unbucketed = summary.GetProperty("unbucketedOperationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        if (!markdown.Contains($"- Bucket: `{bucketName}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Operations: `{operations}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Buckets: `{bucketCount}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Issues: `{issues}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Unbucketed operations: `{unbucketed}`", StringComparison.Ordinal) ||
            !table.Contains($"Bucket: {bucketName}; operations={operations}; buckets={bucketCount}; issues={issues}; unbucketed={unbucketed}", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var bucket in timeline.GetProperty("buckets").EnumerateArray())
        {
            var start = bucket.GetProperty("bucketStartUtc").GetString() ?? "";
            var end = bucket.GetProperty("bucketEndUtc").GetString() ?? "";
            var operationCount = bucket.GetProperty("operationCount").GetInt32().ToString(CultureInfo.InvariantCulture);
            var bySource = JoinJsonCounts(bucket.GetProperty("bySource"));
            var byAction = JoinJsonCounts(bucket.GetProperty("byAction"));
            var byCategory = JoinJsonCounts(bucket.GetProperty("byCategory"));
            var byOperator = JoinJsonCounts(bucket.GetProperty("byOperator"));
            var byReceipt = JoinJsonCounts(bucket.GetProperty("byReceiptStatus"));
            var issueCount = bucket.GetProperty("issueCount").GetInt32().ToString(CultureInfo.InvariantCulture);
            if (!markdown.Contains($"| {start} | {end} | {operationCount} | {bySource} | {byAction} | {byCategory} | {byOperator} | {byReceipt} | {issueCount} |", StringComparison.Ordinal) ||
                !table.Contains($"{start}", StringComparison.Ordinal) ||
                !table.Contains($"operations={operationCount}", StringComparison.Ordinal) ||
                !table.Contains($"bucketEnd={end}", StringComparison.Ordinal) ||
                !table.Contains($"sources={bySource}", StringComparison.Ordinal) ||
                !table.Contains($"actions={byAction}", StringComparison.Ordinal) ||
                !table.Contains($"categories={byCategory}", StringComparison.Ordinal) ||
                !table.Contains($"operators={byOperator}", StringComparison.Ordinal) ||
                !table.Contains($"receipts={byReceipt}", StringComparison.Ordinal) ||
                !table.Contains($"issues={issueCount}", StringComparison.Ordinal) ||
                !table.Contains($"issueSeverity={JoinJsonCounts(bucket.GetProperty("issuesBySeverity"))}", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return CountsRendered(timeline.GetProperty("issuesBySeverity"), markdown, table);
    }

    private static bool WorkflowRegistryOutputFormatParityReady(JsonElement registry, string table, string markdown)
    {
        var workflowCount = registry.GetProperty("workflowCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var validWorkflowCount = registry.GetProperty("validWorkflowCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var invalidWorkflowCount = registry.GetProperty("invalidWorkflowCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var mutatingWorkflowCount = registry.GetProperty("mutatingWorkflowCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var dryRunCommandCount = registry.GetProperty("dryRunCommandCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var approvalRequiredStepCount = registry.GetProperty("approvalRequiredStepCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var rollbackSupportedWorkflowCount = registry.GetProperty("rollbackSupportedWorkflowCount").GetInt32().ToString(CultureInfo.InvariantCulture);
        var success = registry.GetProperty("success").GetBoolean() ? "yes" : "no";
        var exists = registry.GetProperty("exists").GetBoolean() ? "yes" : "no";

        if (!table.Contains($"Schema: {registry.GetProperty("schemaVersion").GetString()}", StringComparison.Ordinal) ||
            !table.Contains($"Workflows: {workflowCount}; valid: {validWorkflowCount}; invalid: {invalidWorkflowCount}; mutating: {mutatingWorkflowCount}; dryRunCommands={dryRunCommandCount}; approvalRequired={approvalRequiredStepCount}; rollbackSupported={rollbackSupportedWorkflowCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Schema: `{registry.GetProperty("schemaVersion").GetString()}`", StringComparison.Ordinal) ||
            !markdown.Contains($"- Exists: {exists}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Success: {success}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Workflows: {workflowCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Valid workflows: {validWorkflowCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Invalid workflows: {invalidWorkflowCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Dry-run commands: {dryRunCommandCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Approval-required steps: {approvalRequiredStepCount}", StringComparison.Ordinal) ||
            !markdown.Contains($"- Rollback-supported workflows: {rollbackSupportedWorkflowCount}", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var workflow in registry.GetProperty("workflows").EnumerateArray())
        {
            var name = workflow.GetProperty("name").GetString() ?? "";
            var status = workflow.GetProperty("canRun").GetBoolean() ? "OK" : "FAIL";
            var risk = workflow.GetProperty("riskLevel").GetString() ?? "";
            var stepCount = workflow.GetProperty("stepCount").GetInt32().ToString(CultureInfo.InvariantCulture);
            var scopeCsv = string.Join(",", workflow.GetProperty("readWriteScope").EnumerateArray().Select(item => item.GetString()));
            var scopeMarkdown = string.Join(", ", workflow.GetProperty("readWriteScope").EnumerateArray().Select(item => item.GetString()));
            var inputsCsv = string.Join(",", workflow.GetProperty("inputs").EnumerateArray().Select(item => item.GetString()));
            var inputsMarkdown = string.Join(", ", workflow.GetProperty("inputs").EnumerateArray().Select(item => item.GetString()));
            var outputsCsv = string.Join(",", workflow.GetProperty("outputs").EnumerateArray().Select(item => item.GetString()));
            var outputsMarkdown = string.Join(", ", workflow.GetProperty("outputs").EnumerateArray().Select(item => item.GetString()));
            var dryRunCommands = workflow.GetProperty("dryRunCommands").GetArrayLength().ToString(CultureInfo.InvariantCulture);
            var approvalCommands = workflow.GetProperty("approvalCommands").GetArrayLength().ToString(CultureInfo.InvariantCulture);
            var rollback = workflow.GetProperty("rollbackSupport").GetBoolean() ? "yes" : "no";
            var receiptsCsv = string.Join(",", workflow.GetProperty("receiptSchemas").EnumerateArray().Select(item => item.GetString()));
            var receiptsMarkdown = string.Join(", ", workflow.GetProperty("receiptSchemas").EnumerateArray().Select(item => item.GetString()));
            var acceptanceCsv = string.Join(",", workflow.GetProperty("acceptanceEvidence").EnumerateArray().Select(item => item.GetString()));
            var acceptanceMarkdown = string.Join(", ", workflow.GetProperty("acceptanceEvidence").EnumerateArray().Select(item => item.GetString()));
            var dryRunCommandsTable = workflow.GetProperty("dryRunCommands").EnumerateArray()
                .Select(item => $"dry-run command: {item.GetString()}")
                .ToArray();
            var approvalCommandsTable = workflow.GetProperty("approvalCommands").EnumerateArray()
                .Select(item => $"approval command: {item.GetString()}")
                .ToArray();
            var dryRunCommandsMarkdown = string.Join("<br>", workflow.GetProperty("dryRunCommands").EnumerateArray().Select(item => item.GetString()));
            var approvalCommandsMarkdown = string.Join("<br>", workflow.GetProperty("approvalCommands").EnumerateArray().Select(item => item.GetString()));

            if (!table.Contains($"{status,-4} {name} risk={risk} steps={stepCount} scope={scopeCsv} rollback={rollback} dryRuns={dryRunCommands} approvals={approvalCommands} receipts={receiptsCsv}", StringComparison.Ordinal) ||
                !table.Contains($"inputs={inputsCsv} outputs={outputsCsv}", StringComparison.Ordinal) ||
                !table.Contains($"acceptance evidence={acceptanceCsv}", StringComparison.Ordinal) ||
                dryRunCommandsTable.Any(command => !table.Contains(command, StringComparison.Ordinal)) ||
                approvalCommandsTable.Any(command => !table.Contains(command, StringComparison.Ordinal)) ||
                !markdown.Contains($"| {name} | {status} | `{risk}` | `{scopeMarkdown}` | {inputsMarkdown} | {outputsMarkdown} | {dryRunCommandsMarkdown} | {approvalCommandsMarkdown} | {rollback} | `{receiptsMarkdown}` | {acceptanceMarkdown} |", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CountsRendered(JsonElement counts, string markdown, string table)
    {
        if (counts.ValueKind != JsonValueKind.Array || counts.GetArrayLength() == 0)
            return true;

        foreach (var count in counts.EnumerateArray())
        {
            var name = count.GetProperty("name").GetString() ?? "";
            var value = count.GetProperty("count").GetInt32().ToString(CultureInfo.InvariantCulture);
            if (!markdown.Contains($"| {name} | {value} |", StringComparison.Ordinal) ||
                !table.Contains($"  - {name}: {value}", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string JoinJsonCounts(JsonElement counts)
    {
        if (counts.ValueKind != JsonValueKind.Array || counts.GetArrayLength() == 0)
            return "none";

        return string.Join(", ", counts.EnumerateArray().Select(count =>
            $"{count.GetProperty("name").GetString()}={count.GetProperty("count").GetInt32().ToString(CultureInfo.InvariantCulture)}"));
    }

    private static IReadOnlyList<QueryOutputProjection> ParseQueryMarkdownRows(string markdown)
    {
        var rows = new List<QueryOutputProjection>();
        var lines = SplitLines(markdown);
        var headerIndex = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), "| Timestamp | Source | Action | Receipt | Artifact |", StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
            return rows;

        foreach (var line in lines.Skip(headerIndex + 2))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("|", StringComparison.Ordinal))
                break;

            var cells = SplitMarkdownTableRow(line);
            if (cells.Length == 5)
            {
                rows.Add(new QueryOutputProjection(cells[0], cells[1], cells[2], cells[3], cells[4]));
            }
        }

        return rows;
    }

    private static IReadOnlyList<QueryOutputProjection> ParseQueryTableRows(string table, int expectedCount)
    {
        var rows = new List<QueryOutputProjection>();
        foreach (var line in SplitLines(table).Skip(3).Take(expectedCount))
        {
            if (!TryParseQueryTableRow(line, out var row))
                return Array.Empty<QueryOutputProjection>();

            rows.Add(row);
        }

        return rows;
    }

    private static bool TryParseQueryTableRow(string line, out QueryOutputProjection row)
    {
        row = new QueryOutputProjection("", "", "", "", "");
        var columns = line.Split(new[] { ' ' }, 5, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length != 5)
        {
            return false;
        }

        row = new QueryOutputProjection(
            columns[0],
            columns[1],
            columns[2],
            columns[3],
            columns[4]);
        return true;
    }

    private static string[] SplitMarkdownTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith("|", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var current = trimmed[i];
            if (current == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }

            if (current == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(current);
        }

        cells.Add(cell.ToString().Trim());
        return cells.ToArray();
    }

    private static string[] SplitLines(string value) =>
        value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private sealed record QueryOutputProjection(
        string Timestamp,
        string Source,
        string Action,
        string Receipt,
        string Artifact)
    {
        public static QueryOutputProjection FromJson(JsonElement operation) =>
            new(
                ReadString(operation, "timestamp"),
                ReadString(operation, "source"),
                ReadString(operation, "action"),
                ReadString(operation, "receiptStatus"),
                ReadString(operation, "artifactPath"));

        private static string ReadString(JsonElement element, string propertyName)
        {
            var value = element.GetProperty(propertyName).GetString();
            return string.IsNullOrWhiteSpace(value) ? "n/a" : value;
        }
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }

    private static bool JsonArrayCountContains(JsonElement array, string value) =>
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().Any(item =>
            string.Equals(item.GetProperty("name").GetString(), value, StringComparison.OrdinalIgnoreCase) &&
            item.GetProperty("count").GetInt32() > 0);

    private static SortedDictionary<string, string> SnapshotLocalFiles(string root)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
            return snapshot;

        foreach (var path in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            snapshot[$"{relative}/"] = "<dir>";
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            snapshot[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return snapshot;
    }

    private static bool JsonArrayContains(JsonElement array, string value) =>
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().Any(item =>
            string.Equals(item.GetString(), value, StringComparison.OrdinalIgnoreCase));

    private static string? FindSourceSmokeRoot()
    {
        var starts = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                var smokeRoot = Path.Combine(dir.FullName, "docs", "smoke", "v5.0");
                if (HasV5SmokeDisclosureArtifacts(smokeRoot))
                    return smokeRoot;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static string? GetProjectDocsRootForReleaseClaims(string projectDirectory)
    {
        var docsRoot = Path.Combine(projectDirectory, "docs");
        return HasSourceDocsArtifacts(docsRoot)
            ? docsRoot
            : null;
    }

    private static string? FindSourceDocsRoot()
    {
        var starts = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                var docsRoot = Path.Combine(dir.FullName, "docs");
                if (HasSourceDocsArtifacts(docsRoot))
                    return docsRoot;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static bool HasSourceDocsArtifacts(string docsRoot)
    {
        if (!Directory.Exists(docsRoot))
            return false;

        return File.Exists(Path.Combine(docsRoot, "roadmap-v5-v6.md")) ||
               File.Exists(Path.Combine(docsRoot, "release-checklist.md")) ||
               HasV5SmokeDisclosureArtifacts(Path.Combine(docsRoot, "smoke", "v5.0"));
    }

    private static bool HasV5SmokeDisclosureArtifacts(string smokeRoot)
    {
        if (!Directory.Exists(smokeRoot))
            return false;

        return File.Exists(Path.Combine(smokeRoot, "gap-report.md")) ||
               Directory.EnumerateFiles(smokeRoot, "revit-*-issue-closure.md", SearchOption.TopDirectoryOnly).Any();
    }

    private static string TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string? ValidateV60OfficeRolloutStatus(string path)
    {
        if (!File.Exists(path))
            return "docs/smoke/v6.0/office-rollout-status.json is missing; v6.0 office rollout completion status is not machine-readable.";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var minimumCount = ReadJsonInt(root, "minimumOfficePilotCount");
            var completedCount = ReadJsonInt(root, "completedOfficePilotCount");
            var completionClaim = ReadJsonBool(root, "officeRolloutCompletion");
            var supportClaim = ReadJsonBool(root, "productionSupportClaim");
            var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? "", "..", "..", ".."));
            var completedPilotsComplete = completedCount.HasValue &&
                CompletedOfficePilotEvidenceComplete(repositoryRoot, root, completedCount.Value);
            var productionSupportReviewComplete = supportClaim is not true ||
                ProductionSupportReviewComplete(repositoryRoot, root);
            var requiredEvidenceComplete =
                JsonPathBoolEquals(root, "requiredEvidence.doctor", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.status", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.workbench", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.release", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.ledgerQuery", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.ledgerValidate", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.ledgerStatsAnalyticsSnapshot", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.ledgerTimelineAnalyticsSnapshot", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.journalVerify", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.rollbackResult", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.userReview", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.bimManagerSignoff", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.projectCopyOwnerSignoff", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.supportTicketReview", true) &&
                JsonPathBoolEquals(root, "requiredEvidence.multiUserRolloutPostmortem", true);

            if (!JsonPathStringEquals(root, "schemaVersion", "v6-office-rollout-status.v1") ||
                minimumCount.GetValueOrDefault() < 2 ||
                completedCount.GetValueOrDefault(-1) < 0 ||
                !JsonArrayLengthEquals(root, "completedPilotIds", completedCount.GetValueOrDefault()) ||
                !JsonArrayLengthEquals(root, "completedPilots", completedCount.GetValueOrDefault()))
            {
                return "docs/smoke/v6.0/office-rollout-status.json must use v6-office-rollout-status.v1, minimumOfficePilotCount>=2, completedOfficePilotCount>=0, and matching completedPilotIds/completedPilots.";
            }

            if (!requiredEvidenceComplete)
                return "docs/smoke/v6.0/office-rollout-status.json must require all command, review, signoff, support-review, and postmortem evidence fields.";

            if (!completedPilotsComplete)
                return "docs/smoke/v6.0/office-rollout-status.json completedPilots entries must include complete per-pilot evidence flags and matching packet Pilot identifiers.";

            var belowMinimum = completedCount.GetValueOrDefault() < minimumCount.GetValueOrDefault();
            var reachedMinimum = completedCount.GetValueOrDefault() >= minimumCount.GetValueOrDefault();
            if (!((belowMinimum && completionClaim is false && supportClaim is false) ||
                  (reachedMinimum && completionClaim.HasValue && supportClaim.HasValue && completedPilotsComplete && productionSupportReviewComplete)))
            {
                return "docs/smoke/v6.0/office-rollout-status.json must keep completion/support false below the pilot threshold, or provide a consistent threshold-reached status with complete per-pilot evidence and productionSupportReviewPath when production support is claimed.";
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"docs/smoke/v6.0/office-rollout-status.json is not readable valid JSON: {ex.Message}";
        }
    }

    private static bool TryGetJsonPath(JsonElement element, string path, out JsonElement property)
    {
        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
            {
                property = default;
                return false;
            }
        }

        property = current;
        return true;
    }

    private static bool JsonPathStringEquals(JsonElement element, string path, string expected) =>
        TryGetJsonPath(element, path, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool JsonPathBoolEquals(JsonElement element, string path, bool expected) =>
        TryGetJsonPath(element, path, out var property) &&
        (expected ? property.ValueKind == JsonValueKind.True : property.ValueKind == JsonValueKind.False);

    private static bool JsonPathStringNonEmpty(JsonElement element, string path) =>
        TryGetJsonPath(element, path, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString());

    private static bool CompletedOfficePilotEvidenceComplete(string root, JsonElement status, int expectedCount)
    {
        if (!TryReadUniqueStringArray(status, "completedPilotIds", expectedCount, out var completedPilotIds))
            return false;

        if (!TryGetJsonPath(status, "completedPilots", out var completedPilots) ||
            completedPilots.ValueKind != JsonValueKind.Array ||
            completedPilots.GetArrayLength() != expectedCount)
        {
            return false;
        }

        var evidencePilotIds = new HashSet<string>(StringComparer.Ordinal);
        return completedPilots.EnumerateArray().All(pilot =>
            TryReadNonEmptyJsonString(pilot, "pilotId", out var pilotId) &&
            completedPilotIds.Contains(pilotId) &&
            evidencePilotIds.Add(pilotId) &&
            JsonPathPublicOfficePilotEvidencePacket(pilot, "evidencePacketPath") &&
            CompletedOfficePilotEvidencePacketComplete(root, pilot.GetProperty("evidencePacketPath").GetString(), pilotId) &&
            JsonPathBoolEquals(pilot, "doctor", true) &&
            JsonPathBoolEquals(pilot, "status", true) &&
            JsonPathBoolEquals(pilot, "workbench", true) &&
            JsonPathBoolEquals(pilot, "release", true) &&
            JsonPathBoolEquals(pilot, "ledgerQuery", true) &&
            JsonPathBoolEquals(pilot, "ledgerValidate", true) &&
            JsonPathBoolEquals(pilot, "ledgerStatsAnalyticsSnapshot", true) &&
            JsonPathBoolEquals(pilot, "ledgerTimelineAnalyticsSnapshot", true) &&
            JsonPathBoolEquals(pilot, "journalVerify", true) &&
            JsonPathBoolEquals(pilot, "rollbackResult", true) &&
            JsonPathBoolEquals(pilot, "userReview", true) &&
            JsonPathBoolEquals(pilot, "bimManagerSignoff", true) &&
            JsonPathBoolEquals(pilot, "projectCopyOwnerSignoff", true) &&
            JsonPathBoolEquals(pilot, "supportTicketReview", true) &&
            JsonPathBoolEquals(pilot, "multiUserRolloutPostmortem", true));
    }

    private static bool JsonPathPublicOfficePilotEvidencePacket(JsonElement element, string path)
    {
        if (!TryReadNonEmptyJsonString(element, path, out var value))
            return false;

        var trimmed = value.Trim();
        return !trimmed.Contains('\\', StringComparison.Ordinal) &&
            !trimmed.Contains(':', StringComparison.Ordinal) &&
            !trimmed.StartsWith("/", StringComparison.Ordinal) &&
            !trimmed.Contains("../", StringComparison.Ordinal) &&
            !trimmed.Contains("/..", StringComparison.Ordinal) &&
            trimmed.StartsWith("docs/smoke/v6.0/", StringComparison.Ordinal) &&
            trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompletedOfficePilotEvidencePacketComplete(string root, string? relativePath, string expectedPilotId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var fullPath = Path.Combine(root, relativePath.Trim());
        if (!File.Exists(fullPath))
            return false;

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return ContainsPilotIdentifier(text, expectedPilotId) &&
            ContainsAll(text,
            "Pilot identifier",
            "Required Commands",
            "doctor --check-version 2026 --output json",
            "status --output json",
            "workbench verify --contract workbench-contract.v2",
            "release verify --strict --output json",
            "ledger query --source ledger --output json",
            "ledger validate --source ledger --output json",
            "ledger stats --source ledger --analytics-snapshot",
            "ledger timeline --source ledger --analytics-snapshot",
            "journal verify --output json",
            "Live Operation Evidence",
            "Rollback result",
            "User Review",
            "BIM manager signoff",
            "Project-copy owner signoff",
            "Support ticket review",
            "Multi-user rollout postmortem",
            "Boundary summary");
    }

    private static bool ProductionSupportReviewComplete(string root, JsonElement status)
    {
        if (!TryReadNonEmptyJsonString(status, "productionSupportReviewPath", out var relativePath))
            return false;

        var trimmed = relativePath.Trim();
        if (trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed.Contains(':', StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.Contains("../", StringComparison.Ordinal) ||
            trimmed.Contains("/..", StringComparison.Ordinal) ||
            !trimmed.StartsWith("docs/smoke/v6.0/", StringComparison.Ordinal) ||
            !trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fullPath = Path.Combine(root, trimmed);
        if (!File.Exists(fullPath))
            return false;

        try
        {
            var text = File.ReadAllText(fullPath);
            return ContainsAll(
                text,
                "Production support review",
                "private support review approved",
                "office rollout completion",
                "production support claim");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ContainsPilotIdentifier(string text, string expectedPilotId)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
                continue;

            var label = trimmed[2..separator].Trim();
            if (!string.Equals(label, "Pilot identifier", StringComparison.OrdinalIgnoreCase))
                continue;

            return string.Equals(
                trimmed[(separator + 1)..].Trim(),
                expectedPilotId,
                StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryReadUniqueStringArray(
        JsonElement element,
        string path,
        int expectedCount,
        out HashSet<string> values)
    {
        values = new HashSet<string>(StringComparer.Ordinal);
        if (!TryGetJsonPath(element, path, out var property) ||
            property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() != expectedCount)
        {
            return false;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(item.GetString()) ||
                !values.Add(item.GetString()!))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadNonEmptyJsonString(JsonElement element, string path, out string value)
    {
        value = "";
        if (!TryGetJsonPath(element, path, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool? ReadJsonBool(JsonElement element, string path) =>
        TryGetJsonPath(element, path, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static int? ReadJsonInt(JsonElement element, string path) =>
        TryGetJsonPath(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool JsonArrayLengthEquals(JsonElement element, string path, int expected) =>
        TryGetJsonPath(element, path, out var property) &&
        property.ValueKind == JsonValueKind.Array &&
        property.GetArrayLength() == expected;

    private static bool GapReportDisclosesMissingSmoke(string gapReportText, string year) =>
        gapReportText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line =>
                line.Contains($"Revit {year}", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("not live verified", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> GetBuiltInWorkflowTemplateIssues(IReadOnlyList<string> requiredTemplates)
    {
        var issues = new List<string>();
        var required = requiredTemplates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInTemplates = WorkflowCommand.BuiltInTemplates;
        var templateNames = builtInTemplates
            .Select(template => template.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTemplates = requiredTemplates
            .Where(template => !templateNames.Contains(template))
            .ToArray();
        if (missingTemplates.Length > 0)
        {
            issues.Add($"missing template definitions {string.Join(", ", missingTemplates)}");
        }

        var acceptanceNames = WorkflowCommand.BuiltInAcceptanceWorkflowNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingAcceptance = requiredTemplates
            .Where(template => !acceptanceNames.Contains(template))
            .ToArray();
        if (missingAcceptance.Length > 0)
        {
            issues.Add($"missing acceptance examples {string.Join(", ", missingAcceptance)}");
        }

        var templatesDir = WorkflowCommand.FindBuiltInWorkflowTemplatesDirectory();
        if (templatesDir == null)
        {
            issues.Add("workflow template directory not found");
            return issues;
        }

        foreach (var template in builtInTemplates.Where(template => required.Contains(template.Name)))
        {
            var path = Path.Combine(templatesDir, template.File);
            if (!File.Exists(path))
            {
                issues.Add($"{template.Name} file missing: {template.File}");
                continue;
            }

            try
            {
                var loaded = WorkflowLoader.Load(path);
                var simulation = WorkflowValidator.Simulate(loaded);
                if (!string.Equals(simulation.Name, template.Name, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"{template.Name} file declares name '{simulation.Name}'");
                }

                if (simulation.StepCount == 0)
                {
                    issues.Add($"{template.Name} has no workflow steps");
                }

                if (simulation.Steps.All(step =>
                    string.Equals(step.Mode, "read-only", StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add($"{template.Name} has no dry-run or approved mutating review step");
                }

                if (simulation.Issues.Count > 0)
                {
                    var summary = simulation.Issues
                        .Take(3)
                        .Select(issue => $"{issue.Severity}:{issue.Path}")
                        .ToArray();
                    issues.Add($"{template.Name} has workflow issues {string.Join(", ", summary)}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or YamlDotNet.Core.YamlException)
            {
                issues.Add($"{template.Name} failed to load: {ex.Message}");
            }
        }

        return issues;
    }

    private static TraceabilityRuntimeCheck BuildTraceabilityRuntimeCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-trace-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);

            var scheduleExportReady = RunScheduleExportTraceCheck(root);
            var scheduleDiffReady = RunScheduleDiffTraceCheck(root);
            var deliveryReady = RunDeliveryBundleTraceCheck(root, out var bundleDryRunClean);
            var issuePackageReady = RunIssuePackageTraceCheck(root, out var issueDryRunClean);
            var dryRunClean = bundleDryRunClean && issueDryRunClean;
            var evidence = dryRunClean
                ? "runtime self-check produced non-empty SHA256 evidence and kept package dry-runs no-write"
                : "runtime self-check detected dry-run package writes";

            return new TraceabilityRuntimeCheck(
                scheduleExportReady,
                scheduleDiffReady,
                deliveryReady,
                issuePackageReady,
                evidence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new TraceabilityRuntimeCheck(false, false, false, false, $"runtime self-check failed: {ex.Message}");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            TryDeleteDirectory(root);
        }
    }

    private static bool RunScheduleExportTraceCheck(string root)
    {
        var schedulesDirectory = Path.Combine(root, ".revitcli", "schedules");
        Directory.CreateDirectory(schedulesDirectory);
        File.WriteAllText(Path.Combine(schedulesDirectory, "issue.yml"), """
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Door Schedule
    category: Doors
    fields: [Mark, Level]
    keyColumns: [Mark]
""");
        using var client = new RevitClient(new HttpClient(new TraceabilityScheduleHandler())
        {
            BaseAddress = new Uri("http://localhost:17839")
        });
        var outputDirectory = Path.Combine(root, "exports", "schedules", "current");
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var output = new StringWriter();
        var exitCode = SchedulesCommand.ExecuteBatchExportAsync(
                client,
                "issue",
                outputDirectory,
                "csv",
                manifestPath,
                "json",
                output)
            .GetAwaiter()
            .GetResult();
        if (exitCode != 0 || !File.Exists(manifestPath))
            return false;

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return document.RootElement.TryGetProperty("profile", out var profile) &&
               profile.GetString() == "issue" &&
               document.RootElement.TryGetProperty("command", out var command) &&
               command.GetString()?.Contains("schedules batch-export", StringComparison.OrdinalIgnoreCase) == true &&
               document.RootElement.TryGetProperty("manifestPath", out var manifest) &&
               !string.IsNullOrWhiteSpace(manifest.GetString()) &&
               document.RootElement.TryGetProperty("modelPath", out var modelPath) &&
               !string.IsNullOrWhiteSpace(modelPath.GetString()) &&
               document.RootElement.TryGetProperty("documentName", out var documentName) &&
               !string.IsNullOrWhiteSpace(documentName.GetString()) &&
               document.RootElement.TryGetProperty("documentVersion", out var documentVersion) &&
               !string.IsNullOrWhiteSpace(documentVersion.GetString()) &&
               document.RootElement.TryGetProperty("entries", out var entries) &&
               entries.ValueKind == JsonValueKind.Array &&
               entries.EnumerateArray().Any(entry =>
                   entry.GetProperty("success").GetBoolean() &&
                   entry.GetProperty("bytes").GetInt64() > 0 &&
                   HasSha256(entry, "sha256"));
    }

    private static bool RunScheduleDiffTraceCheck(string root)
    {
        var baseline = Path.Combine(root, "exports", "schedules", "baseline");
        var current = Path.Combine(root, "exports", "schedules", "diff-current");
        Directory.CreateDirectory(baseline);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(baseline, "Door Schedule.csv"), "Mark,Level\nD-001,L1\n");
        File.WriteAllText(Path.Combine(current, "Door Schedule.csv"), "Mark,Level\nD-001,L2\n");
        var output = new StringWriter();
        var exitCode = SchedulesCommand.ExecuteCompareAsync(
                baseline,
                current,
                "Mark",
                "json",
                output)
            .GetAwaiter()
            .GetResult();
        if (exitCode != 2)
            return false;

        using var document = JsonDocument.Parse(output.ToString());
        var file = document.RootElement.GetProperty("files").EnumerateArray().FirstOrDefault();
        return file.ValueKind == JsonValueKind.Object &&
               file.GetProperty("beforeBytes").GetInt64() > 0 &&
               file.GetProperty("afterBytes").GetInt64() > 0 &&
               HasSha256(file, "beforeSha256") &&
               HasSha256(file, "afterSha256");
    }

    private static bool RunDeliveryBundleTraceCheck(string root, out bool dryRunClean)
    {
        WriteTraceabilityDeliveryEvidence(root);
        var bundlePath = Path.Combine(root, "deliverables", "issue-package.zip");
        var output = new StringWriter();
        var exitCode = DeliverablesCommand.ExecuteBundleAsync(
                root,
                bundlePath,
                dryRun: true,
                force: false,
                outputFormat: "json",
                output)
            .GetAwaiter()
            .GetResult();
        dryRunClean = !File.Exists(bundlePath) && !File.Exists(bundlePath + ".receipt.json");
        if (exitCode != 0 || !dryRunClean)
            return false;

        using var document = JsonDocument.Parse(output.ToString());
        return document.RootElement.TryGetProperty("files", out var files) &&
               document.RootElement.TryGetProperty("command", out var command) &&
               command.GetString()?.Contains("deliverables bundle", StringComparison.OrdinalIgnoreCase) == true &&
               files.ValueKind == JsonValueKind.Array &&
               files.EnumerateArray().Any() &&
               files.EnumerateArray().All(file =>
                   file.GetProperty("bytes").GetInt64() > 0 &&
                   HasSha256(file, "sha256"));
    }

    private static bool RunIssuePackageTraceCheck(string root, out bool dryRunClean)
    {
        WriteTraceabilityDeliveryEvidence(root);
        var profilePath = Path.Combine(root, ".revitcli", "issue.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(profilePath, "schemaVersion: issue-profile.v1\n");
        var bundlePath = Path.Combine(root, "deliverables", "issue-package.zip");
        var output = new StringWriter();
        var exitCode = IssueCommand.ExecutePackageAsync(
                profilePath,
                bundlePath,
                dryRun: true,
                signJournal: false,
                includeReceipts: true,
                outputFormat: "json",
                output)
            .GetAwaiter()
            .GetResult();
        using var document = JsonDocument.Parse(output.ToString());
        var receiptPath = document.RootElement.GetProperty("receiptPath").GetString();
        dryRunClean = !File.Exists(bundlePath) &&
                      (string.IsNullOrWhiteSpace(receiptPath) || !File.Exists(receiptPath));
        if (exitCode != 0 || !dryRunClean)
            return false;

        return document.RootElement.TryGetProperty("files", out var files) &&
               files.ValueKind == JsonValueKind.Array &&
               files.EnumerateArray().Any() &&
               files.EnumerateArray().All(file =>
                   file.GetProperty("bytes").GetInt64() > 0 &&
                   HasSha256(file, "sha256"));
    }

    private static void WriteTraceabilityDeliveryEvidence(string root)
    {
        var outputDirectory = Path.Combine(root, "deliverables", "pdf");
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "A101.pdf"), "pdf-bytes");

        var receiptPath = Path.Combine(root, ".revitcli", "receipts", "export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "export-receipt.v1",
            action = "export",
            success = true,
            dryRun = false,
            outputDir = outputDirectory,
            timestamp = "2026-05-22T00:00:00Z"
        }));

        var manifestPath = Path.Combine(root, ".revitcli", "deliveries", "manifest.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "delivery-manifest.v1",
            kind = "export",
            success = true,
            dryRun = false,
            format = "pdf",
            receiptPath,
            timestamp = "2026-05-22T00:00:00Z"
        }) + Environment.NewLine);
    }

    private static bool HasSha256(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        property.GetString() is { Length: 64 } value &&
        value.All(Uri.IsHexDigit);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record TraceabilityRuntimeCheck(
        bool ScheduleExport,
        bool ScheduleDiff,
        bool DeliverablesBundle,
        bool IssuePackage,
        string Evidence);

    private static FaultInjectionRuntimeCheck BuildFaultInjectionRuntimeCheck()
    {
        var root = Path.Combine(Path.GetTempPath(), $"revitcli-workbench-faults-{Guid.NewGuid():N}");
        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);

            var missingProfileReady = RunMissingProfileFaultCheck(root);
            var scheduleFaultsReady = RunScheduleFaultInjectionCheck(root);
            var deliveryFaultsReady = RunDeliveryManifestFaultCheck(root);
            var bundlePathFaultReady = RunDeliverablesBundlePathFaultCheck(root);
            var packageCleanupReady = RunIssuePackageWriteFaultCheck(root);
            var evidence = "runtime self-check exercised missing profile, missing schedule export, stale schedule compare paths, missing manifest fields, missing receipt, tampered receipt, malformed manifest/receipt, bundle path write failure, and package write cleanup";
            return new FaultInjectionRuntimeCheck(missingProfileReady, scheduleFaultsReady, deliveryFaultsReady, bundlePathFaultReady, packageCleanupReady, evidence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return new FaultInjectionRuntimeCheck(false, false, false, false, false, $"runtime self-check failed: {ex.Message}");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            TryDeleteDirectory(root);
        }
    }

    private static bool RunMissingProfileFaultCheck(string root)
    {
        var output = new StringWriter();
        var exitCode = IssueCommand.ExecutePreflightAsync(
                Path.Combine(root, ".revitcli", "missing-issue.yml"),
                "json",
                "error",
                output)
            .GetAwaiter()
            .GetResult();
        return exitCode == 1 &&
               output.ToString().Contains("Issue profile not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RunScheduleFaultInjectionCheck(string root)
    {
        var scheduleRoot = Path.Combine(root, "schedule-faults");
        var scheduleDir = Path.Combine(scheduleRoot, ".revitcli", "schedules");
        Directory.CreateDirectory(scheduleDir);
        File.WriteAllText(Path.Combine(scheduleDir, "issue.yml"), """
schemaVersion: schedule-spec.v1
set: issue
schedules:
  - name: Missing Schedule
    category: Doors
    fields: [Mark]
""");

        var previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(scheduleRoot);
            using var client = new RevitClient(new HttpClient(new FaultInjectionScheduleHandler())
            {
                BaseAddress = new Uri("http://localhost:17839")
            });
            var exportOutput = new StringWriter();
            var manifestPath = Path.Combine(scheduleRoot, "exports", "manifest.json");
            var exportExit = SchedulesCommand.ExecuteBatchExportAsync(
                    client,
                    "issue",
                    Path.Combine(scheduleRoot, "exports"),
                    "csv",
                    manifestPath,
                    "json",
                    exportOutput)
                .GetAwaiter()
                .GetResult();
            var exportReady = exportExit == 2 &&
                              JsonContainsIssueCode(File.ReadAllText(manifestPath), "export-failed");

            var compareOutput = new StringWriter();
            var compareExit = SchedulesCommand.ExecuteCompareAsync(
                    Path.Combine(scheduleRoot, "missing-baseline"),
                    Path.Combine(scheduleRoot, "missing-current"),
                    "Mark",
                    "table",
                    compareOutput)
                .GetAwaiter()
                .GetResult();
            var compareReady = compareExit == 1 &&
                               compareOutput.ToString().Contains("Baseline directory not found", StringComparison.OrdinalIgnoreCase);

            return exportReady && compareReady;
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    private static bool RunDeliveryManifestFaultCheck(string root)
    {
        var malformedManifestRoot = Path.Combine(root, "malformed-manifest");
        var manifestDir = Path.Combine(malformedManifestRoot, ".revitcli", "deliveries");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.jsonl"), "{bad-json" + Environment.NewLine);
        var manifestOutput = new StringWriter();
        var manifestExit = DeliverablesCommand.ExecuteVerifyAsync(malformedManifestRoot, "json", manifestOutput)
            .GetAwaiter()
            .GetResult();
        var manifestReady = manifestExit == 1 && JsonContainsIssueCode(manifestOutput.ToString(), "manifest-json-invalid");

        var malformedReceiptRoot = Path.Combine(root, "malformed-receipt");
        var receiptDir = Path.Combine(malformedReceiptRoot, ".revitcli", "receipts");
        Directory.CreateDirectory(receiptDir);
        var receiptPath = Path.Combine(receiptDir, "bad.json");
        File.WriteAllText(receiptPath, "{bad-json");
        var receiptManifestDir = Path.Combine(malformedReceiptRoot, ".revitcli", "deliveries");
        Directory.CreateDirectory(receiptManifestDir);
        File.WriteAllText(Path.Combine(receiptManifestDir, "manifest.jsonl"), JsonSerializer.Serialize(new
        {
            schemaVersion = "delivery-manifest.v1",
            kind = "export",
            success = true,
            dryRun = false,
            receiptPath
        }) + Environment.NewLine);
        var receiptOutput = new StringWriter();
        var receiptExit = DeliverablesCommand.ExecuteVerifyAsync(malformedReceiptRoot, "json", receiptOutput)
            .GetAwaiter()
            .GetResult();
        var receiptReady = receiptExit == 1 && JsonContainsIssueCode(receiptOutput.ToString(), "receipt-json-invalid");

        var missingReceiptRoot = Path.Combine(root, "missing-receipt");
        var missingReceiptManifestDir = Path.Combine(missingReceiptRoot, ".revitcli", "deliveries");
        Directory.CreateDirectory(missingReceiptManifestDir);
        File.WriteAllText(Path.Combine(missingReceiptManifestDir, "manifest.jsonl"), JsonSerializer.Serialize(new
        {
            schemaVersion = "delivery-manifest.v1",
            kind = "export",
            success = true,
            dryRun = false,
            receiptPath = Path.Combine(missingReceiptRoot, ".revitcli", "receipts", "missing.json")
        }) + Environment.NewLine);
        var missingReceiptOutput = new StringWriter();
        var missingReceiptExit = DeliverablesCommand.ExecuteVerifyAsync(missingReceiptRoot, "json", missingReceiptOutput)
            .GetAwaiter()
            .GetResult();
        var missingReceiptReady = missingReceiptExit == 1 &&
                                  JsonContainsIssueCode(missingReceiptOutput.ToString(), "receipt-missing");

        var missingFieldsRoot = Path.Combine(root, "missing-manifest-fields");
        var missingFieldsManifestDir = Path.Combine(missingFieldsRoot, ".revitcli", "deliveries");
        Directory.CreateDirectory(missingFieldsManifestDir);
        File.WriteAllText(Path.Combine(missingFieldsManifestDir, "manifest.jsonl"), JsonSerializer.Serialize(new
        {
            success = true,
            dryRun = false
        }) + Environment.NewLine);
        var missingFieldsOutput = new StringWriter();
        var missingFieldsExit = DeliverablesCommand.ExecuteVerifyAsync(missingFieldsRoot, "json", missingFieldsOutput)
            .GetAwaiter()
            .GetResult();
        var missingFieldsReady = missingFieldsExit == 1 &&
                                 JsonContainsIssueCode(missingFieldsOutput.ToString(), "manifest-schema-invalid") &&
                                 JsonContainsIssueCode(missingFieldsOutput.ToString(), "manifest-kind-invalid") &&
                                 JsonContainsIssueCode(missingFieldsOutput.ToString(), "receipt-path-missing");

        var tamperedReceiptRoot = Path.Combine(root, "tampered-receipt");
        var tamperedReceiptDir = Path.Combine(tamperedReceiptRoot, ".revitcli", "receipts");
        Directory.CreateDirectory(tamperedReceiptDir);
        var tamperedReceiptPath = Path.Combine(tamperedReceiptDir, "receipt.json");
        File.WriteAllText(tamperedReceiptPath, JsonSerializer.Serialize(new
        {
            schemaVersion = "publish-receipt.v1",
            action = "publish",
            success = true,
            dryRun = false
        }));
        var tamperedManifestDir = Path.Combine(tamperedReceiptRoot, ".revitcli", "deliveries");
        Directory.CreateDirectory(tamperedManifestDir);
        File.WriteAllText(Path.Combine(tamperedManifestDir, "manifest.jsonl"), JsonSerializer.Serialize(new
        {
            schemaVersion = "delivery-manifest.v1",
            kind = "export",
            success = true,
            dryRun = false,
            receiptPath = tamperedReceiptPath
        }) + Environment.NewLine);
        var tamperedOutput = new StringWriter();
        var tamperedExit = DeliverablesCommand.ExecuteVerifyAsync(tamperedReceiptRoot, "json", tamperedOutput)
            .GetAwaiter()
            .GetResult();
        var tamperedReady = tamperedExit == 1 &&
                            JsonContainsIssueCode(tamperedOutput.ToString(), "receipt-schema-invalid") &&
                            JsonContainsIssueCode(tamperedOutput.ToString(), "receipt-action-mismatch");

        return manifestReady && receiptReady && missingReceiptReady && missingFieldsReady && tamperedReady;
    }

    private static bool RunDeliverablesBundlePathFaultCheck(string root)
    {
        var bundleRoot = Path.Combine(root, "bundle-path-write");
        Directory.CreateDirectory(bundleRoot);
        WriteTraceabilityDeliveryEvidence(bundleRoot);
        var parentAsFile = Path.Combine(bundleRoot, "review-as-file");
        File.WriteAllText(parentAsFile, "not a directory");
        var bundlePath = Path.Combine(parentAsFile, "package.zip");
        var output = new StringWriter();
        var exitCode = DeliverablesCommand.ExecuteBundleAsync(
                bundleRoot,
                bundlePath,
                dryRun: false,
                force: false,
                outputFormat: "json",
                output)
            .GetAwaiter()
            .GetResult();
        if (exitCode != 1)
            return false;

        using var document = JsonDocument.Parse(output.ToString());
        var rootElement = document.RootElement;
        return !rootElement.GetProperty("bundleWritten").GetBoolean() &&
               !rootElement.GetProperty("receiptWritten").GetBoolean() &&
               JsonContainsIssueCode(output.ToString(), "bundle-write-failed");
    }

    private static bool RunIssuePackageWriteFaultCheck(string root)
    {
        var packageRoot = Path.Combine(root, "package-write");
        Directory.CreateDirectory(packageRoot);
        WriteTraceabilityDeliveryEvidence(packageRoot);
        var profilePath = Path.Combine(packageRoot, ".revitcli", "issue.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        File.WriteAllText(profilePath, "schemaVersion: issue-profile.v1\n");
        var bundlePath = Path.Combine(packageRoot, "deliverables", "issue-package.zip");
        Directory.CreateDirectory(bundlePath);
        var output = new StringWriter();
        var exitCode = IssueCommand.ExecutePackageAsync(
                profilePath,
                bundlePath,
                dryRun: false,
                signJournal: false,
                includeReceipts: true,
                outputFormat: "json",
                output)
            .GetAwaiter()
            .GetResult();
        if (exitCode != 1)
            return false;

        using var document = JsonDocument.Parse(output.ToString());
        var rootElement = document.RootElement;
        return !rootElement.GetProperty("bundleWritten").GetBoolean() &&
               !rootElement.GetProperty("receiptWritten").GetBoolean() &&
               !File.Exists(rootElement.GetProperty("receiptPath").GetString()!) &&
               JsonContainsIssueCode(output.ToString(), "bundle-write-failed");
    }

    private static bool JsonContainsIssueCode(string json, string code)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("issues", out var issues) &&
               issues.ValueKind == JsonValueKind.Array &&
               issues.EnumerateArray().Any(issue =>
                   issue.TryGetProperty("code", out var issueCode) &&
                   string.Equals(issueCode.GetString(), code, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record FaultInjectionRuntimeCheck(
        bool MissingProfile,
        bool ScheduleFaults,
        bool DeliveryFaults,
        bool BundlePathFault,
        bool PackageCleanup,
        string Evidence);

    private sealed class FaultInjectionScheduleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object payload;
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/schedules")
            {
                payload = ApiResponse<ScheduleInfo[]>.Ok(Array.Empty<ScheduleInfo>());
            }
            else if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/schedules/export")
            {
                payload = ApiResponse<ScheduleData>.Fail("schedule not found");
            }
            else
            {
                payload = ApiResponse<object>.Fail($"unexpected self-check request: {request.Method} {request.RequestUri?.AbsolutePath}");
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class TraceabilityScheduleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object payload;
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/schedules")
            {
                payload = ApiResponse<ScheduleInfo[]>.Ok(new[]
                {
                    new ScheduleInfo
                    {
                        Id = 100,
                        Name = "Door Schedule",
                        Category = "Doors",
                        FieldCount = 2,
                        RowCount = 1
                    }
                });
            }
            else if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/status")
            {
                payload = ApiResponse<StatusInfo>.Ok(new StatusInfo
                {
                    RevitVersion = "2026",
                    DocumentName = "Traceability.rvt",
                    DocumentPath = "D:/models/Traceability.rvt"
                });
            }
            else if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/schedules/export")
            {
                payload = ApiResponse<ScheduleData>.Ok(new ScheduleData
                {
                    Columns = new List<string> { "Mark", "Level" },
                    Rows = new List<Dictionary<string, string>>
                    {
                        new() { ["Mark"] = "D-001", ["Level"] = "L1" }
                    },
                    TotalRows = 1
                });
            }
            else
            {
                payload = ApiResponse<object>.Fail($"unexpected self-check request: {request.Method} {request.RequestUri?.AbsolutePath}");
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static bool IsLegacyMcpHiddenAndDeprecated(out string evidence)
    {
        using var client = new RevitClient();
        var root = CliCommandCatalog.CreateRootCommand(
            client,
            new CliConfig(),
            includeInteractiveCommand: true,
            includeBatchCommand: true);
        var mcp = root.Subcommands.FirstOrDefault(command =>
            string.Equals(command.Name, "mcp", StringComparison.OrdinalIgnoreCase));
        if (mcp == null)
        {
            evidence = "Legacy MCP compatibility command is absent from the root command.";
            return false;
        }

        var serve = mcp.Subcommands.FirstOrDefault(command =>
            string.Equals(command.Name, "serve", StringComparison.OrdinalIgnoreCase));
        var isDeprecated =
            !string.IsNullOrWhiteSpace(mcp.Description) &&
            mcp.Description.Contains("deprecated", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(serve?.Description) &&
            serve.Description.Contains("deprecated", StringComparison.OrdinalIgnoreCase);
        var hidden = mcp.IsHidden && serve?.IsHidden == true;
        var publicCatalogExcluded = !CliCommandCatalog.TopLevelCommandNames
            .Contains("mcp", StringComparer.OrdinalIgnoreCase);

        evidence = hidden && isDeprecated && publicCatalogExcluded
            ? "Legacy `mcp serve` remains hidden, deprecated, and excluded from public command discovery."
            : "Legacy `mcp serve` must stay hidden, deprecated, and excluded from public command discovery.";
        return hidden && isDeprecated && publicCatalogExcluded;
    }

    private static void AddMissingCompletionValues(
        ICollection<string> issues,
        string label,
        IEnumerable<string> availableValues,
        IEnumerable<string> requiredValues)
    {
        var available = availableValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requiredValues
            .Where(value => !available.Contains(value))
            .ToArray();

        if (missing.Length > 0)
        {
            issues.Add($"{label} missing {string.Join(", ", missing)}");
        }
    }

    private static void AddUnexpectedCompletionValues(
        ICollection<string> issues,
        string label,
        IEnumerable<string> availableValues,
        IEnumerable<string> unexpectedValues)
    {
        var available = availableValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = unexpectedValues
            .Where(available.Contains)
            .ToArray();

        if (unexpected.Length > 0)
        {
            issues.Add($"{label} unexpectedly include {string.Join(", ", unexpected)}");
        }
    }

    private static WorkbenchCheckResult Check(
        string id,
        bool passed,
        string evidence,
        WorkbenchRuntimeEvidence? runtimeEvidence = null) =>
        new(id, passed ? "pass" : "fail", evidence, runtimeEvidence);

    private static async Task WriteTableAsync(TextWriter output, WorkbenchContract contract)
    {
        await output.WriteLineAsync($"RevitCli workbench contract ({contract.SchemaVersion})");
        await output.WriteLineAsync("Command      Risk         Paths  JSON  Markdown  Dry-run                         Receipt");
        await output.WriteLineAsync("-----------  -----------  -----  ----  --------  ------------------------------  -------------------------------------------");
        foreach (var command in contract.Commands)
        {
            await output.WriteLineAsync(
                $"{command.Name,-11}  {command.Risk,-11}  {command.CommandPaths.Count,-5}  {YesNo(command.SupportsJson),-4}  {YesNo(command.SupportsMarkdown),-8}  {TrimCell(command.DryRun, 30),-30}  {TrimCell(command.Receipt, 43)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Recommended first commands:");
        foreach (var command in contract.Commands)
        {
            await output.WriteLineAsync($"  {command.Name,-11} {command.RecommendedFirstCommand}");
        }
    }

    private static async Task WriteMarkdownAsync(TextWriter output, WorkbenchContract contract)
    {
        await output.WriteLineAsync("# RevitCli Workbench Contract");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{contract.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Command | Risk | JSON | Markdown | Dry-run | Receipt | First command |");
        await output.WriteLineAsync("|---|---|---:|---:|---|---|---|");
        foreach (var command in contract.Commands)
        {
            await output.WriteLineAsync(
                $"| `{command.Name}` | {command.Risk} | {YesNo(command.SupportsJson)} | {YesNo(command.SupportsMarkdown)} | {command.DryRun} | {command.Receipt} | `{command.RecommendedFirstCommand}` |");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("## Callable Command Paths");
        foreach (var command in contract.Commands)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync($"### {command.Name}");
            foreach (var path in command.CommandPaths)
                await output.WriteLineAsync($"- `revitcli {path}`");
        }
    }

    private static async Task WriteReceiptsTableAsync(TextWriter output, WorkbenchReceiptIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench receipts ({index.SchemaVersion})");
        await output.WriteLineAsync("Schema                       Command                    Path pattern");
        await output.WriteLineAsync("---------------------------  -------------------------  ------------------------------------------------");
        foreach (var receipt in index.Receipts)
        {
            await output.WriteLineAsync(
                $"{receipt.SchemaVersion,-27}  {receipt.CommandPath,-25}  {TrimCell(receipt.PathPattern, 48)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Dry-run and review commands:");
        foreach (var receipt in index.Receipts)
        {
            await output.WriteLineAsync($"  {receipt.SchemaVersion,-27} dry-run: {receipt.DryRunCommand}");
            await output.WriteLineAsync($"  {receipt.SchemaVersion,-27} review:  {receipt.ReviewCommand}");
        }
    }

    private static async Task WritePathsTableAsync(TextWriter output, WorkbenchPathIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench paths ({index.SchemaVersion})");
        await output.WriteLineAsync("Path                              Risk         JSON  Markdown  Dry-run                         Receipt");
        await output.WriteLineAsync("--------------------------------  -----------  ----  --------  ------------------------------  --------------------------------");
        foreach (var path in index.Paths)
        {
            await output.WriteLineAsync(
                $"{TrimCell(path.Path, 32),-32}  {path.Risk,-11}  {YesNo(path.SupportsJson),-4}  {YesNo(path.SupportsMarkdown),-8}  {TrimCell(path.DryRun, 30),-30}  {TrimCell(path.Receipt, 32)}");
        }
    }

    private static async Task WritePathsMarkdownAsync(TextWriter output, WorkbenchPathIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Paths");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Path | Risk | JSON | Markdown | Dry-run | Receipt | Exit notes |");
        await output.WriteLineAsync("|---|---|---:|---:|---|---|---|");
        foreach (var path in index.Paths)
        {
            await output.WriteLineAsync(
                $"| `{path.CommandLine}` | {path.Risk} | {YesNo(path.SupportsJson)} | {YesNo(path.SupportsMarkdown)} | {path.DryRun} | {path.Receipt} | {path.ExitCodeNotes} |");
        }
    }

    private static async Task WriteExitCodesTableAsync(TextWriter output, WorkbenchExitCodeIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench exit codes ({index.SchemaVersion})");
        await output.WriteLineAsync("Command      Success  Failure       Notes");
        await output.WriteLineAsync("-----------  -------  ------------  ------------------------------------------------------------");
        foreach (var command in index.Commands)
        {
            await output.WriteLineAsync(
                $"{command.Command,-11}  {string.Join("/", command.SuccessExitCodes),-7}  {TrimCell(string.Join("/", command.FailureExitCodes), 12),-12}  {TrimCell(command.Notes, 60)}");
        }
    }

    private static async Task WriteExitCodesMarkdownAsync(TextWriter output, WorkbenchExitCodeIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Exit Codes");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Command | Success | Failure | First command | Notes |");
        await output.WriteLineAsync("|---|---|---|---|---|");
        foreach (var command in index.Commands)
        {
            await output.WriteLineAsync(
                $"| `{command.Command}` | `{string.Join(", ", command.SuccessExitCodes)}` | `{string.Join(", ", command.FailureExitCodes)}` | `{command.RecommendedFirstCommand}` | {command.Notes} |");
        }
    }

    private static async Task WriteExtensionsTableAsync(TextWriter output, WorkbenchExtensionIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench extensions ({index.SchemaVersion})");
        await output.WriteLineAsync("Extension       File pattern                         Validation");
        await output.WriteLineAsync("--------------  -----------------------------------  ------------------------------------------------");
        foreach (var extension in index.Extensions)
        {
            await output.WriteLineAsync(
                $"{extension.Name,-14}  {TrimCell(extension.FilePattern, 35),-35}  {TrimCell(extension.ValidationCommand, 48)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Dry-run or preview commands:");
        foreach (var extension in index.Extensions)
        {
            await output.WriteLineAsync($"  {extension.Name,-14} {extension.DryRunCommand}");
        }
    }

    private static async Task WriteExtensionsMarkdownAsync(TextWriter output, WorkbenchExtensionIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Extensions");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Extension | File pattern | Validation | Dry-run / preview | Write behavior | Notes |");
        await output.WriteLineAsync("|---|---|---|---|---|---|");
        foreach (var extension in index.Extensions)
        {
            await output.WriteLineAsync(
                $"| `{extension.Name}` | `{extension.FilePattern}` | `{extension.ValidationCommand}` | `{extension.DryRunCommand}` | {extension.WriteBehavior} | {extension.Notes} |");
        }
    }

    private static async Task WriteOutputsTableAsync(TextWriter output, WorkbenchOutputIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench outputs ({index.SchemaVersion})");
        await output.WriteLineAsync("Name                    Command path                 Table  JSON schema                   Markdown");
        await output.WriteLineAsync("----------------------  ---------------------------  -----  ----------------------------  --------");
        foreach (var contract in index.Outputs)
        {
            await output.WriteLineAsync(
                $"{TrimCell(contract.Name, 22),-22}  {TrimCell(contract.CommandPath, 27),-27}  {YesNo(contract.SupportsTable),-5}  {TrimCell(contract.JsonSchema, 28),-28}  {YesNo(contract.SupportsMarkdown)}");
        }
    }

    private static async Task WriteOutputsMarkdownAsync(TextWriter output, WorkbenchOutputIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Outputs");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Name | Command path | Table | JSON schema | Markdown | Notes |");
        await output.WriteLineAsync("|---|---|---:|---|---:|---|");
        foreach (var contract in index.Outputs)
        {
            await output.WriteLineAsync(
                $"| `{contract.Name}` | `{contract.CommandPath}` | {YesNo(contract.SupportsTable)} | `{contract.JsonSchema}` | {YesNo(contract.SupportsMarkdown)} | {contract.Notes} |");
        }
    }

    private static async Task WriteSafeguardsTableAsync(TextWriter output, WorkbenchSafeguardIndex index)
    {
        await output.WriteLineAsync($"RevitCli workbench safeguards ({index.SchemaVersion})");
        await output.WriteLineAsync("Name                  Risk         Dry-run / preview");
        await output.WriteLineAsync("--------------------  -----------  ------------------------------------------------------------");
        foreach (var safeguard in index.Safeguards)
        {
            await output.WriteLineAsync(
                $"{TrimCell(safeguard.Name, 20),-20}  {safeguard.Risk,-11}  {TrimCell(safeguard.DryRunCommand, 60)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Approval and review:");
        foreach (var safeguard in index.Safeguards)
        {
            await output.WriteLineAsync($"  {safeguard.Name,-20} approve: {safeguard.ApprovalCommand}");
            await output.WriteLineAsync($"  {safeguard.Name,-20} review:  {safeguard.ReviewCommand}");
        }
    }

    private static async Task WriteSafeguardsMarkdownAsync(TextWriter output, WorkbenchSafeguardIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Safeguards");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Name | Path | Risk | Dry-run / preview | Approval | Receipt | Review | Notes |");
        await output.WriteLineAsync("|---|---|---|---|---|---|---|---|");
        foreach (var safeguard in index.Safeguards)
        {
            await output.WriteLineAsync(
                $"| `{safeguard.Name}` | `{safeguard.CommandPath}` | {safeguard.Risk} | `{safeguard.DryRunCommand}` | `{safeguard.ApprovalCommand}` | {safeguard.Receipt} | `{safeguard.ReviewCommand}` | {safeguard.Notes} |");
        }
    }

    private static async Task WriteProjectTableAsync(TextWriter output, WorkbenchProjectInventory inventory)
    {
        await output.WriteLineAsync($"RevitCli workbench project ({inventory.SchemaVersion})");
        await output.WriteLineAsync($"Project: {inventory.ProjectDirectory}");
        await output.WriteLineAsync($"Artifacts: {inventory.ArtifactCount}; present: {inventory.PresentCount}; missing: {inventory.MissingCount}; empty: {inventory.EmptyCount}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("Artifact            Kind       Status   Count  Path");
        await output.WriteLineAsync("------------------  ---------  -------  -----  ----------------------------------------");
        foreach (var artifact in inventory.Artifacts)
        {
            await output.WriteLineAsync(
                $"{TrimCell(artifact.Name, 18),-18}  {artifact.Kind,-9}  {artifact.Status,-7}  {artifact.Count,-5}  {TrimCell(artifact.RelativePath, 40)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Review commands:");
        foreach (var artifact in inventory.Artifacts)
        {
            await output.WriteLineAsync($"  {artifact.Name,-18} {artifact.ReviewCommand}");
        }
    }

    private static async Task WriteProjectMarkdownAsync(TextWriter output, WorkbenchProjectInventory inventory)
    {
        await output.WriteLineAsync("# RevitCli Workbench Project");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{inventory.SchemaVersion}`");
        await output.WriteLineAsync($"Project: `{inventory.ProjectDirectory}`");
        await output.WriteLineAsync($"Artifacts: `{inventory.ArtifactCount}`; present: `{inventory.PresentCount}`; missing: `{inventory.MissingCount}`; empty: `{inventory.EmptyCount}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Artifact | Kind | Status | Count | Path | Review | Notes |");
        await output.WriteLineAsync("|---|---|---|---:|---|---|---|");
        foreach (var artifact in inventory.Artifacts)
        {
            await output.WriteLineAsync(
                $"| `{artifact.Name}` | {artifact.Kind} | `{artifact.Status}` | {artifact.Count} | `{artifact.RelativePath}` | `{artifact.ReviewCommand}` | {artifact.Notes} |");
        }
    }

    private static async Task WriteHandoffTableAsync(TextWriter output, WorkbenchHandoffReport handoff)
    {
        await output.WriteLineAsync($"RevitCli workbench handoff ({handoff.SchemaVersion})");
        await output.WriteLineAsync($"Project: {handoff.ProjectDirectory}");
        await output.WriteLineAsync($"Verification: {YesNo(handoff.Success)}; checks: {handoff.CheckCount}; issues: {handoff.IssueCount}");
        await output.WriteLineAsync($"Artifacts: {handoff.ArtifactCount}; present: {handoff.PresentArtifactCount}; missing: {handoff.MissingArtifactCount}; empty: {handoff.EmptyArtifactCount}");
        await output.WriteLineAsync($"Readiness actions: {handoff.ReadinessActionCount}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("Readiness checks:");
        await output.WriteLineAsync("Status  Check");
        await output.WriteLineAsync("------  --------------------------------");
        foreach (var check in handoff.VerificationChecks)
        {
            await output.WriteLineAsync(
                $"{check.Status,-6}  {TrimCell(check.Id, 32)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Readiness actions:");
        if (handoff.ReadinessActions.Count == 0)
        {
            await output.WriteLineAsync("  (none)");
        }
        else
        {
            await output.WriteLineAsync("Phase             Artifact            Status   Working directory                    Command");
            await output.WriteLineAsync("----------------  ------------------  -------  -----------------------------------  ----------------------------------------------------------------");
            foreach (var action in handoff.ReadinessActions)
            {
                await output.WriteLineAsync(
                    $"{TrimCell(action.Phase, 16),-16}  {TrimCell(action.Artifact, 18),-18}  {action.Status,-7}  {TrimCell(action.WorkingDirectory, 35),-35}  {TrimCell(action.CommandLine, 64)}");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Phase             Working directory                    Command");
        await output.WriteLineAsync("----------------  -----------------------------------  ----------------------------------------------------------------");
        foreach (var command in handoff.Commands)
        {
            await output.WriteLineAsync(
                $"{TrimCell(command.Phase, 16),-16}  {TrimCell(command.WorkingDirectory, 35),-35}  {TrimCell(command.CommandLine, 64)}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Notes:");
        foreach (var note in handoff.Notes)
            await output.WriteLineAsync($"  - {note}");
    }

    private static async Task WriteHandoffMarkdownAsync(TextWriter output, WorkbenchHandoffReport handoff)
    {
        await output.WriteLineAsync("# RevitCli Workbench Handoff");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{handoff.SchemaVersion}`");
        await output.WriteLineAsync($"Project: `{handoff.ProjectDirectory}`");
        await output.WriteLineAsync($"Verification: `{handoff.Success.ToString().ToLowerInvariant()}`; checks: `{handoff.CheckCount}`; issues: `{handoff.IssueCount}`");
        await output.WriteLineAsync($"Artifacts: `{handoff.ArtifactCount}`; present: `{handoff.PresentArtifactCount}`; missing: `{handoff.MissingArtifactCount}`; empty: `{handoff.EmptyArtifactCount}`");
        await output.WriteLineAsync($"Readiness actions: `{handoff.ReadinessActionCount}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("## Verification Checks");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Status | Check | Evidence |");
        await output.WriteLineAsync("|---|---|---|");
        foreach (var check in handoff.VerificationChecks)
        {
            await output.WriteLineAsync(
                $"| `{check.Status}` | `{check.Id}` | {check.Evidence} |");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("## Readiness Actions");
        await output.WriteLineAsync();
        if (handoff.ReadinessActions.Count == 0)
        {
            await output.WriteLineAsync("- None.");
        }
        else
        {
            await output.WriteLineAsync("| Phase | Artifact | Status | Command | Working directory | Reason |");
            await output.WriteLineAsync("|---|---|---|---|---|---|");
            foreach (var action in handoff.ReadinessActions)
            {
                await output.WriteLineAsync(
                    $"| `{action.Phase}` | `{action.Artifact}` | `{action.Status}` | `{action.CommandLine}` | `{action.WorkingDirectory}` | {action.Reason} |");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("## Commands");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Phase | Command | Working directory | Purpose |");
        await output.WriteLineAsync("|---|---|---|---|");
        foreach (var command in handoff.Commands)
        {
            await output.WriteLineAsync(
                $"| `{command.Phase}` | `{command.CommandLine}` | `{command.WorkingDirectory}` | {command.Purpose} |");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("## Notes");
        foreach (var note in handoff.Notes)
            await output.WriteLineAsync($"- {note}");
    }

    private static async Task WriteReceiptsMarkdownAsync(TextWriter output, WorkbenchReceiptIndex index)
    {
        await output.WriteLineAsync("# RevitCli Workbench Receipts");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{index.SchemaVersion}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Schema | Action | Command | Writes on | Path pattern | Dry-run | Review |");
        await output.WriteLineAsync("|---|---|---|---|---|---|---|");
        foreach (var receipt in index.Receipts)
        {
            await output.WriteLineAsync(
                $"| `{receipt.SchemaVersion}` | `{receipt.Action}` | `{receipt.CommandPath}` | {receipt.WritesOn} | `{receipt.PathPattern}` | `{receipt.DryRunCommand}` | `{receipt.ReviewCommand}` |");
        }
    }

    private static async Task WriteVerificationTableAsync(TextWriter output, WorkbenchVerification verification)
    {
        await output.WriteLineAsync($"RevitCli workbench verification ({verification.SchemaVersion})");
        await output.WriteLineAsync($"Project: {verification.ProjectDirectory}");
        await output.WriteLineAsync($"Success: {YesNo(verification.Success)}; checks: {verification.CheckCount}; issues: {verification.IssueCount}");
        await output.WriteLineAsync();
        await output.WriteLineAsync("Status  Check                             Evidence");
        await output.WriteLineAsync("------  --------------------------------  ------------------------------------------------------------");
        foreach (var check in verification.Checks)
        {
            await output.WriteLineAsync(
                $"{check.Status,-6}  {check.Id,-32}  {TrimCell(check.Evidence, 60)}");
        }
    }

    private static async Task WriteVerificationMarkdownAsync(TextWriter output, WorkbenchVerification verification)
    {
        await output.WriteLineAsync("# RevitCli Workbench Verification");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"Schema: `{verification.SchemaVersion}`");
        await output.WriteLineAsync($"Project: `{verification.ProjectDirectory}`");
        await output.WriteLineAsync($"Success: `{verification.Success.ToString().ToLowerInvariant()}`");
        await output.WriteLineAsync($"Checks: `{verification.CheckCount}`; issues: `{verification.IssueCount}`");
        await output.WriteLineAsync();
        await output.WriteLineAsync("| Status | Check | Evidence |");
        await output.WriteLineAsync("|---|---|---|");
        foreach (var check in verification.Checks)
        {
            await output.WriteLineAsync(
                $"| `{check.Status}` | `{check.Id}` | {check.Evidence} |");
        }
    }

    private static IReadOnlyList<string> GetCommandPaths(string name) =>
        name.ToLowerInvariant() switch
        {
            "status" => new[] { "status" },
            "doctor" => new[] { "doctor" },
            "inspect" => new[] { "inspect categories", "inspect params", "inspect schedules", "inspect sheets", "inspect workflows", "inspect plans" },
            "query" => new[] { "query" },
            "examples" => new[] { "examples", "examples workbench", "examples workflow", "examples recipes" },
            "workbench" => new[]
            {
                "workbench contract",
                "workbench contract --contract workbench-contract.v2",
                "workbench verify",
                "workbench verify --contract workbench-contract.v2",
                "workbench receipts",
                "workbench paths",
                "workbench exits",
                "workbench extensions",
                "workbench outputs",
                "workbench safeguards",
                "workbench project",
                "workbench handoff"
            },
            "release" => new[] { "release verify", "release verify --strict" },
            "check" => new[] { "check" },
            "score" => new[] { "score", "score --history" },
            "sheets" => new[] { "sheets verify", "sheets issue-meta", "sheets renumber", "sheets index init", "sheets index show" },
            "rooms" => new[] { "rooms renumber" },
            "marks" => new[] { "marks assign", "marks verify" },
            "schedules" => new[] { "schedules ensure", "schedules batch-export", "schedules compare" },
            "views" => new[] { "views audit", "views template-apply", "views clone-set" },
            "links" => new[] { "links audit", "links repair" },
            "model" => new[] { "model map-check", "model map-fix" },
            "schedule" => new[] { "schedule list", "schedule export", "schedule create" },
            "export" => new[] { "export" },
            "publish" => new[] { "publish" },
            "set" => new[] { "set" },
            "import" => new[] { "import" },
            "plan" => new[] { "plan show", "plan apply" },
            "rollback" => new[] { "rollback" },
            "workflow" => new[]
            {
                "workflow init",
                "workflow validate",
                "workflow simulate",
                "workflow review",
                "workflow registry",
                "workflow run",
                "workflow suggest",
                "workflow examples",
                "workflow receipts"
            },
            "deliverables" => new[] { "deliverables list", "deliverables stats", "deliverables verify", "deliverables plan", "deliverables bundle" },
            "issue" => new[] { "issue preflight", "issue diff", "issue package" },
            "standards" => new[] { "standards install", "standards validate" },
            "family" => new[] { "family ls", "family validate", "family purge", "family export" },
            "history" => new[] { "history capture", "history list", "history prune", "history diff", "history trend" },
            "journal" => new[] { "journal show", "journal stats", "journal review", "journal sign", "journal verify" },
            "report" => new[] { "report weekly", "report knowledge" },
            "ledger" => new[] { "ledger append", "ledger replay", "ledger query", "ledger validate", "ledger stats", "ledger timeline", "ledger analytics" },
            _ => new[] { name }
        };

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string TrimCell(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + ".";

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/');

    private static bool TryNormalizeWorkbenchContract(
        string? contractSchema,
        TextWriter output,
        out string normalizedContract,
        out string verificationSchema)
    {
        normalizedContract = string.IsNullOrWhiteSpace(contractSchema)
            ? "workbench-contract.v1"
            : contractSchema.Trim();
        verificationSchema = "workbench-verification.v1";

        if (string.Equals(normalizedContract, "workbench-contract.v1", StringComparison.OrdinalIgnoreCase))
        {
            normalizedContract = "workbench-contract.v1";
            return true;
        }

        if (string.Equals(normalizedContract, "workbench-contract.v2", StringComparison.OrdinalIgnoreCase))
        {
            normalizedContract = "workbench-contract.v2";
            verificationSchema = "workbench-verify-report.v2";
            return true;
        }

        output.WriteLine("Error: --contract must be 'workbench-contract.v1' or 'workbench-contract.v2'.");
        return false;
    }

    private static string ProjectDirOption(string projectDirectory)
    {
        var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (string.Equals(projectDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        // A single commandLine must stay safe in both PowerShell and POSIX shells.
        // Paths containing apostrophes cannot be represented portably; use workingDirectory instead.
        if (projectDirectory.Contains('\''))
            return string.Empty;

        return $" --dir {QuoteArgument(projectDirectory)}";
    }

    private static string QuoteArgument(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    public sealed record WorkbenchContract(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchCommandContract> Commands);

    public sealed record WorkbenchReceiptIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchReceiptContract> Receipts)
    {
        public int ReceiptCount => Receipts.Count;
    }

    public sealed record WorkbenchReceiptContract(
        string SchemaVersion,
        string Action,
        string CommandPath,
        string WritesOn,
        string PathPattern,
        string DryRunCommand,
        string ReviewCommand);

    public sealed record WorkbenchPathIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchCallablePath> Paths)
    {
        public int PathCount => Paths.Count;
    }

    public sealed record WorkbenchExitCodeIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchExitCodeContract> Commands)
    {
        public int CommandCount => Commands.Count;
    }

    public sealed record WorkbenchExitCodeContract(
        string Command,
        IReadOnlyList<string> CommandPaths,
        string Risk,
        IReadOnlyList<string> SuccessExitCodes,
        IReadOnlyList<string> FailureExitCodes,
        string Notes,
        string RecommendedFirstCommand);

    public sealed record WorkbenchExtensionIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchExtensionPoint> Extensions)
    {
        public int ExtensionCount => Extensions.Count;
    }

    public sealed record WorkbenchExtensionPoint(
        string Name,
        string Purpose,
        string FilePattern,
        string ValidationCommand,
        string DryRunCommand,
        string WriteBehavior,
        string Notes);

    public sealed record WorkbenchOutputIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchOutputContract> Outputs)
    {
        public int OutputCount => Outputs.Count;
    }

    public sealed record WorkbenchOutputContract(
        string Name,
        string CommandPath,
        bool SupportsTable,
        string JsonSchema,
        bool SupportsMarkdown,
        string Notes);

    public sealed record WorkbenchSafeguardIndex(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Product,
        string Purpose,
        IReadOnlyList<WorkbenchSafeguardContract> Safeguards)
    {
        public int SafeguardCount => Safeguards.Count;
    }

    public sealed record WorkbenchSafeguardContract(
        string Name,
        string CommandPath,
        string Risk,
        string DryRunCommand,
        string ApprovalCommand,
        string Receipt,
        string ReviewCommand,
        string Notes);

    public sealed record WorkbenchProjectInventory(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string ProjectDirectory,
        IReadOnlyList<WorkbenchProjectArtifact> Artifacts)
    {
        public int ArtifactCount => Artifacts.Count;

        public int PresentCount => Artifacts.Count(artifact =>
            string.Equals(artifact.Status, "present", StringComparison.OrdinalIgnoreCase));

        public int MissingCount => Artifacts.Count(artifact =>
            string.Equals(artifact.Status, "missing", StringComparison.OrdinalIgnoreCase));

        public int EmptyCount => Artifacts.Count(artifact =>
            string.Equals(artifact.Status, "empty", StringComparison.OrdinalIgnoreCase));
    }

    public sealed record WorkbenchProjectArtifact(
        string Name,
        string Kind,
        string RelativePath,
        string Status,
        int Count,
        string ReviewCommand,
        string Notes);

    public sealed record WorkbenchHandoffReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string ProjectDirectory,
        bool Success,
        int CheckCount,
        int IssueCount,
        int ArtifactCount,
        int PresentArtifactCount,
        int MissingArtifactCount,
        int EmptyArtifactCount,
        IReadOnlyList<WorkbenchCheckResult> VerificationChecks,
        IReadOnlyList<WorkbenchReadinessAction> ReadinessActions,
        IReadOnlyList<WorkbenchHandoffCommand> Commands,
        IReadOnlyList<string> Notes)
    {
        public int ReadinessActionCount => ReadinessActions.Count;

        public int CommandCount => Commands.Count;
    }

    public sealed record WorkbenchReadinessAction(
        string Phase,
        string Artifact,
        string Status,
        string CommandLine,
        string WorkingDirectory,
        string Reason);

    public sealed record WorkbenchHandoffCommand(
        string Phase,
        string CommandLine,
        string WorkingDirectory,
        string Purpose);

    public sealed record WorkbenchCallablePath(
        string Path,
        string CommandLine,
        string Command,
        string Risk,
        bool SupportsJson,
        bool SupportsMarkdown,
        string DryRun,
        string Receipt,
        string RecommendedFirstCommand,
        string ExitCodeNotes);

    public sealed record WorkbenchCommandContract(
        string Name,
        string Purpose,
        string Risk,
        bool SupportsJson,
        bool SupportsMarkdown,
        string DryRun,
        string Receipt,
        string RecommendedFirstCommand,
        string ExitCodeNotes)
    {
        public IReadOnlyList<string> CommandPaths => GetCommandPaths(Name);
    }

    public sealed record WorkbenchVerification(
        string SchemaVersion,
        string ContractSchema,
        DateTimeOffset GeneratedAt,
        string ProjectDirectory,
        bool Success,
        int CheckCount,
        int IssueCount,
        int CommandCount,
        int ReceiptContractCount,
        int RecipeTopicCount,
        IReadOnlyList<WorkbenchCheckResult> Checks);

    public sealed record WorkbenchCheckResult(
        string Id,
        string Status,
        string Evidence,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        WorkbenchRuntimeEvidence? RuntimeEvidence = null);

    public sealed record WorkbenchRuntimeEvidence(
        bool CommandSpine,
        bool CommandSpineOutputParity,
        bool CommandSpineNoWrites,
        bool WorkflowRegistry,
        bool LedgerAppend,
        bool LedgerQueryValidate,
        bool LedgerStats,
        bool LedgerTimeline,
        bool LedgerAnalytics,
        bool LedgerReplay,
        bool StandardsValidate,
        bool IssuePreflight,
        bool IssuePackageDryRun,
        bool DeliverablesVerify,
        bool JournalVerify,
        bool HistoryList,
        bool HistoryListCountConsistency,
        bool HistoryListRowOrder,
        bool RollbackDryRun,
        bool RollbackDryRunPreview,
        bool RollbackNoMutatingSetRequest,
        WorkbenchHistoryListEvidence HistoryListEvidence,
        WorkbenchRollbackDryRunEvidence RollbackDryRunEvidence,
        bool AllRuntimeChecksPass);
}
