# RevitCli 创新点深度展开:B 级 + C 级

> 2026-04-29 复审:本文已归档,不再代表当前路线图。
> 当前方向见 [`docs/roadmap-2026q4-v4.md`](./roadmap-2026q4-v4.md)
> 和 [`docs/codex-cli-architect-workflows.md`](./codex-cli-architect-workflows.md)。
> 可保留"让 Codex CLI 更容易调用普通 RevitCli 命令"的部分启发,
> 但 MCP 主线化、agent-native 协议栈、`--output agent` 默认化都不再推进。

> 用途:为 codex 后续执行准备的详细设计入口;每个 B 级条目都达到"可直接做 spec"的细度,C 级保持战略级(可决策但不立即落地)
> 创建:2026-04-26
> 配套:[ideation-agent-native.md](./ideation-agent-native.md)(A 级 + 总览) · [narrative.md](./narrative.md)(对外叙事) · [roadmap-2026q2-q3.md](./roadmap-2026q2-q3.md)(技术路线)

---

## 〇、B/C 级在整体路线图中的位置

### 与 A 级的关系

A 级 = 协议栈三层主干(IO / 状态 / 演进)
B 级 = 协议栈的**支撑组件 / 增强能力**(让主干更精确、更经济、更智能)
C 级 = 战略级**新维度**(可能定义品类,但风险或工程量足以独立成项)

```
                   A 级:三层协议栈主干
                          ↓
            B 级:让协议栈更好用的支撑组件
            ├─ B1 协商  ├─ B2 意图  ├─ B3 钻取
            ├─ B4 推荐  ├─ B5 约束  ├─ B6 估时
            ├─ B7 review总结  ├─ B8 跨项目索引
                          ↓
            C 级:开辟新维度(高风险高回报)
            ├─ C1 多 agent 协调
            ├─ C2 自然语言层
            ├─ C3 实时陪坐
            └─ C4 联邦学习
```

### 与 v1.5-v2.0 milestone 的咬合

| Milestone      | 嵌入的 B 级条目                                                   | 备注                 |
| -------------- | ----------------------------------------------------------------- | -------------------- |
| v1.5 Auto-fix  | B5 部分(constraint 作 fix 的高级层)                               | B5 完整版可独立 v2.x |
| v1.6 History   | B4 Suggestion 的数据来源 / B6 Time Estimate 的 baseline           |                      |
| v1.7 CI        | B7 Diff Review 用作 PR comment 模板                               |                      |
| v1.8 Family    | B8 Cross-Project Index 顺手做(family 跨项目索引天然契合)          |                      |
| v1.9 Profile   | B2 Intent Templates 与 profile 共生(intent 是 profile 的执行实例) |                      |
| v2.0 Dashboard | B7 Diff Review / B4 Suggestion 都需要可视化呈现                   |                      |

### 阅读建议

- **codex 启动一个新 spec 时**:先读对应条目的"深入设计"+"实施分步",再回看 A 级文档对齐协议栈位置
- **决策投不投入某条目时**:看"工程量 / 风险"+"前置依赖"
- **想增删条目时**:先看"内在统一逻辑"(它是协议栈哪一层的支撑),不在体系内的提案默认拒绝

---

## 一、B 级条目深度展开

每条结构:**痛点 → 创新核心 → 深入设计 → 优势 / 劣势 → 实施分步 → 验收 → 风险 → 工程量 → codex 入口**

---

### B1. Negotiation Mode(JSONL 协商)

#### 痛点

agent 调 CLI 时,模糊输入常见三种处理方式:

1. CLI 报错让 agent 重试 — **token / 延迟双重浪费**
2. CLI 凭默认假设猜 — **改的是真实模型,代价高**
3. CLI 强制 dry-run + agent 解析输出再决定 — **多一次往返**

缺少的是**结构化的中间状态**:CLI 知道自己缺什么信息,但没法标准化地问 agent。

具体场景:

- `set walls --param Height --value 3000` — `Height` 单位是 mm 还是 m?
- `set doors --param Mark --value W01` — `Mark` 这个参数有 3 个同名(实例 / 类型 / 共享),改哪个?
- `import doors.csv --on-missing error` — CSV 有 5 行 Mark 在模型里找不到,真要 error 吗还是 warn?
- 大批量 `set` 触发 50 次以上改动 — 默认提示二次确认

每一种现在要么报错让 agent 重试,要么静默用默认值。**信息丢失,token 浪费**。

#### 创新核心

命令支持 `--interactive-jsonl` 标志,启用后命令进入"半互动"模式:

- 遇到不确定输入,**从 stdout 发出 JSONL 问题**
- agent **从 stdin 写 JSONL 应答**
- CLI 收到应答后继续执行
- 协议消息是结构化的,机器可解析,**不依赖自然语言**

#### 深入设计

##### 协议消息类型(第一版 4 种,后续可扩)

```jsonc
// 1. clarify:开放性澄清
{
  "id": "q1",
  "type": "clarify",
  "question": "参数 'Mark' 在 doors 上有 3 个同名,选哪个?",
  "options": [
    { "id": "instance", "label": "实例参数 'Mark'" },
    { "id": "type",     "label": "类型参数 'Mark'" },
    { "id": "shared",   "label": "共享参数 'Mark' (project-wide)" }
  ],
  "urgency": "blocking",
  "default": "instance",
  "default_after_seconds": 0  // 0 = 不超时;>0 = 超时取 default
}

// 2. confirm:yes/no
{
  "id": "q2",
  "type": "confirm",
  "question": "即将修改 87 个元素,超过 max-changes=50 阈值,继续?",
  "default": false,
  "urgency": "blocking"
}

// 3. pick_one:从列表选一项(类似 clarify 但带索引)
{
  "id": "q3",
  "type": "pick_one",
  "question": "找到 5 个匹配 Mark='W01' 的元素,改哪个?",
  "options": [
    { "id": "1", "label": "Wall #12345 — 1F-N-W01", "preview": {...} },
    { "id": "2", "label": "Wall #67890 — 2F-N-W01", "preview": {...} }
  ],
  "default": null,
  "urgency": "blocking"
}

// 4. free_text:自由输入(谨慎使用)
{
  "id": "q4",
  "type": "free_text",
  "question": "请提供日志中提及的 view name(找不到 'A1-Plan'):",
  "default": null,
  "urgency": "blocking"
}
```

##### Agent 应答格式

```jsonc
{
  "id": "q1",
  "answer": "instance",
  "confidence": 0.85, // 可选,CLI 端低于阈值可触发二次确认
  "reasoning": "default Revit semantics for Mark", // 可选,记 journal 用
}
```

##### 协议生命周期

```
CLI 启动 → 读取 stdin 一直到第一条消息(可空)
       → 进入主流程
       → 遇到协商点:写一条 question 到 stdout(单行 JSONL,带 \n)
       → flush stdout
       → 阻塞读 stdin 一行,parse JSON
       → 应用 answer,继续
       → 主流程结束:写终态 result 到 stdout
       → 退出
```

##### 一致性约定

- 协商消息一律 **单行 JSON + `\n` 结尾**(JSONL),便于 line-buffered 处理
- 每条消息含 `__protocol__: "revitcli-jsonl-v1"`(版本协商)
- 普通输出(非协商)用 `--output agent` 时也是 JSONL,但不带 `__protocol__` 字段
- 区分:`type: "result"` 是命令最终结果,`type: "clarify|confirm|pick_one|free_text"` 是协商问题,`type: "log"` 是日志事件

##### Fallback 行为

- 用户没传 `--interactive-jsonl` → CLI 永远不发协商消息,遇到模糊用默认 + warn
- 用户传了但 stdin 不可读(管道关闭)→ 走 `default`,记 journal 标 "fallback used"
- 协议版本不匹配 → CLI 报错并退出

#### 优势

- **大幅降低重试 / token 浪费**:模糊点一次解决,无需多轮试错
- **比单纯 dry-run 更精准**:dry-run 只能告诉"会改什么",协商能解决"应该改什么"
- **对人类用户也能升级体验**:命令本来就支持交互式 prompt,只是协议变了
- **agent 测试可重放**:把协商消息和应答记录下来,改 prompt 后重放验证

#### 劣势

- **每个支持协商的命令都要改造**:不是单纯改输出格式,要把"卡点"提取成协商点
- **协议演进会让 v1.x 和 v2.x agent 不兼容**:必须在协议里带版本号
- **测试复杂度上升**:状态机测试,要模拟 agent 应答时序
- **多个并发协商**:第一版串行,未来需要时支持多 question 并行收集

#### 实施分步

##### Step 1 — Spec(0.5d)

`docs/superpowers/specs/2026-{date}-jsonl-negotiation-design.md`:

- 协议消息 schema(JSON Schema 形式)
- 兼容性规则(版本协商、缺字段处理)
- 选定试点命令(推荐 `set`)
- 卡点清单(参数同名、单位、超阈值、ID 不存在等)

##### Step 2 — 共享层 DTOs(0.5d)

`shared/RevitCli.Shared/Negotiation/`:

- `NegotiationMessage`(基类)/ `ClarifyQuestion` / `ConfirmQuestion` / `PickOneQuestion` / `FreeTextQuestion`
- `NegotiationAnswer`
- `[JsonPropertyName]` 全部齐
- 测试:序列化/反序列化 / 缺字段 / 版本号

##### Step 3 — Negotiator 抽象(0.5d)

`src/RevitCli/Negotiation/`:

- `INegotiator` 接口:`Task<TAnswer> AskAsync<TAnswer>(NegotiationMessage q, CancellationToken ct)`
- `JsonlNegotiator` 实现:stdin/stdout 读写,line-buffered
- `DefaultNegotiator` 实现:不交互,直接走默认值
- `Program.cs` 根据 `--interactive-jsonl` 选择实现

##### Step 4 — Set 命令改造(1d)

`src/RevitCli/Commands/SetCommand.cs`:

- 卡点 1:多个匹配的同名参数 → `clarify`
- 卡点 2:超 `max-changes` 阈值 → `confirm`
- 卡点 3:单位歧义 → `clarify`
- 卡点 4:`--id` 找不到对应元素 → `pick_one`(给类似 ID 候选)

每个卡点自己有单元测,加一个集成测(`SetCommandJsonlTests`)模拟 agent 应答。

##### Step 5 — Import / Fix 跟进(每个 0.5d,可分次做)

按 set 模板改造,卡点列表各自不同。

##### Step 6 — 文档 + agent 接入示例(0.5d)

`docs/agent-integration/jsonl-negotiation.md`:

- 协议规范
- 三种 client 的接入示例(Claude SDK、OpenAI Function Calling、Cursor MCP)
- 故障排查

#### 验收标准

- [ ] `set walls --param Mark --value W01 --interactive-jsonl` 在多歧义情况下发出 clarify 问题
- [ ] agent simulator 测试:对每种 question 类型 happy path + 超时 + 协议错误
- [ ] `--interactive-jsonl` + 静默 stdin → 走 default,journal 留痕
- [ ] 至少 3 个命令(set / import / fix)接入
- [ ] 协议版本字段在所有消息中存在,bumping 到 v2 时 CLI 拒绝 v1 应答

#### 风险与权衡

| 风险                        | 影响                | 缓解                                 |
| --------------------------- | ------------------- | ------------------------------------ |
| 协议太重(消息类型过多)      | 难推广,agent 难实现 | 第一版只 4 种类型,扩展按需           |
| 协议太轻(只能 yes/no)       | 不够用,agent 还得猜 | clarify + pick_one 解决多分支        |
| 长时间无应答                | 命令挂起            | `default_after_seconds` 触发 default |
| 多 agent 并行调同一 session | 应答错位            | 第一版禁用并行;`id` 字段强制匹配     |

#### 工程量

中,3-4 周(spec 0.5d + 实现 6-8d + 测试 + 文档)

#### codex 入口

```
1. 用 superpowers:brainstorming 复核协议设计,特别是 question 类型边界
2. 写 spec:docs/superpowers/specs/2026-{date}-jsonl-negotiation-design.md
3. 写 plan:docs/superpowers/plans/2026-{date}-jsonl-negotiation.md
4. 实施:Step 1 → 6,每个 step 独立 commit
5. 真机验证:在 set 命令上跑端到端,从 Claude Desktop 模拟 agent 应答
```

---

### B2. Intent Templates(意图模板)

#### 痛点

agent 拿到工程师的"把北侧 3 楼以上窗户改成节能型号",得自己拼:

```bash
revitcli query windows --filter "Level >= 3 AND Orientation = North" --output json
# 解析输出,提取 ID 列表
revitcli set --ids-from /tmp/ids.txt --param Type --value EnergyEfficient
```

每次都要拆,**容易出错**(filter 语法、参数名、单位转换)。工程师每次都要重复教 agent 同样的拆解模式。

更深层:**agent 的"多步推理"成本和工程师的"重复劳动"成本被各自承担,而它们本质是同一个意图**。

#### 创新核心

`revitcli intent` 命令族,接受**结构化意图**:

- 意图 schema 描述"我要做什么",CLI 内部编排执行多步
- agent 只需把自然语言 → 意图 JSON,不需要拼命令
- CLI 把意图 → 命令序列,内部自带最佳实践
- **CLI 不内置 LLM**,只提供意图模板和编排引擎

#### 深入设计

##### Intent schema 例子

```yaml
# 内置 template:bulk-update-parameter
intent: bulk-update-parameter
filter:
  category: windows
  where:
    - { field: Level, op: gte, value: 3 }
    - { field: Orientation, op: eq, value: North }
action:
  param: Type
  value: EnergyEfficient
policies:
  on-empty: warn # warn | error | skip
  max-changes: 100
  require-confirm: true # 让 B1 协商触发 confirm
  with-baseline: true # 自动 snapshot 基线
```

##### 内置 template 第一版(8 个)

| Template ID               | 用途                  | 编排                                                   |
| ------------------------- | --------------------- | ------------------------------------------------------ |
| `bulk-update-parameter`   | 批量改参数            | filter → preview → confirm → set                       |
| `bulk-rename`             | 按 regex 重命名       | filter → 渲染新名 → preview → set                      |
| `batch-export-by-pattern` | 按 sheet 命名规则导出 | publish 包装                                           |
| `pre-issue-checklist`     | 出图前清单            | check → fix → snapshot → diff @-1                      |
| `monthly-health-report`   | 月度健康报告          | snapshot → diff(过去 30d) → score → 渲染 markdown      |
| `migrate-family`          | 族迁移(库版本升级)    | family validate → identify users → set type → snapshot |
| `room-data-completion`    | 房间数据补全          | check missing → import CSV → check again               |
| `purge-unused-types`      | 清理未用类型          | family ls --unused → confirm → purge                   |

每个 template 在 `RevitCli.Intents.BuiltIn/` 下一个独立类,实现 `IIntentTemplate.Execute(...)`。

##### 自定义 template

放 `.revitcli/intents/<name>.yml`,schema 一致。优先级:本地 > 项目 > 内置。

##### 与 workflow(A3)的关系

- **workflow** = 步骤流(每个步骤是一条命令,含条件 / 跳转)
- **intent** = 单步意图(语义高级,内部编排 N 条命令)
- **intent 可作为 workflow 的步骤**:`workflow.yml` 里 `run: revitcli intent run bulk-update-parameter --params ...`
- 边界:intent 是"一个事",workflow 是"一串事"

##### 命令矩阵

```bash
revitcli intent list                       # 列出所有 template(内置 + 本地)
revitcli intent show <name>                # 显示 template schema 和说明
revitcli intent run <name> [--params ...]  # 执行 template
revitcli intent run --file ./intent.yml    # 从文件执行(ad-hoc)
revitcli intent validate ./intent.yml      # lint
```

##### Agent 用法

```jsonc
// agent 把自然语言转成
{
  "intent": "bulk-update-parameter",
  "params": {
    "filter": { "category": "windows", "where": [...] },
    "action": { "param": "Type", "value": "EnergyEfficient" }
  }
}

// 调用
revitcli intent run --json - < intent.json
```

#### 优势

- **agent 学一次模板,所有项目复用**
- **工程师能审 intent JSON**(结构化,易读,可签入 git)
- **意图集中**:同一意图的最佳实践在 template 里集中维护,不分散在 N 处
- **可作为 workflow 的原语**,提升 workflow 的语义层次

#### 劣势

- **模板覆盖有限**,长尾场景仍需 raw 命令(预期 80% 用 template,20% 用 raw)
- **跟 workflow 边界模糊**,需要 doc 明确分工
- **schema 演进**:一旦发布 template schema,改动要兼容
- **agent 选 template 困难**:8 个 template,agent 怎么选?需要 `intent suggest` 配套

#### 实施分步

1. **Spec**(1d):template 列表 + schema + 与 workflow 关系
2. **Intent 引擎**(1d):`IIntentTemplate` 接口 + 加载器 + 执行器
3. **8 个内置 template**(每个 0.3-0.5d):每个独立类 + 测试
4. **CLI 命令**(0.5d):`intent list/show/run/validate`
5. **Schema validator**(0.5d):YAML schema + JSON Schema 双轨
6. **`intent suggest`**(0.5d):基于关键词推荐 template(简单关键词匹配,不用 LLM)
7. **测试**(1d):每个 template 一个集成测
8. **文档**(0.5d):`docs/intents/builtin.md` + 自定义 template 教程

#### 验收标准

- [ ] 8 个内置 template 全部可执行
- [ ] `intent run --file ./custom.yml` 能跑自定义 template
- [ ] `intent validate` 能 catch schema 错误
- [ ] `intent suggest "出图前检查"` 推荐 `pre-issue-checklist`
- [ ] 与 workflow(A3)有至少 1 个集成测试

#### 工程量

中,3-4 周

#### codex 入口

```
读 docs/superpowers/specs/2026-04-22-schedule-command-design.md(类似的"命令簇"风格)做参考,然后写 intent spec
```

---

### B3. Progressive Disclosure(分层展开)

#### 痛点

`query walls` 一次返回 200 个,token 浪费严重。agent 不知道总量先就被淹了。

#### 创新核心

`--summarize` / `--by <field>` / `--drill <key>` 三件套:

- `query --summarize` → 只回总数 + 标量统计
- `query --summarize --by Type` → 按类型分组,每组数量
- `query --summarize --by Type --drill "WallType:外墙-200mm"` → 钻取该组的元素列表(分页)

#### 深入设计

##### 输出对比

```bash
# 全量(现状)
$ revitcli query walls --output json
[{ ... 200 个完整元素 ... }]

# 仅摘要
$ revitcli query walls --summarize --output agent
{
  "summary": "186 walls; 92% are exterior; height range 2.4-4.5m",
  "stats": {
    "total": 186,
    "by_type": { "外墙-200mm": 92, "内墙-100mm": 78, "玻璃幕墙": 16 },
    "height": { "min": 2400, "max": 4500, "median": 3000, "unit": "mm" }
  },
  "next_actions": [
    { "command": "revitcli query walls --summarize --by Type --drill 外墙-200mm" }
  ]
}

# 钻取
$ revitcli query walls --summarize --by Type --drill "外墙-200mm" --output agent
{
  "summary": "92 walls of type '外墙-200mm'",
  "items": [/* 前 20 项,带 next 分页指针 */],
  "more": { "total": 92, "shown": 20 }
}
```

##### 与 A1 的关系

B3 实质上是 A1 的子集——**A1 设计时把 progressive disclosure 内建即可**,不必独立条目。

但作为独立条目仍有价值:**对人类 CLI 用户也有用**(`query | grep` 太粗糙;`query --summarize --by Mark` 直接看分布)。

#### 实施

合并到 A1 实施,**不单独立项**。提取出的独立工作:

- `--summarize` 的标量统计算法(median / percentile / 分布)
- `--by` 字段的 schema 提取(哪些字段可分组)
- 中文字段支持(`Mark` / `名称` / `类别` 同等)

#### 工程量

小,1 周(合并到 A1)

---

### B4. Suggestion Engine(主动建议)

#### 痛点

agent 只能被动响应工程师指令。但 BIM 工作流里,**"接下来做什么" 是工程师自己也常问的问题**。

具体场景:

- 工程师早上打开项目,不知道今天该处理哪些积压问题
- 资深工程师离职后,新人不知道这个项目的"通常流程"是什么
- 项目阶段从 SD → DD,该启动哪些标准检查?

agent 不主动告诉,工程师就靠记忆 / 经验。

#### 创新核心

`revitcli suggest` 主动列出"基于当前状态推荐做的事":

- 数据来源:check 结果 + history 趋势 + profile 阶段声明 + workflow 频率
- 输出:优先级排序的建议清单
- 每条建议都带 "为什么推荐" + "执行命令"

#### 深入设计

##### 启发式来源

```
启发式 #1 — 失败检查 → 推荐 fix
  if check 有 failure: suggest "fix --rule <rule>"
  priority: HIGH

启发式 #2 — history trend 异常 → 推荐查根因
  if score 在过去 7 天降幅 > 10%: suggest "history diff @-7 @latest --review"
  priority: MED

启发式 #3 — profile 阶段切换 → 推荐阶段标准动作
  if profile.stage 已变 (如 SD → DD) 但对应 workflow 未跑: suggest "workflow run dd-checklist"
  priority: HIGH

启发式 #4 — 例行工作流久未跑
  if workflow 上次跑距今 > workflow.expected_interval: suggest "workflow run <name>"
  priority: MED

启发式 #5 — 上游变更
  if profile / library 改了但项目未同步: suggest "family validate --library ..."
  priority: LOW

启发式 #6 — 知识录制提示
  if 工程师 30 分钟内手动跑 N+ 次相同命令: suggest "learn start" 录制
  priority: LOW(配合 A4)

启发式 #7 — 跨项目模式
  if 同团队其他项目近期跑过某 workflow,本项目未跑: suggest "workflow run <name>"
  priority: LOW(配合 B8 跨项目索引)
```

##### 输出格式

```
$ revitcli suggest

Suggested next steps for project "Office-Tower-A":

[HIGH] 12 doors 缺 Mark
       why: check 结果 + project profile 要求 Mark non-null
       run: revitcli fix --rule naming --dry-run
       est: 30s

[HIGH] 进入 DD 阶段后未跑 dd-checklist
       why: profile.stage = DD,workflow.dd-checklist.last_run = null
       run: revitcli workflow run dd-checklist
       est: 5min

[MED]  上次 publish 已 7 天 (expected weekly)
       why: workflow.weekly-issue last_run = 7d ago
       run: revitcli workflow run weekly-issue

[LOW]  族库 ./lib/ 有 3 个族版本更新
       why: library mtime 改于 2 天前,本项目 family snapshot 未同步
       run: revitcli family validate --library ./lib/

[LOW]  你今天手动改了 23 次 'FireRating' — 要不要录成 fix playbook?
       why: 30min 内 23 次相似 set,A4 录制器建议
       run: revitcli learn start --hint "fire-rating-batch"
```

##### 命令选项

```bash
revitcli suggest                 # 全部启发式
revitcli suggest --priority HIGH # 只看高优先级
revitcli suggest --topic safety  # 按主题(safety / quality / output / cleanup)
revitcli suggest --output agent  # 给 agent
```

##### Agent 用法

```jsonc
// agent 调用
{ "suggest": [...] }

// agent 决策:对每条建议,要不要执行?
// - HIGH 自动执行(跟工程师确认 confirm)
// - MED 等工程师明确要求才执行
// - LOW 仅展示
```

#### 优势

- **把工具从被动 → 主动**,跟 agent 模式天然契合
- **启发式可逐步增强**,从静态规则 → 学习
- **降低 agent 编排负担**:agent 不用想"接下来该干啥",CLI 直接告诉
- **新人 onboarding 友好**:打开 project,suggest 告诉你"通常该做什么"

#### 劣势

- **误导性建议会失去信任**:建议错的事,用户下次就不看了
- **优先级算法持续调**:HIGH/MED/LOW 阈值要 tune
- **启发式之间可能矛盾**(#1 推荐 fix,#3 推荐 workflow,谁先?)
- **数据依赖**:启发式需要 history(v1.6)+ workflow 状态(A3)

#### 实施分步

1. **Spec**(1d):启发式清单 + 优先级公式 + 输出 schema
2. **Suggester 框架**(1d):`ISuggester` 接口 + 启发式 plug-in 加载
3. **每个启发式**(每个 0.3d × 7 = 2d):独立类 + 测试
4. **优先级合并器**(0.5d):同时多启发式触发同元素时的去重 / 合并
5. **CLI 命令**(0.5d):`suggest` 主命令 + `--priority/--topic`
6. **Agent output 适配**(0.5d):next_actions 格式
7. **测试**(1d):每个启发式 + 综合场景

#### 验收标准

- [ ] 7 个启发式全部可独立触发
- [ ] 启发式互斥时优先级合并正确(同问题不重复推荐)
- [ ] `suggest --output agent` 输出 schema 一致
- [ ] 集成测试:模拟一个 project state,suggest 输出符合预期

#### 工程量

中,3-4 周

#### codex 入口

依赖:v1.5(fix)+ v1.6(history)+ A3(workflow)。**v1.7 之后做最自然**。

---

### B5. Constraint Solver(约束求解)

#### 痛点

`set` 是单向写,改完一次后没法保证状态持续合规。profile 里能描述"应该是什么样",但工具只能 check + alert,**不能 enforce**。

具体:

- profile 里写"外墙必须是防火等级 A1 或 A2",`check` 能告诉你哪些违反,但不能自动修
- v1.5 fix 是"按规则修复一次",但是命令式的;若稍后又有新元素违反,得再 fix 一次
- 工程师真正想要的是 desired-state:"我声明这个状态,你保证它"

类似的成熟方案:Terraform / Ansible / Kubernetes Operator — 都是 declare → reconcile。

#### 创新核心

`revitcli enforce`:

- 输入:**约束声明**(profile 里的 constraints 节点)
- 行为:差异检测 → 自动修正方案 → dry-run 或 apply
- 可单次执行,也可 watch 模式持续监听

#### 深入设计

##### Constraint schema(在 profile 里)

```yaml
# .revitcli.yml
constraints:
  - name: external-walls-must-be-fire-rated
    where: { category: walls, field: IsExterior, value: true }
    assert:
      - { field: FireRating, op: in, values: [A1, A2] }
    enforce_strategy: setParam # 与 v1.5 fix strategy 共用
    enforce_value: A2 # 默认值
    severity: error

  - name: rooms-must-have-numbers
    where: { category: rooms }
    assert:
      - { field: Number, op: not-null }
      - { field: Number, op: matches, regex: "^\\d{4}$" }
    enforce_strategy: skipNonCompliant # 不能自动修复时跳过 + 报错
    severity: warn

  - name: sheets-must-be-named-by-pattern
    where: { category: sheets }
    assert:
      - { field: Name, op: matches, regex: "^[A-Z]\\d-\\d+ - .+$" }
    enforce_strategy: renameByPattern
    enforce_pattern: "{prefix}-{number} - {original}"
    severity: warn
```

##### 命令矩阵

```bash
revitcli enforce --plan          # 只看差异(dry-run)
revitcli enforce --apply         # 应用修正
revitcli enforce --apply --constraint external-walls-must-be-fire-rated
revitcli enforce --watch         # 持续监听(可选,基于 Revit 事件)
```

##### 与 v1.5 fix 的边界

| 维度          | fix                 | enforce              |
| ------------- | ------------------- | -------------------- |
| 触发          | 命令式(用户跑)      | 声明式(profile 声明) |
| 范围          | 按 rule             | 按 constraint        |
| 持久性        | 一次性              | 周期 / 持续          |
| 复用 strategy | 自定义 fix strategy | 复用 fix strategy    |

**实现上 enforce 是 fix 的高级层**——constraint → check → 调用对应 fix strategy。所以 v1.5 fix 是 enforce 的前置。

##### Watch 模式(可选,放后续)

- 订阅 Revit `DocumentChanged` 事件
- 每次变更后跑 enforce(节流 / debounce)
- 输出 violation 到 journal
- 不自动 apply(避免 agent 失控),只通知

#### 优势

- **从命令式 → 声明式,符合现代 DevOps 思维**
- **跟 v1.5 fix 完美整合**(constraint = check + fix combined)
- **profile 升级为"项目宪法"**:声明的状态被强制保持
- **多人协作场景神器**:每次合并都跑 enforce,自动统一标准

#### 劣势

- **工程量大**:约束语言设计 + 求解器 + watch 模式
- **跟 v1.5 fix 边界需要清晰**(实施时容易做成"另一个 fix")
- **强制性可能反感**:工程师想破例时怎么办?需要 `--allow-override` 机制
- **冲突约束**:两条 constraint 矛盾时怎么处理?需要明确规则

#### 实施分步

##### 必须等 v1.5 完成后再做(基础设施依赖)

1. **Spec**(2d):constraint 语言 + 与 fix 的关系 + 冲突处理
2. **Profile schema 扩展**(0.5d):`constraints` 节点
3. **Constraint 求解器**(2d):check phase + plan phase + apply phase
4. **CLI 命令**(1d):`enforce` 主命令 + 选项
5. **Watch 模式**(2d,可后置):addin 端事件订阅 + CLI 端 watch loop
6. **测试**(1d):每种 op + 多约束 + 冲突场景
7. **文档**(0.5d)

#### 验收标准

- [ ] 5 种 op 支持:in / not-in / matches / not-null / op
- [ ] dry-run 输出 violations 清单 + 修正预览
- [ ] apply 走 fix strategy,所有改动有 baseline
- [ ] 多 constraint 同时跑,优先级合并正确
- [ ] watch 模式 1 小时不漏报,不重复报

#### 工程量

大,6-8 周

#### codex 入口

**v1.5 完成后再启动**。依赖:fix strategy 框架已就绪。

---

### B6. Time Estimate(执行时间估算)

#### 痛点

agent 调命令前不知道这次要 1s 还是 5min。

具体场景:

- agent 调 `snapshot` 100-sheet 模型,默默等 30s,可能超时被 abort
- agent 调 `publish` 全量,实际 5min,期间 agent 不知道还能不能干别的
- 工程师等长命令时不知道还要多久,焦虑

#### 创新核心

`revitcli estimate <command>`:

- 基于历史耗时回答 ETA
- 命令本身支持 `--progress` 输出进度
- 命令可 `--background` 异步,`revitcli wait <task-id>` 同步等

#### 深入设计

##### estimate 命令

```bash
$ revitcli estimate snapshot
Estimated: 24s (P50) / 45s (P95) based on 12 prior runs
Confidence: high (recent 3 runs all within 20-30s range)

$ revitcli estimate publish weekly-issue
Estimated: 4m32s (P50) / 6m18s (P95) based on 8 prior runs
Confidence: medium (variance high; recent runs trending longer)

$ revitcli estimate set --filter "..."
Estimated: 1.2s (P50) / 3.5s (P95) based on 47 prior runs (similar filter scope)
Confidence: medium

# 没历史数据的情况
$ revitcli estimate intent run migrate-family --params ...
Estimated: unknown (no prior runs)
Default budget: 60s (will exceed → log warning)
```

##### Progress 输出

```bash
$ revitcli snapshot --progress
[snapshot] Capturing categories... 1/12 (walls)
[snapshot] Capturing categories... 8/12 (sheets)
[snapshot] Capturing schedules... 4/16
[snapshot] Hashing... done (24s)
{...result JSON...}
```

`--progress` 在 `--output agent` 下转为 JSONL 进度事件:

```jsonc
{"type":"progress","stage":"categories","done":1,"total":12,"label":"walls"}
{"type":"progress","stage":"hashing","done":1.0,"label":"done"}
{"type":"result","data":{...}}
```

##### Background / Wait

```bash
$ revitcli snapshot --background
{"task_id":"snap-20260426-143022","estimated_seconds":24}

# agent 干别的事...

$ revitcli wait snap-20260426-143022 --timeout 60
{...result JSON...}
```

#### 优势

- **agent 决策更精确**(同步等 vs 先做别的)
- **工程师等待体验改善**
- **能 detect 异常**:实际耗时远超 P95,触发性能问题告警
- **跟 A5 journal 数据自然衔接**(journal 自带耗时)

#### 劣势

- **新用户/新项目准确度低**:冷启动需要数据积累
- **跟 progress 报告重叠**:需要明确分工(estimate 是事前,progress 是事中)
- **filter / scope 影响大**:`set --filter "all"` 和 `set --filter "Mark=W01"` 耗时差很多,estimate 算法要敏感

#### 实施分步

##### 依赖 A5 journal 数据先到位

1. **Spec**(0.5d):estimate 公式 + 命令矩阵
2. **Estimator**(1d):从 journal 读历史 + 算 P50/P95 + confidence 评估
3. **Progress 抽象**(1d):`IProgressReporter` 接口 + 各命令接入
4. **Background / Wait**(1d):task store + wait 实现
5. **测试**(1d)
6. **文档**

#### 验收标准

- [ ] estimate 在 ≥3 次历史后输出准确 ETA(±20%)
- [ ] estimate 在无数据时输出 "unknown" + default budget,不崩溃
- [ ] `--progress` 在 `--output agent` 下输出 JSONL 进度
- [ ] background / wait 端到端

#### 工程量

小-中,2-3 周

#### codex 入口

**A5 完成后做**。

---

### B7. Diff Review Mode(自动初审)

#### 痛点

v1.1 `diff` 输出 markdown / JSON / table,**都是数据**。agent 看 raw diff 无法判断"这个变化是合理还是异常的"。工程师 review 大 diff 也费时。

具体:

- 周一 vs 周五 diff,200 个元素改动,大部分是"日常"
- 但藏着 2 个"应该警惕"的(墙体被删,FireRating 降级)
- 现在的 diff 不区分这两类

#### 创新核心

`diff --review`:**自然语言风格的变化总结** + 异常标记

- 不是 LLM 生成,是基于规则的"变化分类 + 模板渲染"
- 启发式:routine / notable / anomaly 三档

#### 深入设计

##### 输出例子

```
$ revitcli diff snap-mon.json snap-fri.json --review

Summary: 47 element changes between snap-mon and snap-fri.

✓ Routine (35 changes)
  - 23 doors: 'LastModified' updated (no functional change)
  - 12 walls: 'Comments' updated (consistent with weekly review pattern)

⚠ Notable (10 changes)
  - 2 walls deleted from category=ExteriorWall (rare event in this project)
  - 1 wall: FireRating changed A1 → A2 (regulatory implication)
  - 7 rooms: Number reformatted (consistent batch operation)

✗ Anomaly (2 changes)
  - 9 rooms lost their Number values (likely accident, never seen in past 30 days)

Recommended actions:
  - Investigate "rooms losing Number" — see element 12345-12353
    journal blame suggests last-touched-by: agent-session "morning-2026-04-26"
  - Verify wall deletions were intentional — see journal entries 2026-04-26T08:42

Use --max-rows N to expand any section.
```

##### 启发式分类规则

| 分类        | 规则                                                                  |
| ----------- | --------------------------------------------------------------------- |
| **Routine** | 历史 30d 内同类变化 frequency > 80%                                   |
| **Notable** | 频率 20-80%,或带规则关键字(Fire/Egress/Structure)                     |
| **Anomaly** | 频率 < 20%,或属于 "rare-event" 列表(deletion / regulatory param 改动) |

##### 与 A5 journal 联动

- "上次类似变化是谁做的" — 来自 journal blame
- "本次变化的 session note" — 来自 session(A2)
- 让 agent 看 review 时直接拿到"出问题谁负责"

##### 命令选项

```bash
revitcli diff <a> <b> --review               # 启用 review 模式
revitcli diff <a> <b> --review --output json  # 给 agent
revitcli diff <a> <b> --review --threshold 0.1  # 自定义 anomaly 阈值
revitcli diff <a> <b> --review --learn          # 学这次结果(把 anomaly 加入 baseline)
```

#### 优势

- **agent 5 秒看完决定要不要深入**
- **工程师 PR review 体验好**
- **跟 v1.7 CI / PR comment 完美集成**
- **跟 A5 journal 联动有"责任归属"语义**

#### 劣势

- **启发式分类需要历史数据**(冷启动时所有变化都是 anomaly)
- **误分类 anomaly 会失去信任**:重要的别误为 routine
- **规则表维护**:rare-event 列表需要持续更新

#### 实施分步

##### 依赖 A5(journal blame)+ A1(agent output)

1. **Spec**(1d):启发式公式 + 输出模板 + 与 v1.1 diff 的关系
2. **Classifier**(1d):分类算法 + 规则表
3. **Review renderer**(1d):text / agent json
4. **Journal 集成**(0.5d):blame 信息嵌入
5. **`--learn` baseline 机制**(0.5d)
6. **测试**(1d)

#### 验收标准

- [ ] 在 30d 历史的项目上,Routine / Notable / Anomaly 分类合理(人工抽查 80% 以上认同)
- [ ] Agent JSON 输出含 next_actions
- [ ] `--learn` 后再跑同样 diff,anomaly 减少
- [ ] 集成 v1.7 PR comment 模板,GitHub 渲染正常

#### 工程量

中,3 周

#### codex 入口

依赖:v1.1 diff(已完成)+ A5 journal + history 数据。**v1.7 CI 集成时一起做**。

---

### B8. Cross-Project Index(跨项目索引)

#### 痛点

一个团队同时维护多个 RVT 项目。具体痛点:

- 改了一个族,**其他项目用了同一个族不知情**
- "哪些项目用了这个族 / 这个 profile / 违反了这个规则"现在没法回答
- 团队级 BIM 治理缺数据基础

具体场景:

- "这个客户要求所有项目都把 'FireRating' 字段填齐,我们 12 个项目里有几个达标?"
- "这个族库升级到 v3 了,哪些项目还在用 v2?"
- "有个新规则,我想看现存项目里多少元素违反"

#### 创新核心

`revitcli index` 命令族:扫描多个项目目录,建立索引

- `index add <project-path>` 注册项目
- `index update` 重扫所有
- `index find-family <name>` 跨项目找族用户
- `index audit-rule <rule>` 跨项目违规
- `index outdated --library <path>` 哪些项目族版本落后

#### 深入设计

##### 索引位置

`~/.revitcli/index/`(用户级,跨项目)

- `projects.json` 注册的项目列表
- `<project-id>/snapshot.json` 该项目最新 snapshot 副本
- `<project-id>/profile-resolved.json` 解析后的 profile

##### 数据来源

每个项目的 `.revitcli/history/` 最新 snapshot + 项目 profile,**不需要打开 Revit**(只读 JSON)。

##### 命令矩阵

```bash
revitcli index add /path/to/project1
revitcli index add /path/to/project2
revitcli index list                                    # 已注册项目
revitcli index update [--project name | --all]         # 刷新索引
revitcli index find-family "门-平开-双扇"
  → project1 (用了 8 个), project3 (用了 23 个)

revitcli index audit-rule naming-fire-rating
  → project1: 5 violations
  → project3: 0 violations
  → project5: 12 violations (重点)

revitcli index outdated --library /shared/lib/
  → project2: 族 'window-energy' v1.0 < library v2.1
  → project3: 族 'door-fire' v3.2 < library v3.5

revitcli index export --output report.html
  → 跨项目治理报告
```

##### 跟 v1.6 history 关系

`history capture` 自动通知 index update(如果项目已注册)。否则 manual `index update`。

##### 跟 v1.9 profile registry 关系

profile registry 提供 profile 来源,index 提供项目用了哪个 profile + 合规情况。两者合并 = 团队治理仪表盘。

##### Agent 用法

```bash
revitcli index audit-rule fire-safety --output agent
{
  "summary": "5 of 12 projects have fire-safety violations",
  "violations": [...],
  "next_actions": [
    { "command": "revitcli index find-similar-fix --rule fire-safety", "rationale": "其他项目修过类似" }
  ]
}
```

#### 优势

- **打开"团队级洞察"维度**
- **跟 v1.9 profile registry 天然衔接**
- **多项目治理报告**,适合给 BIM 总监看
- **agent 跨项目协作的基础**(C1 多 agent 协调的前置)

#### 劣势

- **扫描多项目需要权限管理**(用户家目录外的项目?)
- **索引一致性**(项目改了,索引何时刷)
- **隐私**:跨项目元数据汇总,需要避免泄漏不该共享的项目信息

#### 实施分步

##### 依赖 v1.6 history(数据源)+ v1.9 profile registry(配置源)

1. **Spec**(1d):索引 schema + 命令矩阵 + 隐私边界
2. **IndexStore**(1d):add/list/update/remove
3. **跨项目查询**(2d):find-family / audit-rule / outdated 三个 querier
4. **HTML 报告**(0.5d)
5. **`history capture` 钩子**(0.5d):自动通知 index
6. **测试**(1d)
7. **文档**

#### 验收标准

- [ ] 注册 5 个项目后,find-family 准确
- [ ] audit-rule 跨项目合并报告,数字与单项目 check 一致
- [ ] HTML 报告渲染正确
- [ ] index update 增量更新,不重扫已索引项目

#### 工程量

大,5-7 周

#### codex 入口

**v1.6 + v1.9 完成后做**。最自然的位置是 v1.9 的延伸。

---

## 二、C 级条目深度展开

C 级是**战略级 / 高风险高回报**,展开度比 B 级浅一档——重点是"决策框架"而非"实施步骤"。每条结构:**痛点 → 创新核心 → 深入设计 → 优劣 → 前置依赖 → 何时启动 → 工程量**

---

### C1. Multi-Agent Coordination(多 agent 协调)

#### 痛点

大型 BIM 项目的真实工作流:

- 建筑 / 结构 / 机电三个专业的 BIM 工程师并行工作,各改各的 RVT
- 每周"碰图"——把三个 RVT 合起来看冲突(传统用 Navisworks)
- 冲突发现后,谁改、改什么、何时改,**全靠人协商**
- agent 时代:三个专业的 agent 应该能自动协调

具体场景:

- 结构梁穿过建筑的吊顶净高线 → 谁让?
- 机电管线挤占了建筑预留的吊顶空间 → 改管路还是改吊顶?
- 客户改了平面布局,三个专业都要响应,谁先?

#### 创新核心

多 agent 通过 RevitCli + IFC 中介通信:

```
建筑 agent (revitcli)  ─┐
结构 agent (revitcli)  ─┼─→ 联合协调器 → 冲突清单 + 责任分配
机电 agent (revitcli)  ─┘
                            ↓
               每个 agent 收到针对自己的"协调建议"
```

命令:`revitcli coordinate --our model.rvt --their structural.ifc mep.ifc`

#### 深入设计

##### 数据流

1. 每个专业 RvtCli 输出**"我做了啥"snapshot**(v1.1 已有 snapshot,扩展加 IFC 兼容字段)
2. 中介合并为 **federated snapshot**(IFC 互操作 + 自定义协议)
3. 冲突检测:几何冲突(Clash Detection)+ 元数据冲突(命名 / 编号重复)
4. **责任分配启发式**:
   - "谁最后改的让"
   - "业主关心的那一方让"
   - "改成本低的那一方让"
   - "结构主导优先"(行业惯例)
5. 输出协调建议给各专业 agent,各 agent 回到自己的 RVT 调整

##### 与 IFC 关系

- IFC 是 BIM 行业标准但缺 BIM 元数据(参数 / 视图 / 工作集)
- RevitCli snapshot 互通是**关键差异**:不是替代 IFC,是叠加
- 第一版可只支持 RvtCli ↔ RvtCli(都用 RevitCli 的项目),IFC 互操作放后续

##### 与 Autodesk Construction Cloud(ACC)关系

- ACC 是 Autodesk 的协作平台(云端付费)
- RevitCli 是命令行客户端,**线下 / 跨厂商**
- 不是替代关系,是补充关系

##### 命令矩阵

```bash
revitcli coordinate plan --our our.rvt --their team-b.rvt team-c.rvt
  # 输出冲突清单 + 责任分配

revitcli coordinate apply --plan ./plan.json
  # 各方应用各自部分

revitcli coordinate watch
  # 持续监听其他方的 snapshot 变化
```

#### 优势

- **解决真实痛点**(跨专业碰图是 BIM 工作流痛点 #1)
- **战略级,可能定义行业标准**
- **多 agent 时代的天然出口**:agent 之间能协调,人监督

#### 劣势

- **复杂度爆炸**:多专业 RVT 实测困难,每个团队的 RVT 风格不同
- **依赖 IFC 互操作 + 行业标准协商**:工程量大
- **做不好会被认为是 ACC 的弱版**:Autodesk 在云端做的事,RevitCli 在本地做,差异化要清晰
- **冲突解决算法是难题**:启发式不准,会被吐槽

#### 前置依赖

- v1.6 history(snapshot 时序基础)
- B8 cross-project index(跨项目数据结构)
- IFC 读写能力(目前只有 IFC 导出,没有读)
- MCP server 主线化(C2 的副产品)

#### 何时启动

**v2.0 之后**。等单专业 agent-native 工作流稳定 + 用户基数足够。或:有 BIM 行业大客户驱动(企业用户带需求和数据)。

#### 工程量

特大,3-6 个月

---

### C2. Natural Language Wrapper(自然语言层)

#### 痛点

- 非技术 BIM 工程师(纯设计师)不会命令行
- agent 时代他们想用自然语言:"查一下外墙总长度"
- 现在 RevitCli 命令对他们门槛太高

#### 创新核心

两个候选路径:

**路径 A:内置 LLM**

- `revitcli ask <question>` 自然语言 → 命令 → 结果 → 自然语言总结
- 用户配 API key

**路径 B:作为 MCP server**(推荐)

- 把 RevitCli 命令矩阵 expose 为 MCP tools
- LLM 在 client(Claude Desktop / Cursor / Continue)端,通过 tool calling 调
- CLI 不绑定 LLM

#### 深入设计

##### 路径 A 优劣

- ✓ 用户体验直接(一个 ask 命令)
- ✗ **违反"工具不该绑定特定 LLM"原则**(用户全局规则、行业最佳实践)
- ✗ 第三方 LLM 调用有成本和数据合规
- ✗ LLM 行为不稳定,误调用 set 风险高

##### 路径 B 优劣

- ✓ **不绑特定 LLM**,任何 MCP-capable client 都可用
- ✓ 数据流由 client 控制(本地 / 云,用户决定)
- ✓ 自然集成到 agent 生态
- ✗ 需要用户配 MCP client,门槛仍存在
- ✗ MCP 协议演进有变数

##### 推荐:路径 B

实施:

```bash
revitcli mcp serve  # stdio MCP server,把命令矩阵暴露成 tools
```

每个 CLI 命令变成一个 MCP tool,schema 自动从 `CliCommandCatalog` 生成。

#### 关键决策:MCP 主线化?

如果走路径 B,**MCP server 实质上变成 RevitCli 的核心出口之一**——而 21 天前的 vision 文档说"MCP 仅作为可选分发渠道"。

需要重新评估:**MCP 是否升级为路线图主线?**

倾向:**升级**。理由:

- MCP 是 agent-native BIM 协议层最直接的兑现
- 不与 BIMOps 主线冲突(BIMOps 是用例,MCP 是接入方式)
- 工程量小(3.5d 即可),回报大

#### 前置依赖

- A1(agent output)— MCP tool 的输出层
- A2(session)— MCP 调用的状态层
- A5(journal)— MCP 调用的审计层

**A1+A2+A5 完成后做 MCP 即可**。

#### 何时启动

**v1.5 - v1.6 之间** (3.5d 小活,作为缓冲)。或在第一次有"我想接 Claude Desktop"的真实用户需求时立即做。

#### 工程量

中(3.5d 完成路径 B)/ 大(2 周以上做路径 A)

---

### C3. Real-Time Co-Pilot Mode(实时陪坐)

#### 痛点

- 工程师在 Revit 里手动操作时,agent 不在场
- 出错没人及时拉一把
- 知识固化(A4 learn)是事后,不是当下

#### 创新核心

agent 在 Revit 里"陪坐":

- addin 端事件订阅,把工程师每个动作 push 给 agent
- agent 实时给建议(side panel 或 CLI 通道)

#### 深入设计

##### 技术架构

```
Revit (工程师手动操作)
   ↓ DocumentChanged 事件
Add-in (订阅 + 序列化)
   ↓ HTTP push 或 server-sent events
CLI (实时推到 active session)
   ↓ MCP push notification
Agent (LLM client)
   ↓ 渲染建议
Revit dock panel(addin 端 WPF)
```

##### UX 设计是难点

- 打扰 vs 帮助平衡
- 建议什么时候出?改完一个元素就出?太烦
- 等工程师停手 N 秒再出?延迟感
- agent 建议能直接 apply 吗?需要工程师审

##### 与 A4 learn 关系

C3 是 A4 的实时版:

- A4:事后 `learn start/stop` 录制
- C3:全时录制 + 实时建议

#### 优势

- **终极 co-pilot 体验**
- **学习数据立刻可用**(A4 实时版)

#### 劣势

- **UX 设计极复杂**(打扰 vs 帮助平衡)
- **Revit DocumentChanged 事件量大**,过滤算法需要工程
- **agent 接入需要长连接**,违反 stdio 一次性调用模式
- **WPF UI 开发**:addin 端要做 GUI,跟纯 CLI 项目定位有些拧

#### 前置依赖

- A1, A2, A4, A5 全部完成
- C2 MCP server 主线化(实时 push 需要长连接)

#### 何时启动

有 1 个核心用户长期使用 + 愿意配合迭代 UX。或战略级合作伙伴提需求。

#### 工程量

特大,3-4 个月

---

### C4. Federated Learning(联邦学习)

#### 痛点

- 每个团队的 fix history 是孤岛
- 行业级最佳实践没有汇总途径
- 资深工程师的隐性知识无法跨团队传播

#### 创新核心

多团队的 fix / workflow / journal 数据(去敏感后)联合学习,产出**行业级 playbook 库**。

#### 深入设计

##### 数据流

1. **客户端去敏**:项目名 / 团队 / 客户信息全删
2. **上传中心服务器**(opt-in,可选)
3. **中心服务器 aggregate + cluster**
4. **推送回客户端**"行业常见 playbook"

##### 隐私保护

至少一种:

- 差分隐私(Differential Privacy)
- 联邦学习(Federated Learning,真分布式不集中数据)
- 同态加密(Homomorphic Encryption)

##### 商业模式潜在路径

- **免费版**:基于本地数据,纯个人 / 小团队
- **企业版**:接入 federated 数据池,提升推荐质量;付费

##### 关键风险:BIM 数据敏感性

BIM 数据可能含:

- 投资 / 合同信息(项目预算)
- 客户机密(产品布局 / 技术方案)
- 政府敏感(政府项目)

去敏感不严会有法务问题。第一版必须**明确不收集任何识别信息**,只收集"行为模式"(改了什么参数 / 跑了什么 workflow)。

#### 优势

- **商业化路径**(企业愿意付费换更好推荐)
- **行业影响力放大**
- **网络效应**:用户越多,数据越好,推荐越准

#### 劣势

- **隐私法务复杂**
- **中心服务器需要运维 + 成本**
- **冷启动**:启动需要先有用户基数

#### 前置依赖

- 用户基数 ≥ 1000 团队(否则数据池太小,推荐不准)
- 隐私法务咨询完成
- 商业化决策(免费 vs 付费)

#### 何时启动

**商业化阶段**。或被收购方提供资源。

#### 工程量

超大,6+ 个月,且大量是**非工程**工作(法务 / 运维 / 商业)

---

## 三、B/C 级整体优先级判断

### 决策矩阵

| 条目                          | 价值 | 工程量 | 依赖                | 第一波? | 第二波? | 暂缓       |
| ----------------------------- | ---- | ------ | ------------------- | ------- | ------- | ---------- |
| **B1** Negotiation            | 高   | 中     | 无                  | ✓       |         |            |
| **B2** Intent Templates       | 高   | 中     | A3 workflow         |         | ✓       |            |
| **B3** Progressive Disclosure | 中   | 小     | (合并到 A1)         | ✓       |         |            |
| **B4** Suggestion Engine      | 中   | 中     | v1.5 + v1.6 + A3    |         | ✓       |            |
| **B5** Constraint Solver      | 高   | 大     | v1.5 fix            |         | ✓       |            |
| **B6** Time Estimate          | 中   | 小-中  | A5 journal          | ✓(轻量) |         |            |
| **B7** Diff Review            | 中   | 中     | v1.1 + A5 + history |         | ✓       |            |
| **B8** Cross-Project Index    | 中   | 大     | v1.6 + v1.9         |         |         | ✓(放 v2.x) |
| **C1** Multi-Agent            | 极高 | 特大   | B8 + IFC + C2       |         |         | ✓(v2.0+)   |
| **C2** NL Wrapper(MCP)        | 高   | 中     | A1+A2+A5            | ✓(短)   |         |            |
| **C3** Real-Time Co-Pilot     | 高   | 特大   | A 全 + C2           |         |         | ✓          |
| **C4** Federated Learning     | 极高 | 超大   | 用户基数            |         |         | ✓(商业化)  |

### 第一波(嵌入 v1.5-v1.7,做)

- **B1 Negotiation** + **B3 Progressive Disclosure**(合并到 A1)+ **B6 Time Estimate**(轻量)
- **C2 MCP server**(3.5d 小活)

### 第二波(v1.7-v1.9,做)

- **B2 Intent Templates**
- **B4 Suggestion Engine**
- **B5 Constraint Solver**
- **B7 Diff Review**

### 第三波(v2.x 之后,可能做)

- **B8 Cross-Project Index**
- **C1 Multi-Agent Coordination**

### 暂缓(等条件)

- **C3 Real-Time Co-Pilot** — 等核心用户
- **C4 Federated Learning** — 等商业化

---

## 四、依赖关系图

```
                ┌─ A1 Agent Output (主线)
                │   ├─ B3 Progressive Disclosure (合并)
                │   ↓
                │   B6 Time Estimate ← A5 Journal
                │   ↓
v1.4.5 / v1.5 ──┤   B1 JSONL Negotiation (无依赖)
                │   ↓
                │   C2 MCP Server (3.5d) ← A1 + A2 + A5
                │
                ├─ A5 Journal (主线,与 v1.5 一起)
                │   ↓
                │   B7 Diff Review ← v1.1 + A5 + history
                │
v1.6 History ───┤
                │   B4 Suggestion ← v1.5 + history + A3
                │
v1.7 CI ────────┤
                │   B2 Intent ← A3 workflow
                │
v1.8 Family ────┤
                │
v1.9 Profile ───┤
                │   B5 Constraint Solver ← v1.5 fix
                │   B8 Cross-Project Index ← v1.6 + v1.9
                │
v2.0 Dashboard ─┤
                │
                └─ C1 Multi-Agent ← B8 + IFC + C2
                  C3 Real-Time ← 全部 A
                  C4 Federated ← 商业化
```

---

## 五、给 codex 的执行入口总览

### 启动一个 B 级条目时

```
1. 读本文对应条目的"深入设计 + 实施分步"
2. 读 docs/ideation-agent-native.md 确认与 A 级的咬合
3. 读 docs/roadmap-2026q2-q3.md §11 看 superpowers 工作流接入
4. 用 superpowers:brainstorming 复核(所有创建新功能前必走)
5. 写 spec(参考 docs/superpowers/specs/2026-04-23-model-as-code-design.md)
6. 写 plan(参考 docs/superpowers/plans/2026-04-24-import-csv.md)
7. 实施(每 step 一 commit,严格跑测试 + smoke)
8. 自查:用 superpowers:requesting-code-review
9. 真机:scripts/smoke-revit2026.ps1
10. PR + 合并 + tag(参考 PR #5)
```

### 启动 C 级条目时

**必须先做用户访谈或战略评估**——C 级动作大,做错代价高。具体:

- 找 3-5 个目标用户(BIM 总监 / 多专业 lead),用本文相关条目讨论
- 验证痛点排序、技术路径、商业可行性
- 然后才走 spec → plan → execute

### 跨条目协调

每完成一个 B 级条目,**回头复核**:

- 与已完成条目是否有重复?
- 与未完成条目的接口是否预留好?
- A 级协议栈三层(IO / 状态 / 演进)是否被新条目动摇?

---

## 六、关键决策待回答

执行前需要用户决策:

1. **MCP 主线化吗?** — C2 是否升级为 v1.5-2.0 的标配?(我的倾向:升级)
2. **B1 协议跟 MCP 的关系?** — JSONL 协商是 RevitCli 自有协议,MCP 也有自己的工具调用协议;两者并存还是 B1 走 MCP?
3. **B5 Constraint 跟 v1.5 fix 是否合并?** — 实施时容易做成"另一个 fix",需要早期决断
4. **B8 是否单独立项 vs 嵌入 v1.9 profile registry?** — 取决于 v1.9 时 cross-project 需求强度
5. **C 级条目要不要落 stub?** — 即使不做,要不要在 README 或 ROADMAP 提及 "future"?(我的倾向:不提,避免超前承诺)

---

_文档版本:1.0(2026-04-26)_
_维护原则:每个条目可独立演进。完成一个就回头修订本文(标"DONE"),失败/抛弃的修订保留为"DEPRECATED"段。_
