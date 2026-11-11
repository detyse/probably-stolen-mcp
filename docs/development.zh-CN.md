# 开发指南

[English](development.md) · [项目首页](../README.zh-CN.md) · [使用指南](usage-guide.zh-CN.md)

本文集中说明源码构建、调试、测试与接口扩展。Agent 的使用与调用规则见 [README](../README.zh-CN.md#agent-开发)。

## 环境要求

| 用途 | 要求 |
|---|---|
| 构建 Mod 与运行集成测试 | Windows x64、.NET 10 SDK、PowerShell、uv；首次需要下载 NuGet 和 Python 依赖 |
| 加载 Mod | 已安装 Steam 正式版游戏，使用游戏自带的 Mod 加载器和 Unity/Mono 环境 |
| 运行 Python MCP | [uv](https://docs.astral.sh/uv/getting-started/installation/) 管理 Python 3.11+ 和依赖；无需 .NET Runtime |
| 使用 Agent | 支持启动本地 stdio MCP 服务的 AI 客户端，并在客户端配置好模型 |
| 获取源码 | Git，或下载仓库源码 |

SDK 10 用于编译 `netstandard2.1` C# Mod 和开发测试，游戏内使用自带的 Unity/Mono。默认外部 MCP 服务为 Python，原 C# MCP 服务已移除。

游戏 DLL 仅供本机编译引用，不随项目分发。支持范围、游戏版本和验证结果见[能力清单](capabilities.md)与[跨夜生产测试](overnight-tests.zh-CN.md)。

## 从源码构建

源码可以克隆或下载解压到任意目录。使用 Git 时，在准备存放项目的位置打开 PowerShell。

```powershell
git clone https://github.com/detyse/probably-stolen-mcp.git
Set-Location '.\probably-stolen-mcp'
```

以下命令均在仓库根目录（包含 `scripts` 的目录）执行。

在 Steam 的游戏管理菜单中选择“浏览本地文件”，找到包含 `Probably Stolen.exe` 的目录。将 `<游戏安装目录>` 替换为该目录，然后在 PowerShell 中执行：

```powershell
$gamePath = '<游戏安装目录>'
.\scripts\Build.ps1 -GamePath $gamePath
```

脚本会还原锁定的 NuGet 依赖、编译、运行协议测试并生成：

```text
artifacts/
  mod/probably-stolen-mcp/
    manifest.xml
    PSMcp.Mod.dll
    PSMcp.Contracts.dll
  python/
    probably_stolen_mcp-0.2.7-py3-none-any.whl
  mcp-config.json
```

只有编译、测试和打包全部完成，默认构建命令才成功。若遇 Windows 应用控制拦截，请看[常见问题](#构建与运行问题)；编译通过不等于运行验证通过。

生成文件后，继续按[安装步骤](../README.zh-CN.md#1-安装-mod)安装 Mod 并连接客户端。`mcp-config.json` 包含构建机器上的绝对路径，移动文件位置或换机器后需要更新。

## Python 环境与打包

在仓库根目录安装锁定依赖并检查入口，下面的 Python 测试无需 .NET 或游戏文件。

```powershell
uv sync --locked
uv run --locked probably-stolen-mcp --help
uv run --locked pytest
```

使用官方 MCP Python SDK v2（`MCPServer`，对应 STS2MCP 所用 `FastMCP` 的新入口）和 httpx，版本由 `uv.lock` 锁定。首次运行可由 uv 下载 Python 和依赖。

默认客户端命令为 `uv --directory <仓库目录> run --locked --no-dev probably-stolen-mcp`。升级时用生成配置替换旧 dotnet 服务条目，沿用原 Mod 的 `connection.json`、`--connection` 和 `PSMCP_CONNECTION_FILE`，Mod ID 与桥接协议 2 保持兼容。

独立安装时运行 `uv build --wheel`，将生成的 wheel 安装到 Python 3.11+ 环境（可用 pip）。客户端 command 指向该环境的 Python 绝对路径，args 为 `["-m", "psmcp"]`。wheel 内含全部帮助主题，离开源码目录也能使用；仍需 Python 解释器及依赖。具体命令见[英文打包步骤](development.md#python-setup-and-packaging)。

## 工作方式

```text
Agent / AI 客户端
       │ MCP / stdio：客户端启动并管理服务进程
       ▼
Python MCP（Python 3.11+）
       │ HTTP：127.0.0.1:15527，Bearer 令牌
       ▼
PSMcp.Mod（.NET Standard 2.1）
       │ 主线程队列
       ▼
游戏状态与原生操作接口
```

游戏查询请求当前状态，以 JSON 返回；`get_help` 直接读取 MCP 内置文档，无需游戏连接。`get_view` 可按需返回当前游戏帧；当前使用请求—响应方式，不持续推送状态或画面。Agent 完成一次操作后需要再次查询，确认游戏的变化。

HTTP 服务仅监听本机，无需部署公网服务器。**AI 客户端应配置 stdio MCP 的启动命令；`http://127.0.0.1:15527` 是 Mod 的内部桥接地址，不是 Streamable HTTP MCP 地址。**

## 扩展 Agent 接口

使用已有接口开发 Agent 时，先看[动作循环](../README.zh-CN.md#动作循环)与[调用规则](usage-guide.zh-CN.md#8-agent-的调用规则)。扩展项目时可从以下位置入手。

- 在 [PSMcp.Mod](../src/PSMcp.Mod) 增加原生交互适配，让动作发现与执行遵守同一套游戏规则和参数约束。
- 在 [PSMcp.Contracts](../src/PSMcp.Contracts) 定义共享数据，通过 [Python MCP](../mcp/psmcp) 暴露 MCP 工具。已有适配与限制见[实现说明](mcp-implementation.md)。
- 在 [docs/help](help) 增加帮助主题，源码运行直接读取文档，wheel 部署需重新打包并重启 MCP。文档文件名即主题名，使用小写英文，以 `# 标题` 开头。
- 修改工具、控件或限制时同步更新[能力清单](capabilities.md)，分别记录已接入与实机验证状态。

项目与程序集使用 `PSMcp.*`，Mod ID 和 MCP 配置键为 `probably-stolen-mcp`，环境变量使用 `PSMCP_*`，游戏内显示名为 **Probably Stolen MCP**。

## 连接配置与启动检查

在仓库根目录检查服务入口。

```powershell
uv run --locked --no-dev probably-stolen-mcp --help
```

不加 `--help` 会启动 stdio 服务并等待客户端请求。日志写入 stderr，stdout 用于 MCP 协议。

Mod 的连接文件通常位于下方路径，以游戏日志为准。

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json
```

自定义连接文件时，在客户端的服务参数末尾追加 `--connection` 和文件绝对路径，或设置服务进程的 `PSMCP_CONNECTION_FILE` 环境变量，命令行参数优先。Mod 重启时更换令牌，MCP 每次调用重新读取文件。

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
$connectionPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\ModData\probably-stolen-mcp\connection.json'

function Invoke-PSMcp {
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

Invoke-PSMcp -Path health
$stateReply = Invoke-PSMcp -Path state
$stateReply | ConvertTo-Json -Depth 12
```

连接文件路径不同的话，修改 `$connectionPath`。返回值中的 `success`、`errorCode`、`message` 表示实际结果。**即使 HTTP 状态码为 200，也需要检查 JSON 中的 success**；例如过期快照会作为业务错误返回。

### 执行一个动作

以下示例会改变游戏进度。在同一个 PowerShell 会话中执行，先查看可用动作，再输入你要执行的完整动作名：

```powershell
$stateReply = Invoke-PSMcp -Path state
if (!$stateReply.success) { throw $stateReply.message }
$stateReply.state.availableActions | Format-Table name, description
$actionName = Read-Host '输入上表中要执行的完整动作名'

# 输入期间游戏可能变化，提交前再取一次快照。
$stateReply = Invoke-PSMcp -Path state
if (!$stateReply.success) { throw $stateReply.message }
if ($actionName -notin @($stateReply.state.availableActions.name)) {
    throw '这个动作当前不可用，请重新观察。'
}
$actionJson = [ordered]@{
    action = $actionName
    expectedSnapshotId = $stateReply.state.snapshotId
    requestId = [guid]::NewGuid().ToString('D')
} | ConvertTo-Json -Compress

$actionReply = Invoke-PSMcp -Path actions -ActionJson $actionJson
$actionReply | ConvertTo-Json -Depth 12
Invoke-PSMcp -Path state | ConvertTo-Json -Depth 12
```

如果响应丢失，先通过 `GET /actions/<requestId>` 查询原结果。重试同一次命令时，复用上面的 `$actionJson`，不要重新生成 UUID。POST 请求必须是带 Content-Length 的 JSON，正文不超过 16 KiB；上述 PowerShell 调用会按此方式发送。

桥接使用 `TcpListener`，不依赖 HTTP.sys。支持 `GET /health`、`GET /state`、`GET /view`、`GET /actions/<requestId>` 和 `POST /actions`。桥接只接受 `127.0.0.1` 对应的 Host，并拒绝携带 Origin 的请求。请使用 MCP 客户端或上述 PowerShell 示例，不要从网页脚本直接访问。

## 构建与运行问题

| 现象 | 检查或处理 |
| --- | --- |
| 找不到 `dotnet` | 安装 .NET 10 SDK 后重新打开终端；构建脚本也会尝试 `%ProgramFiles%\dotnet\dotnet.exe`。此项仅涉及 Mod 构建与开发测试。 |
| 缺少游戏程序集 / GamePath 错误 | `-GamePath` 应指向包含 `Probably Stolen.exe` 和 `Probably Stolen_Data` 的目录。 |
| 发布文件被占用 | 先关闭游戏，停止客户端中的 MCP 服务及本地测试驱动，再重新构建。 |

游戏日志通常位于：

```text
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log
```

若默认端口被占用，在游戏的 Mod 配置中修改 Probably Stolen MCP 的 `port`，重启后再连接。

**Windows 应用控制拦截：** 若运行时出现 `0x800711C7` 或“应用程序控制策略已阻止此文件”，表示系统拒绝加载 DLL。编译成功仍可能遇到此问题。查看事件查看器中的 `Microsoft-Windows-CodeIntegrity/Operational`，核实被拦截文件和适用策略，并由设备策略管理方按本机开发软件流程处理；本文不提供关闭安全策略的步骤。`-SkipTests` 只能跳过测试完成打包，不能解决运行拦截。

更多连接与动作错误见[使用指南的常见问题](usage-guide.zh-CN.md#9-常见问题)。更新 Mod 与 MCP 时使用同一次构建，移动项目后重新生成客户端配置。

## 测试与实机验证

构建参数：

| 参数 / 环境变量 | 用途 |
|---|---|
| `-GamePath <目录>` | 指定本机游戏目录 |
| `PSMCP_GAME_PATH` | 设置后可省略 `-GamePath` |
| `-NoRestore` | 使用当前目录中已还原的依赖；首次构建或迁移目录后应先还原 |
| `-SkipTests` | 跳过测试、只构建和打包；不能据此声称运行验证通过 |

构建缓存位于 `.cache/`，缓存、bin、obj 和 artifacts 均被 Git 忽略。仅测试协议和 MCP 时无需游戏文件：

```powershell
.\scripts\Test.ps1
```

GitHub [自动检查](../.github/workflows/ci.yml) 在 Windows / .NET 10 SDK / uv 上运行 Python 测试、wheel 安装检查，以及 C# 桥接到 Python MCP 的集成测试；它不编译依赖游戏 DLL 的 Mod，也不替代实机验收。

测试使用模拟游戏状态，运行生产 HTTP 桥接代码、真实 Python MCP 服务进程和官方 C# MCP 客户端，覆盖主线程调度、过期状态、重复动作、部分失败、排队超时、认证和 stdio 工具调用。它不能代替游戏内测试。

完整测试包含内置帮助的 stdio 验证；也可单独运行 `.\scripts\Test.ps1 -HelpOnly`。该模式从临时工作目录启动 MCP，使用不存在的游戏连接文件，检查所有主题、工具声明和非法主题处理；不验证游戏 HTTP 桥接。`-HelpOnly` 与 `-Offline` 互斥。

扩展帮助只需在 [docs/help](help) 增加小写英文文件名的 Markdown，以 `# 标题` 开头，然后重新构建／发布 MCP。文件名即 topic，自动列入目录，无需新增工具或注册表。同步更新 [能力清单](capabilities.md) 的支持和验证状态；源码运行从文档读取帮助，wheel 使用打包时内容，更新安装包后重启 MCP。

测试结果、版本与证据单独维护在 [能力清单](capabilities.md)、[职业测试记录](profession-tests.zh-CN.md)和[跨夜生产记录](overnight-tests.zh-CN.md)。

### 主线程与动作处理

游戏访问全部在 Unity `Update` 中处理，网络线程不直接操作游戏对象。队列最多容纳 64 个请求，每帧最多处理 8 个；等待主线程的超时为 5 秒。排队超时且尚未开始的动作会被取消，不会在后续帧补执行。已开始执行却超时的操作报告结果不确定，不自动重试。

### 实机验证顺序

1. 按[官方指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)在游戏可执行文件旁创建空的 `dev.txt`，用 F8 打开开发控制台，执行 `mod-scan probably-stolen-mcp`。确认能力声明覆盖扫描结果且无拒绝项；当前清单声明 `network`。
2. 启用 Mod 并重启，在主菜单检查 `get_bridge_status` 和未载入存档的状态。
3. 载入测试存档，对比现金、租金、顾客、对话选项和背包。
4. 执行一个当前允许的动作并检查效果；用相同 UUID 和参数重试，确认没有重复操作。
5. 手动改变局面后提交旧快照，确认被拒绝；切换存档和返回主菜单后重复检查。

### 项目结构

| 路径 | 职责 |
|---|---|
| [src/PSMcp.Contracts](../src/PSMcp.Contracts) | 协议模型、快照版本、请求去重和主线程队列 |
| [src/PSMcp.Mod](../src/PSMcp.Mod) | 官方 IMod 入口、游戏适配器和本机 HTTP 服务 |
| [mcp/psmcp](../mcp/psmcp) | 官方 Python MCP SDK、stdio 工具和桥接客户端 |
| [tests/PSMcp.SmokeTests](../tests/PSMcp.SmokeTests) | 行为测试和端到端协议测试 |
| [scripts/Build.ps1](../scripts/Build.ps1) | 构建、测试、打包并生成客户端配置 |
| [scripts/Inspect-GameApi.ps1](../scripts/Inspect-GameApi.ps1) | 只读查看本机游戏程序集元数据，不执行游戏代码 |

## 参考

- [官方 Mod 入门](https://github.com/dragonpan2/probably-stolen-modding/blob/main/README.zh-CN.md)
- [官方 Mod 指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)
- [官方 Mod API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)
- [官方 Python MCP SDK](https://github.com/modelcontextprotocol/python-sdk)
- [STS2MCP 架构参考](https://github.com/Gennadiyev/STS2MCP)

项目原创代码与文档使用 [MIT License](../LICENSE)。游戏名称、游戏内文字和实测截图属于各自权利人，不因本仓库 MIT 许可证获得额外授权；游戏程序、反编译代码和提取素材不随项目分发。依赖保留各自许可证。本项目是独立 Mod，不是游戏官方发行。分发边界见[官方模组行为准则](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.zh-CN.md)。
