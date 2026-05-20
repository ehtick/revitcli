# RevitCli v4.1 → v5.0 Terminal-First Mechanical-Work Blueprint

> Period: 2026 Q3 onward (v4.0 baseline shipped 2026-05-19)
> Status: drafted by ChatGPT Pro on 2026-05-19; strategic blueprint, not a locked implementation plan
> Predecessor: [docs/roadmap-2026q4-v4.md](roadmap-2026q4-v4.md)
> North star: every milestone resolves a specific repetitive Revit task an architect currently does by hand, with dry-run + receipt + rollback safety reused from v4.

假设：以下蓝图把 v4.0 视为已完成基线。RevitCli 的定位是 terminal-first Revit automation，并采用 `CLI -> HTTP REST -> Revit Add-in -> Revit API` 的本地架构；现有能力已经覆盖 `inspect`、`set`、`plan`、`rollback`、`export`、`publish`、`schedule`、`workflow`、`deliverables`、`journal` 等命令面。v4 文档（[roadmap-2026q4-v4.md](roadmap-2026q4-v4.md)）已经把目标收束为可被 Codex CLI 安全调用的本地终端工作台，并明确排除 MCP、内嵌 LLM、SaaS、全自动模型修改等方向。

## 顶层主题

v4.0 之后，RevitCli 的叙事应从"Codex CLI 可调用的安全 BIM 命令层"，推进到"真正闭环建筑师 90% 的重复出图、编号、表格、视图和交付机械活"。核心不是扩大概念，而是把每周、每次出图都会重复的高频事务，拆成可 dry-run、可审阅、可回滚、可留痕的终端流程。

优先级按"单次节省时间 × 频度 × 与现有安全网贴合度"排序；`v4.1` 到 `v4.3` 保持 4–8 周小步快跑，`v5.0` 才做契约收束。

## 总览表

| 版本   | 主题               | 解决的痛点                          | 典型架构师场景                                          | 关键命令名                                                                                  | 是否破坏性    |
| ------ | ------------------ | ----------------------------------- | --------------------------------------------------------- | ------------------------------------------------------------------------------------------ | ------------- |
| `v4.1` | 图纸发行元数据批处理 | 图签、版本号、日期、图纸编号、交付清单 | 出图前夜把 200 张图纸刷到 `R03 / 2026-05-19`，人工 3–4 小时 | `revitcli sheets issue-meta`、`revitcli sheets renumber`、`revitcli deliverables plan`     | 否            |
| `v4.2` | 房间与门窗编号引擎  | 房间号、门窗 `Mark`、楼层/区域/类型规则 | 每轮平面调整后重排 800 个房间、门窗编号，人工 4–6 小时         | `revitcli rooms renumber`、`revitcli marks assign`、`revitcli marks verify`               | 否            |
| `v4.3` | 表格稳定生成与比对  | 门表、窗表、房间表、面积表、材料表       | 每次出图前重建/导出/核对 10–30 张 schedule                  | `revitcli schedules ensure`、`revitcli schedules compare`、`revitcli schedules batch-export` | 否          |
| `v4.4` | 视图标准化与成套复制 | 视图模板、浏览器分组、视图复制命名      | 新增"报批版/招标版"图纸集，批量复制视图并套模板                | `revitcli views audit`、`revitcli views template-apply`、`revitcli views clone-set`        | 否            |
| `v4.5` | 协同模型卫生检查    | 链接路径、坐标验证、Phase/Worksets 映射 | 周会前确认结构/机电链接未丢、坐标未漂、工作集归属正确          | `revitcli links audit`、`revitcli links repair`、`revitcli model map-check`                | 否            |
| `v5.0` | 出图闭环工作台契约  | 图纸 QA、历史差异、发行包、契约升级      | 一条命令生成出图前检查、变更摘要、交付包和可签名证据            | `revitcli issue preflight`、`revitcli issue diff`、`revitcli issue package`                | 是，限 schema |

---

## `v4.1` — Sheet Issue Control

| 项     | 内容                                                                                                          |
| ------ | ------------------------------------------------------------------------------------------------------------- |
| 周期   | 4–5 周                                                                                                        |
| 痛点描述 | 每次出图都要把图签里的版本号、日期、阶段、负责人、图纸编号刷一遍；200 张图纸常见耗时 3–4 小时，且错一张就会造成发行事故。 |
| 频度证据 | 每次正式出图、报批、招标、施工图送审都会发生；大型项目每周或双周一次。                                          |
| 架构师目标 | 架构师能在终端里批量更新图纸发行信息、预览差异、生成交付计划，不再逐张打开 sheet 点图签参数。                  |

### 新命令 / 新 schema 草案

| 命令                         | 必填参数                                                   | 选填 / 默认值                                                       | 输出 schema              | 风险等级 |
| ---------------------------- | ---------------------------------------------------------- | ------------------------------------------------------------------- | ------------------------ | -------- |
| `revitcli sheets issue-meta` | `--selector`、`--issue-code`、`--issue-date`、`--plan-output` | `--param-map .revitcli/sheets/titleblock-map.yml`、`--dry-run` 默认建议先开 | `sheet-issue-plan.v1`    | write    |
| `revitcli sheets renumber`   | `--rule .revitcli/sheets/numbering.yml`、`--plan-output`     | `--selector all`、`--max-changes`、`--dry-run`                       | `sheet-renumber-plan.v1` | write    |
| `revitcli deliverables plan` | `--profile`                                                | `--since baseline.json`、`--output markdown`                         | `delivery-plan.v1`       | export   |

### 安全契约

| 项           | 设计                                                                                                                                                |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| dry-run 路径 | `revitcli sheets issue-meta ... --dry-run --output markdown` 输出每张图纸旧值/新值、缺失参数、只读参数、跳过原因。                                  |
| receipt      | 实际写入生成 `.revitcli/receipts/sheet-issue-*.json`，schema 为 `sheet-issue-receipt.v1`，记录 sheet id、titleblock id、旧参数、新参数、命令行、operator、model fingerprint。 |
| rollback 来源 | `revitcli rollback .revitcli/receipts/sheet-issue-*.json` 按 receipt 中旧值恢复；若当前值已被第三方改动，则标为 conflict 并拒绝静默覆盖。              |

### 验收门

| `workbench verify` 新检查项            | 通过条件                                                                      |
| ---------------------------------- | ------------------------------------------------------------------------- |
| `verify.sheetIssueDryRunRequired`  | `sheets issue-meta`、`sheets renumber` 无 `--dry-run` 或 plan apply 前不允许直接写。 |
| `verify.sheetReceiptRollbackShape` | receipt 必须包含旧值、新值、element ids、model fingerprint、rollback actions。         |
| `verify.sheetParamMapCoverage`     | 标准包中声明的图签参数映射能解析到真实 title block 参数。                                       |

### 可行性证据

依赖现有 `inspect sheets`、`sheets verify`、`set/import --plan-output`、`plan apply`、`rollback`、`publish`、`deliverables bundle`、`journal sign/verify`。这是典型参数批写，不需要新建模型几何。

### 明确不做什么

不做图签族几何编辑；不自动决定发行策略；不替代项目经理审批；不把 dashboard 做成发行入口。

---

## `v4.2` — Numbering Engine for Rooms / Marks

| 项     | 内容                                                                                                |
| ------ | --------------------------------------------------------------------------------------------------- |
| 周期   | 6–7 周                                                                                              |
| 痛点描述 | 平面调整后，房间号、门 `Mark`、窗 `Mark` 经常乱序、重复或跨楼层冲突；人工在表格和视图里来回改，800 个对象可耗时 4–6 小时。 |
| 频度证据 | 方案深化、初设、施工图、消防审查前都会重排；每个项目至少多轮发生。                                  |
| 架构师目标 | 架构师能在终端里按楼层、区域、类型、防火分区规则重新编号，不再手动找重复 `Mark`。                  |

### 新命令 / 新 schema 草案

| 命令                      | 必填参数                                                | 选填 / 默认值                                              | 输出 schema                | 风险等级  |
| ------------------------- | ------------------------------------------------------- | ---------------------------------------------------------- | -------------------------- | --------- |
| `revitcli rooms renumber` | `--rule .revitcli/numbering/rooms.yml`、`--plan-output` | `--scope all`、`--dry-run`、`--max-changes 500`            | `room-numbering-plan.v1`   | write     |
| `revitcli marks assign`   | `--category doors\|windows`、`--rule`、`--plan-output`   | `--sort level,zone,type,location`、`--dry-run`             | `mark-assignment-plan.v1`  | write     |
| `revitcli marks verify`   | `--category doors,windows`                              | `--against .revitcli/numbering/*.yml`、`--output markdown` | `mark-verify-report.v1`    | read-only |

### 安全契约

| 项           | 设计                                                                                                  |
| ------------ | ----------------------------------------------------------------------------------------------------- |
| dry-run 路径 | 所有编号写入先生成 frozen element id plan；报告重复号、跳号、缺楼层、缺区域、只读 `Mark`。              |
| receipt      | `.revitcli/receipts/numbering-*.json`，schema 为 `numbering-receipt.v1`，记录每个元素旧编号、新编号、排序键、规则版本。 |
| rollback 来源 | 从 receipt 恢复旧 `Number` / `Mark`；若元素已删除则跳过并记录；若当前值不同于 receipt 新值则冲突。       |

### 验收门

| `workbench verify` 新检查项              | 通过条件                                             |
| ------------------------------------ | ------------------------------------------------ |
| `verify.numberingRulesDeterministic` | 同一模型、同一 rule、同一 snapshot 产生相同计划。                 |
| `verify.numberingNoSilentOverwrite`  | 发现重复目标编号时必须 fail plan，除非显式 `--allow-resequence`。 |
| `verify.numberingRollbackComplete`   | plan 中每个 write action 都有旧值 rollback action。      |

### 可行性证据

依赖现有 `query <category>`、`inspect params rooms|doors|windows`、`set --plan-output`、`import --plan-output`、`plan show/apply`、`snapshot/diff`。编号本质是参数写入，适合复用现有 dry-run 与 receipt 设施。

### 明确不做什么

不做空间拓扑自动推理引擎；不替建筑师决定编号规则；不做门窗族几何修改；不在模型里自动移动房间或门窗。

---

## `v4.3` — Schedule Factory & Diff

| 项     | 内容                                                                                  |
| ------ | ------------------------------------------------------------------------------------- |
| 周期   | 6–8 周                                                                                |
| 痛点描述 | 门表、窗表、房间表、面积表、材料表经常字段缺失、过滤器错误、排序不一致；出图前要逐张 schedule 检查和导出。 |
| 频度证据 | 每次出图、报审、招标清单都会发生；BIM 经理通常每周核对一次。                          |
| 架构师目标 | 架构师能在终端里确保 schedule 结构存在、批量导出、与上次发行版比对，不再手动重建表格。 |

### 新命令 / 新 schema 草案

| 命令                              | 必填参数                                            | 选填 / 默认值                                  | 输出 schema                   | 风险等级  |
| --------------------------------- | --------------------------------------------------- | ---------------------------------------------- | ----------------------------- | --------- |
| `revitcli schedules ensure`       | `--spec .revitcli/schedules/*.yml`、`--plan-output` | `--dry-run`、`--mode create-only\|sync-fields` | `schedule-ensure-plan.v1`     | write     |
| `revitcli schedules batch-export` | `--set issue`、`--output-dir`                       | `--format csv`、`--manifest`                   | `schedule-export-manifest.v1` | export    |
| `revitcli schedules compare`      | `--from baseline-dir`、`--to current-dir`           | `--keys Number,Mark`、`--output markdown`      | `schedule-diff-report.v1`     | read-only |

### 安全契约

| 项           | 设计                                                                                |
| ------------ | ----------------------------------------------------------------------------------- |
| dry-run 路径 | `schedules ensure --dry-run` 只报告将创建/调整的字段、排序、过滤器，不写模型。      |
| receipt      | `.revitcli/receipts/schedule-ensure-*.json`，记录创建的 schedule id、旧字段配置、旧过滤器、旧排序。 |
| rollback 来源 | 新建 schedule 可删除；修改字段/过滤器/排序可按 receipt 恢复；导出操作生成 manifest，不需要模型 rollback。 |

### 验收门

| `workbench verify` 新检查项          | 通过条件                                                                 |
| -------------------------------- | -------------------------------------------------------------------- |
| `verify.scheduleSpecSchema`      | `schedule-spec.v1` 字段、过滤器、排序、key columns 完整可校验。                      |
| `verify.scheduleExportTraceable` | 每个 CSV 都能追溯到 schedule id、model fingerprint、receipt 或 manifest entry。 |
| `verify.scheduleEnsureRollback`  | 所有 schedule 结构写入都有旧结构 baseline。                                      |

### 可行性证据

依赖现有 `schedule list/export/create`、`inspect schedules`、`publish`、`deliverables manifest`、`diff`、`standards validate`。此 milestone 把"表格"从一次性导出升级为版本化规格。

### 明确不做什么

不做 Excel 插件；不做云端清单协同；不自动解释规范条文；不替代造价或清单软件。

---

## `v4.4` — View Standards & View Set Clone

| 项     | 内容                                                                  |
| ------ | --------------------------------------------------------------------- |
| 周期   | 6–8 周                                                                |
| 痛点描述 | 成套视图复制、命名、套模板、放进浏览器分组是高度重复工作；报批版、招标版、施工版之间常有几十到上百个视图要处理。 |
| 频度证据 | 每个阶段切换、每个专业拆包、每次新增图纸集都会发生。                  |
| 架构师目标 | 架构师能在终端里审计视图模板合规、批量套模板、按规则复制视图集，不再在 Project Browser 里逐项拖改。 |

### 新命令 / 新 schema 草案

| 命令                            | 必填参数                                                  | 选填 / 默认值                                  | 输出 schema                | 风险等级  |
| ------------------------------- | --------------------------------------------------------- | ---------------------------------------------- | -------------------------- | --------- |
| `revitcli views audit`          | `--rules .revitcli/views/standards.yml`                   | `--templates`、`--browser`、`--output markdown` | `view-standards-report.v1` | read-only |
| `revitcli views template-apply` | `--selector`、`--template`、`--plan-output`               | `--dry-run`、`--exclude locked`                | `view-template-plan.v1`    | write     |
| `revitcli views clone-set`      | `--from-set`、`--to-prefix`、`--naming-rule`、`--plan-output` | `--dry-run`、`--include-sheets false`         | `view-clone-plan.v1`       | write     |

### 安全契约

| 项           | 设计                                                                                          |
| ------------ | --------------------------------------------------------------------------------------------- |
| dry-run 路径 | 列出将复制的 view、目标名称、模板变化、浏览器分组变化、潜在重名冲突。                         |
| receipt      | `.revitcli/receipts/view-mutation-*.json`，记录旧 template id、新 template id、新建 view ids、命名规则版本。 |
| rollback 来源 | 套模板按旧 template id 恢复；克隆视图按 receipt 删除新建 view，若新 view 已被放到 sheet 上则阻止删除并提示人工处理。 |

### 验收门

| `workbench verify` 新检查项                | 通过条件                                  |
| -------------------------------------- | ------------------------------------- |
| `verify.viewMutationPlanIdsFrozen`     | plan 必须冻结源 view id、目标名称和 template id。 |
| `verify.viewCloneNoNameCollision`      | 目标名称冲突时必须 fail，不能自动覆盖。                |
| `verify.viewRollbackGuardsPlacedViews` | rollback 删除 view 前必须检查是否已放置到 sheet。   |

### 可行性证据

依赖现有 `inspect sheets`、`snapshot`、`standards validate`、`plan apply`、`journal`。风险集中在视图属性与新建视图，不涉及 Family Editor 几何。

### 明确不做什么

不做自动排版；不自动选择裁剪框；不移动 annotation；不承诺复制所有 Revit 视图类型的复杂状态，先覆盖平面、天花、立面、剖面中最稳定的子集。

---

## `v4.5` — Coordination Hygiene

| 项     | 内容                                                                                  |
| ------ | ------------------------------------------------------------------------------------- |
| 周期   | 7–8 周                                                                                |
| 痛点描述 | 协同周会前常要确认结构/机电/室内链接是否丢失、路径是否变更、坐标是否漂移、工作集和阶段是否归属错误；人工检查易漏。 |
| 频度证据 | 每周协同、每次模型交换、每次出图前都会发生。                                          |
| 架构师目标 | 架构师能在终端里审计链接、坐标、阶段、工作集映射，并有限修复路径和参数归属，不再逐个打开 Manage Links 和属性面板。 |

### 新命令 / 新 schema 草案

| 命令                       | 必填参数                                          | 选填 / 默认值                                          | 输出 schema             | 风险等级  |
| -------------------------- | ------------------------------------------------- | ------------------------------------------------------ | ----------------------- | --------- |
| `revitcli links audit`     | `--rules .revitcli/links/rules.yml`               | `--check paths,loaded,coordinates`、`--output markdown` | `link-audit-report.v1`  | read-only |
| `revitcli links repair`    | `--map .revitcli/links/paths.yml`、`--plan-output` | `--dry-run`、`--max-changes 20`                        | `link-repair-plan.v1`   | write     |
| `revitcli model map-check` | `--against .revitcli/model-mapping.yml`           | `--worksets`、`--phases`、`--output json`              | `model-map-report.v1`   | read-only |
| `revitcli model map-fix`   | `--against`、`--plan-output`                      | `--scope rooms,doors,walls`、`--dry-run`               | `model-map-fix-plan.v1` | write     |

### 安全契约

| 项           | 设计                                                                                                   |
| ------------ | ------------------------------------------------------------------------------------------------------ |
| dry-run 路径 | 链接修复先报告旧路径、新路径、load status；workset/phase 修复先报告旧 id、新 id、不可写原因。          |
| receipt      | `.revitcli/receipts/coordination-*.json`，记录 link external reference、旧路径、旧状态、phase/workset 旧值。 |
| rollback 来源 | 链接按旧路径/旧加载状态恢复；phase/workset 参数按旧值恢复；若链接源文件不存在则 rollback fail 并输出人工操作说明。 |

### 验收门

| `workbench verify` 新检查项             | 通过条件                                           |
| ----------------------------------- | ---------------------------------------------- |
| `verify.linkRepairNoCoordinateMove` | `links repair` 只能修路径/加载状态，不允许自动移动或旋转链接模型。      |
| `verify.modelMapWritableProbe`      | `model map-fix` 必须先验证目标元素可写、工作集可访问。            |
| `verify.coordinationReceiptPaths`   | receipt 必须包含旧路径、新路径、存在性检查、hash 或 timestamp 证据。 |

### 可行性证据

依赖现有 `doctor`、`query`、`check`、`standards validate`、`plan/receipt/rollback`、`journal review`。此版本把协同风险先做成可读报告，再允许有限、安全的路径与参数修复。

### 明确不做什么

不做坐标自动对齐；不替代 clash detection；不接 ACC/cloud sync；不自动 reload 未经确认的外部模型；不处理跨团队权限问题。

---

## `v5.0` — Issue Closure Workbench Contract

| 项     | 内容                                                                                               |
| ------ | -------------------------------------------------------------------------------------------------- |
| 周期   | 10–12 周                                                                                           |
| 痛点描述 | 真正出图前，建筑师需要同时做图纸 QA、表格 QA、变更说明、导出、打包、签名、留痕；这些能力 v4 已分散存在，但还不是一个闭环。 |
| 频度证据 | 每次正式发行都发生；项目后期频率最高，错误成本也最高。                                            |
| 架构师目标 | 架构师能在终端里完成"发行前检查 → 差异摘要 → 导出计划 → 打包 → receipt/journal 签名"，不再拼接多条命令和人工清单。 |

### 新命令 / 新 schema 草案

| 命令                                                           | 必填参数                            | 选填 / 默认值                                              | 输出 schema                    | 风险等级  |
| -------------------------------------------------------------- | ----------------------------------- | ---------------------------------------------------------- | ----------------------------- | --------- |
| `revitcli issue preflight`                                     | `--profile .revitcli/issue.yml`     | `--output markdown`、`--fail-on warning\|error`            | `issue-preflight-report.v1`   | mixed     |
| `revitcli issue diff`                                          | `--from baseline.json`              | `--to current`、`--review`、`--output markdown`            | `issue-diff-report.v1`        | read-only |
| `revitcli issue package`                                       | `--profile`、`--bundle-path`        | `--dry-run`、`--sign-journal`、`--include-receipts true`   | `issue-package-receipt.v1`    | export    |
| `revitcli workbench verify --contract workbench-contract.v2`   | `--project .`                       | `--output json`                                            | `workbench-verify-report.v2`  | read-only |

### 安全契约

| 项           | 设计                                                                                                                                             |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| dry-run 路径 | `issue package --dry-run` 汇总将运行的 check、export、schedule export、bundle、journal sign；不写交付文件。                                      |
| receipt      | `.revitcli/receipts/issue-package-*.json`，引用所有子 receipt、manifest、bundle hash、journal signature。                                       |
| rollback 来源 | v5.0 的 `issue package` 默认不改模型；若 profile 中包含 `sheet issue-meta` 等写步骤，必须引用独立 plan receipt，由原命令 rollback。导出文件只提供 delete manifest，不伪装为模型 rollback。 |

### 验收门

| `workbench verify` 新检查项           | 通过条件                                                                        |
| --------------------------------- | --------------------------------------------------------------------------- |
| `verify.contractV2Compat`         | `workbench-contract.v2` 声明所有 schema、exit code、receipt location；`v1` 兼容模式保留。 |
| `verify.issueNoHiddenMutation`    | `issue preflight/package` 中任何模型写入必须显式引用 plan 文件，不能隐藏写操作。                    |
| `verify.issuePackageTraceability` | ZIP 内每个交付件能追溯到 manifest、receipt、model fingerprint、journal entry。            |
| `verify.dashboardOptional`        | dashboard 构建失败不得影响 CLI issue flow；只能作为可选可视化。                                |

### 可行性证据

依赖 v4.1–v4.5 的 sheet、numbering、schedule、view、coordination 报告，也依赖现有 `workflow run --timeout-ms`、`deliverables bundle`、`journal sign/verify`、`history diff`、`report weekly/knowledge`。v5.0 的重点是统一契约，不是发明新自动化内核。

### 明确不做什么

不做内嵌 agent；不把自然语言放进 RevitCli；不做 SaaS 审批流；不做云端项目门户；不自动修改模型来"通过检查"；不把 dashboard 变成中心入口。

---

## 排期建议

| 阶段     |    建议节奏 | 成功标准                                                           |
| -------- | ----------: | ------------------------------------------------------------------ |
| `v4.1` |    4–5 周   | 发行元数据批处理可 dry-run、apply、rollback，200 张图纸场景可稳定复现。 |
| `v4.2` |    6–7 周   | 房间号与门窗 `Mark` 生成 deterministic plan，重复号处理可解释。     |
| `v4.3` |    6–8 周   | schedule 规格化、批量导出、CSV diff 成为出图工作流标准步骤。        |
| `v4.4` |    6–8 周   | 视图模板合规和成套克隆可回滚，命名冲突零静默覆盖。                  |
| `v4.5` |    7–8 周   | 协同检查报告可用于周会，链接修复和映射修复保持保守边界。            |
| `v5.0` |  10–12 周   | `issue preflight`、`issue diff`、`issue package` 形成可签名、可追溯的发行闭环。 |
