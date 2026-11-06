# MCP 简短入门

适用：本仓库 PSMcp 0.2.5 / 桥接协议 2。帮助内置在 MCP 程序集中，游戏未启动、连接文件缺失或没有仓库文件时也可读。

平时按“读状态 → 选当前动作 → 执行 → 再读状态”操作。需要说明时 get_help(topic="actions")；省略 topic 返回本页与全部 topics。其他主题覆盖物品、鉴定、机器、交易、夜间和菜单。新增主题会自动进入目录。

1. get_bridge_status 检查认证连接；成功不等于已载入存档。
2. get_game_state 的数据在 state 中，包括 snapshotId、context、interaction、availableActions、controls、inventory、limitations。聚焦工具 get_available_actions、get_ui_state、get_inventory、get_trade_state 的字段在结果顶层。
3. 复制当前 availableActions[].name，连同该次观察的 snapshotId 和新 UUID 调用 perform_action；arguments 只填写该动作 parameters 允许的字段。
4. 重新读取状态或 get_view 截图，确认真实变化。dispatched 仅表示已调用原生处理函数。

get_entity 查询当前暴露的物品／容器／控件，不会自动打开或检测。get_view 返回 MCP 图片和同帧快照，视觉 Agent 可以查看。未知隐藏真假、身份、未来事件和真实价值不会被导出。

主菜单也可能有可用 UI 动作；gameLoaded=false 不能单独判断是否可操作。当前动作、参数范围和候选引用是实时信息；帮助中的名称只是类型示例，不得自己拼引用。

验收状态见 get_help(topic="capabilities")：部分场景的实机证据来自迁移前的同一功能实现；鉴定、机器、新游戏选人等仍需验收。帮助读取通过不代表这些玩法通过实机测试。
