# 深空当铺交互清单

本清单用于设计 Probably Stolen MCP 的游戏控制能力。按玩家经营流程整理了 **132 个交互族**，覆盖开局、日常交易、验货鉴定、库存、生产、夜间活动和长期经营。一个交互族可以有多个物品或界面变体；这不是按钮数量，也不是已验证的完成率。MCP 组织方案见 [MCP 设计草案](mcp-design.md)。

资料核对日期为 **2026 年 11 月 7 日，亚洲上海时区**。基线是本机 Steam App 4348910、Build **26337366**；首次设计时仓库的编译基线为 26284217，0.2 现已针对 26337366 构建。此次资料核对没有启动游戏、加载存档或执行游戏动作。

## 证据与范围

| 标记 | 含义 | 能证明什么 |
| --- | --- | --- |
| A | 本机程序集公开元数据 | 类型、字段和方法签名存在；不能证明入口当前可用、已经解锁或调用安全 |
| W | 已阅读的 Wiki 或玩家攻略正文 | 对应玩家操作与规则的资料依据，具体版本仍须实机核对 |
| C | 已打开的目录或条目摘要 | 发现某个玩法或操作候选，细节尚未逐条核实 |
| I | 当前仓库实现 | 源码已有对应能力；不表示通过实机验证 |

下表的 I 标记记录首次设计时已有的实现；0.2 的共用执行入口和各组映射见 [实现与覆盖说明](mcp-implementation.md)，不要用 I 缺失判断当前接口能力。A 和 W 可叠加，不能替代实机验证。普通玩家可观察的线索与后台答案应严格区分；读取到字段不代表可以向 AI 暴露它。

本机只读核对采用现有 [Inspect-GameApi.ps1](../scripts/Inspect-GameApi.ps1)，使用 MetadataLoadContext，没有实例化游戏对象或执行其代码。工作输出保存在被忽略的 `.cache/research/game-api.txt` 与 `.cache/research/specialized-api.txt`，可按文末命令重新生成；不分发游戏程序集。

当前 Assembly-CSharp.dll 的 SHA256 为 `6398D44AA1675E1F02B63BC668868F970E0F5D3AE5A50F2ACED8D06CA084E3B5`。这只标识本次读取的文件，不是兼容性认证。

## 开局与会话

依据：[操作指南][controls]、本机 MainMenuUIController、NewGameDifficultyUI、NewGameSpecializationUI、NewGamePerksUI、GameOverUIManager 元数据。菜单操作细节待实机展开。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| SYS01 | 查看连接与当前游戏会话 | 主菜单或游戏内；区分未载入、读取中与可操作 | A I |
| SYS02 | 新建一局 | 主菜单；读取实际可选开局，不能把目录中的全部职业视为已开放 | A C |
| SYS03 | 选择难度 | 新游戏面板；读取当前选项和说明 | A C |
| SYS04 | 选择初始特性 | 特性面板；读取点数、互斥和可选状态 | A C |
| SYS05 | 设置跳过开场与无尽模式 | 新游戏面板；读取各自开关及影响 | A C |
| SYS06 | 继续或载入存档 | 菜单实际允许时；切换后使旧会话对象引用失效 | A |
| SYS07 | 打开选项及改变交互设置 | 选项面板；文本输入、单击激活等设置影响操作解释 | A W |
| SYS08 | 查看结局与选择继续经营或返回菜单 | 结局界面；仅提供游戏实际显示的分支 | A W |

## 一天的经营循环

依据：[操作指南][controls]、[夜间规则][afterhours]、PlayerStore、StoreBedroll、StartOfDayUIManager、EndOfDayUIManager 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| DAY01 | 阅读并结束早晨报告 | MORNING；读取昨夜收入、支出、生产和事件反馈 | A |
| DAY02 | 打开卷帘门开始营业 | CanStartDay 与卷帘门按钮可用；处理可能出现的面板 | A I |
| DAY03 | 呼叫下一位顾客 | 当前对话与场景允许；不应跳过未完成流程 | A I |
| DAY04 | 送走当前顾客 | CanDismissCurrentClient；区分送走与继续叫人 | A I |
| DAY05 | 查看客流与展示柜带来的来客提示 | 只读取游戏公开的预期客流，不导出隐藏的未来队列 | A C |
| DAY06 | 关闭卷帘门结束营业 | CanEndDay；可能有确认，闭店仍可整理、外出 | A I |
| DAY07 | 打开夜间服务与结算面板 | 闭店后；读取服务是否启用、费用和余额 | A W |
| DAY08 | 使用睡袋确认入睡 | 当晚准备完成；经历夜间结算后进入下一天早晨 | A W |

## 对话与界面流程

依据：[操作指南][controls]、DialogUIManager、DialoguePhoneUIManager、ConfirmPanelUIManager、GenericConfirmationUIManager、RecorderUIManager、PaperUI 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| DIA01 | 手动继续当前对话 | 等待手动推进时；区别于打字动画与待选分支 | A W |
| DIA02 | 选择可见对话分支 | 当前说话者与面板有效；使用本次分支标识 | A I |
| DIA03 | 回看已经发生的对话 | 录音机或记录界面；区分历史文本和当前提示 | A W |
| DIA04 | 回应确认框 | 读取正文与实际按钮；支持确认、取消、单按钮通知 | A |
| DIA05 | 打开或关闭面板和返回上一层 | 遵循当前模态层级；不能关闭教学强制步骤 | A W |
| DIA06 | 阅读手册、纸张、笔记与翻页 | 玩家拥有或当前可以打开的资料；保留图像线索 | A C |

## 库存与空间操作

依据：[操作指南][controls]、[物品状态][conditions]、GameItem、GameInventory、GameGridInventory、GameGridScrollableInventory、GameSlotInventory、InventorySortHelper 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| INV01 | 查看物品提示和操作菜单 | 显示名、可见标签、数量、归属、所在容器和公开操作；当前只实现主库存顶层的 ID、名称和数量 | A I 部分 |
| INV02 | 查看或打开容器内部 | 已打开、已解锁且玩家可访问；支持嵌套与分页 | A W |
| INV03 | 在格子间放置物品 | 校验形状、坐标、空位、接收规则、归属与锁定 | A W |
| INV04 | 旋转、翻转和恢复朝向 | 移动中的物品；按真实形状重新检查放置 | A W |
| INV05 | 快速转移到库存、展示柜或储藏区 | 目标随按键、窗口和上下文变化，必须明确返回目标 | A W |
| INV06 | 多选和批量移动 | 逐件检查容纳情况，保留每件结果和未移动物品 | A W |
| INV07 | 拆分数量与合并堆叠 | 仅在该物品支持时；不把所有物品视为可堆叠 | A |
| INV08 | 排序、固定与取消固定 | 对应格子支持整理；固定项与旋转设置影响结果 | A W |
| INV09 | 使用工具箱槽位装备和收回工具 | 实际拥有、兼容且已开放的槽位 | A W |
| INV10 | 激活、切换或打开自有物品 | 区分 Activate、Toggle 与内容窗口，遵循可用性检查 | A |
| INV11 | 将一个物品或工具用于另一目标 | 校验来源、目标、距离或界面上下文；消耗取决于具体操作 | A W |
| INV12 | 放入垃圾桶、取回及清空后的处置 | 放入与永久清空是不同动作；特殊交易会使用垃圾桶 | A W |

## 柜台交易

依据：[顾客需求与预算][customer]、[以物易物][barter]、NegociationUIManager、PlayerStore、StoreClient.ClientIntent 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| TRD01 | 打开当前顾客交易平板 | 顾客已到柜台、对话与锁定允许 | A W |
| TRD02 | 阅读需求、限制和剩余预算 | 品类、排除项、种类多样、法律警示和本次已买商品 | A W |
| TRD03 | 选中或切换正在讨论的商品 | 平板、秤盘与柜台选中对象要一致 | A |
| TRD04 | 将自有货品放上柜台或撤下 | 公开报价和拒收理由随摆货更新，可能触发验货 | A W |
| TRD05 | 买入单件商品 | 店铺付款；卖家货品归属、报价、现金和接收空间有效 | A |
| TRD06 | 批量买入或接受批发报价 | 保留原生整体确认、优惠和收货行为 | A |
| TRD07 | 向顾客出售单件商品 | 需求匹配，成交前可能重新验货 | A W |
| TRD08 | 批量出售和超预算确认 | 超预算确认不增加实收；读取最终将收取的金额 | A W |
| TRD09 | 查看、选择并接受易物报价 | 读取当前需求与可见交换组合，交出匹配商品 | A W |
| TRD10 | 回应求购、采购及取货型来访 | PROCUREMENT_OFFER 与 PROCUREMENT_COLLECT 等是候选流程；以可见对白和面板确认变体 | A |

## 议价与施压

依据：[议价规则][bargaining]、BargainUIManager、BargainType 元数据。店铺买卖方向与顾客需求面板的方向需要分别命名。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| BGR01 | 打开议价并阅读预览 | 已解锁且顾客允许；只导出界面公开的概率与声望预览 | A W |
| BGR02 | 店铺出售时提供折扣 | 读取可见档位，以及售价和顾客预算变化 | A W |
| BGR03 | 店铺出售时要求加价 | 本方向尚未议价；接受、拒绝和离开均是可能结果 | A W |
| BGR04 | 店铺收购时要求折扣 | 使用购买方向的机会，不与出售方向混淆 | A W |
| BGR05 | 店铺收购时主动加价 | 可见选项允许；支付与声望结果在成交后核对 | A W |
| BGR06 | 敲诈或威胁改造体 | 只在游戏正常开放相应选项时提供；不能通过检查可用性提前泄露身份 | A W |

## 验货与检测

依据：[验货工具][inspection]、[水质检测][water]、InspectionUIManager、WaterScannerUIManager、WaterStripUIManager、ChemScannerUIManager、PlantAnalyserUIManager、AugScanner 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| INS01 | 用放大镜打开检查托盘 | 物品有可检查入口；买入前与买入后权限不同 | A W |
| INS02 | 对照手册观察商品细节 | 烟盒、券、注射器、浆果等保留视觉信息，不输出真假答案 | A W |
| INS03 | 选择缺陷原因并向卖家对质 | 卖家仍在场且物品未购入、未锁定；读取可见原因 | A W |
| INS04 | 按面板申明正品或赝品 | 仅在相应面板开放时；错误判断也必须走正常后果 | A W |
| INS05 | 使用水质试纸并读取结果 | 新试纸会被使用；结果对应这次取样 | A W |
| INS06 | 使用水质扫描仪 | 按原生电源、耗时和归属条件运行，读取检测面板 | A W |
| INS07 | 使用成分光谱仪 | 检测液体或成品，读取已显示成分和纯度 | A W |
| INS08 | 根据检测结果质疑或匹配标签 | 区分已检测与已贴标；只能调用当前面板开放操作 | A W |
| INS09 | 分析植物或种子的生长需求 | 使用分析仪后读取公开需求与性状 | A W |
| INS10 | 检查动物遗传与健康信息 | 使用相应设备，读取玩家已获得的结果 | A W |
| INS11 | 给当前顾客做改造体扫描 | 自有设备、电量和本次扫描条件有效；结果与本次来访绑定 | A W |

## 鉴定业务

依据：[鉴定委员会][appraisal]、[官方鉴定扩展说明][modguide]、AppraisalHelper、AppraisalServiceHelper、AppraisalRankHelper、InspectionUIManager 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| APP01 | 加入委员会、延期或拒绝 | 由实际来访对白提供入口，费用与选项取决于进度 | A W |
| APP02 | 展示鉴定证书招揽业务 | 证书位置是经营决策的一部分 | A W |
| APP03 | 逐件检查并输入估价 | 机械手、肋明治、等离子燃料匣等类型按解锁情况出现 | A W |
| APP04 | 提交估价并读取评级和报酬 | 不读取 getCorrectValue 等后台答案；拒收可能影响耐心 | A W |
| APP05 | 达到条件后培训与晋升 | 读取当前职级、公开进度、费用和对白 | A W |
| APP06 | 管理证书接单类别与补发 | 类型屏蔽规则和补发入口按当前证书与对白核对 | A W |

## 机器共性交互

依据：[官方机器窗口说明][modguide]、[旧版机器攻略][oldmachines]、MachineHelper、MachineryHelper、HopperHelper、MachineStorageCoordinator 等元数据。旧攻略只辅助发现交互，不采用其旧版价格和效率数值。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| MCH01 | 打开机器并查看各面板 | 电源、模组、原料、控制、输出按实际 role 或适配结果区分 | A W |
| MCH02 | 装卸电池与可用供电设备 | 插槽兼容、拥有权、电量与供电服务共同约束 | A W |
| MCH03 | 查看或切换开关、运行模式 | 区分物品 Toggle、控件按钮和自动运行 | A |
| MCH04 | 装卸、摆放与调整模组 | 格子布局、特效、损坏与兼容性影响效果 | A C |
| MCH05 | 投料、装入容器与收取输出 | 原料、液体容器和成品槽使用不同接收规则 | A W |
| MCH06 | 选择配方、参数或目标范围 | 只接受该机器当前可见的选项、滑条范围和步长 | A W |
| MCH07 | 启动加工、执行输出或等待周期 | 即时加工、实时进度、夜间加工分别处理 | A W |
| MCH08 | 维修、扩容和专用升级 | 当前物品提供入口时，核对材料、风险与空间变化 | A C |
| MCH09 | 使用涡轮增压器等加速道具 | 道具与目标兼容；不能把所有周期强行立即完成 | A C |
| MCH10 | 配置料斗与储物协调器 | 连接、筛选和预设列为候选；控件和顺序须逐台实机核实 | A C |

## 生产与设备专用流程

依据：各行所列攻略及本机 Machine 系列类型。每行是专用流程，不代表一个原子动作；都要展开为上面的物品、机器和时间交互。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| PRD01 | 取水、转移、分装与混合液体 | 容器用到水龙头或其他容器；按容量与实际转移量反馈。[水处理][water] | A W |
| PRD02 | 滤材、净水片、紫外线与净水器处理 | 不同操作有不同耗材和进度；处理后重新检测。[水处理][water] | A W |
| PRD03 | 水分收集器与装瓶设备生产 | 安装容器、电源并收取产物；具体机器窗口待展开。[设备目录][wiki] | A C |
| PRD04 | 熔炼废料和矿石 | 输入、模组、电源、夜间周期与输出空间。[熔炼][smelting] | A C |
| PRD05 | 3D 打印和钥匙卡解序 | 分别检查蓝图或芯片、原料、卡片、供电与进度。[设备目录][wiki] | A C |
| PRD06 | 酿酒与陈化 | 配料、启动发酵、观察天数，陈化设备另行操作。[酿酒][wine] | A W |
| PRD07 | 水培种植和收获 | 水、电、养分、种子与成熟状态；睡眠推进生长。[水培][hydroponics] | A W |
| PRD08 | 植物 DNA 提取与编辑 | 先检测，再按设备面板使用数据盘与编辑功能。[植物基因][genetics] | A C |
| PRD09 | 捕鼠、喂养、繁殖和屠宰 | 捕鼠夹、笼子、饲料、健康与工具操作分别建模。[养鼠][rats] | A W |
| PRD10 | 养鱼、收肉、采卵及物种专用功能 | 水电、饲料、鱼龄和物种影响可用动作。[养鱼实玩指南][fish] | A W |
| PRD11 | 培养肉、饲料加工和自动屠宰 | 设备存在；具体按钮、配方与运行时机须验证。[玩法目录][wikily] | A C |
| PRD12 | 搅拌、加热、蒸发、成型与检测 | 多台设备接力；实时与夜间步骤不能合并跳过。[化学加工][chemistry] | A W |
| PRD13 | 拆枪、翻新、组装和改装 | 消耗材料、兼容槽位、成功风险与零件归属。[枪械经营][gunsmithing] | A W |
| PRD14 | 冷藏、充电、投影、防盗及自动售卖 | 多种支持设备；售货机与售水机的补货、配置、结算须分别核实。[设备目录][wiki] | A C |

## 人脉与店铺服务

依据：[王尔德人脉][network]、[夜间防盗][insurance]、[清洁服务][janitorial]、PhoneUIManager、DialoguePhoneUIManager、WildUIManager、StoreServiceSheet 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| NET01 | 阅读名片或已获得的电话号码 | 联系信息只来自玩家已经获得的资料 | A C |
| NET02 | 拨号、结束通话和选择电话对白 | 号码、接听状态和电话对话独立于柜台对白 | A |
| NET03 | 浏览人脉节点与前置 | 读取已显示节点、费用、解锁状态和说明 | A W |
| NET04 | 购买或再次使用人脉升级 | 前置、冷却、现金与人情余额按原生结算 | A W |
| NET05 | 联系供应商与收货商 | 电话预约和之后的到店来访是两个阶段 | A W |
| NET06 | 启用或停用夜间服务 | 供电、清洁、保险、介绍客流和其他已解锁服务 | A W |
| NET07 | 扩建展示柜、仓储与经营基础设施 | 升级与实际容量变化须读取；不能只改接口中的容量 | A C |

## 委托与长期经营

依据：[市场委托][requests]、[枪械订单][gunorders]、[结局与买断][endings]、[物资请求补充][requests2]、StoreRequest、GunOrderData、PlayerStore、PCUIManager 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| QST01 | 浏览终端订单 | 委托人、指定商品、数量、期限、报酬与公开要求 | A W |
| QST02 | 完成交货与领取报酬 | 原生自动选货；不能虚构任意选择交货物品的能力 | A W |
| QST03 | 查看与履行枪械订单或改装服务 | 必需参数、额外奖励、客户枪和自有零件的归属不同 | A W |
| QST04 | 办理或升级营业与武器许可 | 对应人脉或来访对白；保留许可与产权区别 | A W |
| QST05 | 缴租、请求宽限及处理借贷 | 当前租金、欠款、到期信息和实际可见选项 | A W |
| QST06 | 买断店铺或选择并偿还按揭 | 原生资格、合同和确认；胜利后可能继续经营 | A W |
| QST07 | 推进剧情选择、保管与转交任务物品 | 使用可见对白、实际交付和期限；不预读未来剧情旗标 | A C |

## 治安与风险处置

依据：[举报顾客][reporting]、[犯罪与藏匿][crime]、[验货与改造体][inspection]、[结局][endings]，StoreSecButton、StorePoster、StoreGlass、WantedUIManager、BargainUIManager、PlayerStore 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| SEC01 | 阅读通缉信息并观察当前顾客 | 保留外貌、文书与对白线索，不直接映射隐藏身份 | A C |
| SEC02 | 按报警按钮举报顾客 | 允许玩家判断错误；举报之后由游戏处理奖励与处罚 | A C |
| SEC03 | 拉开或盖上海报暗格 | 物品移动与暗格是否敞开分别影响后续检查 | A C |
| SEC04 | 使用走私容器或整理待查货物 | 合法可达库存操作；不能把放入暗格当作物品消失 | A C |
| SEC05 | 回应检查、罚款、扣押与特殊盘查 | 按当前对白和确认操作；不直接调用后台调查推进函数 | A C |
| SEC06 | 查看已公开犯罪记录与调查反馈 | 只呈现实际文书或面板公开的内容 | A C |
| SEC07 | 使用霰弹枪等店内防卫操作 | 专用店铺交互，与库存里的普通商品枪区分 | A W |
| SEC08 | 调整防护玻璃等安全设施 | 按当前场景实际入口与锁定条件操作 | A |
| SEC09 | 应对袭击、炸弹或危险包裹 | 可见的交付、检查和对白构成流程；特殊变体须逐剧情验证 | A C |

## 夜间外出

依据：[夜间地点与规则][afterhours]、[官方夜间地点说明][modguide]、MapUIManager、TransitUI、CommissaryUIManager、AfterhourLocation 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| NIT01 | 打开外出入口并选择随身背包或口袋 | 闭店、进度、伤势与当晚次数允许 | A W |
| NIT02 | 查看目的地、解锁与地点事件 | 正常显示的地点卡片；分别记录未发现和暂不可用 | A W |
| NIT03 | 选择地点并确认出发 | 保留共用访问限制和出门确认 | A W |
| NIT04 | 在垃圾场搜索 | 读取剩余次数和本次结果；不预读掉落或随机数 | A W |
| NIT05 | 装入战利品与丢下物品 | 随身空间、场景地面和本地柜台分别处理 | A W |
| NIT06 | 在垃圾场遗弃危险废料 | 留在地面与确认返回构成处置，不能当作普通清垃圾 | A W |
| NIT07 | 在博士商店买卖设备和材料 | 现金、当次库存、货品归属与运输规则 | A W |
| NIT08 | 兑换食品券与配给站采购 | 票券有效性、独立兑换余额、库存与物品收纳 | A W |
| NIT09 | 返回前确认遗留物品 | 地点规则不同；逐件列出将带回、留下或无法接收的物品 | A W |
| NIT10 | 返回店铺并完成当晚剩余安排 | 返回不等于入睡，也不自动授予第二次外出机会 | A W |

## 身体状态与其他物品

依据：[伤势与成瘾][health]、[操作指南][controls]、[物品目录][wiki]、MindReprogrammerHelper、MachineCassettePlayer、ScratcherUIManager、GameItem 元数据。

| 编号 | 交互族 | 条件与需要观察的信息 | 证据 |
| --- | --- | --- | --- |
| LIF01 | 查看伤势、成瘾和已公开状态 | 区分当前数值、说明与未知状态，不导出隐藏触发器 | A W |
| LIF02 | 使用绷带或其他治疗物品 | 自有物品、伤势类型和适用条件有效 | A W |
| LIF03 | 使用可消费物品处理需求或特性 | 消耗物品并保留正常副作用，按可用操作列出 | A W |
| LIF04 | 使用心智重编程器等特性设备 | 控件、成本、冷却和可编辑特性须逐项核实 | A C |
| LIF05 | 购入、使用和兑奖彩票或刮刮卡 | 刮开与领奖分开；不能读取未揭示结果 | A C |
| LIF06 | 开箱、拆包与使用钥匙卡 | 兼容卡片、封装、锁定与归属；开箱可能影响交货资格 | A W |
| LIF07 | 蓝色改名与红色改标签 | 分别调用相应面板；名称、宣称属性和检测结果分开保存 | A C |
| LIF08 | 放入磁带、播放与切换曲目 | 播放器及磁带容器是独立交互；不硬编码曲目组合 | A C |

## 本机接口对设计的约束

以下仅摘录本机公开元数据中与架构有关的接口名称。它们是适配器候选，不应直接变成开放给客户端的方法调用能力。

| 发现 | 设计含义 |
| --- | --- |
| PlayerStore.StoreState 包含 CLOSED、OPEN、MORNING | 既有 Phase 只能表达粗粒度状态；还需要地点、对话、模态与交互锁 |
| ClientIntent 包含买卖、易物、租金、贷款、采购、批发、鉴定、枪械等类型 | 顾客不只是一个名称和预算；每种当前流程需要不同的可见状态 |
| ClientIntent 还含 EXPEDITION，程序集还有其他战斗与空间站类型 | 代码存在不等于正式版入口开放；列入待核实，不据此宣称可玩 |
| GameItem 提供 May/Can Activate、Toggle、Target 等不同入口 | 通用物品能力可以复用，但仍需权限、上下文和正常入口检查 |
| GameInventory 与 GameGridInventory 提供 TryInventorySlot 等检查 | 空间和数量是核心规则，移动要经过完整的校验和回调 |
| NegociationUIManager 有单件、批量、批发、超预算确认入口 | 复用原生交易流程；不绕过预算、再验货和确认 |
| BargainUIManager 有四个买卖方向操作及 Blackmail、ThreatenAug | 方向明确建模；只呈现当前正常 UI 已开放的施压选项 |
| InspectionUIManager 有对质、真假申明和估价提交入口 | 视觉证据、玩家判断、提交结果应分开 |
| 水质与化学检测面板分别有 tested、label、confront、match | 宣称标签、检测结果、质疑与修改标签不可混成一个字段或动作 |
| ConfirmPanelUIManager 与 GenericConfirmationUIManager 并存 | 统一抽象必须适配多种原生确认框，不能只处理一种 |
| MachineHelper 与 Machinery 控件暴露槽位、按钮、滑条、标签页 | 可设计统一机器描述；不能假定每台机器都有相同操作 |
| 官方钩子覆盖开闭店、入睡醒来、外出返回和夜间服务 | 可用于事件采集和结果关联；仍须重新观察最终界面状态 |

可复现的只读检查示例：

```powershell
.\scripts\Inspect-GameApi.ps1 -GamePath 'D:\Apps\Steam\steamapps\common\Probably Stolen' -TypeNames 'PlayerStore+StoreState','StoreClient+ClientIntent','BargainType'
.\scripts\Inspect-GameApi.ps1 -GamePath 'D:\Apps\Steam\steamapps\common\Probably Stolen' -TypeNames 'NegociationUIManager','InspectionUIManager','MapUIManager','MachineHelper','GameItem'
```

## 覆盖验收与待核实项

“几乎全部”按覆盖审计验收，不按工具数量验收。每个可达的原生玩家交互，最终都应有记录：所处阶段、打开的界面、目标、前置条件、玩家可见输入、正常执行入口、结果与测试证据。未实现项也要能明确说明。

1. 对所有可玩开局核对新游戏和教学流程，展开早晨、营业、闭店、外出、结局界面；关闭教学和无尽模式单独检查。
2. 从原生物品交互和可打开窗口发现变体，核对所有工具目标、机器控件、液体操作、容器与物品右键菜单。类型名与 Wiki 目录只能提供候选。
3. 展开本清单中的组合行，尤其是自动售货、售水、储物协调器、料斗、基因编辑、特殊机器和任务来访。部分攻略页正文抓取不稳定，目前只给 C 证据。
4. 逐项验证普通状态、禁用原因、空间不足、数量不足、归属不符、过期局面、模态阻塞、切换存档与部分完成。
5. 为剧情与稀有来访建立场景清单，通过正常对白和面板核对分支。Wiki 的结局总数存在分类差异，不据此声明已经覆盖全部结局。
6. 验货外观、通缉人物和刮卡需要图像输入或忠实的可见线索适配；纯文本状态未必足够。
7. EXPEDITION 等未确认开放入口，以及已废弃类型和未来更新内容，单独记录为待核实或排除项，不计入当前可玩覆盖。

## 来源

优先顺序是本机正常 UI 与当前版本实机行为、官方 Mod 文档、玩家实玩资料、Wiki 正文、目录候选。Wiki 可帮助发现范围，不能独自证明接口可以安全调用。

| 来源 | 用途 |
| --- | --- |
| [Steam 官方商店][steam] | 游戏定位与公开玩法范围 |
| [官方 Mod 入门][modreadme]、[官方 Mod 指南][modguide] | 生命周期、钩子、机器窗口、夜间地点与鉴定扩展；本次核对仓库最新提交为 2bce9d264ab2386fed0adc49ef8ae3ce189601ef，2026 年 11 月 5 日 |
| [中文攻略目录][wiki] | 全局交互候选与专用系统入口；包含不同版本，应逐篇辨认 |
| [Wikily 系统目录][wikily] | 独立交叉检查交易、枪械订单、健康、结局等范围 |
| [Steam 玩家攻略目录][steamguides] | 寻找实玩来源；旧机器攻略明确注明缺少正式版资料 |
| [Vi.ki 导览][viki] | 发现补充资料与官方公告链接；不作为接口技术依据 |

[steam]: https://store.steampowered.com/app/4348910/Probably_Stolen/
[modreadme]: https://github.com/dragonpan2/probably-stolen-modding/blob/2bce9d264ab2386fed0adc49ef8ae3ce189601ef/README.zh-CN.md
[modguide]: https://github.com/dragonpan2/probably-stolen-modding/blob/2bce9d264ab2386fed0adc49ef8ae3ce189601ef/ModdingGuide.zh-CN.md
[wiki]: https://probablystolenguide.com/zh-hans/
[wikily]: https://wikily.gg/probably-stolen/
[steamguides]: https://steamcommunity.com/app/4348910/guides/
[viki]: https://probablystolen.vi.ki/
[controls]: https://probablystolenguide.com/zh-hans/guides/controls-and-interface/
[customer]: https://probablystolenguide.com/zh-hans/guides/customer-requests/
[bargaining]: https://probablystolenguide.com/zh-hans/wiki/bargaining/
[inspection]: https://probablystolenguide.com/zh-hans/wiki/inspection-tools/
[appraisal]: https://probablystolenguide.com/zh-hans/wiki/appraiser-board/
[afterhours]: https://probablystolenguide.com/zh-hans/guides/after-hours-locations/
[water]: https://probablystolenguide.com/zh-hans/guides/water-purification/
[wine]: https://probablystolenguide.com/zh-hans/guides/wine-making/
[rats]: https://probablystolenguide.com/zh-hans/guides/rat-breeding/
[fish]: https://steamcommunity.com/sharedfiles/filedetails/?id=3831346037
[hydroponics]: https://probablystolenguide.com/zh-hans/guides/hydroponic-farming/
[genetics]: https://probablystolenguide.com/zh-hans/guides/plant-genetics/
[smelting]: https://probablystolenguide.com/zh-hans/guides/scrap-smelting/
[chemistry]: https://probablystolenguide.com/zh-hans/guides/chemistry/
[gunsmithing]: https://probablystolenguide.com/zh-hans/guides/gunsmithing/
[oldmachines]: https://steamcommunity.com/sharedfiles/filedetails/?id=3805839811
[network]: https://probablystolenguide.com/zh-hans/guides/joe-wild-starter-kits/
[requests]: https://probablystolenguide.com/zh-hans/guides/marketplace-requests/
[requests2]: https://wikily.gg/probably-stolen/store-requests
[gunorders]: https://wikily.gg/probably-stolen/gun-orders
[barter]: https://wikily.gg/probably-stolen/barter
[conditions]: https://wikily.gg/probably-stolen/item-conditions
[health]: https://wikily.gg/probably-stolen/health-and-wounds
[endings]: https://wikily.gg/probably-stolen/endings
[reporting]: https://probablystolenguide.com/zh-hans/guides/reporting-customers/
[crime]: https://probablystolenguide.com/zh-hans/guides/crime-and-hiding/
[insurance]: https://probablystolenguide.com/zh-hans/guides/theft-insurance-and-alarms/
[janitorial]: https://probablystolenguide.com/zh-hans/guides/janitorial-service/
