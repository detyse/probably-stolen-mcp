# Probably Stolen MCP

[English](README.md) · [使用指南](docs/usage-guide.zh-CN.md) · [Agent 开发](#agent-开发)

用 Probably Stolen MCP，你可以问 AI 助手店里有哪些货，让它查看顾客的报价，或替你开店营业。

## 开始使用

你需要安装 Steam 版《深空当铺》，并准备一个支持本地 stdio MCP 服务的 AI 客户端。支持 Windows x64，或在 Linux x64 上通过 Steam Proton 运行 Windows 版游戏；Python MCP 服务在主机上原生运行。**两个平台共用同一份 Mod ZIP 和版本号。** Proton 设置、自定义前缀和源码构建见 [Linux 指南](docs/linux.zh-CN.md)。

### 1. 安装 Mod

1. [下载模组压缩包](https://github.com/detyse/probably-stolen-mcp/releases/download/v0.2.8/probably-stolen-mcp-0.2.8-mod.zip)，打开 ZIP。
2. 退出游戏。在 Steam 库中右键点击《深空当铺》，选择 **管理 → 浏览本地文件**。
3. 把 ZIP 里的 **Mods** 文件夹复制到刚打开的游戏目录。更新旧版时替换原来的 Mod 文件。
4. 启动游戏，在**模组（Mods）**菜单中启用 **Probably Stolen MCP**。按提示允许网络连接，以便 AI 客户端连接游戏，然后重启游戏。

如果之前装过 PawnPilot，请按[升级说明](docs/usage-guide.zh-CN.md#3-安装-mod)迁移。

### 2. 连接 AI 客户端

1. [安装 uv](https://docs.astral.sh/uv/getting-started/installation/)。第一次连接时，它会自动准备 Python 和所需的软件包。
2. [下载对应版本的 MCP 服务源码](https://github.com/detyse/probably-stolen-mcp/archive/refs/tags/v0.2.8.zip)，解压到打算长期保留的位置。在包含 `pyproject.toml` 的文件夹中打开终端。
3. 按操作系统运行命令，生成本机配置。

   **Windows（PowerShell）：**

   ```powershell
   .\scripts\Configure-Mcp.ps1
   ```

   **Linux：**

   ```bash
   python3 scripts/Configure-Mcp.py
   ```

   Linux 会自动查找常见 Steam 目录及配置的额外游戏库中的 Proton 连接文件，也能识别 Flatpak 路径。自定义位置可在 Python 脚本后添加 `--connection /连接文件的绝对路径/connection.json`。

4. 打开生成的 `artifacts/mcp-config.json`，把其中的 `probably-stolen-mcp` 条目添加到 AI 客户端 MCP 设置的 `mcpServers` 下。如果客户端用表单添加服务，选择 **stdio（本地命令）**，填入文件里的 `command` 和 `args`。
5. 刷新客户端的 MCP 连接或重启客户端。如果以后移动了源码文件夹，重新运行脚本，并更新客户端配置。

### 3. 试一次

载入存档后，可以这样问 AI 助手。

> 我现在有多少钱？店里有哪些货？

等它能读到店里的情况，再试试让它开店营业。游戏可能会自动保存这些操作，第一次尝试时可以用测试存档。更多例子见[让 AI 经营一天](docs/usage-guide.zh-CN.md#6-跑完一天)。

0.2.8 是实验版。已有 Linux／Proton 基础实机记录覆盖 Mod 加载、认证连接、主菜单状态与截图、选项打开／滚动／关闭、动作状态查询，以及游戏重启后的新连接；Linux 存档内的交易、生产与完整经营日尚未验证。准确构建版本与范围见[能力清单](docs/capabilities.md)和[实机记录](docs/validation/2026-11-22-linux.json)。

## 0.2.8 新增内容

- Linux 原生 MCP，自动发现 Steam／Proton 连接文件，也可手动指定路径。
- Linux 配置、构建和测试脚本，以及 Windows／Ubuntu CI。
- `bash scripts/Run-Game.sh` 使用窗口模式参数启动 Steam 游戏，方便本机测试；游戏保存的分辨率可能覆盖请求的窗口尺寸。
- 共用一份 Mod 发布包；桥接协议仍为 2，11 个 MCP 工具保持不变。

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
