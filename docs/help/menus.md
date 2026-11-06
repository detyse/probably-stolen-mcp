# 菜单、对话与原生界面

0.2.6 已实测 9 个可开局职业从主菜单进入店铺、特性选择／互斥／撤销、难度与开局开关，以及返回主菜单。另 3 个职业由游戏标为开发中，不能继续。指定存档载入另有迁移前证据。gameLoaded=false 时仍可能有允许的菜单动作。

1. get_ui_state 读取 screenText、controls、interaction、availableActions 和 snapshotId。
2. 用 label、windowTitle、nearbyText 辨认控件；同名按钮还可结合 bounds 与 get_view 截图。
3. 检查 enabled、disabledReason、actions；从 availableActions 复制完整动作及所需参数。
4. 点击、长按、开关、文本、下拉、标签页、滚动或关闭窗口后重新观察。

常见参数：toggle、text、index、value、upperValue，按当前 parameters 使用。ui_hold / world_hold 由游戏处理长按时长；input_in_progress 时等待完成／取消再读状态。被遮挡或不支持的控件不能强行调用。

对白分支使用当前 dialogue_choice:<index> 完整名称，手动继续用实际出现的 advance_dialogue；读取状态不会继续对白。确认框期间先阅读正文和后果，再调用其当前按钮。

顾客放大模式中使用当前 toggle_customer_zoom / exit_customer_inspection 并配合截图。持枪模式中只使用列出的 fire_shotgun / put_away_shotgun，动作保留原生后果。这些特殊模式尚未实测，不能将文档描述当成流程通过。

返回菜单、切换场景或存档后实体引用与快照可能失效，重新观察。没有任意坐标点击、任意按键或后台强制解锁入口。

职业、难度、特性卡分别返回 specialization、difficulty、starting_perk 控件；isOn 表示当前选择，禁用卡没有动作。查看开发中职业卡不等于允许开局。

游戏内当前列出 navigate_back 时，可通过原生后退关闭顶部窗口／选择或打开暂停菜单；每次执行后再观察，不能假定一次必定到主菜单。
