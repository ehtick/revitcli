# Sheet Frame Verify

Use when the architect asks whether the sheet set is complete, numbered
correctly, or ready to publish. The verify command is read-only against Revit.

## Prompt Shape

```text
出图前帮我看看图纸编号和必需图纸有没有问题。
```

## Command Path

```powershell
revitcli inspect sheets --issues-only --output markdown
revitcli sheets verify --output json --issues-only
```

If the project has no sheet index yet:

```powershell
revitcli sheets index init
revitcli sheets index show
```

Review `.revitcli/sheets/index.yml` before treating it as the project standard.

## Apply Boundary

`sheets verify` does not write to Revit. `sheets index init` only writes the
local YAML expectation file and refuses to overwrite it unless `--force` is
provided.
