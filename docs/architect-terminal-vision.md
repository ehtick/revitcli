# Architect Terminal Vision

RevitCli exists to help architects finish repetitive Revit work from a
terminal: checking models, exporting drawings, exporting schedules,
finding errors, batch-editing parameters, packaging deliverables, and
leaving an audit trail.

## North Star

An architect should be able to open Revit, open a terminal, and ask
Codex CLI to call RevitCli for the boring work. The work remains visible:
normal commands, dry-run output, receipts, journals, and rollback paths.

## Product Shape

- RevitCli is the deterministic BIM command layer.
- Codex CLI can be the conversational terminal operator.
- Revit stays local and open on the user's machine.
- Project standards live in versioned profiles and workflows.
- Outputs should be readable by architects and scriptable by tools.

## Core Workflows

- Check model health before issue.
- Publish DWG/PDF/IFC packages.
- Export schedule data to CSV/JSON.
- Find setup, add-in, profile, or publish errors.
- Preview and apply safe parameter changes.
- Capture snapshots, diffs, history, and journal receipts.

## Non-Goals

- No MCP roadmap.
- No embedded LLM or natural-language parser inside RevitCli.
- No hidden autonomous writes.
- No cloud model upload or SaaS control plane.
- No broad AI-to-Revit platform positioning.

## Safety Bar

Every risky workflow should support read-only discovery, dry-run or plan
output, explicit approval for writes, journal entries, and a rollback or
recovery story. If Codex CLI is involved, it must show the RevitCli
commands it plans to run before mutating the model.
