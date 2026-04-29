# RevitCli Agent-Native 创新脑暴

> 2026-04-29 复审:本文已归档,不再代表当前路线图。
> 当前方向见 [`docs/roadmap-2026q4-v4.md`](./roadmap-2026q4-v4.md)
> 和 [`docs/codex-cli-architect-workflows.md`](./codex-cli-architect-workflows.md)。
> 可保留其中"Codex CLI 调普通 shell 命令"的启发,但 MCP server、
> agent-native 协议层、`--output agent` 主线化都不是当前计划。

> 创建:2026-04-26
> 定位:针对"会用 agent 的 BIM 工程师让 agent 帮忙做繁琐工作"场景的创新点探索
> 状态:**脑暴稿**,不是 spec,不是 plan;成熟的方向单独走 superpowers brainstorm → spec → plan
> 配套:与 [`docs/roadmap-2026q2-q3.md`](./roadmap-2026q2-q3.md) 衔接,但定位互补——roadmap 是"BIMOps 运行器"主线,本文是"Agent-Native 工作台"侧线

---

## 一、第一性原理:agent + BIM 工程师的真正信息流

```
工程师意图 → agent 理解 → agent 调 CLI → CLI 调 Revit → 结果回 agent → agent 反馈工程师
        ①              ②                                    ③             ④
```

每个箭头都是 v1.5-v2.0 主线 roadmap **没真正解决**的:

- **①** 工程师意图怎么沉淀?现在是一次性口头指令,下次还得重复
- **②** agent 怎么高效调 CLI?现在 CLI 是给人设计的,agent 用起来 token 浪费严重
- **③** CLI 给 agent 看的输出怎么压缩?1000 行表格塞 LLM 上下文是灾难
- **④** agent 干完工程师怎么验证 / 复盘 / 责问?没有结构化的 audit 路径

下面 6 个 A 级创新点正好覆盖这 4 个箭头。

---

## 二、A 级创新点(强冲击,直接解决痛点)

### A1. Agent-Optimized Output Layer(箭头 ③)

**痛点**
工程师让 agent 跑 `revitcli query walls`,2000 行表格塞进上下文,token 爆炸 + agent 抓不住重点。`--output json` 也没好多少:字段语义对 agent 不透明(`Mark` 是啥?数值带单位吗?)。

**创新**
新增 `--output agent` 模式,本质是**为 LLM 设计的输出协议**:

```jsonc
{
  "summary": "186 walls; 92% are exterior; height range 2.4-4.5m",
  "schema": {
    "Mark": {
      "type": "string",
      "purpose": "user-defined locator",
      "nullable": true,
    },
    "Height": { "type": "number", "unit": "mm", "domain": "geometric" },
  },
  "items": [
    /* 前 20 项 */
  ],
  "more": {
    "total": 186,
    "shown": 20,
    "next": "revitcli query walls --offset 20",
  },
  "next_actions": [
    {
      "command": "revitcli query walls --filter 'Mark is null'",
      "rationale": "8 walls 缺 Mark",
    },
  ],
  "warnings": ["3 walls 在工作集 ext 但 IsExterior=false,数据不一致"],
}
```

**优势**

- 立竿见影降 token 70%+(摘要 + 分页 + 自描述 schema)
- agent 看 `next_actions` 直接知道下一步,不用反复试
- `warnings` 提供"agent 看不出来但应该警惕"的语义信号
- 完全向后兼容(只是新增一个 `--output` 选项)

**劣势**

- schema 维护负担:每个 DTO 字段要标注 purpose / unit / domain
- 摘要生成可能不准(墙高分布需要算分位数,不是单纯 max/min)
- 设计 spec 需要邀请用 agent 的人参与,否则容易闭门造车

**工程量**:中(2-3 周),全在 CLI 端,addin 不动

---

### A2. Conversational Session(跨命令上下文,箭头 ② + ③)

**痛点**
agent 调一次 CLI 是无状态的。它先 `query walls` 抓 200 个,再 `set --ids-from ...` 写值——中间得自己存 ID 列表。每次启动 agent 都重新探索同一个文档,极其浪费。

**创新**
`revitcli session start "morning"` → 后续命令都基于这个 session,有 cache + 引用语法:

```bash
revitcli session start morning
revitcli query walls --filter "height > 3000"      # 结果自动缓存
revitcli set @last --param "Comments" --value "tall wall"   # @last 引用上次结果
revitcli session show                              # 列出 session 上下文
revitcli session note "客户要求把这批墙改成防火墙"     # 自然语言注解
revitcli session end --save replay.jsonl           # 保留可重放
```

**优势**

- 跨命令引用(`@last` / `@last-N` / 命名 selection),agent 不必维护 ID 列表
- session 自带 audit log → 直接对接 A5
- 自然语言注解(`session note`)是 agent 给工程师/未来自己的"为什么这么改"备忘
- session 文件可签入 git,变成 PR 的一部分:"今天我让 agent 改了什么"

**劣势**

- session 跟 active document 绑定,文档切换时语义模糊
- `@last` 这种引用对人类用户也有用,但要避免把 CLI 复杂化
- 状态文件并发写要处理(多 agent 并行 session)

**工程量**:中(3-4 周),需要一个 session store(文件系统 KV)+ 引用语法解析

---

### A3. Workflow Playbooks(箭头 ① + ②)

**痛点**
工程师每天都做的"出图前检查清单"是隐性知识,散落在脑子里。让 agent 跑得每次重新解释。同一个项目里 10 次"准备月度交付",10 次手动拼命令。

**创新**
`.revitcli/workflows/` 目录,YAML 定义多步骤工作流:

```yaml
# .revitcli/workflows/pre-issue.yml
name: pre-issue-checklist
description: 出图前必跑的检查与导出
steps:
  - name: "校准房间编号"
    run: revitcli check --rule duplicate-room-numbers
    on_fail: { ask: "有重复房间号,要 fix 还是 abort?", choices: [fix, abort] }
  - name: "确保所有图签字段"
    run: revitcli check --rule sheet-metadata
  - name: "对比上次出图基线"
    run: revitcli history diff @-1 @latest --since-mode content
  - name: "出图"
    run: revitcli publish pre-issue-pdf
    when: "{{ steps['校准房间编号'].status == 'pass' }}"
  - name: "归档快照"
    run: revitcli history capture --tag issue
```

agent 只需要:`revitcli workflow run pre-issue` 一句话搞定。

**优势**

- 知识固化:工程师的隐性流程变成可版本化的 yaml
- agent 友好:不用让 agent 拼 10 个命令
- 团队复用:跟 profile 一样可以共享
- 跟 v1.7 CI 集成天然契合(workflow 本来就是 step list)

**劣势**

- 跟 GitHub Actions 风格类似但要自己实现条件 / 模板渲染
- `on_fail.ask` 需要 agent 协议(对接 B1)
- 工程师写 YAML 的门槛——可能需要 `revitcli workflow record`(对接 A4)

**工程量**:中-大(4-6 周),需要 mini DSL + 模板引擎 + 条件求值

---

### A4. Knowledge Capture(自动经验固化,箭头 ① 的核心)

**痛点**
工程师手动改东西的时候 agent 看不见,知识流失。新人入职、换项目、这个工程师调走——经验全丢。

**创新**
`revitcli learn`,**录制工程师当前会话**(CLI 操作 + Revit journal 摘录),自动抽 pattern 并转换成可复用产物:

```bash
revitcli learn start
# 工程师手动跑了 10 次 set 命令,改了不同墙的同一个参数
# 工程师跑了一些 query 看分布
revitcli learn stop --suggest
# 输出:
# 检测到模式:你在过去 30 分钟改了 23 面外墙的 "热阻 R" 参数,值都是 "R-25"
# 建议:
#   1. 写 fix playbook → 自动应用到所有外墙
#   2. 写 workflow 步骤
#   3. 加 check 规则:外墙必须 R-25
# 选哪个?[1/2/3/none]
```

**优势**

- 把工程师"做完一次"自动升格为"模板",避免下一次再做
- 跟 A3 workflow 闭环:learn 录制 → 转 workflow → agent 复用
- 跟 v1.5 fix playbook 闭环:learn 录制 → 转 fix strategy
- 这是真正的"agent 学工程师"路径——不依赖 LLM,靠**操作日志的模式抽取**

**劣势**

- pattern 提取算法有难度(需要能识别"23 次同样的 set"是同一意图,而不是 23 个偶然)
- Revit journal 解析 + CLI 日志合并,跨数据源
- 隐私 / 误报:可能把"误操作"也当 pattern 抽出来,要给工程师审批界面

**工程量**:大(6-8 周),核心是 pattern 检测算法

---

### A5. Replay & Audit(agent 可信度的根基,箭头 ④)

**痛点**
工程师不敢让 agent 在生产模型上自由跑——agent 改错了怎么办?改了什么?谁来兜底?**这是阻碍 agent 普及的最大心理障碍**。

**创新**
每次 CLI 调用(尤其是写操作)都被结构化记录到 `.revitcli/journal/`,且支持:

- `revitcli journal show` — 列出所有操作
- `revitcli journal explain <id>` — 这条操作改了什么、为什么、agent 当时的上下文是什么
- `revitcli journal replay <id>` — 重放
- `revitcli journal undo <id>` — 反向(基于 v1.5 的 baseline 机制)
- `revitcli journal blame <element-id>` — 这个元素的最后 N 次改动是谁做的(agent / 人工 / 哪个 session)
- `revitcli journal sign --gpg` — 重要操作签名

**优势**

- 工程师**敢让** agent 改东西:有底线
- 出了问题能追责到具体 agent 调用 + 上下文
- "blame" 是 git blame 的 BIM 版,极有冲击力
- 跟 A2 session 完美衔接(session 就是 journal 的高层视图)
- 对企业场景至关重要(合规、审计)

**劣势**

- 写路径全部要走 journal,性能开销
- "blame" 需要每次写都记录前值快照,存储增长
- 签名机制对个人用户太重,需要 opt-in

**工程量**:中(3-4 周),journal 设计 + blame 索引

---

### A6. Visual / Multi-Modal Context(箭头 ② 的解锁,vision agent 时代)

**痛点**
agent 只能"看"文本。但 BIM 是高度视觉的——一张图纸看一眼就知道哪里不对劲。Claude / GPT-4V / Gemini 都已经是 vision-capable,但 RevitCli 没给它们看的东西。

**创新**
`revitcli capture` 命令簇,把 Revit 的视觉表达暴露给 agent:

```bash
revitcli capture --view "1F-Plan" --output base64       # 楼层平面
revitcli capture --view-3d --camera default              # 3D 视角
revitcli capture --sheet "A-101" --output png            # 整张图纸
revitcli capture --element 12345 --view-context auto     # 这个元素在 3 个相关视图的截图
```

输出 base64 PNG / JPG,agent 直接喂给 vision model。

**优势**

- 直接打开"agent 看图说话"场景:工程师上传"客户标了红线的图",agent 看图 + 调 CLI 改对应位置
- 解锁视觉冲突检测(管线穿梁、门洞错位)
- 跟 A1 agent-output 配合:文本摘要 + 关键截图,agent 决策更准
- addin 端 ExportImage API 现成,工程量低

**劣势**

- 截图体积大(单张 200KB-2MB),agent 上下文要算账
- "auto view-context"(自动推断哪些视图相关)是个推荐算法
- 视觉一致性(中文字体、视图模板)在 CI / 跨平台环境下不稳定

**工程量**:小-中(2-3 周),addin 加端点 + CLI 封装 + base64 编码

---

## 三、B 级创新点(支撑性,值得做)

| #      | 创新点                           | 一句话                                                                                                                               | 工程量 |
| ------ | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ | ------ |
| **B1** | **Negotiation Mode(JSONL 协商)** | agent 遇到模糊输入时,CLI 用 JSONL 反问澄清,agent 答完命令继续——比每次失败重试经济得多                                                | 中     |
| **B2** | **Intent Templates**             | `revitcli intent "把北侧 3 楼以上窗户改成节能型号"` 用结构化模板让 agent 把自然语言→意图,CLI 编排多步执行(CLI 不内置 LLM,只提供模板) | 中     |
| **B3** | **Progressive Disclosure**       | `query --summarize --by Type --drill <key>` 钻取式查询,agent 先看摘要再决定要不要全量(其实是 A1 的子集,可合并)                       | 小     |
| **B4** | **Suggestion Engine**            | `revitcli suggest` 基于当前模型 + 项目阶段 + 历史,主动列"现在该做的事";让 agent 不只被动响应                                         | 中     |
| **B5** | **Constraint Solver**            | `revitcli enforce --constraint "外墙类型 in {A1, A2}"`,desired-state 风格,自动检查 + 修正                                            | 大     |
| **B6** | **Time Estimate**                | `revitcli estimate <command>` 基于历史耗时告知 agent 这次要 N 秒,agent 决定同步等还是先做别的                                        | 小     |
| **B7** | **Diff Review Mode**             | `diff --review` 用自然语言风格总结变化,标"异常"项,给 agent 一份初审报告                                                              | 中     |
| **B8** | **Cross-Project Index**          | 跨项目索引"哪些项目用了这个族" / "哪些项目违反了规则 X",团队级洞察                                                                   | 大     |

---

## 四、C 级创新点(战略级,风险大)

- **C1. Multi-Agent Coordination** — 结构 agent + 机电 agent + 建筑 agent 通过 RevitCli 协调冲突。需要先有 IFC 互操作。**复杂度爆炸**,但价值极大
- **C2. Natural Language Wrapper** — 内置 LLM,`revitcli ask "外墙总长多少"`。**违反"工具不该绑特定 LLM"原则**,但对非技术用户有吸引力
- **C3. Real-Time Co-Pilot Mode** — agent 在 Revit 里"陪坐",工程师每个动作 agent 实时建议。需要 addin 端事件订阅。**用户接受度未知**
- **C4. Federated Learning** — 多个团队的 fix history 联合学习,去敏感后产生"行业级最佳实践 playbook"。**隐私风险高,但可能是商业化路径**

---

## 五、不计成本的最强方案

如果资源无限,把上面全做,**且重构 RevitCli 的命令协议层**,成为 **Agent-Native BIM 工作台**:

1. 所有命令默认输出 agent 协议(`--output agent` 成默认,人类用 `--output table`)
2. 所有命令默认进入 session 上下文,无 session 时自动开匿名 session
3. 所有命令默认录 journal,journal 默认 git-tracked
4. 内置一个 standard MCP server(`revitcli mcp serve`),把 workflow / intent / suggest 全暴露
5. workflow + learn + suggest 形成闭环:agent 干活 → learn 抽 pattern → suggest 推荐复用 → workflow 固化
6. 加一个 `revitcli agent-shell` REPL,让 agent 进入后类似 bash,但每条命令都自动 session-aware + JSONL 协商

**价值**:RevitCli 不再是"命令行工具",而是 **agent 操作 BIM 模型的标准界面**——类似 Hugging Face datasets 之于 ML,Terraform Provider 之于云资源。

**代价**

- 现有 BIMOps 用户(纯命令行 / CI 流水线)体验会被重新设计影响
- 维护面 5-10 倍扩大
- 需要一个 agent 协议规范(类似 LSP / MCP),否则各家 agent 集成乱套
- 团队规模需求至少 3-4 人全职(目前看起来是个人项目)

**何时启动**
除非有外部资金 / 团队扩张,否则**不建议**。建议先按 A1-A6 渐进,等市场验证有 agent 用户群,再考虑重构。

---

## 六、与 v1.5-v2.0 主线 roadmap 的衔接

不需要推翻 roadmap,而是**把 A 级创新点编织进去**:

| 主线 milestone | 嵌入哪个创新                                | 怎么做                                                                    |
| -------------- | ------------------------------------------- | ------------------------------------------------------------------------- |
| v1.5 Auto-fix  | **A5 journal/replay** + **A1 agent output** | fix 必须有 journal,baseline + journal 形成可责问链路;fix `--output agent` |
| v1.6 History   | **A2 session**                              | session 历史本身就是 history 的来源,合并设计                              |
| v1.7 CI        | **A3 workflow**                             | GitHub Action 跑 workflow 而不是裸命令;PR comment 用 agent output 格式    |
| v1.8 Family    | **A6 capture** 顺手做                       | family 资产可视化,agent 看族缩略图                                        |
| v1.9 Profile   | **A4 learn** + **A3 workflow** 联动         | learn 输出可写到 profile/playbook;workflow 是新一层 profile               |
| v2.0 Dashboard | **A5 journal** 作为数据源                   | dashboard 展示 agent 操作历史、blame                                      |

**或者**重排:把 A1(agent output)和 A5(journal/replay)提前到 **v1.5 之前的"v1.4.5"小版本**,作为"agent 时代的基础设施"先打底,再做 v1.5-v2.0。这是更激进但更符合"agent + BIM 工程师"定位的顺序。

---

## 七、最关键的判断

**最优先做 = A5(journal/replay/blame)**

理由:它解决的是**信任问题**——工程师不敢让 agent 在真实模型上跑,这是 agent 普及的最大障碍。所有其他创新(workflow / learn / suggest)都建立在"工程师敢用 agent"的前提上,journal 是这个前提的根。

**次优先 = A1(agent output)**,因为它降 token,所有 agent 用例都受益,见效快。

**第三优先 = A3(workflow)**,因为它把"繁琐工作"模板化——这正是核心场景。

---

## 八、待决问题(留给后续讨论)

1. **定位是否正式翻转?** — 21 天前的 vision 文档说"不做 AI-to-Revit 桥梁",现在场景修订为"agent + BIM 工程师"。要不要更新 `memory/project_vision.md` 把这条 vision 升级为双轨(BIMOps + Agent-Native)?
2. **agent 协议要不要标准化?** — A1 / A2 / A5 都涉及 agent 协议,要么现在就定一份 spec(类似 MCP / LSP),要么各创新点各自演化最后再统一(后者风险高)
3. **MCP 主线 vs 侧线?** — 旧 vision 把 MCP 当"可选分发",但 agent-native 路径下 MCP 是天然出口。要不要把 MCP server 提升为 v1.5-2.0 的标配?
4. **谁来用?** — A 级创新都假设"会用 agent 的 BIM 工程师"存在。需要找 5-10 个真实用户访谈,验证痛点排序

---

_文档版本:1.0(2026-04-26)_
_下次评审:挑出第一个落地的 A 级创新点(建议 A5),走 superpowers brainstorm → spec → plan,正式进入 v1.4.5 / v1.5_
