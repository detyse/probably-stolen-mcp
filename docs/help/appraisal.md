# 验货、鉴定与估价

工具使用与通用面板入口已接入，各品类完整鉴定流程仍未实测。没有一键返回隐藏真假或正确估价的工具。

1. get_game_state / get_inventory 找到待检物品与已拥有的工具。
2. get_available_actions 查工具的 item_use_on 动作，确认目标在 parameters.targetId 的 choices 中；也可能有物品查看／激活入口。
3. perform_action 使用完整动作名，以 arguments.targetId 指定待检物品，并携带当前 snapshotId 和新 UUID。
4. get_ui_state 读取检测文字与数值；get_view 返回图片，视觉 Agent 可观察图案、外观和手册。根据玩家可见证据判断。
5. 重新观察当前面板，选择真假声明、缺陷／对质按钮。估价业务若出现输入框，使用列出的文本动作（arguments.text），再重新观察并提交。
6. 读取实际反馈、已揭示属性、评级或报酬；一次 dispatched 不代表鉴定结束。

features[].knowledge=public_claim 是界面声称，可能不真实；revealed 表示游戏已揭示该属性，不代表整件物品的全部问题都已查清。检测器如果明确显示结论，可以读取公开结论。交易报价不是正确估价答案。

本帮助说明操作方法，不包含所有品类的真假判定标准。需要游戏手册时，用当前动作打开可访问的资料，再读文字／截图；图像线索需要视觉能力。控件缺失、禁用或证据不足时报告缺口，不猜测真假，也不从后台读取隐藏答案。
