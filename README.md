# Probably Automated

《深空当铺：可能是偷的》（Probably Stolen）的全 C# AI 接入项目。它提供游戏 Mod 和 MCP 服务，让支持 MCP 的 AI 客户端读取游戏状态、执行当前允许的操作。模型与决策由 AI 客户端负责，项目本身不内置模型。

**当前版本：0.1.0 开发版。** 已实现状态读取、主线程动作队列和五个 MCP 工具；买卖、议价等经营操作尚未接入，暂时不能自主完成一天的经营。游戏内加载、模组扫描和真实操作仍待验证。

## 工作方式

```text
Agent / AI 客户端
       │ MCP / stdio：客户端启动并管理服务进程
       ▼
PawnPilot.Mcp（.NET 10）
       │ HTTP：127.0.0.1:15527，Bearer 令牌
       ▼
PawnPilot.Mod（.NET Standard 2.1）
       │ 主线程队列
       ▼
游戏状态与原生操作接口
```

每次查询都会请求当前游戏状态，以 JSON 返回。当前使用请求—响应方式，不持续推送状态或游戏画面。Agent 完成一次操作后需要再次查询，确认游戏的变化。

HTTP 服务仅监听本机，无需部署公网服务器。**AI 客户端应配置 stdio MCP 的启动命令；`http://127.0.0.1:15527` 是 Mod 的内部桥接地址，不是 Streamable HTTP MCP 地址。**

- [环境要求](#环境要求)
- [快速开始](#快速开始)
- [Agent 使用方式](#agent-使用方式)
- [手动 HTTP 调用](#手动-http-调用)
- [常见问题](#常见问题)
- [开发与验证](#开发与验证)

## 环境要求

| 用途 | 要求 |
|---|---|
| 构建项目 | Windows x64、.NET 10 SDK、PowerShell；首次还原依赖需要访问 NuGet |
| 加载 Mod | 已安装 Steam 正式版游戏，使用游戏自带的 Mod 加载器和 Unity/Mono 环境 |
| 运行已构建的 MCP | .NET 10 Runtime；安装 SDK 后已具备运行环境 |
| 使用 Agent | 支持启动本地 stdio MCP 服务的 AI 客户端，并在客户端配置好模型 |
| 获取源码 | Git，或下载仓库源码 |

Python、uv、Unity Editor 和完整 Visual Studio 均非必需。Mod 与 MCP 服务都是 C#，但目标框架不同：SDK 10 编译 `netstandard2.1` Mod，外部 MCP 服务运行在 .NET 10 上。

已使用 SDK **10.0.403** 编译；当前游戏引用来自 Steam App **4348910**、Build **26284217**。游戏 DLL 仅供本机编译引用，不随项目分发。清单暂未声明经过实机验证的游戏版本；Steam 补丁标签不能直接代替游戏内部版本字符串。

## 快速开始

以下示例使用项目目录 `D:\Projects\PawnPilot`，游戏目录 `D:\Apps\Steam\steamapps\common\Probably Stolen`。请按自己的安装位置调整。已有构建产物时，可从第 2 步开始。

### 1. 构建项目

在 PowerShell 中执行：

```powershell
Set-Location 'D:\Projects\PawnPilot'
.\scripts\Build.ps1 -GamePath 'D:\Apps\Steam\steamapps\common\Probably Stolen'
```

脚本会还原锁定的 NuGet 依赖、编译、运行协议测试并生成：

```text
artifacts/
  mod/PawnPilot/
    manifest.xml
    PawnPilot.Mod.dll
    PawnPilot.Contracts.dll
  mcp/
    PawnPilot.Mcp.dll
    ... MCP 的运行依赖
  mcp-config.json
```

只有编译、测试和打包全部完成，默认构建命令才成功。若遇 Windows 应用控制拦截，请看[常见问题](#常见问题)；编译通过不等于运行验证通过。

### 2. 安装并启用 Mod

**先关闭游戏。** 在仓库根目录执行以下命令，将三个 Mod 文件复制到游戏中：

```powershell
$gamePath = 'D:\Apps\Steam\steamapps\common\Probably Stolen'
if (!(Test-Path -LiteralPath (Join-Path $gamePath 'Probably Stolen.exe'))) {
    throw '游戏目录不正确，请修改 $gamePath。'
}
$modPath = Join-Path $gamePath 'Mods\PawnPilot'
New-Item -ItemType Directory -Path $modPath -Force | Out-Null
foreach ($file in @('manifest.xml', 'PawnPilot.Mod.dll', 'PawnPilot.Contracts.dll')) {
    Copy-Item -LiteralPath (Join-Path '.\artifacts\mod\PawnPilot' $file) -Destination $modPath -Force
}
```

也可以手动把 `artifacts/mod/PawnPilot` 文件夹复制到游戏的 `Mods` 目录。最终结构应为：

```text
Probably Stolen/
  Probably Stolen.exe
  Mods/
    PawnPilot/
      manifest.xml
      PawnPilot.Mod.dll
      PawnPilot.Contracts.dll
```

启动游戏，在 Mods 菜单启用 PawnPilot，并按提示重启。Mod 目录中只放上述三个文件；MCP 的依赖留在 `artifacts/mcp`，游戏的 Unity、Harmony、Newtonsoft.Json 等库由游戏提供。

Mod 成功启动后监听 `127.0.0.1:15527`，生成本次运行的认证令牌，并写入：

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\pawnpilot\connection.json
```

以 Mod 日志输出的实际路径为准。文件中包含连接地址和控制游戏所需的令牌，不要分享或提交到仓库。端口可在游戏的 Mod 配置中修改，重启生效；MCP 每次调用会重新读取连接文件。

动作会影响当前游戏进度，也可能被游戏自动保存。首次联调请使用测试存档。

### 3. 配置 MCP 客户端

构建生成的 [artifacts/mcp-config.json](artifacts/mcp-config.json) 包含本机可用的绝对路径。对于采用 `mcpServers` JSON 格式的客户端，将其中的 `pawnpilot` 条目合并到已有配置，保留其他服务。

本例对应的配置为：

```json
{
  "mcpServers": {
    "pawnpilot": {
      "command": "C:\\Program Files\\dotnet\\dotnet.exe",
      "args": [
        "D:\\Projects\\PawnPilot\\artifacts\\mcp\\PawnPilot.Mcp.dll"
      ]
    }
  }
}
```

如果客户端通过界面添加服务，选择 **stdio / 本地命令**，填写同样的 command 和 args。不同客户端的配置位置、格式可能不同；配置后刷新 MCP 连接或重启客户端。

通常由客户端启动服务，无需提前在终端手动运行。以下命令仅用于检查启动方式：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' '.\artifacts\mcp\PawnPilot.Mcp.dll' --help
```

不加 `--help` 会启动 stdio 服务并等待协议请求，这是正常行为。日志写入 stderr，stdout 专用于 MCP 协议。

如果连接文件不在默认位置，在客户端的 args 中追加：

```json
[
  "D:\\Projects\\PawnPilot\\artifacts\\mcp\\PawnPilot.Mcp.dll",
  "--connection",
  "C:\\实际数据目录\\ModData\\pawnpilot\\connection.json"
]
```

也可设置 MCP 进程的环境变量 `PAWNPILOT_CONNECTION_FILE`；`--connection` 优先。客户端需要能启动本地进程并访问游戏的连接文件；仅支持云端远程 MCP 的客户端无法直接使用这个 stdio 服务。

### 4. 验证连接

1. 游戏停留在主菜单，让 Agent 调用 `get_bridge_status`。返回 `success: true` 表示 Mod 的认证 HTTP 服务可达。
2. 调用 `get_game_state`。尚未载入存档时，预期 `state.gameLoaded: false`；这与桥接是否连通是两件事。
3. 载入测试存档，再次调用 `get_game_state`。对比游戏界面与返回的现金、顾客和库存。
4. 调用 `get_available_actions` 查看当前允许的操作。确认状态正确后，再执行一个动作并读取新状态。

看到五个 MCP 工具只代表服务注册成功；应完成上述检查，确认它确实连上正在运行的游戏。

## Agent 使用方式

### 工具与当前能力

| MCP 工具 | 参数 | 返回内容 / 行为 |
|---|---|---|
| `get_bridge_status` | 无 | 检查认证连接，不要求已载入存档 |
| `get_game_state` | 无 | 营业阶段、现金、租金、顾客、最近对话、可见选项、主背包及可用动作 |
| `get_inventory` | 无 | 主背包顶层物品的运行期 ID、显示名和数量 |
| `get_available_actions` | 无 | 当前动作、可见对话选项及 `snapshotId` |
| `perform_action` | `action`、`expectedSnapshotId`、`requestId` | 执行一个当前允许的动作 |

状态查询的完整数据位于 `get_game_state` 返回的 `state` 中；`get_inventory` 和 `get_available_actions` 会把所需字段放在结果顶层。MCP 同时返回结构化 JSON 和文本内容，工具错误标记为 `isError`。

| 动作名 | 行为 |
|---|---|
| `open_shop` | 使用原生卷帘门按钮开店 |
| `next_customer` | 呼叫下一位顾客 |
| `dismiss_customer` | 送走当前顾客，不自动呼叫下一位 |
| `close_shop` | 使用原生卷帘门按钮关店 |
| `dialogue_choice:<index>` | 选择当前可见且可点击的对话选项；使用返回的实际 index |

仅执行 `availableActions` 中返回的完整动作名。开关店可能出现确认面板，该面板尚未接入；动作列表为空时，可能需要等待动画，或由玩家完成当前尚未支持的交互。

**尚未实现：** 物品移动、柜台货物导出、买卖、议价、验货、鉴定、手动推进对话、夜间行动和生产流程。库存不会递归展开容器，也不导出隐藏真假、NPC 隐藏身份、未来事件或真实物品价值。

### 可以直接发给 Agent 的指令

先做只读检查：

> 使用 pawnpilot 的 get_bridge_status 和 get_game_state 检查连接，告诉我是否载入存档、当前现金、顾客和可用动作。先不要执行动作。

确认连接后，可做一次操作：

> 读取最新状态。如果 availableActions 中有 next_customer，就生成一个新的 UUID，使用当前 snapshotId 调用 perform_action，只呼叫一次下一位顾客。随后读取状态说明结果。如果动作不可用或结果不确定，报告原因。

### 动作与重试规则

1. 读取状态，从 `availableActions` 选择动作；保存作出决定时的 `snapshotId`。
2. 为这次决定生成一个标准 UUID（`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`），传入 `requestId`；将快照 ID 传入 `expectedSnapshotId`。
3. `success: true`、`actionStatus: dispatched` 表示游戏处理函数已执行。动画或对话可能尚未完成，应再读状态确认效果。
4. `stale_snapshot` 表示局面已变化。根据返回的新状态重新判断，并为新决定使用新 UUID。
5. 超时或断线后先读状态。如需重试原命令，使用**相同 requestId、相同 action、相同 expectedSnapshotId**，不能仅为重试换一个 UUID。
6. 桥接运行期间保留近期 256 条已处理命令的结果；记录尚在缓存中时，相同请求直接返回原结果。游戏重启后不会保留该缓存，旧快照也会失效，应重新观察。

## 手动 HTTP 调用

这是调试接口，不需要启动 MCP 服务，但必须先启用 Mod 并运行游戏。所有路由都需要当前连接文件中的 Bearer 令牌。

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/health` | 检查桥接，不访问游戏主线程 |
| GET | `/state` | 在游戏主线程读取状态 |
| POST | `/actions` | 提交带快照 ID 和请求 UUID 的动作 |

### 查询状态

在 PowerShell 中定义下面的辅助函数。它每次调用都读取当前令牌，不会把令牌打印出来：

```powershell
$connectionPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\pawnpilot\connection.json'

function Invoke-PawnPilot {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('health', 'state', 'actions')]
        [string]$Path,
        [string]$ActionJson
    )
    $connection = Get-Content -LiteralPath $connectionPath -Raw | ConvertFrom-Json
    $requestArgs = @{
        Uri = $connection.baseUrl + $Path
        Headers = @{ Authorization = "Bearer $($connection.token)" }
        TimeoutSec = 10
    }
    if ($PSBoundParameters.ContainsKey('ActionJson')) {
        $requestArgs.Method = 'Post'
        $requestArgs.ContentType = 'application/json; charset=utf-8'
        $requestArgs.Body = [System.Text.Encoding]::UTF8.GetBytes($ActionJson)
    } else {
        $requestArgs.Method = 'Get'
    }
    Invoke-RestMethod @requestArgs
}

Invoke-PawnPilot -Path health
$stateReply = Invoke-PawnPilot -Path state
$stateReply | ConvertTo-Json -Depth 12
```

连接文件路径不同的话，修改 `$connectionPath`。返回值中的 `success`、`errorCode`、`message` 表示实际结果。**即使 HTTP 状态码为 200，也需要检查 JSON 中的 success**；例如过期快照会作为业务错误返回。

### 执行一个动作

以下示例会改变游戏进度。在同一个 PowerShell 会话中执行，先查看可用动作，再输入你要执行的完整动作名：

```powershell
$stateReply = Invoke-PawnPilot -Path state
if (!$stateReply.success) { throw $stateReply.message }
$stateReply.state.availableActions | Format-Table name, description
$actionName = Read-Host '输入上表中要执行的完整动作名'

# 输入期间游戏可能变化，提交前再取一次快照。
$stateReply = Invoke-PawnPilot -Path state
if (!$stateReply.success) { throw $stateReply.message }
if ($actionName -notin @($stateReply.state.availableActions.name)) {
    throw '这个动作当前不可用，请重新观察。'
}
$actionJson = [ordered]@{
    action = $actionName
    expectedSnapshotId = $stateReply.state.snapshotId
    requestId = [guid]::NewGuid().ToString('D')
} | ConvertTo-Json -Compress

$actionReply = Invoke-PawnPilot -Path actions -ActionJson $actionJson
$actionReply | ConvertTo-Json -Depth 12
Invoke-PawnPilot -Path state | ConvertTo-Json -Depth 12
```

如果响应丢失，先查状态。重试同一次命令时，复用上面的 `$actionJson`，不要重新生成 UUID。POST 请求必须是带 Content-Length 的 JSON，正文不超过 16 KiB；上述 PowerShell 调用会按此方式发送。

桥接只接受 `127.0.0.1` 对应的 Host，并拒绝携带 Origin 的请求。请使用 MCP 客户端或上述 PowerShell 示例，不要从网页脚本直接访问。

## 常见问题

| 现象 | 检查或处理 |
|---|---|
| 找不到 `dotnet` | 安装 SDK 后重新打开终端；构建脚本也会尝试 `C:\Program Files\dotnet\dotnet.exe`。客户端可直接填写绝对路径。 |
| 缺少游戏程序集 / GamePath 错误 | `-GamePath` 应指向包含 `Probably Stolen.exe` 和 `Probably Stolen_Data` 的目录。 |
| Mods 菜单找不到 PawnPilot | 检查是否多套了一层目录，以及 `Mods/PawnPilot/manifest.xml` 是否存在。 |
| `connection_file_unavailable` | 确认 Mod 已启用并重启游戏；查看日志中的真实连接文件路径，必要时配置 `--connection`。 |
| `bridge_unavailable` | 游戏可能未启动、Mod 启动失败，或连接文件来自上一次运行。查看游戏日志。 |
| HTTP 401 / `unauthorized` | 令牌已失效；重新读取当前连接文件。MCP 和上面的辅助函数会自动重读。 |
| HTTP 403 / `forbidden_origin` | 使用连接文件中的 `127.0.0.1` 地址，不改成 `localhost`；不要附加 Origin。 |
| `queue_timeout` / `bridge_timeout` | 让游戏保持响应并回到前台；失焦、暂停或加载期间可能不处理主线程请求。 |
| `stale_snapshot` / `action_unavailable` | 重新读取状态与动作列表，根据新状态决定。 |
| `request_id_conflict` | 一个 UUID 被用于不同参数；原命令的重试必须保持三个参数一致，新决定使用新 UUID。 |
| `action_outcome_unknown` | 动作可能已经发生，先查状态；不要用新 UUID 盲目重试。 |
| `protocol_mismatch` | 同时更新 Mod 和 MCP，避免混用不同协议版本。 |
| MCP 启动后终端一直等待 | stdio 服务在等待客户端请求；应由支持 MCP 的客户端连接。 |

游戏日志通常位于：

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

若默认端口被占用，在游戏的 Mod 配置中修改 PawnPilot 的 `port`，重启后再连接。

**Windows 应用控制拦截：** 若运行时出现 `0x800711C7` 或“应用程序控制策略已阻止此文件”，表示系统拒绝加载 DLL。编译成功仍可能遇到此问题。查看事件查看器中的 `Microsoft-Windows-CodeIntegrity/Operational`，核实被拦截文件和适用策略，并由设备策略管理方按本机开发软件流程处理；本文不提供关闭安全策略的步骤。`-SkipTests` 只能跳过测试完成打包，不能解决运行拦截。

### 更新与停用

更新时先关闭游戏，重新构建后覆盖 Mod 的三个文件，并让 MCP 客户端重新启动服务。保持 Mod 与 MCP 来自同一次构建。

移动项目目录后，重新构建以更新 `artifacts/mcp-config.json` 中的绝对路径，并同步修改客户端配置。

在 Mods 菜单停用 PawnPilot 并重启游戏，即可停止游戏桥接；同时可从 AI 客户端移除服务配置。停用不会撤销已经发生的游戏操作。本版本不添加自定义物品或存档格式。

## 开发与验证

构建参数：

| 参数 / 环境变量 | 用途 |
|---|---|
| `-GamePath <目录>` | 指定本机游戏目录 |
| `PAWNPILOT_GAME_PATH` | 设置后可省略 `-GamePath` |
| `-NoRestore` | 使用当前目录中已还原的依赖；首次构建或迁移目录后应先还原 |
| `-SkipTests` | 跳过测试、只构建和打包；不能据此声称运行验证通过 |

构建缓存位于 `.cache/`，缓存、bin、obj 和 artifacts 均被 Git 忽略。仅测试协议和 MCP 时无需游戏文件：

```powershell
.\scripts\Test.ps1
```

测试使用模拟游戏状态，运行生产 HTTP 桥接代码、真实 MCP 服务进程和官方 MCP 客户端，覆盖主线程调度、过期状态、重复动作、部分失败、排队超时、认证和 stdio 工具调用。它不能代替游戏内测试。

截至 **2026-10-31** 的开发验证记录：

- 迁移前，29 项协议与 MCP 检查全部通过，包括对发布产物的验证。
- 迁至 `D:\Projects\PawnPilot` 后，编译与打包成功，0 警告、0 错误；测试运行被 Windows 应用控制策略拦截，新目录运行验证尚未完成。
- 尚未完成真实游戏中的 Mod 加载、扫描与动作验证。

### 主线程与动作处理

游戏访问全部在 Unity `Update` 中处理，网络线程不直接操作游戏对象。队列最多容纳 64 个请求，每帧最多处理 8 个；等待主线程的超时为 5 秒。排队超时且尚未开始的动作会被取消，不会在后续帧补执行。已开始执行却超时的操作报告结果不确定，不自动重试。

### 实机验证顺序

1. 按[官方指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)在游戏可执行文件旁创建空的 `dev.txt`，用 F8 打开开发控制台，执行 `mod-scan pawnpilot`。确认能力声明覆盖扫描结果且无拒绝项；当前清单声明 `network`。
2. 启用 Mod 并重启，在主菜单检查 `get_bridge_status` 和未载入存档的状态。
3. 载入测试存档，对比现金、租金、顾客、对话选项和背包。
4. 执行一个当前允许的动作并检查效果；用相同 UUID 和参数重试，确认没有重复操作。
5. 手动改变局面后提交旧快照，确认被拒绝；切换存档和返回主菜单后重复检查。

### 项目结构

| 路径 | 职责 |
|---|---|
| [src/PawnPilot.Contracts](src/PawnPilot.Contracts) | 协议模型、快照版本、请求去重和主线程队列 |
| [src/PawnPilot.Mod](src/PawnPilot.Mod) | 官方 IMod 入口、游戏适配器和本机 HTTP 服务 |
| [src/PawnPilot.Mcp](src/PawnPilot.Mcp) | 官方 C# MCP SDK、stdio 工具和桥接客户端 |
| [tests/PawnPilot.SmokeTests](tests/PawnPilot.SmokeTests) | 行为测试和端到端协议测试 |
| [scripts/Build.ps1](scripts/Build.ps1) | 构建、测试、打包并生成客户端配置 |
| [scripts/Inspect-GameApi.ps1](scripts/Inspect-GameApi.ps1) | 只读查看本机游戏程序集元数据，不执行游戏代码 |

## 参考

- [官方 Mod 入门](https://github.com/dragonpan2/probably-stolen-modding/blob/main/README.zh-CN.md)
- [官方 Mod 指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)
- [官方 Mod API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)
- [官方 C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [STS2MCP 架构参考](https://github.com/Gennadiyev/STS2MCP)

MIT License，见 [LICENSE](LICENSE)。游戏程序与资源属于其各自权利人，不随本项目分发。
