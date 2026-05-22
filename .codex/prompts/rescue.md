# Rescue turn

Build or tests are red. The combined build + test log appears above this
section. Your job is **root cause + minimal honest fix**, in one commit.

## Required reading

- `.codex/state/last-build.log` and `.codex/state/last-test.log` (already in
  context).
- `git diff HEAD~1..HEAD` if a previous commit exists, otherwise
  `git diff --staged`.
- The active feature md (path is in `.codex/state/current-feature.txt`).

## Allowed actions

- Read the stack trace, identify the actual cause, write the minimal fix.
- Fix mis-quoted JSON expectations, wrong path separators, missing `await`,
  null reference paths, regex overspecification.
- Adjust a test whose assertion was tied to unstable formatting (timestamp,
  random order) to use a stable substring or property check — but the test
  must still verify the original intent.
- Commit the fix. Body ends with:
  ```
  Root cause: <one sentence>
  Verify: <single shell command that now passes>
  ```

## Forbidden (these are the patterns that make CI dishonest)

- Adding `[Skip]`, `[SkippedOnLinuxCI]`, `[Trait("skip","true")]`, or any
  conditional that disables a failing test.
- Replacing an assertion with `Assert.True(true)` or weakening expectations
  to fit the current (wrong) output.
- Wrapping failing code in `if (false) { ... }` or commenting it out.
- Using `--filter "FullyQualifiedName!~<failing test>"` to bypass.
- Deleting a failing test rather than fixing the code.
- `catch { }` / `catch (Exception) { }` with no rethrow.
- Reverting the parent worker's entire diff to "make it go away" unless you
  are confident the worker's approach is wrong AND you write a Notes line
  stating why.

## Escape valve

If the failure is genuinely out of this environment's reach (requires
Revit + Windows, or depends on a service that doesn't exist yet), leave
HEAD unchanged. Set the feature `status: blocked` and add a Notes line:

```
Blocked: <failing test or build error>.
Requires: <environment / next missing piece>.
Hypothesis: <your best guess at root cause>.
```

## Retry cap

The tick script tracks rescue attempts per checkbox. If you can see (from
`.codex/state/last-tick.md`) that this is your SECOND rescue on the same
checkbox and you still cannot fix it honestly, set `status: blocked` and
exit. The third rescue is not allowed.
