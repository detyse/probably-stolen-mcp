# Probably Stolen MCP 使用指南

[English](usage-guide.md) · [项目首页](../README.zh-CN.md) · [能力与实测记录](capabilities.md)

适用版本：**0.2.7 开发版 / 桥接协议 2**，Windows x64。本项目让你自己的 AI 客户端操作《Probably Stolen》；模型、模型账号和推理由客户端提供。

这是实验性 Mod。已有经营、展示柜、电话与升级、举报、拾荒、整理和垃圾桶的实机记录；记录来自迁移前构建。0.2.6 更名包已通过 9 个可开局职业与对应物品操作，见[职业实测报告](profession-tests.zh-CN.md)。0.2.7 补充[跨夜生产验证](overnight-tests.zh-CN.md)。完整经营日、完整鉴定和全部夜间地点仍未在更名包全面验收。具体以[能力清单](capabilities.md)为准。

## 1. 使用前准备

需要：

- 已安装的 Steam 版《Probably Stolen》，并能正常启动。
- Windows x64 与 PowerShell。
- [uv](https://docs.astral.sh/uv/getting-started/installation/)（用于管理 Python 3.11+ 和 MCP 依赖）。
- 能启动**本地 stdio MCP** 的 AI 客户端，模型已配置好。看游戏图片还需要支持图片工具结果的客户端和视觉模型。

客户端需要能启动本地进程，并访问游戏的连接文件。

本项目使用游戏自带的 Mod 加载器。已有实机记录的基线为 Steam Build **26496118**、画面版本 **VERSION 100-Rev12**。0.2.7 发布包使用 Build **26525552** 的程序集编译；编译成功不能证明已在这个新版游戏中完成实测。

## 2. 获取安装文件

从 [Releases](https://github.com/detyse/probably-stolen-mcp/releases/tag/v0.2.7) 下载 `probably-stolen-mcp-0.2.7-mod.zip`。ZIP 仅含 `Mods/probably-stolen-mcp/` 目录及三个已编译 Mod 文件，直接解压到游戏安装目录即可，无需 .NET SDK。

Python MCP 服务另行克隆或下载本仓库到固定目录。将 uv 加入 PATH 后，在仓库根目录运行 `./scripts/Configure-Mcp.ps1` 生成 `artifacts/mcp-config.json`；首次启动 MCP 时会联网准备 Python 和依赖。使用预编译 Mod 可跳过 .NET 构建步骤。如需自行编译 Mod，请参考[开发指南](development.zh-CN.md#从源码构建)；源码构建会生成以下文件：

| 生成位置 | 用途 |
| --- | --- |
| `artifacts/mod/probably-stolen-mcp/` | 安装到游戏中的三个 Mod 文件 |
| `artifacts/python/*.whl` | 包含帮助文档的 Python 安装包，可选安装方式 |
| `artifacts/mcp-config.json` | 根据本机路径生成的客户端配置 |

移动文件位置或换一台电脑后，重新运行 `./scripts/Configure-Mcp.ps1` 更新配置。

## 3. 安装 Mod

先退出游戏。如果使用过旧 PawnPilot，先在游戏内停用它，再退出，将旧 `Mods/PawnPilot` 文件夹移出 `Mods`，避免两个桥接争用端口。

在 Steam 中选择“浏览本地文件”，找到包含 `Probably Stolen.exe` 的目录，将 Release ZIP 解压到这里即可。如果使用源码构建产物，则将下方 `<游戏安装目录>` 替换为实际路径，在仓库根目录打开 PowerShell 并执行：

~~~powershell
$gamePath = '<游戏安装目录>'
if (!(Test-Path -LiteralPath (Join-Path $gamePath 'Probably Stolen.exe'))) {
    throw '请先将 $gamePath 设置为正确的游戏目录。'
}
$modPath = Join-Path $gamePath 'Mods\probably-stolen-mcp'
New-Item -ItemType Directory -Path $modPath -Force | Out-Null
foreach ($file in @('manifest.xml', 'PSMcp.Mod.dll', 'PSMcp.Contracts.dll')) {
    Copy-Item -LiteralPath (Join-Path '.\artifacts\mod\probably-stolen-mcp' $file) -Destination $modPath -Force
}
~~~

也可以手动复制该文件夹。最后应当是：

~~~text
Probably Stolen/
  Probably Stolen.exe
  Mods/
    probably-stolen-mcp/
      manifest.xml
      PSMcp.Mod.dll
      PSMcp.Contracts.dll
~~~

启动游戏，在 Mods 菜单启用 **Probably Stolen MCP**，按提示允许网络连接，以便 AI 客户端连接游戏，然后重启游戏。另行保留源码目录供 Python MCP 服务使用。

Mod 启动成功后，通常会生成：

~~~text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json
~~~

以 Mod 日志中的实际路径为准。文件含本次运行的控制令牌，MCP 自动读取；不要粘贴到聊天、Issue 或仓库。每次重启 Mod 都会更换令牌。

## 4. 把 MCP 接到 AI 客户端

打开生成的 `artifacts/mcp-config.json`，将 `probably-stolen-mcp` 条目合并进客户端的 MCP 配置，保留其他服务。

下面展示采用 `mcpServers` JSON 格式的配置结构，`<…>` 表示需要替换的绝对路径。实际使用时请复制生成文件中的真实路径：

~~~json
{
  "mcpServers": {
    "probably-stolen-mcp": {
      "command": "<uv 可执行文件的绝对路径>",
      "args": [
        "--directory",
        "<仓库绝对路径>",
        "run",
        "--locked",
        "--no-dev",
        "probably-stolen-mcp"
      ]
    }
  }
}
~~~

使用设置界面的客户端，选择 **stdio / 本地命令**，分别填写 `command` 和 `args`。保存后刷新 MCP 连接或重启客户端。

**不要把 `http://127.0.0.1:15527` 填成远程 MCP URL。** 这是 Mod 的内部 HTTP 桥接端口，MCP 对外使用 stdio。通常由客户端启动服务，不需要提前手动运行。

连接文件路径不同时，可在 `args` 最后追加 `"--connection", "连接文件的绝对路径"`，或设置服务进程环境变量 `PSMCP_CONNECTION_FILE`。命令行参数优先。端口在游戏内 Mod 配置中修改，重启生效。

## 5. 确认连通，再做第一次操作

先把下面的话发给 AI：

> 使用 probably-stolen-mcp，先调用 get_help 读取入门，再调用 get_bridge_status 和 get_game_state。报告是否连通、是否载入存档、天数、现金和当前可用动作。此步骤只检查状态。

当前构建提供 11 个工具。游戏未启动时 `get_help` 仍应成功；能列出工具不代表已经连上游戏。`get_bridge_status` 返回 `success: true` 才表示桥接连接成功。在主菜单时 `state.gameLoaded: false` 是正常的。

载入一个用于尝试的存档，保持游戏运行。游戏操作会正常影响物品、现金、时间和自动保存。然后可以让 AI：

> 读取最新状态；如果 availableActions 中有 open_shop，就执行一次并读取新状态，确认是否开店。若已经开店，报告当前阶段，不额外接待顾客。

日常使用时不必手写 JSON。客户端中的 Agent 负责根据工具返回的动作和参数调用。

## 6. 跑完一天

在测试存档中，可以直接发给 AI：

> 使用 probably-stolen-mcp 完成开店、接待一位顾客并完成一笔交易、送客、闭店、睡眠到次日。允许执行这段流程中的正常游戏操作、费用结算和自动保存。先读 get_help 的 actions、trade 和 night 主题；每步依据最新 availableActions 操作，处理游戏里的对话和确认，再检查实际结果。结束时报告交易内容、现金变化和次日天数；如果缺少必要动作或结果无法确定，说明停在哪一步。

流程中需要核对：

| 阶段 | 如何判断完成 |
| --- | --- |
| 开店 | 阶段和卷帘门状态改变；仅收到 `dispatched` 不够 |
| 接待与交易 | 顾客、报价、现金和物品归属按实际交易改变 |
| 送客与闭店 | 顾客离开；原生闭店确认已处理，进入夜间阶段 |
| 睡眠 | `sleep` 打开日结面板；选择服务、确认后重新查询，核对天数增长 |

交易报价来自游戏界面，不等于物品真实价值。这个流程是一个任务示例；不同顾客、剧情和游戏版本可能需要额外适配。

## 7. 展示柜、摆放、垃圾桶和夜间

可以给 AI 一个具体目标：

| 目标 | 可直接使用的指令示例 |
| --- | --- |
| 展示柜与摆放 | “把这块灯牌放进展示柜，必要时旋转，再检查位置和归属。” |
| 整理与撤销 | “整理当前库存，检查数量；再撤销这次整理，核对布局是否恢复。” |
| 背包与垃圾桶 | “打开背包，把指定的废品放进垃圾桶，核对来源和目标数量。” |
| 电话与升级 | “打电话查看可用升级，报告费用和前置条件。”；购买时补充要买的升级 |
| 举报 | “对当前顾客执行游戏允许的举报，检查警方处理和回执。” |
| 夜间拾荒 | “准备背包，去垃圾场拾荒一次，装好找到的物品并返回，报告结果。” |

游戏正常处理费用、容量、所有权、伤势、误报惩罚和遗留物品。放入垃圾桶与清空垃圾桶是不同操作；睡眠服务可能清理垃圾。物品铺放由当前 `item_move` 动作完成：支持合法目标容器、格子位置、旋转和翻转，参数以该次返回为准。

床铺通过当前 `sleep` 动作操作；背包内容需要先打开才会出现在库存查询里。无需客户端为每个页面另写点击规则：Mod 汇总游戏规则和界面状态，返回此刻可用的动作。尚未适配的专用控件仍可能没有操作入口。

### 新游戏与职业物品

让 Agent 先读取主菜单，按“新游戏 → 职业 → 特性 → 难度／开局选项 → 开始游戏”逐步操作。卡片的 isOn 表示选中状态；禁用卡和开发中职业不能据名称强行继续。游戏内可用时调用 navigate_back，检查顶部窗口是否已关闭或暂停菜单是否出现，再选择主菜单。

打开职业设备后，用 containers[].label、windowTitle 和 parentItemId 识别输入／输出、种子、水、动物等区域，再从 item_move 的候选目的地中选择。图标槽位的 label 可能为空。工具的合法目标也随游戏条件变化：例如满瓶倒空后才允许用配给包补水。加工后核对实际物品变化；刀具处理动物可能需要两步，跨夜生产需要正常结算。详见[职业实测结果](profession-tests.zh-CN.md)。

### 跨夜生产状态

已呈现、归玩家所有的物品会返回 `statusText`：使用原生普通悬浮提示中的生产进度、动物／鱼状态、水量、电量和工具次数。文本随游戏语言变化，可能含游戏富文本标记；空字符串表示所接入的提示没有内容，`null` 表示不适用或不可用（包括开发模式）。它不是全部机器的统一配方接口，最多 8192 字符。

睡前服务控件返回 `kind=checkbox`、`isOn` 和服务名称／费用。通过当前 `ui_click` 原生切换后重新读取，避免误关电力。确认睡眠后，以天数变化及输入消耗、输出槽新物品判断生产完成；`dispatched` 和进度增加都不等于产出。具体调用与结果见[独立跨夜测试记录](overnight-tests.zh-CN.md)。

## 8. Agent 的调用规则

只需遵循“观察 → 选择当前动作 → 执行 → 核对结果”：

1. `get_game_state` 的数据在 `state` 内；`get_available_actions` 的 `snapshotId` 和 `availableActions` 在顶层。
2. 复制 `availableActions[].name`，不要把 `kind`、描述、物品名称或数字 ID 当成动作名。
3. 用同次观察的 `snapshotId` 和新 UUID 调用 `perform_action`；`arguments` 只填写该动作 `parameters` 允许的字段与候选引用。
4. 操作后重新观察；`dispatched` 表示已调用处理函数，动画、确认和交易可能还没有完成。
5. `stale_snapshot` 时重新观察、决定并生成新 UUID。超时或断线则先查 `get_action_status` 和当前状态；重试原命令必须保持 UUID、动作、快照与完整参数不变。

下面只是**参数模板**，占位符必须替换为当次真实值；无参数动作省略 `arguments`：

~~~json
{
  "action": "<完整动作 name>",
  "expectedSnapshotId": "<同次观察的 snapshotId>",
  "requestId": "<新生成的 UUID>"
}
~~~

需要说明时调用 `get_help(topic="actions")`；物品、鉴定、机器、交易、夜间、菜单分别有 `items`、`appraisal`、`machines`、`trade`、`night`、`menus`。内置帮助当前以中文为主；英文操作说明见本指南的 [English 版本](usage-guide.md)。静态帮助不能替代实时动作列表。

## 9. 常见问题

| 现象 | 处理 |
| --- | --- |
| 找不到 `uv` 或 Python | 安装 uv 并重启客户端，检查 command 路径；首次启动需要联网下载 Python 和依赖 |
| 提示缺少 `Assembly-CSharp.dll` | `-GamePath` 应指向游戏 exe 所在目录，不能指向仓库或 Managed 子目录 |
| Mods 菜单找不到 Mod | 检查文件夹是否多套一层、三个文件是否齐全；查看游戏 Mod 日志 |
| `connection_file_unavailable` | 启用 Mod 并重启游戏，核对连接文件位置；必要时设置 `--connection` |
| `bridge_unavailable` | 游戏是否运行、Mod 是否启动成功、是否有旧 Mod 占端口 |
| `unauthorized` / 401 | 使用当前 Mod 生成的连接文件；不要手动保留旧令牌 |
| `protocol_mismatch` | 从同次构建同时更新 Mod 和 MCP，并重启两者 |
| 有截图或文字，却没有对应动作 | 看当前控件的 `enabled`、`disabledReason` 和 `interaction`；可能禁用、遮挡或尚未适配 |
| `stale_snapshot` / `action_unavailable` | 重新读状态再决定，不沿用旧引用 |
| `action_outcome_unknown` | 查询原 UUID 的状态并核对游戏；不要换 UUID 盲目重复交易 |
| 游戏查询或截图超时 | 让游戏恢复响应并保持画面渲染；处理动画／窗口后再观察 |
| 手动启动 MCP 后没输出 | stdio 服务在等待客户端请求；用 `--help` 检查启动命令 |

更多调试接口见 [开发指南](development.zh-CN.md#手动-http-调用)。反馈问题时附游戏 Build、Mod／MCP 版本、复现步骤、工具名和错误码；去掉令牌、个人路径以及不相关的存档信息。

## 10. 更新、卸载与数据说明

更新前退出游戏，并停止客户端中的 MCP 服务和本地联调驱动，避免构建时发布文件被占用。拉取源码并重新构建，覆盖三个 Mod 文件，再启动游戏和 MCP。移动仓库后重新生成配置，并更新客户端中的绝对路径。旧 PawnPilot 的自定义端口及连接文件设置需要迁移。

停用时在 Mods 菜单关闭 Probably Stolen MCP 并重启，客户端可移除 MCP 配置。停用不会撤销已经发生的操作；本版本不新增自定义物品或存档格式。

Mod 与 MCP 之间只使用本机认证连接。AI 客户端会接收请求的游戏状态与游戏截图；使用云端模型时，这些工具结果可能发送给该服务商，取决于客户端设置。本项目不内置模型账号，也不实现独立遥测上传。连接令牌只用于本机桥接。

项目原创代码与文档使用 [MIT License](../LICENSE)。游戏名称、游戏内文字及实测截图属于各自权利人，不因本仓库 MIT 许可证获得额外授权；游戏程序、反编译代码和提取素材不随项目提供。模组开发与分发要求见[官方行为准则](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.zh-CN.md)。
