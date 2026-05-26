# Worker turn

You have been assigned a single checkbox in the active feature md. The
checkbox slug, feature file, and time budget appear above this section. Do
that one thing.

## Hard rules

1. Edit only files matched by the feature md's `scope-paths`. The tick
   script preserves pre-existing dirty paths, but it will physically
   revert out-of-scope worker-created edits from this tick.
2. Read every file you intend to edit at least once this session before
   writing.
3. If your checkbox touches public CLI behavior or an HTTP handler, ship a
   test in the same change (you may stub the test if you are spark-drafter,
   but you must not leave it asserting `true`).
4. `dotnet build src/RevitCli/RevitCli.csproj -c Debug -v q` must succeed
   when you finish.
5. `dotnet test tests/RevitCli.Tests/ --no-build -v q` must succeed when
   you finish (unless your role is addin-implementer changing only add-in
   code that doesn't affect CLI tests).
6. Forbidden: `[Skip]`, `Assert.True(true)`, empty `catch`, `if (false)`
   wrappers, `--filter "not <failing>"`, adding NuGet outside
   `Directory.Packages.props`, modifying `.codex/**` except the active
   feature md's `status:` / Notes when declaring an honest blocker.
7. Do not edit the feature md to mark your own checkbox as done. The tick
   script marks it only after build/test pass, review, no `status: blocked`,
   and at least one worker-created change from this tick.

## Stop conditions

- **Done** + build & tests green → exit cleanly. No git commit (the tick
  script commits after scope enforcement).
- **Time budget hit, work incomplete** → set the feature `status: blocked`
  and append a Notes line stating exactly what's unfinished.
- **Honest fix attempt failed** (test still red after one focused try) →
  same as above. Do not weaken assertions. Do not patch around.

## Style discipline

- Don't refactor neighboring code "while you're in there".
- Don't add comments explaining what the next line obviously does.
- Don't write multi-paragraph docstrings.
- Don't print to stdout from production code unless via the existing
  `TextWriter` / Spectre.Console paths.
