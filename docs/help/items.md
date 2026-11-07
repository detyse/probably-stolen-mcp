# 物品使用、工具与容器

入口已接入；回执阅读、绷带消耗、背包开关／卸载和部分转移整理有迁移前实机证据，0.2.6 另有 9 个职业开局及对应物品操作实测，所有物品变体并未全部验收。详见 get_help(topic="capabilities")。

1. get_inventory 读取已呈现容器、物品 entityId、归属、数量、形状和位置。可用 containerId、offset、limit 分页（limit 1–200）。未打开内容不会自动导出。
2. get_entity(entityId) 查询该对象和关联动作，或在 get_available_actions 中按 kind、targetId、description 找动作。
3. 根据目的选当前动作，执行后核对物品数量、归属、位置、容器或界面变化。

| 类型示例 | 用途与参数 |
| --- | --- |
| item_open / item_close | 打开／关闭物品内容窗口；打开后再读库存获得嵌套容器 |
| item_examine | 打开当前可用的查看界面 |
| item_activate | 原生激活、阅读或消耗；先确认对象和说明 |
| item_toggle | 原生切换／拆包，具体效果随物品而异 |
| item_select / cancel_item_selection | 选择工具／取消当前选择 |
| item_use_on | arguments.targetId 从 parameters 的合法目标 choices 中选择；动作自身 targetId 指工具 |
| item_pin | 用 toggle 固定／取消固定 |
| item_unload | 原生卸载，可能跳过固定内容或部分完成，重新检查库存 |
| inventory_sort / inventory_undo_sort | 整理／撤销本次原生整理；只在动作出现时可用 |

## 移动和摆放

使用当前 item_move 完整名称。destinationId 从候选容器中选；quantity 可按参数指定，省略时通常为整堆。省略 x/y 由游戏自动找位置；指定时 x、y 必须同时提供，rotation 为 0–3，flipped 为布尔值。指定旋转／翻转需要坐标。

检查形状、空间、归属与插入／移出锁。非法摆放会拒绝；转移后对照来源和目标的数量、归属与位置。拆分和堆叠由原生放置规则决定，不提供原子多件事务或全局回滚。放入垃圾桶和永久清空是不同后果，不要将它们混为一谈。

所有上表名称仅是类型示例；实际执行要复制 availableActions 中的完整 name，参数按当次 parameters 填写。物品名称或 numeric id 不能用来编造实体引用。

设备容器的 label 来自可见面板名称或插槽提示，可能为空。结合 windowTitle、parentItemId 和合法 destinationId 选择输入／输出位置，不能仅凭容器排列次序判断。

物品 statusText 读取已呈现、归玩家所有物品的原生普通悬浮提示片段，包括生产、动物／鱼、水量、电量及次数。最多 8192 字符，随游戏语言变化并可能包含富文本标记；空字符串表示没有这些状态内容，null 表示不可用或不适用。开发模式不导出，未公开基因和真实估价不在此字段中。
