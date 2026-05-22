# Architect turn

You are **feature-architect**. There is no in-progress or planning feature
right now. Generate the next one from the roadmap and exit.

## Procedure

1. Read `docs/roadmap-v4.1-v5.md`. Find the earliest milestone (v4.1, then
   v4.2, ...) that does not yet have a corresponding file under
   `.codex/features/`.
2. If that milestone defines multiple commands (the table at the top of each
   milestone section), pick the first command listed.
3. Use `repo-explorer` or grep to confirm none of that command's expected
   files exist yet under the relevant `Commands/` / `Handlers/` directories.
4. Write the feature md at `.codex/features/v<version>-<command-slug>.md`,
   matching the format documented in your agent toml. status: in-progress.
5. Do not edit any other file. Do not commit (the tick script handles
   commits).

## Sizing reminder

8–14 checkboxes. Each ≤ 15 minutes for a single worker. Every checkbox has
an assignee from {spark-drafter, cli-implementer, addin-implementer,
test-author}. Last two checkboxes typically `tests-*` and `docs`.

## exit-criteria

Every line must be a paste-and-run shell command, parseable today, not
deferred to future workbench-verify checks.
