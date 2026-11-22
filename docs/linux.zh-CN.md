# Linux / Steam Proton 使用说明

[English](linux.md) · [项目首页](../README.zh-CN.md) · [能力与实测状态](capabilities.md)

MCP 服务在 Linux 上使用原生 Python 3.11+ 运行，Windows 版游戏和 C# Mod 一起在
Proton 下运行。这里增加的是 Linux 主机支持，不提供原生 Linux 游戏程序。
游戏 Steam App ID 为 **4348910**。Linux 玩法兼容性仍属实验范围，编译和协议测试
不能代替实机验收。2026-11-22 已验证 Mod 加载、主菜单读取与截图、选项打开／滚动／关闭、动作状态查询，以及游戏重启后的新连接。未载入存档，经营玩法仍待验证。详情见 [Linux 实机记录](validation/2026-11-22-linux.json)。

## 安装并连接

1. 通过 Steam 安装游戏，使用 Proton 启动一次，再退出游戏。通过 Steam 的
   **管理 → 浏览本地文件**定位游戏目录。
2. 把发布包里的 `Mods` 文件夹复制到游戏目录；或按下文构建，把
   `artifacts/mod/probably-stolen-mcp` 复制到游戏的 `Mods` 目录中。
   Windows 和 Proton 使用相同的托管 Mod 文件。
3. 启动游戏，在 **Mods** 菜单启用 **Probably Stolen MCP**，确认网络权限并重启。
   这一步权限确认需要在游戏里完成。
4. 在 Linux 主机上[安装 uv](https://docs.astral.sh/uv/getting-started/installation/)，
   把仓库放在长期保留的位置，在仓库根目录运行：

   ```bash
   python3 scripts/Configure-Mcp.py
   ```

5. 将生成的 `artifacts/mcp-config.json` 中的服务条目加入 AI 客户端本地 stdio MCP
   设置。客户端运行的是 Linux `uv`。移动仓库后需要重新生成配置。
6. 刷新 MCP 连接，先调用 `get_bridge_status`，再调用 `get_game_state`。
   连接文件缺失通常表示 Mod 尚未启用并重启。

使用预编译 Mod 无需安装 .NET SDK，只有源码构建和 C# 测试才需要。
游戏未启动时仍可使用 `get_help`。

本机测试默认传入 1280×720 窗口模式启动参数；游戏保存的分辨率可能覆盖窗口尺寸：

```bash
bash scripts/Run-Game.sh
```

该脚本仅设置本次启动参数，不修改 Steam 的全局设置。需要其他窗口尺寸时，可以在命令后追加
`-screen-width 1600 -screen-height 900`。

## 连接文件在哪里

Proton 将每个游戏的 Windows 用户数据放在单独的前缀目录，见
[Valve Proton FAQ](https://github.com/ValveSoftware/Proton/wiki/Proton-FAQ#how-does-proton-manage-wine-prefixes)。
默认 Steam 库对应的文件为：

```text
~/.local/share/Steam/steamapps/compatdata/4348910/pfx/drive_c/users/steamuser/AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/probably-stolen-mcp/connection.json
```

自动发现会检查 `$XDG_DATA_HOME/Steam`（默认为 `~/.local/share/Steam`）、
`~/.steam/steam`、`~/.steam/root` 和 Flatpak 的
`~/.var/app/com.valvesoftware.Steam/.local/share/Steam`，并读取这些位置的
`steamapps/libraryfolders.vdf` 查找额外游戏库。支持空格、中文路径，重复目录别名会去重。
只检查本游戏的前缀，不扫描其他游戏的数据。

优先级：`--connection` → `PSMCP_CONNECTION_FILE` → `STEAM_COMPAT_DATA_PATH/pfx`
→ `WINEPREFIX` → Steam 自动发现。前两个参数指向 JSON 文件，前缀变量指向目录。
前缀自动查找使用 Proton 的 `steamuser` 用户名；其他 Wine 用户名请直接指定连接文件。
发现多份不同连接文件时要求明确指定路径；指定前缀无效时也不会改连其他安装。

可将明确的连接路径保存到客户端配置中：

```bash
python3 scripts/Configure-Mcp.py --connection '/连接文件的绝对路径/connection.json'
```

生成器也会保留运行它的终端中的 `PSMCP_CONNECTION_FILE`，或前缀／XDG 环境变量。
配置只保存路径，不复制控制令牌。AI 客户端需要能够读取前缀目录，并访问本机
`127.0.0.1`。识别 Flatpak 路径不等于已经验证沙箱访问权限或 Flatpak 游戏兼容性。
自动发现失败时可指定客户端实际能够读取的文件。不要分享 `connection.json`。
桥接服务继续只监听 IPv4 本机回环地址，无需开放防火墙或公网端口。

## Linux 源码构建与测试

准备 `uv`、Python 3.11+、Bash 和 .NET 10 SDK（见 `global.json`），运行：

```bash
bash scripts/Test.sh
bash scripts/Build.sh --game-path "$HOME/.local/share/Steam/steamapps/common/Probably Stolen"
```

游戏在其他库中时，替换路径或设置 `PSMCP_GAME_PATH`。构建仅引用本地游戏程序集，
不会将游戏 DLL 打包分发。默认先执行 Python 和 C# 集成测试，再生成：

- `artifacts/mod/probably-stolen-mcp`：三个 Mod 文件；
- `artifacts/python`：Python wheel；
- `artifacts/mcp-config.json`：本机 MCP 客户端配置。

`Build.sh` 支持 `--no-restore`、`--skip-tests`。`Test.sh` 支持 `--no-restore`、
`--offline`（跳过本机网络／子进程集成检查）或 `--help-only`（只测 stdio 帮助，不需要 .NET）。
`--offline` 只选择测试子集，首次依赖安装仍需要网络。
CI 同时覆盖 Windows 与 Ubuntu 的协议及独立 wheel 检查；真实游戏程序集编译和
Proton 游戏流程仍需单独验收。
