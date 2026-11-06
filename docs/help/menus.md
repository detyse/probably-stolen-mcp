# 菜单、对话与原生界面

通用 UI 入口已接入；主菜单指定存档载入有迁移前实机证据，新游戏选人完整流程未验收。gameLoaded=false 时仍可能有允许的菜单动作。

1. get_ui_state 读取 screenText、controls、interaction、availableActions 和 snapshotId。
2. 用 label、windowTitle、nearbyText 辨认控件；同名按钮还可结合 bounds 与 get_view 截图。
3. 检查 enabled、disabledReason、actions；从 availableActions 复制完整动作及所需参数。
4. 点击、长按、开关、文本、下拉、标签页、滚动或关闭窗口后重新观察。

常见参数：toggle、text、index、value、upperValue，按当前 parameters 使用。ui_hold / world_hold 由游戏处理长按时长；input_in_progress 时等待完成／取消再读状态。被遮挡或不支持的控件不能强行调用。

对白分支使用当前 dialogue_choice:<index> 完整名称，手动继续用实际出现的 advance_dialogue；读取状态不会继续对白。确认框期间先阅读正文和后果，再调用其当前按钮。

顾客放大模式中使用当前 toggle_customer_zoom / exit_customer_inspection 并配合截图。持枪模式中只使用列出的 fire_shotgun / put_away_shotgun，动作保留原生后果。这些特殊模式尚未实测，不能将文档描述当成流程通过。

返回菜单、切换场景或存档后实体引用与快照可能失效，重新观察。没有任意坐标点击、任意按键或后台强制解锁入口。
