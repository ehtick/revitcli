# Sheet Frame Verify Design

> Spec date: 2026-05-17
> Track: S — Sheet/View Factory (post-v4.0 extension track)
> Slice: S4 — Sheet Frame Verify (first of four S sub-slices)
> Status: first CLI-only slice implemented; deeper view inventory checks deferred

## Summary

S4 v1 introduces a read-only verification command for project sheet frame state:

```bash
revitcli sheets verify
revitcli sheets verify --against .revitcli/sheets/index.yml
revitcli sheets verify --rule numbering.gap --output json
revitcli sheets index init
revitcli sheets index show
```

It checks numbering schemes, required-sheet declarations, and view-to-sheet linkage against an optional `.revitcli/sheets/index.yml` expectation file, and emits structured reports for both architects and Codex CLI.

S4 v1 is intentionally pure read-only. Numbering repair plans, Sheet List schedule writeback, and Pre-Issue integration are deferred to S4 v2 and adjacent tracks.

## Confirmed Decisions

| Decision | Outcome | Reason |
|---|---|---|
| Track entry slice | S4 first (Sheet Frame Verify) | Lowest write risk; establishes "expectation vs reality" semantics that S3 will reuse. |
| MVP scope | Plan B — read-only verify + `.yml` expectation diff | Pure read-only is too thin vs `inspect sheets`; the yml diff is the real innovation. |
| Write boundary | No Revit writes in v1 | Only writes `.revitcli/sheets/index.yml` during `index init`. |
| Title-block reuse | Delegate to existing `inspect sheets` rule pack | Avoid duplicating v2.3 work. |
| Revision-cloud checks | Out of scope | Belongs to P (Pre-Issue Pack) track. |
| Sheet creation | Out of scope | Belongs to S3 (Sheet & View Factory). |
| Numbering repair plan | Out of scope (v2) | First prove read-only semantics; then layer v2.4 plan/receipt. |
| Numbering scheme syntax | Token form `A-{floor:01}{seq:02}` | Token form is reversible (parse + generate); regex is one-way. |
| Rule naming style | Dotted namespace, e.g., `numbering.gap` | Aligns with how rule packs already group concerns. |
| Single-rule crash behavior | Degrade to warning, continue other rules | Verify is diagnostic; one crashed rule should not blind the rest. |
| Built-in defaults when no yml | `numbering.duplicate` + `linkage.orphan` only | Most conservative; anything else needs an explicit declaration. |
| Codex CLI auto-suggest repair | No in v1 | Verify reports; user decides next action. S4 v2 may relax. |
| Test mix | ~60% CLI unit, ~30% rule unit, ~10% Add-in/Revit smoke | Most logic is yaml + rule evaluation; Revit surface is narrow. |
| S3 coupling | Pre-declare `sheets create --from-frame` as future contract | Lets the index.yml schema absorb S3 needs without breaking v1. |

## Goals

- Verify sheet frame state against an optional `.revitcli/sheets/index.yml` expectation file.
- Detect numbering gaps, duplicates, scheme violations, and out-of-range numbers.
- Detect missing required sheets and required-view-on-sheet violations.
- Detect orphan views, overloaded sheets, and drafting-only sheets.
- Delegate title-block field consistency to the existing `inspect sheets` rule pack.
- Emit table, JSON, and Markdown output; stable JSON schema for Codex CLI parsing.
- Provide stable exit codes (`0`/`2`/`3`/`1`/`4`) for shell pipelines.
- Provide `sheets index init` to bootstrap an `.revitcli/sheets/index.yml` from current model state.
- Provide `sheets index show` to render the declared expectation back to the terminal.
- Cover failure paths first per RevitCli convention: parse errors, unknown rules, missing model, degraded delegate, large models.

## Non-Goals

- No Revit writes in v1 (no renumber, no Sheet List schedule mutation, no sheet creation).
- No revision cloud / revision schedule consistency checks (P track owns this).
- No geometric reasoning (axis grids, building outlines, room geometry) — that is S2.
- No automatic repair suggestion or autonomous Codex execution path.
- No DWG / IFC / PDF export-side verification; `publish` track owns that.
- No remote dashboard reporting in v1; terminal and JSON only.

## Architecture and Boundaries

### Position in command hierarchy

- New command group `revitcli sheets`.
- Sub-commands: `sheets verify`, `sheets index init`, `sheets index show`.
- Lives under the v3.0 standards extension track; shares `.revitcli/*.yml` configuration convention.

### Relationship to existing commands

| Existing command | Relationship |
|---|---|
| `inspect sheets` (v2.3) | S4 reuses its underlying sheet/view query; does not re-implement metadata browsing. |
| `standards validate / install` (v3.0) | Shares the `.revitcli/*.yml` convention; sheet index can be packaged inside a standards pack. |
| `check` (Pre-Issue Pack, future P track) | `check issue` will be able to include `sheets verify` as a sub-check in S4 v2 timeframe. |
| `publish` (v2.2) | Verify can be invoked as a pre-publish gate; verify can also explain a failed publish. |

### Hard boundaries (out of v1)

- No writes to Revit Sheet List schedule.
- No sheet creation, sheet deletion, or sheet renumbering.
- No deep title-block field analysis (delegated).
- No revision cloud / revision schedule analysis (P track).
- No autonomous repair suggestions returned in the report.

## Command Matrix

| Command | Type | Input | Output |
|---|---|---|---|
| `revitcli sheets verify` | read-only | active model + optional `./.revitcli/sheets/index.yml` | default table; honors `--issues-only` |
| `revitcli sheets verify --against <file>` | read-only | explicit yaml path | same |
| `revitcli sheets verify --rule <name>` | read-only | single rule by canonical name | same |
| `revitcli sheets verify --issues-only` | read-only | model | only Warning/Error rows |
| `revitcli sheets verify --output {table\|json\|markdown}` | read-only | model | tri-format; JSON is the Codex contract |
| `revitcli sheets index init` | write local yml only | model current state | seeds `./.revitcli/sheets/index.yml` |
| `revitcli sheets index show` | read-only | yml | renders declared expectation in table form |

### Exit-code contract

| Code | Meaning |
|---|---|
| 0 | Verify passed (no issues, or only Info-level findings). |
| 2 | Warning-level issues only (orphan views, overload, drafting-only). |
| 3 | Error-level issues present (gaps, duplicates, missing required sheets). |
| 1 | Command-line / configuration error (bad args, parse error, unknown rule). |
| 4 | Model unavailable (Add-in not loaded, Revit not running, connection timeout). |

## Rule Engine Design

### Rule families (canonical names)

| Family | Rule | Trigger | Default severity |
|---|---|---|---|
| **numbering** | `numbering.scheme` | sheet number does not match declared scheme | error |
| | `numbering.gap` | scheme-derived expected number missing | error |
| | `numbering.duplicate` | same number appears on ≥2 sheets | error |
| | `numbering.outOfRange` | number outside allowed `seqMin`..`seqMax` | warning |
| **required** | `required.missing` | sheet pattern declared in `required[]` has no match | error |
| | `required.viewMissing` | sheet exists but lacks declared view types | error |
| **linkage** | `linkage.orphan` | view not on any sheet and not in ignore list | warning |
| | `linkage.overloaded` | view count on a sheet exceeds threshold | warning |
| | `linkage.draftingOnly` | sheet contains only drafting views | info |
| **delegate** | `delegate.titleBlock` | delegates to existing `inspect sheets` rule pack | warning |

### `.revitcli/sheets/index.yml` schema (v1, `schemaVersion: 1`)

```yaml
name: project-sheet-frame
schemaVersion: 1

numbering:
  scheme: "A-{floor:01}{seq:02}"
  ranges:
    - floors: [1, 2, 3, 4, 5, 6, 7]
      seqMin: 1
      seqMax: 20
  allowedPrefixes: ["A-"]

required:
  - pattern: "A-*01"
    description: "每层平面总图"
    needsViews:
      - viewType: FloorPlan
        minCount: 1
  - pattern: "A-*02"
    needsViews: [CeilingPlan]

linkage:
  ignoreOrphanViews: ["WorkingView/*", "{3D}", "*_QA"]
  overloadThreshold: 6

severities:
  linkage.orphan: warning
  numbering.outOfRange: error

delegate:
  titleBlock:
    inspectSheetsRulePack: default
```

### Rule contract (C# sketch)

```csharp
interface ISheetRule {
    string Name { get; }
    Severity DefaultSeverity { get; }
    IEnumerable<SheetIssue> Run(SheetContext ctx);
}

class SheetIssue {
    string RuleName;
    Severity Severity;        // Error | Warning | Info
    string Message;
    List<string> AffectedSheetIds;
    List<string> AffectedViewIds;
    Dictionary<string, object> Details;
}
```

### Verify JSON output shape

```json
{
  "command": "sheets verify",
  "schemaVersion": 1,
  "configSource": ".revitcli/sheets/index.yml",
  "summary": {
    "totalSheets": 30,
    "totalViews": 184,
    "issuesByRule": { "numbering.gap": 2, "required.missing": 3, "linkage.orphan": 8 },
    "disabledRules": [],
    "degradedRules": [],
    "exitCode": 3
  },
  "issues": [
    {
      "rule": "numbering.gap",
      "severity": "error",
      "message": "A-103 missing between A-102 and A-104",
      "affectedSheetIds": [],
      "affectedViewIds": [],
      "details": { "expectedNumber": "A-103", "floor": 1, "seq": 3 }
    }
  ]
}
```

## Data Flow

```
[CLI args]
   │
   ▼
[1. Parse args] ─── --against / --rule / --issues-only / --output
   │
   ▼
[2. Load index.yml]
   Lookup order:
     (a) --against <path>
     (b) ./.revitcli/sheets/index.yml
     (c) standards-pack-embedded index.yml (if installed)
     (d) none → built-in default rule set
   │
   ▼
[3. Fetch SheetContext via Add-in]
   Reuses inspect-sheets underlying query: sheets, views, view-on-sheet,
   optional title-block parameters.
   │
   ▼
[4. Rule engine execution]
   For each enabled rule → collect SheetIssue list.
   Delegated rule (titleBlock) → call inspect-sheets rule pack.
   │
   ▼
[5. Report generation]
   Formatter: TableRenderer | JsonRenderer | MarkdownRenderer
   exitCode = max(severity → exit code mapping)
```

### Key data shapes

```csharp
class SheetContext {
    IReadOnlyList<SheetSnapshot> Sheets;
    IReadOnlyList<ViewSnapshot> Views;
    IReadOnlyDictionary<string, IReadOnlyList<string>> ViewsOnSheets;
    IndexConfig Config;
}

class SheetSnapshot {
    string Id;
    string Number;              // "A-101"
    string Name;
    string TitleBlockFamily;
    int? FloorLevel;            // parsed from {floor} token
    int? Seq;                   // parsed from {seq} token
}
```

### Edge cases

| Situation | Behavior |
|---|---|
| `index.yml` not found and no `--against` | Use built-in default rules; report `configSource = "(builtin defaults)"` |
| `index.yml` parse error | Exit 1; error includes line/column. |
| `index.yml` references unknown rule | Exit 1; list available rule names. |
| `--rule <unknown>` | Exit 1; list available rule names. |
| Model has no sheets | Exit 0; `summary.totalSheets = 0`. |
| Single rule throws | Degrade rule to warning; list it in `summary.degradedRules`; continue. |
| Delegate (title block) throws | Same degrade path. |
| Model >500 sheets | Stream-process internally; no progress bar to avoid noisy stdout. |
| Scheme token unparseable for a sheet | That sheet skipped for `numbering.*` rules; still evaluated by `required.*` and `linkage.*` normally. |
| Rule disabled in yml | Skip; list in `summary.disabledRules`. |

## Failure Paths and Codex CLI Contract

### Full failure matrix

| Failure | Behavior | Exit | Parsable code |
|---|---|---|---|
| Add-in not loaded / Revit not running | Immediate error; no hang | 4 | `error.code = addin_unavailable` |
| Connection timeout | Retry once, then fail | 4 | `error.code = connection_timeout` |
| `--against <path>` not found | Immediate error | 1 | `error.code = config_missing` |
| `index.yml` parse error | Report with line/column | 1 | `error.code = config_parse` |
| `index.yml` unknown rule reference | Report + list available | 1 | `error.code = config_unknown_rule` |
| `--rule <unknown>` | Same | 1 | same |
| Scheme token invalid | Report | 1 | `error.code = scheme_invalid` |
| Single rule crashes | Degrade to warning, continue | max of remaining | listed in `summary.degradedRules` |
| Delegated rule crashes | Degrade to warning | same | same |
| Model has no sheets | Normal completion | 0 | `summary.totalSheets = 0` |
| `index.yml` missing, no `--against` | Use builtin defaults; one-line notice | per rule results | `configSource = "(builtin defaults)"` |

### Codex CLI conversation patterns

**Pattern 1 — pre-issue health check**

```
Architect: "出图前帮我看看图纸框架有没有问题"
Codex CLI: revitcli sheets verify --output json --issues-only
Codex parses JSON, summarizes in Chinese:
  • 编号缺号 2 处：A-103、A-207
  • 必需图纸缺失 3 张
  • 孤立视图 8 个（warning）
Codex does NOT auto-repair; reports only.
```

**Pattern 2 — baseline a fresh project**

```
Architect: "当前图纸框架记成基线"
Codex CLI: revitcli sheets index init
Codex cats the resulting .revitcli/sheets/index.yml for review.
Codex does NOT edit the yml; architect re-runs init or edits by hand.
```

**Pattern 3 — single-point investigation**

```
Architect: "为啥 A-103 报缺号"
Codex CLI: revitcli sheets verify --rule numbering.gap --output json
Codex parses scheme expectation and reports the gap.
```

### Hard contract for Codex CLI

- Every command supports `--output json` with versioned `schemaVersion`.
- Error messages are UTF-8 plain text; no ANSI color codes; no emoji.
- `--issues-only` available wherever passing-row noise would dilute Codex context.
- Any write step (currently only `sheets index init` writes a local yml) MUST print the target path before writing.
- No hidden network calls, no telemetry, no auto-update during verify.

## Testing Strategy

### Three layers

| Layer | Scope | Share | Revit required |
|---|---|---|---|
| **CLI unit** | Arg parsing, exit codes, formatter output, yaml load, scheme parsing, severity override | ~60% | No |
| **Rule unit** | Each rule's happy path + edges, with a fake `SheetContext` | ~30% | No |
| **Add-in integration + Revit smoke** | Real `SheetContext` fetch, delegate interop, end-to-end against a 5-sheet fixture model | ~10% | Yes |

### Required failure-path coverage

```
null .yml                 empty .yml                yaml syntax error
unknown rule name         scheme unparseable        scheme special chars
500+ sheet model          Chinese sheet names       Chinese sheet number prefix
delegate failure          connection timeout        Add-in not loaded
empty model               single sheet              --rule + --issues-only combo
--against and ./.revitcli/sheets/index.yml both present (--against wins)
```

### Scheme token round-trip

Each scheme must satisfy:

```
parse("A-101", "A-{floor:01}{seq:02}")     → { floor: 1, seq: 1 }
generate({ floor: 1, seq: 3 }, scheme)     → "A-103"
round_trip(generate(parse(x, s), s)) == x
```

Property-based tests cover random `(floor, seq)` pairs across declared ranges.

### Revit smoke admission

Smallest acceptance fixture: 5 sheets, ~20 views, 1 title-block family. The following must all pass on Revit 2026 (and where available, 2024/2025):

```bash
revitcli sheets verify
revitcli sheets verify --against tests/fixtures/index.yml
revitcli sheets index init --output stdout
revitcli sheets verify --rule numbering.gap --output json
```

All four passing → S4 v1 admissible for release.

## v2 Evolution Path

S4 v1 is read-only. Four independent expansion directions, none blocking the others:

| Direction | Command sketch | Reuses | Risk |
|---|---|---|---|
| **A. Renumber plan** | `sheets renumber --plan-output renumber-plan.json` → `plan apply` | v2.4 plan/receipt | Medium — sheet number changes cascade to view refs. |
| **B. Sheet List writeback** | `sheets index sync-to-revit` projects yml into Revit Sheet List schedule | inspect-sheets + Revit schedule API | High — schedule write surface is deep. |
| **C. Pre-Issue integration** | `check issue --include sheets-verify` | v2.5 workflow / check | Low — command composition only. |
| **D. S3 coupling** | `sheets create --from-frame .revitcli/sheets/index.yml` | S3 writes + S4 verify | High but highest leverage — completes verify → create → verify loop. |

### Track-level promise

```
S4 verify finds gaps  →  S3 sheets create fills them  →  S4 verify confirms  →  publish
```

The `.revitcli/sheets/index.yml` becomes the single source of truth for the project sheet frame; verify and create both read it. This is the strategic reason S4 was chosen as the S-track entry point — establishing "expectation vs reality" semantics in a read-only slice before expanding into writes.

## Open Questions for Plan Phase

Items intentionally left open for the writing-plans skill to decide concretely:

- Concrete project/folder layout: `src/RevitCli/Sheets/` or under an existing namespace?
- Whether to add a new Add-in endpoint or extend the existing `inspect sheets` endpoint to carry the extra fields S4 needs (`floor`, `seq`, `viewsOnSheet`).
- YAML parser choice: reuse whatever `standards` track uses, or pick separately.
- Whether `sheets index init` infers `scheme` automatically (from observed numbering) or requires user-supplied scheme.
- Exact rule plugin loading order for `severities:` overrides (config-wins is intended).
- CI gating: do we add a `sheets-verify` job to existing CI, or fold under existing CLI tests?
