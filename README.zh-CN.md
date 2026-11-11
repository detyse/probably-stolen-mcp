# Probably Stolen MCP

[English](README.md) · [使用指南](docs/usage-guide.zh-CN.md) · [Agent 开发](#agent-开发)

让你的 AI Agent 通过 MCP 操作《深空当铺：可能是偷的》（Probably Stolen）。项目提供游戏 Mod 和本地 MCP 服务，用于读取当前局面、查看物品和截图、执行当前允许的游戏操作。模型与决策由你的 AI 客户端负责。

**实验性版本 0.2.7 · 桥接协议 2。** 支持范围和实机证据见[能力清单](docs/capabilities.md)、[职业测试](docs/profession-tests.zh-CN.md)和[跨夜生产测试](docs/overnight-tests.zh-CN.md)。不同游戏版本和场景的覆盖程度有所不同。

## 开始使用

需要 Windows x64、已安装的 Steam 版游戏、[uv](https://docs.astral.sh/uv/getting-started/installation/)，以及能启动本地 **stdio MCP** 服务的 AI 客户端。查看游戏截图还需要客户端和模型支持图片工具结果。

从 [Releases](https://github.com/detyse/probably-stolen-mcp/releases/tag/v0.2.7) 下载 `probably-stolen-mcp-0.2.7-mod.zip`。包内只有放进游戏的三个 Mod 文件，安装无需 .NET SDK。

### 1. 安装 Mod

1. 退出游戏，在 Steam 中选择“浏览本地文件”，打开游戏安装目录。
2. 将 ZIP 解压到游戏安装目录。包内已有 `Mods/probably-stolen-mcp/`，其中包含 `manifest.xml`、`PSMcp.Mod.dll` 和 `PSMcp.Contracts.dll`。
3. 启动游戏，在 Mods 菜单启用 **Probably Stolen MCP**，按提示重启。如果下载包的菜单名称不同，以安装目录中 `manifest.xml` 的 `<Name>` 为准。

从 PawnPilot 升级的步骤见[安装指南](docs/usage-guide.zh-CN.md#3-安装-mod)。

### 2. 连接 AI 客户端

另行克隆或下载本仓库，放到固定目录供 Python MCP 服务使用。在仓库根目录打开 PowerShell，运行 `./scripts/Configure-Mcp.ps1`，按本机路径生成 `artifacts/mcp-config.json`，将其中的服务条目合并到客户端配置。移动目录后重新运行此脚本；首次启动时 uv 会联网准备 Python 和依赖。使用预编译 Mod 时可跳过 .NET 构建步骤。

使用 `mcpServers` 格式的客户端可参考以下结构，将占位符替换为生成文件中的实际路径。

```json
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
```

通过设置界面添加时，选择 **stdio / 本地命令**，分别填写 `command` 和 `args`，然后刷新 MCP 连接或重启客户端。MCP 服务进程由客户端启动和管理。

Mod 的 `http://127.0.0.1:15527` 是内部桥接地址。客户端使用上面的 stdio 启动配置；自定义连接文件位置见[客户端配置指南](docs/usage-guide.zh-CN.md#4-把-mcp-接到-ai-客户端)。

### 3. 试一次

启动游戏，把下面的话发给 Agent。

> 使用 probably-stolen-mcp。先读 get_help，再调用 get_bridge_status 和 get_game_state。报告是否连通、是否载入存档，以及当前天数、现金和可用动作。此步骤只检查状态。

主菜单中的 `gameLoaded: false` 是正常的。载入测试存档后，可以尝试一个动作。

> 读取最新状态。如果 open_shop 可用，就执行一次并重新读取状态，确认是否开店。如果动作不可用或结果不确定，说明原因。

操作会影响当前游戏，也可能被自动保存。首次使用请用测试存档。更长的任务示例见[完成一天经营](docs/usage-guide.zh-CN.md#6-跑完一天)和[物品、摆放与夜间出行](docs/usage-guide.zh-CN.md#7-展示柜摆放垃圾桶和夜间)。

## Agent 开发

通过 MCP 接入，按“观察 → 选择当前动作 → 执行 → 核对结果”循环。Mod 返回游戏规则下的可用操作、合法目标与参数约束；Agent 负责规划、理解证据，以及判断结果是否满足任务。

### 工具

| 工具 | 用途 |
| --- | --- |
| `get_help(topic?)` | 读取内置帮助和主题目录，游戏关闭时也可用 |
| `get_bridge_status` | 检查与 Mod 的认证连接 |
| `get_game_state` | 读取天数、阶段、现金、顾客、库存、界面与可用动作 |
| `get_trade_state` | 查看柜台、公开报价与交易交互 |
| `get_inventory(containerId?, offset?, limit?)` | 分页读取已呈现容器和已打开内容 |
| `get_available_actions` | 获取当前动作、参数约束、`snapshotId`、限制和调用说明 |
| `get_ui_state` | 读取可见文字、控件、禁用原因与界面动作 |
| `get_entity(entityId)` | 查看当前会话已暴露的物品、容器或控件 |
| `get_view` | 获取游戏截图及对应观察 |
| `get_action_status(requestId)` | 查询保留的命令结果，不重复执行 |
| `perform_action(action, expectedSnapshotId, requestId, arguments?)` | 执行一个当前允许的原生动作 |

### 动作循环

1. 读取最新状态。`get_game_state` 的观察位于 `state` 中；`get_available_actions` 的 `snapshotId` 和 `availableActions` 位于顶层。
2. 原样复制 `availableActions[].name`。按该动作的 `parameters` 填写 `arguments`，遵守必填项、范围和候选引用。
3. 使用同次观察的 `snapshotId` 和新 UUID `requestId` 调用 `perform_action`。下面是无参数动作的模板。

```json
{
  "action": "<完整的 availableActions[].name>",
  "expectedSnapshotId": "<同次观察的 snapshotId>",
  "requestId": "<新 UUID>"
}
```

4. 再次观察，核对真实效果。`dispatched` 表示处理函数已调用，动画、确认或生产可能还没有完成。
5. 遇到 `stale_snapshot`，重新观察并用新 UUID 作出新决定。超时或断线后，先查询 `get_action_status` 和当前局面；重试原命令必须保留原 UUID、动作、快照及完整参数。

结果缓存仅保留本次 Mod 会话中最近的 256 条命令。查不到结果不能证明动作从未执行。切换存档或重启游戏后，需要获取新的观察和引用。没有可用动作时，先检查等待状态与限制。

调用规则可查 `get_help(topic="actions")`，其他主题包括 `items`、`appraisal`、`machines`、`trade`、`night`、`menus` 和 `capabilities`。完整说明见[Agent 调用规则](docs/usage-guide.zh-CN.md#8-agent-的调用规则)。

### 扩展接口

日常交互由实时动作列表驱动。需要增加游戏交互或 MCP 工具时，可从[扩展指南](docs/development.zh-CN.md#扩展-agent-接口)、[协议模型](src/PSMcp.Contracts)和[MCP 工具](mcp/psmcp)入手。[实现说明](docs/mcp-implementation.md)记录已有适配和限制。

## 帮助与数据

Mod 未出现、连接失败或动作报错时，先看[常见问题](docs/usage-guide.zh-CN.md#9-常见问题)。反馈时附游戏 Build、Mod/MCP 版本、工具名、错误码与复现步骤。

Mod 连接仅在本机使用。AI 客户端会接收请求的游戏状态和图片，并可能发送给其模型服务商。请保管好 `connection.json` 中的控制令牌，分享日志前移除个人路径。

原创代码与文档使用 [MIT License](LICENSE)，游戏内容保留原权利人的权利。本项目为独立项目，Mod 开发参考[官方指南](https://github.com/dragonpan2/probably-stolen-modding)，架构参考 [STS2MCP](https://github.com/Gennadiyev/STS2MCP)。
