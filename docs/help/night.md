# 一天经营与夜间出行

开闭店、拾荒、返回、睡眠及部分服务结算已有迁移前实机证据；其他地点和变体见 get_help(topic="capabilities")。

1. 读取 phase、day、customer、interaction 与当前动作。用 open_shop 开店，next_customer 叫客，按业务处理后使用 dismiss_customer 送客。
2. close_shop 可能打开确认；读取正文并选择当前确认／取消动作，核对实际阶段。
3. 外出前通过物品／容器动作安排背包或口袋。以 role、windowTitle 和物品信息辨认容器。
4. 地图中用当前 afterhours_select_destination 动作选择目的地；再读取并点击实际出发按钮。选择地点不等于已经出发。
5. 用当前地点面板采购／拾荒；每次重新检查次数、费用、伤势和可用状态。物品移动到合法随身容器后核对空间与数量。
6. 返回前检查随身物品与地面遗留物，读取原生确认。返回店铺后再次核对携带结果。
7. 店内关闭阶段出现 sleep 时可打开日结面板，核对食物、服务、费用和确认。之后读状态确认天数与阶段，不将 sleep 的 dispatched 当作已跨日。

次数耗尽、受伤、空间不足或模态确认可能限制后续动作，以当次 availableActions 为准。放入垃圾桶、遗留地面、离开地点、清洁结算具有不同后果；不提供全局回滚。

睡前服务的原生 TabletCheckbox 会返回 kind=checkbox、isOn、服务名称和费用。只用当前 ui_click 切换，执行后重新核对状态与总费用；不要仅凭按钮文字推断是否已经选中。
