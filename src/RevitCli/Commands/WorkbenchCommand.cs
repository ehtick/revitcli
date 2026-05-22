using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using RevitCli.Client;
using RevitCli.Config;
using RevitCli.Output;
using RevitCli.Plans;
using RevitCli.Workflows;

namespace RevitCli.Commands;

public static class WorkbenchCommand
{
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
            "Validate, simulate, run, and review reusable terminal workflows.",
            "mixed",
            SupportsJson: true,
            SupportsMarkdown: true,
            "required before real workflow runs",
            ".revitcli/workflows/receipts/*.json",
            "revitcli workflow review .revitcli/workflows/pre-issue.yml --output json",
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
            "revitcli standards install ../office-standards --dry-run --output markdown",
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
            "0 when local report generation succeeds; 1 for invalid paths or output options.")
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
            "standards/manifest.yml",
            "revitcli standards validate --manifest standards/manifest.yml --output json",
            "revitcli standards install ../office-standards --dry-run --output markdown",
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
            "v5 issue-closure readiness checks for callable paths, package traceability, hidden mutation gates, and dashboard optionality."),
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
            "Reusable local project knowledge from history, journal, workflow, delivery, standards, and reports.")
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
            "revitcli standards install ../office-standards --dry-run --output markdown",
            "revitcli standards install ../office-standards --force",
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

    private static IReadOnlyList<WorkbenchHandoffCommand> CreateHandoffCommands(string projectDirectory) =>
        new[]
        {
            new WorkbenchHandoffCommand(
                "verify",
                $"revitcli workbench verify{ProjectDirOption(projectDirectory)} --output json",
                "Confirm the local command contract, output schemas, safeguards, receipts, and non-goals."),
            new WorkbenchHandoffCommand(
                "project",
                $"revitcli workbench project{ProjectDirOption(projectDirectory)} --output json",
                "Inspect local profiles, standards, workflows, receipts, history, journal, deliveries, plans, and reports."),
            new WorkbenchHandoffCommand(
                "paths",
                "revitcli workbench paths --output json",
                "Choose concrete callable command paths without scraping help text."),
            new WorkbenchHandoffCommand(
                "receipts",
                "revitcli workbench receipts --output json",
                "Check which write/export paths produce reviewable receipts."),
            new WorkbenchHandoffCommand(
                "safeguards",
                "revitcli workbench safeguards --output json",
                "Review dry-run, approval, receipt, and review commands for risky paths."),
            new WorkbenchHandoffCommand(
                "schedule-create",
                "revitcli schedule create --category Doors --fields \"Mark,Level\" --name \"Door Review\" --dry-run --output json",
                "Preview ViewSchedule writes through the schedule-create.v1 contract before any real create."),
            new WorkbenchHandoffCommand(
                "outputs",
                "revitcli workbench outputs --output json",
                "See the table, JSON schema, and Markdown output contracts available to scripts."),
            new WorkbenchHandoffCommand(
                "examples",
                "revitcli examples workbench --output markdown",
                "Open copy-paste workbench recipes for recurring architect tasks."),
            new WorkbenchHandoffCommand(
                "workflow-discovery",
                $"revitcli inspect workflows{ProjectDirOption(projectDirectory)} --output markdown",
                "Discover local workflow YAML files and next validate/simulate/review/dry-run/receipt commands."),
            new WorkbenchHandoffCommand(
                "plan-discovery",
                $"revitcli inspect plans{ProjectDirOption(projectDirectory)} --output markdown",
                "Discover saved mutation plans and next show/dry-run/apply/rollback-preview commands."),
            new WorkbenchHandoffCommand(
                "workflow-review",
                $"revitcli workflow review .revitcli/workflows/pre-issue.yml{ProjectDirOption(projectDirectory)} --output markdown",
                "Review approval gates and acceptance evidence before any workflow run.")
        };

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
                        $"revitcli history init --dir {QuoteArgument(Path.Combine(projectDirectory, ".revitcli", "history"))}",
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
            "report knowledge"
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
            "workflow-receipts.v1",
            "example-recipes.v1",
            "model-health-history.v1",
            "knowledge-report.v1"
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
            new[] { "validate", "simulate", "review", "run", "suggest", "examples", "receipts" });
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
        yield return Check(
            "completion-surface",
            completionIssues.Count == 0,
            completionIssues.Count == 0
                ? "Shell completions cover inspect, workbench, workflow, schedules, views, links, model, schedule, rooms, marks, and issue subcommands, options, and output-format contracts."
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

        var scheduleExportTraceableReady =
            commandPaths.Contains("schedules batch-export") &&
            outputSchemas.Contains("schedule-export-manifest.v1") &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.ScheduleId)) != null &&
            typeof(SchedulesCommand.ScheduleExportManifestEntry).GetProperty(nameof(SchedulesCommand.ScheduleExportManifestEntry.OutputPath)) != null;
        yield return Check(
            "schedule-export-traceable",
            scheduleExportTraceableReady,
            scheduleExportTraceableReady
                ? "Schedule batch exports write schedule-export-manifest.v1 entries with schedule ids and CSV paths."
                : "Schedule batch export is missing callable path, manifest schema, schedule id, or output path evidence.");

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

        var linkRepairNoCoordinateMove =
            commandPaths.Contains("links repair") &&
            outputSchemas.Contains("link-repair-plan.v1") &&
            safeguardNames.Contains("links-repair") &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewPath)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.OldLoaded)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty(nameof(LinksCommand.LinkRepairAction.NewLoaded)) != null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty("TransformFingerprint") == null &&
            typeof(LinksCommand.LinkRepairAction).GetProperty("Coordinate") == null;
        yield return Check(
            "linkRepairNoCoordinateMove",
            linkRepairNoCoordinateMove,
            linkRepairNoCoordinateMove
                ? "Link repair plans expose only old/new path and load-state changes; no coordinate move fields are present."
                : "Link repair planning is missing callable path, schema, safeguard, path/load fields, or it exposes coordinate mutation fields.");

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
            typeof(IssueCommand.IssuePackageFile).GetProperty(nameof(IssueCommand.IssuePackageFile.SourcePath)) != null;
        yield return Check(
            "issuePackageTraceability",
            issuePackageTraceability,
            issuePackageTraceability
                ? "Issue package receipts trace manifest, child files, bundle hash, and optional journal signature evidence."
                : "Issue package is missing receipt schema, safeguard, manifest, bundle hash, journal signature, or file trace fields.");

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
                ? "Sheet plan receipts expose rollback actions, affected ids, and model/document context."
                : "Sheet plan receipts are missing rollback actions, affected ids, model context, or review commands.");

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

    private static WorkbenchCheckResult Check(string id, bool passed, string evidence) =>
        new(id, passed ? "pass" : "fail", evidence);

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
            await output.WriteLineAsync("Phase             Artifact            Status   Command");
            await output.WriteLineAsync("----------------  ------------------  -------  ----------------------------------------------------------------");
            foreach (var action in handoff.ReadinessActions)
            {
                await output.WriteLineAsync(
                    $"{TrimCell(action.Phase, 16),-16}  {TrimCell(action.Artifact, 18),-18}  {action.Status,-7}  {TrimCell(action.CommandLine, 64)}");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync("Phase             Command");
        await output.WriteLineAsync("----------------  ----------------------------------------------------------------");
        foreach (var command in handoff.Commands)
        {
            await output.WriteLineAsync(
                $"{TrimCell(command.Phase, 16),-16}  {TrimCell(command.CommandLine, 64)}");
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
        await output.WriteLineAsync("| Phase | Command | Purpose |");
        await output.WriteLineAsync("|---|---|---|");
        foreach (var command in handoff.Commands)
        {
            await output.WriteLineAsync(
                $"| `{command.Phase}` | `{command.CommandLine}` | {command.Purpose} |");
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
        return string.Equals(projectDirectory, currentDirectory, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" --dir {QuoteArgument(projectDirectory)}";
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
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
        string Evidence);
}
