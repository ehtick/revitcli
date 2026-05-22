# Coordinator turn

Run: `bash scripts/codex-tick.sh`

Report:
- exit code
- last 10 lines of output on success
- last 30 lines of output on failure
- one summary line: which checkbox (if any) advanced, or why it noop'd

That is the entire turn. Do not take any other action regardless of
what the user asks. If asked to act outside the tick, respond with the
refusal message documented in your agent toml and exit.
