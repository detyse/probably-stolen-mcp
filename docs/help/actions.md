# availableActions 与动作调用

适用：PSMcp 0.2.5 / 协议 2。get_available_actions 返回顶层 snapshotId、gameLoaded、availableActions、interaction、limitations 和 usage。

## 读懂一条动作

- name：完整执行名称，原样传给 perform_action.action。ui_click:<引用>、item_use_on:<引用> 等后缀必须来自当前返回，不能自行拼接。
- kind：动作类别，用于寻找合适操作；不能代替 name。
- description：效果或当前控件标签，用来理解目的；不能当成命令。
- targetId：动作所属的对象。例如 item_use_on 的 targetId 是工具本身。
- parameters：本次 arguments 的约束。检查 name、type、required、min、max、step、choices。只填列出的参数；引用从 choices 选。
- allowWithoutGame：该动作是否允许未载入时执行。主菜单按实时动作列表操作。

注意：arguments.targetId 是工具要作用的目标，可能与动作自身 targetId 完全不同。物品 entityId、容器 entityId 和数字物品 id 也不能混用。

## 提交模板

以下占位符须替换为同次观察的真实值。先读当前 actions，再根据 parameters 填 arguments；无参数动作省略 arguments。

```json
{
  "action": "<复制完整动作 name>",
  "expectedSnapshotId": "<同次观察的 snapshotId>",
  "requestId": "<新生成的标准 UUID>",
  "arguments": {
    "targetId": "<仅在所选动作要求 targetId 时，从 choices 中选>"
  }
}
```

开关／固定用 toggle，数值用 value，范围同时用 value 与 upperValue，文本用 text，选项／标签页用 index，物品转移用 destinationId 等。每次以该动作实际参数表为准。未知字段、越界、非法候选和步长不符会被拒绝。

## 执行后与阻塞

success=true、actionStatus=dispatched 表示处理函数已调用，可能还在动画、长按、对话或确认。重新读状态核对效果；查询本身不代替手动继续或确认。
空动作列表时检查 interaction.waitingFor、blockers、context.inputLocks 和 limitations。长按 input_in_progress 时等待游戏处理再观察；持续缺少输入入口时报告阻塞，不猜命令。
同名按钮用 controls 的 windowTitle、nearbyText、bounds 和截图区分，再将 control.actions 对应到 availableActions。

## 失败和重试

- stale_snapshot：根据返回的新 state 或重新查询作新决定，使用新 UUID。
- action_unavailable／invalid_arguments：重新检查当前动作和参数约束。
- 超时、断线、action_outcome_unknown：先 get_action_status(requestId)，再读当前状态。若重试原命令，保留原 action、expectedSnapshotId、requestId 和全部 arguments，不生成新 UUID。
- request_id_conflict：同一 UUID 被换了命令／参数，核对原始请求。

get_action_status 返回原始结果与当时的 state，不代表最新局面。近期保留 256 条结果，Mod 重启或记录淘汰后可能查不到；action_not_found 不证明从未执行过。结果仍不确定时报告不确定，避免重复效果。
切换场景／存档后引用与旧快照失效，重新观察再决策。
