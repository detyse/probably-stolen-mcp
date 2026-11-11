# Probably Stolen MCP 项目说明

## 默认仓库

- 本仓库是后续开发、构建、提交和 push 的默认目标；不再回写旧 PawnPilot 开发目录。
- 保留当前项目和命名空间 PSMcp.*、Mod ID probably-stolen-mcp、环境变量 PSMCP_*；Mod 显示名为 Probably Stolen MCP。

## 能力问题先查文档

- 用户询问“目前能做什么”“开始菜单／选人能不能控制”“哪些已经实测”时，优先阅读 `docs/capabilities.md`，不要每次重新遍历源码。
- `docs/capabilities.md` 是当前能力与验证状态的入口；`docs/mcp-implementation.md` 补充实现细节；`docs/game-interactions.md` 和 `docs/mcp-design.md` 是玩法目录与设计，不能当成已完成清单。
- 文档足够回答时直接回答。文档缺失、与当前实现冲突，或任务需要修改代码／定位问题时，再定向读取相关文件，并修正文档。
- “已接入”“通用入口已接入”和“已实测”必须区分；编译、协议测试与 MCP 工具注册不能证明真实游戏流程已通过。
- 当前按钮能否操作以最新 `availableActions` 为准；静态能力文档不能替代运行时状态。

## 同步维护

- 修改能力、工具、控件适配或限制时，同步更新 `docs/capabilities.md` 的日期和对应条目。
- 真实游戏验证后，记录版本、场景、调用、实际结果与证据位置，再更新实测状态；没有证据就保留“未实测”。
