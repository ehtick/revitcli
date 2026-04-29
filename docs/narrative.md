# RevitCli Narrative

> 2026-04-29 复审:本文已归档,不再作为对外叙事来源。
> 当前叙事是:RevitCli 是建筑师的本地终端 BIMOps 工具,可被
> Codex CLI 作为外部终端操作者调用;它不是 MCP server、不是
> agent-native 协议层、也不内置 LLM。见
> [`docs/roadmap-2026q4-v4.md`](./roadmap-2026q4-v4.md) 和
> [`docs/codex-cli-architect-workflows.md`](./codex-cli-architect-workflows.md)。

> 用途:对外叙事的统一来源(README / 博客 / 路演 / 用户对话都从这里拿素材)
> 创建:2026-04-26
> 配套:[ideation-agent-native.md](./ideation-agent-native.md)(创新点详细脑暴) · [roadmap-2026q2-q3.md](./roadmap-2026q2-q3.md)(技术路线)

---

## 一、核心定位:协议层,不是工具

> RevitCli is the protocol layer between Revit and AI agents.

不要讲"我们有什么功能"。讲"agent 操作 BIM 模型需要一套基础设施,我们做了那套"。

### A 级创新点的真正结构:三层协议栈

```
┌───────────────────────────────────────────────────┐
│  演进层  How agents get smarter over time         │
│   A4 Learn  →  A3 Workflow                        │  录制 → 模板沉淀
├───────────────────────────────────────────────────┤
│  状态层  How agents maintain context              │
│   A2 Session  ←→  A5 Journal                      │  工作时有记忆,事后能追溯
├───────────────────────────────────────────────────┤
│  IO 层    How agents and CLI talk                 │
│   A1 Agent Output  +  A6 Visual  +  B1 JSONL 协商 │  多模态、低 token、可协商
└───────────────────────────────────────────────────┘
                       ↓
         RevitCli + Revit Add-in (已交付的 v1.0-v1.4)
                       ↓
                   Revit API
```

### 类比可借

- HTTP/TCP/IP 三层栈
- LSP(Language Server Protocol)对应 IDE
- MCP(Model Context Protocol)对应 LLM 工具调用
- **RevitCli 之于 BIM agent ≈ MCP/LSP 之于 LLM/IDE**

一旦听众接受这是协议栈而不是功能集合,叙事就成了 70%。

---

## 二、三柱叙事:把已交付 + 未来创新装进同一故事

讲给所有人都用的骨架。**顺序不能换**——可重复是基础,可信任是前提,可学习是终局。

| 柱子                    | 含义                                          | 当下证据                                                              | A 级支撑                          |
| ----------------------- | --------------------------------------------- | --------------------------------------------------------------------- | --------------------------------- |
| **可重复 Reproducible** | BIM 流程能像 git 工作流一样被版本化、回放、CI | v1.0–v1.4 已交付:check / publish / snapshot / diff / import / profile | A3 Workflow                       |
| **可信任 Trustworthy**  | 让 agent 在生产模型上动手,你不害怕            | dry-run、事务、ExternalEvent 隔离                                     | A5 Journal/Blame、A1 透明输出     |
| **可学习 Learnable**    | 工程师做过的事会变成 agent 下次的能力         | profile/playbook 雏形                                                 | A4 Learn、A3 Workflow、A2 Session |

讲完三柱,听众脑里应该有清晰的递进感:这是按工程顺序解决问题,不是堆功能。

---

## 三、Tagline / Elevator Pitch 候选

按场景挑用,**不要每处都用同一句**——让不同位置承担不同叙事重量。

| #   | 候选                                                                          | 受众               | 优势                                      | 劣势                        |
| --- | ----------------------------------------------------------------------------- | ------------------ | ----------------------------------------- | --------------------------- |
| 1   | **"Make BIM agent-native — without giving up control."**                      | 全部(英文社区为主) | 给希望 + 给安心;对仗工整                  | 略抽象,需展开               |
| 2   | **"Your AI co-pilot for Revit. With brakes."**                                | 工程师、决策者     | "with brakes" 是金句,瞬间传达"安全"差异化 | "co-pilot" 已被 GitHub 占用 |
| 3   | **"BIM 工程师的 git + GitHub Actions + Cursor"**                              | 中文开发者社区     | 三个类比一秒打开想象                      | 受众必须懂这三者            |
| 4   | **"Every change traceable. Every workflow learnable. Every decision yours."** | 决策者、合规视角   | 三句排比;打中信任 + 效率 + 控制权         | 偏 marketing 腔             |
| 5   | **"The agent protocol layer for BIM."**                                       | 开发者、投资人     | 极简,占位品类                             | 对工程师不直观              |
| 6   | **"把繁琐的 BIM 活交给 agent,把判断留给自己。"**                              | 中文工程师         | 朴素直击痛点;"判断"二字尊重工程师         | 缺技术新颖性暗示            |

**推荐组合用**:

- 中文 README / 博客主标题:**#6**
- 英文 GitHub repo description:**#1**
- 社交媒体 / 一秒识别:**#2**
- 开发者社区 / 技术博客副标题:**#5**

---

## 四、受众分层:四种语言,同一内核

### 1. BIM 工程师(用户本体)

**关心**:省时间、不出错、不被领导骂

**讲法**:从具体场景切入,不要从架构。

> 你周五下班前要出图,客户上午又改了 3 处需求。打开 Revit,改门高度,改图签字段,跑一遍审图清单,导出 PDF——这套流程做了 N 次,每次 1 小时。
>
> RevitCli 让你说一句"按周五出图清单跑一遍",agent 跑完给你一份带前后对比的 PR,你 5 分钟看完批准。所有改动都有记录,客户半夜再说"撤销最后一次",`revitcli journal undo` 一键回去。

**钩子**:90 分钟 → 5 分钟。

---

### 2. BIM 总监 / 项目经理(决策者)

**关心**:质量稳定、知识不流失、新人能上手快

**讲法**:从团队风险切入。

> 你的资深工程师调走那天,他脑子里 5 年的项目经验也走了。
>
> RevitCli 让经验在工程师每次操作时被自动捕捉、转成可执行的 workflow,新人 onboarding 当天就能复用。所有 agent 改动有 audit trail,合规和回溯不再是问题。

**钩子**:经验固化为团队资产 + 合规可追溯。

---

### 3. 开发者社区(贡献者、潜在合作者)

**关心**:技术新颖性、可扩展、协议是否开放

**讲法**:从协议层切入。

> BIM 软件至今没有 agent-native 接口——AutoCAD、Revit、ArchiCAD 都假设有一个人坐在屏幕前。
>
> RevitCli 是 BIM 行业的 LSP/MCP:把 Revit API 抽象成 agent 友好的协议(分页、自描述 schema、JSONL 协商、journal 审计),任何 agent 都能接入。我们打地基,生态在上面长。

**钩子**:第一个 agent-native BIM 协议,开放、可扩展。

---

### 4. 投资人 / 战略合作方(资源给予者)

**关心**:品类大小、护城河、竞品

**讲法**:从市场结构切入。

> 全球 BIM 工程师 200 万+,平均 70% 时间在重复操作。
>
> Autodesk 不会做这件事(他们的商业模式是 license 软件,不是省你的时间)。社区 MCP server 解决的是个人玩具场景,不是企业生产场景——没有 audit、没有合规、没有团队治理。
>
> 我们填的是企业级 agent-native BIM 工作台这个空位。护城河是工程师录制的 workflow + 项目级历史,迁移成本随使用时间指数级上升。

**钩子**:Autodesk 不做 + MCP 不够用 + 数据飞轮。

---

## 五、每个 milestone 的叙事意义

技术 release 必须配叙事意义,否则用户每次升级只看到"又加了功能"。

| Milestone      | 叙事意义                     | release post 标题候选                             |
| -------------- | ---------------------------- | ------------------------------------------------- |
| v1.4 候选      | "BIM agent 的诊断仪能装上了" | 三方版本一致:CLI / 安装 / live addin 不再悄悄漂移 |
| v1.5 Auto-fix  | **首次让 agent 真的"动手"**  | check 看见问题,fix 让 agent 修(并留底)            |
| v1.6 History   | **给 agent 长期记忆**        | 模型时序仓库:30 天健康度趋势,任意两点回溯         |
| v1.7 CI        | **agent 进入团队工作流**     | SARIF + GitHub Action,BIM 检查像代码评审一样进 PR |
| v1.8 Family    | **agent 的战场扩展到资产层** | 族管理:purge / validate / 库一致性                |
| v1.9 Profile   | **agent 标准能跨项目共享**   | profile 多继承 + 远程引用,团队级标准              |
| v2.0 Dashboard | **让人类回到监督席**         | Web 看板:agent 干了什么 / 模型怎么变了 / 谁来负责 |

每个 release 都讲"这一步意味着 agent 能力的什么解锁",而不是"这一步加了什么功能"。

---

## 六、可直接使用的叙事样板

### 样板 A:GitHub README 主页(英文)

```markdown
# RevitCli

> Make BIM agent-native — without giving up control.

The protocol layer between Revit and AI agents. Built for BIM engineers
who want to delegate the boring 80% of their work to agents while keeping
final judgment.

## Three pillars

- **Reproducible** — version your standards as `.revitcli.yml`,
  run check/publish/snapshot in CI like any code project
- **Trustworthy** — every agent action goes through journal/replay/blame;
  no change is anonymous, no mistake is irreversible
- **Learnable** — record what an engineer does, distill it into a workflow,
  agent reuses it next time

[Why agent-native?](docs/narrative.md) · [Roadmap](docs/roadmap-2026q2-q3.md)
```

---

### 样板 B:中文博客介绍(开篇 200 字)

> 一个不应该存在的事实:2026 年了,BIM 工程师还在为同一份审图清单手动跑 100 次命令。
>
> 不是工程师不努力,是工具假设有一个人坐在屏幕前。Autodesk 不会改这件事——他们的商业模式不奖励"省你时间"。社区 MCP server 是玩具,没有 audit、没有合规、没有团队治理,也没人敢在生产模型上跑。
>
> 我做了 RevitCli。三句话讲清:
>
> - 把繁琐的 BIM 活交给 agent
> - 每一次改动都留底,出问题能 `journal undo`
> - 工程师做过的事自动变成下次 agent 的模板

---

### 样板 C:电梯路演(60 秒口头)

> 全球 200 万 BIM 工程师每天 70% 时间在重复操作——改参数、跑检查、出图、对客户改单。
>
> AI agent 已经能写代码、能查文档,但**改不了 BIM 模型**——因为 BIM 软件没有 agent-native 接口。
>
> 我们做了那个接口层。叫 RevitCli。
>
> 三件事:**可重复**(像 git 一样版本化标准)、**可信任**(每次 agent 改动都有 audit trail,工程师 5 秒回滚)、**可学习**(工程师做过的事自动成为下次 agent 的模板)。
>
> 现在已经在 v1.4,有 ~250 个测试,在真实 Revit 2024/2025/2026 上跑通。下一站 v1.5 让 agent 第一次"动手"——auto-fix 闭环。
>
> Autodesk 不会做这事,MCP 玩具够不到企业。我们填的是 **agent-native BIM 工作台**这个空位。

---

## 七、关键词与隐喻库(随用随取)

讲故事时塞这些钩子,提升记忆点。

### 类比

- "BIM 工程师的 git" — 强调可版本化
- "Revit 的 GitHub Actions" — 强调 CI / workflow
- "Cursor for BIM" — 强调 agent-native
- "Terraform Provider for Revit" — 强调声明式 / agent 协议
- "BIM 的 LSP" — 强调标准协议层
- "Hugging Face for BIM workflows" — 强调可分享/复用

### 金句

- "Make BIM agent-native — without giving up control."
- "Your AI co-pilot for Revit. With brakes."
- "Every change traceable. Every workflow learnable. Every decision yours."
- "把繁琐交给 agent,把判断留给自己。"
- "经验固化为团队资产,合规可追溯到行。"
- "agent 不该是黑盒——每次改动都有 journal,出问题 5 秒回滚。"

### 数字钩子

- "全球 200 万 BIM 工程师 70% 时间在重复操作"(用之前确认数字)
- "v1.0-v1.4,250+ 测试,跑通 Revit 2024/2025/2026"
- "90 分钟 → 5 分钟"(出图工作流加速对比)
- "v1.5 让 agent 第一次'动手'"

---

## 八、Logo / 视觉建议(占位,等正式做)

视觉钩子还没做,但叙事可以预留:

- 主图候选:**协议栈三层图**(本文 §1)
- 副图候选:**三柱图**(本文 §2)
- 配色:深色背景 + 浅色文字(用户全局偏好;HTML 输出强制深色模式)
- 隐喻视觉:刹车踏板(对应 "with brakes")、git 分支 + Revit 模型混合

---

_文档版本:1.0(2026-04-26)_
_维护原则:叙事素材 single source of truth。每次写 README / blog / 路演稿,从本文拿素材;反过来如果叙事进化,先改本文,再传播到下游。_
