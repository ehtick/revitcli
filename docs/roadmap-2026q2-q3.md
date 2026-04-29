# RevitCli v1.5 → v2.0 推进路线图

> 周期:2026 Q2 末 — Q3 全季
> 起点:v1.4 候选(install/doctor/smoke 版本一致性,刚 merge 进 main)
> 受众:执行此路线图的 AI 工程代理(codex 或同类)
> 工作流约定:遵循项目既有的 superpowers 流程(brainstorm → spec → plan → execute)
> 后续蓝图:完成 v2.1 后进入 [`roadmap-2026q4-v4.md`](./roadmap-2026q4-v4.md)

---

## 0. 当前项目状态(2026-04-26)

### 已交付能力(v1.0 → v1.4 候选)

| 路径         | 命令 / 能力                                                                                                                            |
| ------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| **读**       | `status` / `doctor` / `query` / `audit` / `check` / `score` / `coverage` / `snapshot` / `diff`                                         |
| **写**       | `set`(category+filter / `--id` / `--ids-from` / stdin) · `import`(CSV → 批量参数) · `schedule create`                                  |
| **导出**     | `export`(DWG / PDF / IFC) · `publish`(profile-driven) · `publish --since`(增量)                                                        |
| **基础设施** | `.revitcli.yml` profile(单父继承,REPLACE 而非 deep-merge) · `batch` · `interactive`(REPL) · `completions`(bash/zsh/pwsh) · `init`      |
| **运维**     | `install.ps1`(端到端安装) · `smoke-revit2026.ps1`(真实 Revit 烟雾门) · `doctor` 三方版本一致性(CLI / 安装 addin manifest / live addin) |

### 架构骨架

```
CLI (revitcli.exe, .NET 8, 跨平台)
   ↕ HTTP REST(EmbedIO)
Revit Add-in (net48 / net8.0-windows)
   ↕ ExternalEvent Bridge(主线程隔离)
Revit API
```

- **共享层** `shared/RevitCli.Shared/`(`netstandard2.0`):DTO + `IRevitOperations` 接口。**所有跨进程契约都在这里**,新增端点必须先扩接口
- **Profile 层** `.revitcli.yml`:`check` / `publish` / `init` / `score` / `coverage` 都通过 `ProfileLoader.Discover()` 加载
- **Snapshot schema** 当前 `schemaVersion: 1`,内含 `MetaHash` + `ContentHash`(后者 v1.2 引入)
- **测试** CLI 253 个,addin/protocol 另算,新功能 PR 必须配新测试

### 战略定位(已锁)

> RevitCli 是**本地 BIMOps 运行器**:版本化项目标准 + 可重复交付流水线 + 离线执行 + 多 Revit 版本支持。
> **不是**通用 AI-to-Revit 桥梁(社区 MCP server 已覆盖)。MCP 仅作为**可选分发渠道**,不是主线。

执行此路线图时,所有功能取舍都按这个标尺判断:**它是否让 BIMOps 工作流更可重复、更可版本化、更可 CI?**

---

## 1. 路线图全景与优先级矩阵

### 主线 6 个 milestone

| Milestone | 主题                 | 一句话目标                                    | 影响力 | 工程量       | 风险                 | 与现有架构契合 |
| --------- | -------------------- | --------------------------------------------- | ------ | ------------ | -------------------- | -------------- |
| **v1.5**  | Auto-fix 闭环        | `check` → `fix` 闭环,从"发现问题"到"自动修复" | 极高   | 中           | 中(回滚/审批)        | ⭐⭐⭐⭐⭐     |
| **v1.6**  | History / Trend 时序 | snapshot 仓库化,模型健康度时间序列            | 高     | 小-中        | 低                   | ⭐⭐⭐⭐⭐     |
| **v1.7**  | CI 集成深化          | SARIF 输出 + GitHub Action + PR comment bot   | 中-高  | 中           | 低                   | ⭐⭐⭐⭐       |
| **v1.8**  | Family 资产管理      | `family ls / purge / validate` 覆盖族文件     | 高     | 大           | 中(Revit Family API) | ⭐⭐⭐         |
| **v1.9**  | Profile 治理         | 多继承 / lint / registry,从单项目到团队规模   | 中     | 小-中        | 低                   | ⭐⭐⭐⭐⭐     |
| **v2.0**  | Web Dashboard        | 基于 v1.6 仓库的可视化看板                    | 中     | 大(新前端栈) | 中                   | ⭐⭐           |

### 可并行侧线

| Track           | 主题                                     | 触发条件                                                   |
| --------------- | ---------------------------------------- | ---------------------------------------------------------- |
| **MCP Adapter** | 把 CLI 命令薄封装成 MCP server(可选分发) | v1.5 之后任意 milestone 完成时,作为 0.5 周内的小活随插随用 |

### 为什么是这个顺序

1. **v1.5 先做(闭环优先于扩展)** — 当前 `check` 只能"看见问题",用户还是要手动改。把 `check → fix` 闭起来,工具价值翻倍。同时这一步的产物(可逆 fix + 审批门)是 v1.7 PR 自动评论的基础。
2. **v1.6 紧跟(时序优先于可视化)** — `snapshot` 是 v1.1 的能力,但目前是单点。把它仓库化后,即可回答"过去 N 天模型健康度怎么变",而这是 v2.0 dashboard 的数据底座。**没有 v1.6 就不该做 v2.0**。
3. **v1.7 顺势(协作优先于资产)** — v1.5 的 fix + v1.6 的 trend 都需要落地到团队工作流。SARIF + PR comment 是把 BIMOps "嵌入 git" 的关键一步,工程量适中,风险低。
4. **v1.8 攻坚(资产是另一类底座)** — Family 是项目级资产,有独立的 API 表面。放到第四,等前面三步沉淀了"读-写-审计"的通用模式后再扩展到族,迁移成本最低。
5. **v1.9 治理(规模化前的清场)** — Profile 当前是单项目单文件。一旦想做团队级共享,必须先支持多继承 + 校验 + 仓库化。这是 v2.0 dashboard 多项目视角的前置。
6. **v2.0 收官(可视化是最后一公里)** — 引入前端栈会显著扩大维护面,必须等数据底座(v1.6)+ 多项目治理(v1.9)都稳定再做,避免后续大改。

### 不计成本的最强路径(参考,不推荐)

如果资源无限,最高 ROI 是**v1.5 + v1.6 + v1.7 三个 milestone 并行做**,各自切独立分支:

- v1.5 由 codex 主推,因为有大量 Revit API 写入逻辑
- v1.6 由通用工程代理推,因为是纯 .NET / 文件系统逻辑
- v1.7 完全独立,只需 CLI 输出格式 + GitHub Action 模板

并行劣势:

- 三条分支都改 `IRevitOperations` / profile schema 时会冲突,需要每周 rebase
- v1.7 的 SARIF 输出依赖 v1.5 的 fix 元数据(suggested fix 字段),并行做会产生返工
- 评审带宽:三个 milestone 同时进行人(或 ultrareview)的评审吞吐跟不上

**结论:理论上能压缩 40% 周期,但实际推荐串行**,除非有强外部时间压力。

---

## 2. v1.5 — Auto-fix Playbooks(闭环)

### 价值主张

当前 `check` / `audit` 能找出"门没有 Mark"、"房间编号重复"、"图签缺字段"等问题,但用户还是要打开 Revit 一个个手改。Auto-fix 的目标是:**让规则同时携带修复方案**,用户运行 `revitcli fix` 就能批量改正,且可预览、可回滚、可审批。

闭环成立后的工作流:

```bash
revitcli check                        # 看见 12 个问题
revitcli fix --dry-run                # 看见 10 个问题有自动修复方案
revitcli fix --rule naming --apply    # 批量应用,自动 snapshot baseline
revitcli check                        # 验证已降到 2 个(剩下的需要人工)
```

### 范围(In)

- 新命令 `revitcli fix`:基于 `check` 结果产出 fix plan,支持 `--dry-run` / `--apply` / `--rule` / `--severity`
- Profile 扩展:每条规则可声明 `fix:` 配方,例如 `fix: { strategy: setParam, param: Mark, valueFrom: ... }`
- 至少 4 个内置 fix strategy:
  1. `setParam` — 给参数填值(支持模板:`{element.id}`,`{element.type}`)
  2. `purgeUnplaced` — 删除未放置的房间/视图等
  3. `renameByPattern` — 按 regex 重命名(图纸编号、视图名)
  4. `linkRoomToBoundary` — 把房间标签关联到正确的房间
- 自动 baseline:`fix --apply` 前自动 `snapshot`,放到 `.revitcli/fix-baseline-{timestamp}.json`,失败可 `revitcli rollback <baseline>`
- 审批门:`fix --apply` 默认要求 `--yes` 或交互确认,改写元素数 > N(profile 配)需二次确认
- `revitcli rollback <baseline>` 命令:基于 fix 前 snapshot 反向 import 旧值

### 范围(Out)

- 几何修复(移动墙体、调整门窗位置)— 风险太大,不在闭环范围
- 跨文档 fix — 当前架构假设单 active document,跨文档放 v2.x
- 自定义 fix strategy 插件机制 — 第一版只暴露内置 strategy,不开放自定义脚本

### 关键设计决策

#### 决策 1:fix 是 check 的子命令还是独立命令?

**候选 A:`revitcli check --fix`**(单命令双模式)

- 优势:用户记忆负担小,check 与 fix 共用规则配置
- 劣势:check 当前已经支持 `--report` / `--report-format` / `failOn`,再加 `--fix` 会让选项矩阵爆炸;dry-run 语义模糊(check 的 dry-run 还是 fix 的 dry-run?)

**候选 B:`revitcli fix`(独立命令,推荐)**

- 优势:语义清晰,选项独立(`--apply` / `--rule` / `--max-changes`);可独立测试;符合 \*-able 命令分层(查 / 改 / 审)
- 劣势:多一个命令,但与 set / import 同级,一致性好

**结论:候选 B**。理由:模仿 git 的 `diff` / `apply` 关系,`check` 是 diff,`fix` 是 apply。

#### 决策 2:fix 配方放在 profile 还是单独的 playbook 文件?

**候选 A:profile 内联**(`.revitcli.yml` 的 `checks.<rule>.fix:` 节点)

- 优势:配置集中,一个文件搞定;复用现有 ProfileLoader
- 劣势:profile 会变得臃肿;团队级共享 fix 配方不方便

**候选 B:独立 playbook 文件**(`.revitcli/playbooks/naming-fix.yml`)

- 优势:可单独版本化、共享、复用;支持团队级 playbook 仓库
- 劣势:多一个文件层级

**结论:profile 内联起步,profile 引用 playbook 路径作为升级路径**。第一版先 inline,等 v1.9 治理阶段再支持 `playbooks: ./team-playbooks/` 引用。

#### 决策 3:fix 写入复用 set/import 还是新增 addin 端点?

**候选 A:复用 `/api/elements/set`**(推荐)

- 优势:不需要 addin 升级,v1.4 addin 即可支持;一致的事务/错误语义
- 劣势:无法表达 fix 的语义(为什么改),只是单纯改值

**候选 B:新增 `/api/fix/apply`**

- 优势:可以传递 fix metadata(规则名、置信度),addin 端可写更详细的 journal
- 劣势:addin 必须升级;事务/错误处理重复造轮子

**结论:候选 A**。fix metadata 在 CLI 端记到 journal 即可,addin 不需要知道这是 fix 还是手动 set。

#### 决策 4:回滚机制走 snapshot 还是事务日志?

**候选 A:fix 前 auto-snapshot**(推荐)

- 优势:复用 v1.1 snapshot 设施,无新代码;简单可靠
- 劣势:大模型 snapshot ~30s,fix 前会感知到延迟

**候选 B:Revit 事务日志反向回放**

- 优势:精确回滚,无延迟
- 劣势:Revit Transaction Group API 跨进程难以暴露;复杂

**结论:候选 A**,加 `--no-snapshot` flag 给愿意自担风险的高级用户。

### 实施分步

#### Step 1 — Spec(0.5d)

- 写 `docs/superpowers/specs/2026-{date}-auto-fix-playbooks-design.md`
- 覆盖:fix 命令矩阵、4 个内置 strategy 的 schema、profile 扩展点、rollback 流程、审批门规则
- 必须列出 4 个 strategy 的 happy path + 至少 3 个 edge case

#### Step 2 — Plan(0.5d)

- 写 `docs/superpowers/plans/2026-{date}-auto-fix-playbooks.md`
- 文件清单 + 测试矩阵 + 验收标准
- 估算每个 strategy 的实现量,建议 1 strategy / commit

#### Step 3 — Profile schema 扩展(0.5d)

- `src/RevitCli/Profile/ProjectProfile.cs` 增加 `CheckRuleConfig.Fix` 节点
- DTO:`FixStrategyKind` enum / `FixConfig` record
- 更新 3 个 starter profile 在 `profiles/` 加 fix 示例(注释掉,作为模板)
- 测试:profile 加载/序列化/缺字段处理

#### Step 4 — Fix planner(纯 C#)(1d)

- `src/RevitCli/Fix/FixPlanner.cs`:输入 `CheckResult[]` + profile,输出 `FixPlan`(含每个 strategy 的具体动作)
- `src/RevitCli/Fix/Strategies/` 4 个 strategy 类,实现 `IFixStrategy.Plan(CheckIssue, FixConfig) -> FixAction[]`
- 测试:每个 strategy 独立单测 + 综合 planner 测试,覆盖空规则集 / 未知 strategy / 参数模板渲染

#### Step 5 — Fix command(1d)

- `src/RevitCli/Commands/FixCommand.cs`:整合 `check → planner → preview → apply`
- 选项:`--rule`(可重复)/`--severity`/`--dry-run`(默认)/`--apply`/`--yes`/`--max-changes N`/`--baseline-output PATH`/`--no-snapshot`
- Apply 前自动 snapshot 到 `.revitcli/fix-baseline-{ISO8601}.json`
- 失败:打印 baseline 路径并提示 `revitcli rollback`
- 测试:dry-run 输出 / apply 模拟 / max-changes 触发 / 中途失败的回滚提示

#### Step 6 — Rollback command(0.5d)

- `src/RevitCli/Commands/RollbackCommand.cs`:读 baseline snapshot,对比当前模型,生成反向 SetRequest 批次
- 复用 `import` 的 batch 逻辑,**不写新 addin 端点**
- 测试:无变化 baseline / 部分变化 / 多类别变化 / 缺失元素(已被删除)

#### Step 7 — 命令注册 + 完成度(0.5d)

- `CliCommandCatalog.cs` 注册 `fix` / `rollback`
- `CompletionsCommand.cs` 增加两条命令的 bash/zsh/pwsh 补全
- `InteractiveCommand.cs` 帮助菜单加两条
- README + CHANGELOG

#### Step 8 — 真机验证 + smoke 扩展(1d)

- `scripts/smoke-revit2026.ps1` 加 fix dry-run 流程(不要 apply,避免改测试模型)
- 在真实 Revit 2026 上跑:`check → fix --dry-run → fix --apply --yes → check 应少于初始 → rollback`
- 验证 baseline 文件可读、可 rollback
- 失败模式:fix 中途断电?baseline 已写入,可手动 rollback;**这必须文档化**

### 验收标准

- [ ] `revitcli fix --dry-run` 在 starter profile 下输出至少 1 个 strategy 的 plan
- [ ] `fix --apply` 真实修改元素,自动写 baseline,不要求人工触发 snapshot
- [ ] `rollback <baseline>` 能完整还原 fix 前状态(query 验证)
- [ ] 4 个 strategy 各有 ≥3 个测试 fact,综合测试覆盖中断回滚
- [ ] CHANGELOG 标注**与 v1.4 addin 兼容**(只用 set 端点)
- [ ] smoke 脚本扩展成功,在 v1.4 addin 上 dry-run 通过

### 风险与权衡

| 风险                                  | 影响                       | 缓解                                                           |
| ------------------------------------- | -------------------------- | -------------------------------------------------------------- |
| 大模型 fix 中途断网                   | baseline 已写但 fix 半成功 | 强制 baseline 优先;rollback 命令幂等                           |
| fix strategy 覆盖度低                 | 用户失望"还是要手改"       | 第一版只承诺 4 个 strategy,在 README 明确"渐进扩展"            |
| profile 配置错误导致灾难性写入        | 一次错配改全模型门的 Mark  | `--max-changes` 默认 50,profile 可配;>50 强制 `--yes`          |
| Revit 参数名国际化(中文 / 英文 Revit) | 同一规则在两个语言版本失败 | strategy 支持参数别名列表;运行时 fallback;follow up 单独 issue |

### 给 codex 的入口

```bash
# 1. 启动 brainstorm
"用 superpowers:brainstorming skill 设计 auto-fix playbooks 命令形态"

# 2. 写 spec
"参考 docs/superpowers/specs/2026-04-23-model-as-code-design.md 风格,
写 docs/superpowers/specs/2026-{today}-auto-fix-playbooks-design.md"

# 3. 写 plan
"参考 docs/superpowers/plans/2026-04-24-import-csv.md 风格写实施计划"

# 4. 实施
"按 plan 一步一步来,每完成一步先跑 dotnet test 再 commit"
```

---

## 3. v1.6 — History / Trend(时序)

### 价值主张

`snapshot` 当前是单文件单时刻。BIMOps 真正的诉求是**时间序列**:本周比上周改了多少元素?月度模型健康趋势?某个团队成员主导的修改集中在哪类元素?

闭环工作流:

```bash
revitcli history init                     # 一次性初始化 .revitcli/history/
revitcli history capture                  # 手动 / cron 调用,落盘当前快照
revitcli history list                     # 列最近 N 个 snapshot
revitcli history diff @-2 @-1             # 倒数第二个 vs 上一个
revitcli history trend --metric walls     # 过去 30 天墙数量趋势(ASCII sparkline)
revitcli score --history 30d              # 30 天健康度趋势
```

### 范围(In)

- `.revitcli/history/` 目录:按时间排序的 `snapshot-{ISO8601}-{shortHash}.json`(已 gzip)
- 元数据索引:`.revitcli/history/index.json`,记录每个 snapshot 的 size / element counts / docPath / capture source(manual / cron / fix-baseline)
- 命令:`history init / capture / list / diff / prune / trend`
- `score --history <duration>` 扩展,输出时间序列分数
- Prune 策略:profile 配 `history.retention: 90d` 或 `history.maxFiles: 200`
- 与 v1.5 集成:fix-baseline 自动归档到 history(不重复存)

### 范围(Out)

- 多机同步(团队成员各自的 history 不合并)— 留给 v1.9 federation
- 跨项目趋势(看公司所有项目)— v2.0 dashboard 的事
- 完整版本控制(git-like)— 不重造 git,history 只是时间序列

### 关键设计决策

#### 决策 1:存 snapshot 还是 diff?

**候选 A:全 snapshot 存档**(推荐)

- 优势:简单,任意两点可比;snapshot 已 gzip ~1MB / 千元素,30 天 ~30MB 可接受
- 劣势:存储增长

**候选 B:存第一个 snapshot + 后续 diff**

- 优势:存储省
- 劣势:任意两点比对需重建中间状态;v1.1 diff 是结构化的,不是可逆的

**结论:候选 A**,gzip 后存储够小,简单 > 优化。

#### 决策 2:capture 触发模式

**候选 A:纯手动 + 文档化 cron 模板**(推荐)

- 优势:不引入 daemon,跨平台无负担
- 劣势:用户得自己配 cron / 任务计划

**候选 B:内置 watch 守护进程**

- 优势:开箱即用
- 劣势:跨平台复杂(macOS launchd / Linux systemd / Windows Task Scheduler);CLI 进程本应短命

**结论:候选 A**,在 docs 提供三平台 cron 模板,降低门槛。

#### 决策 3:history 是 .gitignore 还是 commit?

**默认 `.gitignore`**(`.revitcli/history/`),但提供 profile 选项 `history.committed: true` 让团队选择 commit。

- gitignore 优势:不污染 git history;cron 自动跑不会触发 commit 噪音
- committed 优势:团队成员开同一项目自动共享 history(不需要中央服务器)

模板默认 gitignore,文档说明两种选择。

### 实施分步

#### Step 1 — Spec + Plan(1d)

- spec:目录结构、index schema、所有命令选项、retention 算法
- plan:文件清单 + 测试矩阵

#### Step 2 — HistoryStore(纯 C#)(1d)

- `src/RevitCli/History/HistoryStore.cs`:封装目录管理 / index 读写 / gzip / 时间引用解析(`@-N` / `7d ago` / ISO 时间戳)
- `src/RevitCli/History/SnapshotMetadata.cs`:索引项 DTO
- 测试:CRUD / 索引重建 / 损坏 index 恢复 / gzip 完整性

#### Step 3 — `history` 命令簇(1d)

- 子命令:`init` / `capture` / `list` / `diff` / `prune` / `trend`
- `capture` 复用 `SnapshotCommand` 的 IRevitOperations 调用,不重写
- `diff` 复用 v1.1 `DiffCommand` 的 renderer,只是输入源改为 history 引用
- 测试:每个子命令一个测试类

#### Step 4 — Trend 渲染(0.5d)

- ASCII sparkline(`▁▂▃▄▅▆▇█`)+ 数值列
- `--metric <name>` 支持:`elements.<category>` / `score` / `sheets` / `schedules` / 任意 `count.<key>`
- 测试:空数据 / 单点 / 上升 / 下降 / 平滑

#### Step 5 — Score 时序扩展(0.5d)

- `ScoreCommand` 增加 `--history <duration>` 选项
- 输出每天的分数(取当天最后一个 snapshot)
- 测试:无 history / 部分天数缺失 / duration 解析

#### Step 6 — V1.5 集成(0.5d)

- fix-baseline 同时写到 `.revitcli/history/` 并在 metadata 标 `source: fix-baseline`
- list 默认隐藏 fix-baseline,`--include-fixes` 显示
- 测试:source 字段 / 过滤行为

#### Step 7 — 文档 + cron 模板(0.5d)

- `docs/history-cron.md`:三平台 cron 模板(crontab / launchd / Task Scheduler)
- README + CHANGELOG

### 验收标准

- [ ] 30 天每天一个 snapshot 后,`history trend` 渲染 sparkline 正确
- [ ] `history diff @-1 @-7` 等价于 `diff` 两个手动 snapshot
- [ ] gzip 后单 snapshot 平均 < 200KB(基于真实 200-元素模型)
- [ ] `prune --keep 30d` 删除超龄文件且不破坏 index
- [ ] cron 模板在 macOS / Windows 各跑一次手动验证

### 风险与权衡

- **磁盘膨胀**:大型企业项目可能 1MB/snapshot,365 天 = 365MB。**缓解**:profile 配 retention,`prune` 默认非 strict
- **时间引用歧义**:`@-1` 是"上次 capture" 还是"上次 fix-baseline"?**结论**:`@-N` 默认按 capture 计数,`--include-fixes` 改语义并提示用户

---

## 4. v1.7 — CI 集成深化(协作)

### 价值主张

让 BIMOps **进入 PR 评审流程**:开发者改 RVT 后 push,GitHub Action 自动跑 `check`,把问题以 PR comment 的形式贴出来,用 SARIF 格式触发 GitHub Code Scanning。

```yaml
# .github/workflows/revitcli.yml
- name: Run RevitCli check
  run: revitcli check --report-format sarif --report report.sarif
- uses: github/codeql-action/upload-sarif@v3
  with: { sarif_file: report.sarif }
```

### 范围(In)

- `check` / `audit` 输出 SARIF 2.1.0 格式(标准化 GitHub Code Scanning 集成)
- 官方 GitHub Action(独立仓库 `revitcli/revitcli-action` 或本仓 `.github/actions/`)
- PR comment 模板生成器:`revitcli check --report-format pr-comment --report comment.md`
- `publish` 受 webhook 已支持;增加 `check` 的 webhook
- `revitcli ci doctor`:检测 CI 环境(Actions / GitLab / Jenkins),输出适配的命令

### 范围(Out)

- 自建 CI 服务 — 不做
- 多种 SCM 平台的 PR 适配 — 第一版只做 GitHub,GitLab/Bitbucket 列入 follow-up
- 实时协作(多用户同时编辑模型)— 完全不做,Revit Cloud Worksharing 走 BIM360

### 关键设计决策

#### 决策 1:SARIF 输出深度

最小 SARIF 需要:`runs[].tool.driver.name` + `runs[].results[]` 含 `ruleId / level / message / locations`。

Revit 元素没有"文件路径行号"概念,location 怎么填?

**候选 A:`physicalLocation` 留空,`logicalLocations` 填 element id**(推荐)

- 优势:语义正确(element 不是文件)
- 劣势:GitHub UI 不会高亮源码,但仍会列出问题

**候选 B:伪装成 `.revitcli.yml` 行号**

- 优势:UI 高亮配置文件
- 劣势:误导,问题不是配置文件的问题

**结论:候选 A**,加 `properties` 携带 `revitElementId` / `revitCategory` / `documentPath`,工具集成可读。

#### 决策 2:GitHub Action 是 docker 还是 composite?

**候选 A:composite action**(推荐)

- 优势:无 docker 拉取延迟;只需 dotnet 环境(Actions 已自带)
- 劣势:每次都要 install CLI

**候选 B:docker action**

- 优势:环境一致,首次拉镜像后秒启动
- 劣势:维护 image registry;镜像大

**结论:composite**。RevitCli 是 dotnet tool,`dotnet tool install` 后秒级启动,docker 价值低。

### 实施分步

1. **SARIF schema + 单元测试**(1d):`src/RevitCli/Reports/SarifWriter.cs` + `--report-format sarif` 选项
2. **PR comment 模板**(0.5d):`--report-format pr-comment`,Markdown 表格风格
3. **GitHub Action 仓**(0.5d):新仓 / 子目录,`action.yml` + README + 示例 workflow
4. **`ci doctor` 子命令**(0.5d):env 探测 + 输出 ready-to-paste workflow
5. **Webhook for check**(0.5d):复用 publish webhook 风格,加 `check` 事件
6. **文档 + 模板项目**(0.5d):`docs/ci/github-actions.md` + 一个 sample 项目仓

### 验收标准

- [ ] 真实 GitHub repo PR 中触发 SARIF,GitHub Code Scanning UI 显示问题
- [ ] PR comment 模板在真实 PR 上 markdown 渲染正确
- [ ] composite action 在 ubuntu-latest / windows-latest 各一次
- [ ] `ci doctor` 在 Actions 内输出环境识别正确

### 风险

- **SARIF schema 变更**:GitHub 偶尔升级 SARIF 验证规则 → 用 schema validation 测试 + CI 跑 GitHub 官方 sarif-multitool
- **Windows 限定的 CI 路径**:如果用户想在 CI 真跑 Revit(不只是 lint profile),需要 self-hosted Windows runner — 文档明确

---

## 5. v1.8 — Family 资产管理(资产)

### 价值主张

Revit 项目有**两类资产**:模型(.rvt)+ 族(.rfa)。前者已被 v1.0–v1.7 覆盖。族文件的痛点:

- 项目里的族版本和族库不一致
- 未使用的族占用模型大小,影响打开速度
- 族参数命名不规范(没法被 `check` 抓到,因为族是另一个文档)

```bash
revitcli family ls                         # 列出当前模型用到的所有族
revitcli family ls --unused                # 未放置的族(可清理)
revitcli family purge --dry-run            # 预览要清理的族
revitcli family purge --apply              # 清理(自动 snapshot baseline)
revitcli family validate --library ./lib/  # 模型用的族 vs 团队族库的版本差异
revitcli family export --to ./out/ --names "门*"  # 批量导出族文件
```

### 范围(In)

- `family` 命令簇:`ls` / `purge` / `validate` / `export`
- addin 新增端点:`/api/families`(list / purge / export)
- DTO:`FamilyInfo` / `FamilyValidationResult`
- profile 扩展:`families.libraryPath` 指向团队族库目录
- `check` 增加规则:`family-naming` / `family-version-drift`

### 范围(Out)

- 族编辑(写入族参数)— 需要 Family Editor API 上下文,巨复杂,放 v3.x
- 族库 registry(中央仓库)— 文件系统 path 起步,registry 是 v1.9 federation 的事
- 族缩略图渲染 — 用户痛点不强,先不做

### 关键设计决策

#### 决策 1:purge 的安全性

Revit 的 `Document.Delete(elementIds)` 删除 FamilySymbol 是不可逆的。

**强制三层保护**:

1. 默认 `--dry-run`,要 apply 必须 `--apply`
2. `--apply` 前自动 snapshot baseline 到 `.revitcli/history/`(标 `source: family-purge`)
3. `--max-purge N`(默认 20),超过强制 `--yes`

#### 决策 2:validate 的版本判定

族文件没有 SemVer。如何判断 "项目族版本 < 库族版本"?

**候选 A:文件 mtime 比较**

- 优势:零侵入
- 劣势:复制族会改 mtime,误报多

**候选 B:族文件 hash**(推荐)

- 优势:精确判断"内容是否一致"
- 劣势:不能告诉你"哪边新",只能告诉你"不一致"

**候选 C:族内置 `Family.Version` 参数**

- 优势:语义清晰
- 劣势:依赖团队约定写这个参数,推广成本高

**结论:候选 B + 文档建议团队加 `FamilyVersion` 参数**,validate 输出 hash 不一致 + (如果有)version 参数差异。

### 实施分步

1. **Spec + Plan**(1.5d):family API 表面探索,Revit Family API 比 Element API 复杂
2. **addin `/api/families` list**(1d):FilteredElementCollector(typeof(Family)) + 序列化
3. **CLI `family ls`**(0.5d)
4. **addin purge**(1d):事务 + Document.Delete + 错误处理
5. **CLI `family purge`** + baseline(0.5d)
6. **CLI `family validate`**(1d):hash 比较 + library path walking
7. **family export**(1d):addin 端 SaveFamilyToFile API
8. **check 规则:family-naming / family-version-drift**(0.5d)
9. **测试 + smoke**(1d)

### 风险

- **Revit Family API 边界条件多**:嵌套族、共享族、参数化族 — 第一版只支持顶层族,嵌套放 follow-up
- **2024 vs 2026 API 差异**:`Family.GetFamilyTypeIds()` 在 2024 是 `Symbols`,API 名变过 — `IRevitOperations` 接口稳定,addin 内部分版本实现

---

## 6. v1.9 — Profile 治理(治理)

### 价值主张

当前 profile 是**单文件 + 单父 REPLACE**。团队规模化时:

- 一个组织的多个项目要共享标准 → 需要中央 profile
- 标准会演化 → 需要 profile 版本化
- profile 写错了要在 CI 失败 → 需要 lint

```bash
revitcli profile validate                  # lint 当前 .revitcli.yml
revitcli profile show --resolve            # 显示完全展开的 profile
revitcli profile diff @v1.0 @v2.0          # profile 版本对比
revitcli profile install company/standards@v2.1  # 从 registry 安装
```

### 范围(In)

- `profile validate`:schema lint + 引用完整性 + 死规则检测
- `profile show --resolve`:把 extends 链路完全展开(当前是隐式的)
- `profile diff <a> <b>`:profile 版本对比(渲染 markdown)
- 多继承:`extends: [base.yml, ./team-overrides.yml]`(数组)
- Deep-merge 选项:`extendsStrategy: replace | deep-merge`(默认仍 replace 保兼容)
- Registry 协议(纯文件系统起步):`extends: ssh://gitlab/...` 或 `extends: https://...`

### 范围(Out)

- 中央 registry 服务(类似 npm registry)— 第一版只支持 git URL / 本地 path
- profile 的 RBAC — 不在工具范围
- profile 的 GUI 编辑器 — v2.0 dashboard 顺手

### 实施分步

1. **Spec**(1d):多继承合并语义(冲突如何报错)、validate 规则集
2. **ProfileLoader 改造**(1d):支持数组 extends,加 cycle detection
3. **profile validate 命令**(1d)
4. **profile show --resolve**(0.5d)
5. **profile diff**(0.5d)
6. **profile install(git URL)**(1d):用 LibGit2Sharp 浅克隆到 `.revitcli/profiles/<name>@<ref>/`
7. **测试 + 文档**(1d)

### 风险

- **REPLACE → deep-merge 兼容**:严格保持默认 REPLACE,deep-merge 是 opt-in,避免破坏 v1.0–v1.8 用户的 profile 行为

---

## 7. v2.0 — Web Dashboard(可视化)

### 价值主张

v1.6 把 history 落盘了,但只有命令行用户看得到。BIMOps 的实际受益者是项目经理 / 团队 lead,他们更接受 Web UI:

- 模型健康度趋势图
- 多项目横向对比
- check 失败热力图
- fix 历史 + 谁修了什么

### 范围(In)

- 静态 SPA(SvelteKit / Astro 候选,**不引入服务端**)
- 数据源:`.revitcli/history/` 目录(本地)或 git-tracked history(团队)
- 渲染:三个核心视图
  1. **Overview**:当前 score / 元素分类条形图 / 最近 7 天 sparkline
  2. **History**:全时序图 + diff 详情表
  3. **Multi-project**(可选):扫描磁盘上所有 `.revitcli/`,横向对比
- `revitcli dashboard serve --port 8080`:本地静态服务
- `revitcli dashboard build --output ./public/`:产出可部署到 GitHub Pages 的静态站

### 范围(Out)

- 服务端实时编辑 — 不做,dashboard 只读
- 用户认证 — 静态站,认证靠部署平台(GitHub Pages 私有仓)
- 直接连 Revit 操作 — UI 只展示,改动还是走 CLI

### 关键设计决策

#### 决策 1:技术栈

**候选 A:SvelteKit**(推荐)

- 优势:静态导出成熟;bundle 小;学习曲线浅
- 劣势:作者基数比 React 小

**候选 B:Astro**

- 优势:静态优化;岛屿组件
- 劣势:动态图表交互需要额外 hydration 配置

**候选 C:React + Vite**

- 优势:生态最大;招人易
- 劣势:bundle 大;静态导出需自配

**结论:SvelteKit + adapter-static**。本工具不需要大社区,bundle 小更重要。

#### 决策 2:可视化库

**Apache ECharts**(成熟、文档齐、复杂图表)
**Chart.js**(轻量、上手快)
**D3**(灵活但开发成本高)

第一版用 **Chart.js**,够用;复杂场景升级到 ECharts。

### 实施分步

1. **Spec + 设计稿**(2d):线框图 + 数据契约
2. **静态站脚手架**(1d):SvelteKit + adapter-static + Tailwind
3. **History 数据加载层**(1d):浏览器读 history JSON(用户提供路径或上传)
4. **Overview 页**(2d)
5. **History 页**(2d)
6. **Multi-project 页**(可选,2d)
7. **`dashboard serve` 命令**(0.5d):内置 Kestrel 静态文件服务
8. **`dashboard build` 命令**(0.5d):copy 预构建 SPA + 注入 history 路径配置
9. **CI 部署到 GitHub Pages 模板**(0.5d)
10. **测试**(1d):静态站的 Playwright e2e

### 风险

- **维护面扩大**:多了一个前端栈,贡献者门槛高 — 文档独立,prebuilt 静态资源放 release artifact,普通贡献者不需要改前端
- **数据隐私**:dashboard 静态加载本地 history,但如果发布到 GitHub Pages 可能曝光 — 默认 `gh-pages` 模板带 `noindex` + 文档强提醒

---

## 8. 可并行 Track — MCP Adapter

### 定位

**侧线**,不进 milestone 主线。任何 milestone 完成后的小空隙(1-2 天)都可以做。

把 `revitcli` CLI 命令薄封装成 MCP server,对接 Claude / Cursor 等 agent。

```
LLM Client (Claude Desktop / Cursor)
    ↕ MCP stdio
revitcli mcp serve  ← 新子命令,内部调用 RevitClient
    ↕ HTTP
Revit Add-in
```

### 范围

- `revitcli mcp serve`:启动 MCP stdio server
- 工具映射:每个 CLI 命令暴露成一个 MCP tool
- schema:基于 `CliCommandCatalog` 自动生成
- 资源:`revitcli://snapshot/latest` / `revitcli://history/`

### 工程量

- spec + plan:0.5d
- MCP server 实现:1d(用 ModelContextProtocol .NET SDK 时已较成熟)
- tool schema 自动生成:1d
- 测试:0.5d
- 文档 + 三个 Client(Claude / Cursor / Continue)的接入示例:0.5d

**总:约 3.5d**,适合在两个 milestone 之间作为缓冲。

### 不做

- 不做"AI 代写 profile" / "自然语言查模型" 等高级 agent 编排,这些是 client 端的事

---

## 9. 跨阶段全局约束

每个 milestone 都必须满足:

### 1. 兼容性

- `IRevitOperations` 接口变更必须**附加而非破坏**(新方法 OK,删除/改签名 NG)
- profile schema 改动必须保留 `version: 1` 解析路径,新增字段必须有默认值
- snapshot `schemaVersion: 1` 不动,任何字段 v1.4 之后只能附加
- v1.4 addin + v1.5 CLI 必须能跑老 profile;反向不要求

### 2. 跨平台

- CLI 端代码必须在 macOS / Linux 编译过
- Windows-only 路径用 `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` 守护
- 任何使用 `\\` 路径分隔符的代码不进 main

### 3. 测试

- 每个新命令至少 5 个测试 fact(happy + 4 个 edge)
- 每个新 strategy / planner / writer 至少 3 个测试 fact
- 涉及 Revit API 的部分**禁止 mock 数据库**(违反用户全局规则);写到 addin 端的真实集成测试,在 smoke 脚本里跑

### 4. 文档

- 每个 milestone 配 `docs/superpowers/specs/` + `docs/superpowers/plans/`
- README "Commands" 表格必须更新
- CHANGELOG 必须列 Added / Changed / Backward compatibility / Known carry-forward / E2E verification

### 5. 性能基线

- `snapshot` 100-sheet 模型 < 30s
- `check` < 5s 启动 + 1s/100 元素
- `fix --dry-run` < 2 × `check` 时间
- history `prune` 1000 文件 < 1s

### 6. 安全

- 所有写路径(set / import / fix / family purge)默认 dry-run
- 路径输出参数(`--output-dir` / `--baseline-output`)继续受限于用户家目录(已有保护)
- profile 远程引用(v1.9)必须支持 hash pin

### 7. 真机验证(必须)

每个 milestone 在 Windows + Revit 2026 真实跑 `scripts/smoke-revit2026.ps1` 全流程 + 新命令 happy path,失败不许合并。

---

## 10. codex 工作流接入指南

这份 roadmap 是路标,**不是直接施工图**。codex 进入每个 milestone 时,按下面的顺序操作:

### Step A — 明确目标

读这份 roadmap 的对应章节,以及:

- README.md
- 上一版本的 `docs/superpowers/plans/<latest>.md`(学风格)
- CHANGELOG 最近 3 个 release(学语气和验收标准结构)

### Step B — 强制 brainstorm

```
使用 superpowers:brainstorming skill 走一遍这个 milestone 的设计方案
```

输出预期:

- 至少 3 个候选方案,每个含优势 + 劣势
- 推荐方案 + 推荐理由
- 与现有架构的冲突点 + 兼容策略

### Step C — 写 spec

参考最近的 spec 文件结构(`docs/superpowers/specs/2026-04-23-model-as-code-design.md` 是最完整的范本):

1. 价值主张 + 用户故事
2. 命令矩阵 + 选项 + 退出码
3. DTO / schema 定义
4. 错误路径列表
5. 兼容性策略
6. 性能预算
7. 验收标准清单

### Step D — 写 plan

参考最近的 plan 文件结构(`docs/superpowers/plans/2026-04-24-import-csv.md` 体量最接近)。**Plan 必须有**:

- 完整文件清单(每个文件做什么)
- 测试矩阵(每个测试 fact 一行)
- 实施分步(可独立 commit 的粒度)
- 真机验证步骤

### Step E — 实施

每个 commit 必须:

- 编译过
- 跑过 `dotnet test`
- 不破坏既有测试
- commit 消息符合 Conventional Commits(`feat:` / `fix:` / `test:` / `docs:` / `chore:`)

实施过程中遇到设计决策需要二次确认时,**回头改 spec**,不要在代码里悄悄 deviate。

### Step F — Code review 闭环

完成后必须:

- 跑完整 `dotnet test`
- 跑 `scripts/smoke-revit2026.ps1`(Windows + Revit 2026)
- 用 superpowers `requesting-code-review` skill 自查
- 如果是大改,跑 `/ultrareview` 让云端多 agent 评审

### Step G — Release

- 更新 README "Commands" 表格 + Roadmap 勾选
- 写 CHANGELOG 段(学最近的 1.3.0 / 1.2.0 风格)
- 更新 csproj 版本号
- 创建 PR(参考 PR #5 的 body 风格)
- merge 后打 tag,GitHub Actions 自动发 NuGet

---

## 11. 不在路线图内(明确边界)

为防止 scope 膨胀,**以下事项明确不做**:

- **几何修改命令**(`move-wall` / `align-doors` 等)— 风险过大,不在 BIMOps 范围
- **族编辑写入**(改族参数、改族几何)— 需要 Family Editor 上下文,工程量爆炸
- **跨文档操作**(同时操作多个 RVT)— 单 active document 假设是基础,改它会冲击全部命令
- **实时多人协作** — Revit Cloud Worksharing / BIM360 是 Autodesk 的范畴
- **AI 自动写 profile / 自然语言查询** — 这是 Claude / Cursor / Continue 等 client 的事,不是 CLI 的事
- **Revit 之外的 BIM 软件**(ArchiCAD / Bentley)— 项目名就叫 RevitCli
- **GUI 应用**(独立的桌面 app)— 反 BIMOps 定位

---

## 12. 参考文档索引

### 必读

- `README.md` — 项目当前能力总览
- `CHANGELOG.md` — release 风格和验收标准范例
- `docs/superpowers/specs/2026-04-23-model-as-code-design.md` — 最完整的 spec 范本(621 行)
- `docs/superpowers/plans/2026-04-24-import-csv.md` — 最完整的 plan 范本(1978 行)

### 选读

- `docs/revit2026-real-smoke.md` — 真机验证脚本契约
- `docs/revitcli-shortest-roadmap.md` — v0.1 时期的最短路线图(历史)
- `profiles/*.yml` — 三个 starter profile,扩展 profile schema 时的回归基准
- `scripts/install.ps1` — 端到端安装脚本(v1.4 候选)
- `scripts/smoke-revit2026.ps1` — 真机烟雾门(v1.4 候选)

### 全局规则(从用户偏好提炼,与项目无关但执行时必守)

- 自然语言输出**简体中文**
- 多方案输出**必须含最强方案**(理论最优,标注代价)
- 每个方案**列优势 + 劣势/风险**,禁止只讲好处
- 不偏题、无事实错、逻辑闭环
- 推荐工具/依赖前先验证兼容性,不要凭过时训练数据猜
- Python 用 `uv` 隔离;Homebrew 操作前预演依赖连锁

---

_文档版本:1.0(2026-04-26)_
_下次评审:每完成一个 milestone 后回顾,调整后续 milestone 的范围或顺序_
