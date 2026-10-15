# 公共机制与扩展约定

## 灵摆怪兽与灵摆召唤

原 LingBai 标签与关键字的显示名称统一为「灵摆怪兽」，保留原注册 ID 以兼容已有卡。刻度仍读取 IPendulumCard.PendulumScale，与 StarLevel 独立。

PendulumSummon 一场战斗共用一次预占：先用原版手牌视图选择并结算攻击牌，再基于结算后的额外堆打开网格选择灵摆怪兽。两阶段均可空选跳过，仅收到实际 CardPlayStartedEntry 时提交次数。两阶段之间及逐卡打出前重新验证两侧卡、刻度、归属、牌堆与打出条件。无手牌候选但有额外堆候选时，按钮仍可使用。

异响鸣·邀请独立使用①／②选项，不继承其他异响鸣魔法的随机选择或生命变化。②用两次必选确认保证先后顺序并排除同名：先选生成战斗手牌，后选由 PendulumExtraDeck 创建 RunState 额外堆实体，并附上持久化 TemporaryPendulumExtraCapability 标记。召唤只打出战斗副本，副本不继承临时资产标记；实际开始打出后移除临时原件，未开始则恢复原件。未用原件在战斗结束和下次局内准备时清理，避免生成战斗卡直接放入持久牌堆而留下失效卡。

## 超量

XyzMonsterCard 声明阶级、素材数和素材筛选，并复用额外堆手动召唤。阶级独立于 StarLevel；超量怪兽不因阶级获得星级标签。统一手牌选牌确认后，实际素材进入 CombatOnly 的隐藏 XyzMaterialPile；叠放不调用弃牌或消耗命令。失败归还仍在隐藏区的素材，取消选择不移动卡。

MaterialGroupId、MaterialOwnerKey 与素材的 XyzMaterialCapability JSON 状态关联实际实例；编号由同步牌堆状态分配。原拥有者身份避免转移控制权时串用另一玩家的素材。IExtraDeckSummonStaging 在生成战斗副本前交接绑定，异常路径撤销暂存；普通克隆清空宿主和素材的归属，避免共享或复制素材。空素材宿主也保留编号，防止编号被其他活跃宿主重用。

XyzMaterialHooksPatch 从战斗监听器中过滤叠放素材及其附魔、负面附着和组件，叠放入堆期间保留离手清理。去除素材调用原版 CardCmd.Exhaust，恢复正常钩子并触发消耗事件；不触发送墓。宿主在 Hand/Play 间移动保留素材，离开两者则清理剩余素材到消耗堆。素材数量徽标与悬停名称列表读取实际绑定卡。

## 构建、打包、部署

普通 `dotnet build` 仅生成 DLL。打包输出附带 package.sha256；Godot 导出临时依赖只留在构建缓存，不进入模组文件包。`dotnet msbuild -t:PackageMod -p:Configuration=Release` 生成 artifacts/package。`DeployMod` 才向游戏 mods 目录复制成套 DLL/PCK/清单。旧 ExportPck 入口转向 PackageMod，不再直接写入游戏目录。PCK 导出错误使打包失败。

本机路径通过 Local.Build.props（参考同名 example）或 MSBuild 参数覆盖。该配置先于默认属性加载，不提交仓库。dependency-baseline.json 记录此次验证用依赖哈希。最低 RitsuLib 版本更新为此次实际验证的 0.6.7。

## 效果与状态

CardPrefs 的必选流程（min > 0）默认 RequireManualConfirmation，避免候选只有一张或刚好够数时被原版自动选完。直接创建 CardSelectorPrefs 的装备、珠泪融合素材、同步搭档、预视及能力选牌须遵循相同约定。可选流程保留原确认规则和 Cancelable；检索的随机分支继续直接随机处理。

Rules/EffectLedger 表示未使用、预占和已提交次数，PlayerEffectState 以弱引用附着到玩家。同名效果使用完整 ModelId，独立效果使用独立 effect ID。每回合与每场战斗的旧入口保留，以便渐进迁移。周期记录变化时自然过期，旧 lease 不能撤销新周期记录。

特殊效果必须实现 TryActivateSpecialEffect 并返回是否发动；默认成功实现已移除。选择取消返回 false。公共入口预占次数，EffectActivation.PaymentStarted 在支付或首次实际收益开始前提交。异常保守保留次数并向行动执行器传播，不尝试撤销已经触发连锁的支付。ActivationResult 区分发动、未发动、条件变化。

SelectKeywords.ChuWai 是带通用打出行为的「除外」关键字，卡面位置与原版消耗一致，追加在描述末尾。ChuWaiKeywordPatch 在原版 ModifyCardPlayResultLocation 入口前设定默认去向为除外堆，再保留原版的去向修正、重复打出与最终落堆流程；不在 OnPlay/AfterCardPlayed 提前移牌。拥有该关键字时默认优先于消耗去向，单纯在悬停提示中引用关键字不赋予卡牌该行为。刻魔衍生物通过 CanonicalKeywords 声明除外，不再声明消耗。

素材选择先完成，再检查归属、当前牌堆和实体唯一性；MaterialPayment 按真实命令结果校验消耗。新增效果不要只检查“选了几张”而忽略实际支付成功。满手、必须选择、自动打出等卡牌规则沿用具体卡的实现，不在公共层重新解释。

RunLifetime 在退出对局时取消等待的远端选择，重复召唤点击在请求完成前被拒绝。

AsyncScope 隔离异步行动来源，前缀进入、返回 Task 后立即恢复调用方、异步完成或异常时关闭该帧。送墓批处理的延迟窗口保留；登场深度、怪物行动/飞卡/观测不再使用跨异步链共享的全局执行标记。

## 攻击保护

RitsuLib 负责重写原版 AttackCommand.Execute 的每段命中。项目只包装框架最终 Damage 调用，保留框架命中前后钩子。独立伤害与嵌套攻击使用独立作用域。FeatureStatus 核对登场、攻击保护、局内初始化和怪物牌堆所需补丁组；缺失时记录并阻止不完整入口执行。

## 局内初始化和额外卡组

手牌选择的 source 决定选中卡面何时恢复：传入模型会等待该模型的 ExecutionFinished。手动召唤、时机装备等独立流程使用空来源，让选牌结束后立即恢复手牌；提示卡名仍由 CardSelectorPrefs 提供。ShiJiHook 在时机效果的 finally 中通知被选中的卡执行结束，覆盖正常返回、异常和取消。新增独立效果须明确负责这条 UI 生命周期，不能依赖之后的牌堆变化碰巧清理。

RunSetup 在原版可等待钩子中依次处理所有玩家；没有本地玩家匹配失败后操作第一位玩家的兜底。宠物攻击加入可等待回合钩子，使用玩家专用模组随机流和同步的实体顺序。

ExtraDeckStore 只表示局外偏好。ExtraDeckRunState 在新局第一次准备时同步各玩家配置并保存为局内快照；旧局没有快照时，把已有额外堆保存为兼容基线。后续局外修改不影响本局。旧数据无法还原的初始配置不作猜测。

ExtraDeckSeries 以每玩家串行门禁补齐，实际入堆失败传播错误。CreateSummonedCopy 按实际持久卡克隆并记录来源，融合、同步及手动召唤共用此入口；返回时恢复对应持久卡。没有来源记录的历史或生成副本保留同类型兼容处理。

新系列通过 SeriesRegistry.Register 注册匹配条件及额外怪兽类型；不再要求修改中央 AllSeries 数组。规则 ID 必须唯一。

## 怪物副本和 UI

观测数据仅作为生成副本时的资料。获得副本时同步非攻击效果快照，卡面与打出读取同一份数据。原本动态的攻击 DamageCalc 和玩家加成仍动态计算。快照使用原版 SavedProperty 保存元数据；当前战斗中的原生招式引用沿用原有生命周期。

已知零值/空列表与未知数据分开表示，未知字段才使用兜底。观测正常完成才提交，异常丢弃草稿。新观测能力使用完整 ModelId，旧类名仍可读取。

MonsterDeck 决定可强夺的模型和怪物空过，UI 只播放移除演出。怪物牌堆模型使用各端一致的代表玩家，不依赖本机 UI 玩家。UI 不承担死亡状态账本清理。

CardRetrieval 的 RetrievalOptions 明确目的地、选择方式、数量及确认。RetrieverCard 保留兼容入口，互斥布尔参数会被拒绝。CardPrompts 克隆提示文本后注入卡名。

UiConditions 缓存仅供展示使用，钩子变化时失效，100ms 兜底处理无法追踪的自定义变化。实际执行始终重新查询。RunSeenSyncTicker 集中处理私人见卡记录补发；卡面观察只报告可见卡。布局诊断默认关闭，SCM_LAYOUT_DEBUG=1 才开启。

## 保存与迁移

AtomicFile 写临时文件并 Flush 后替换正式文件，保留 .bak。额外配置支持旧类型名到 ModelId 的读取迁移，保留未知条目；损坏或未来版本数据禁止覆盖原文件。写入失败恢复内存选择并向调用者返回失败。局内状态使用 RitsuLib RunSavedData，局外偏好使用本机文件。

## 验证范围

Tests/run-tests.ps1 是正式入口。当前验证包括 65 项控制台断言及 14 项 Godot 主机检查，未执行实际双端联机。发布前按 Tests/README.md 的游戏内场景验证。架构调整没有修改本地化卡牌描述；不应在重构中顺便改变规则。

SpecialEffectHook.ExecuteSpecialEffectAsync 是手牌特殊效果的统一演出入口，先等待 SpecialEffectVfx.Play 完整返回并清理卡面，再执行卡牌详细效果。各张卡不应重复播放这一演出。演出不移动实际牌堆、不消耗发动次数；实际发动/取消仍由原有 ActivationResult 与预占机制决定。

DengChangHook 在原有抽牌排除、同名每战一次与防重入判定之后复用 SpecialEffectVfx.Play，再处理登场详细效果。新增 IDengChangCard 实现直接受公共演出覆盖，卡牌自身不应再次播放。登场次数仍按原规则在触发时标记。

KeMoEquipCapability 在宿主进入弃牌堆时保留装备，包括普通/自动打出后的弃牌；弃牌堆洗回抽牌堆与再次回手也保留，回手刷新装备伤害预览。非弃牌去向的打出仍在 AfterCardPlayed 消耗装备；实际进入其他离场牌堆时由换堆钩子兜底。手牌直接返回抽牌堆仍沿用原卸装规则。

本轮版本为 0.0.2。局内数据记录效果快照协议能力；历史回放缺少新增选择时保留原结算路径，避免等待不存在的记录。旧回放的观测资料和原生属性映射没有完整兼容保证，仍需实机验证。

## 哀唱与驻场演出

珠泪·哀唱通过 SpecialEffectHook 的右键入口发动。EnemyIntentTargetPicker 用当前敌人意图生成临时展示卡，选择结果只映射到战斗实体，不修改怪物意图牌堆。目标在支付前重新查询；ZhuLeiAiChangTargets 以引擎分配的 Creature.CombatId 记录同一战斗内的预占和成功击晕。不同哀唱副本共享账本，切换战斗对象后账本隔离。该账本目前仅在内存中；战斗起点重放与中途快照恢复都尚未验证。

五张保留驻场效果的卡在各自规则入口等待 ZhuChangEffectVfx。演出复用 SpecialEffectVfx 的视觉副本，手牌节点未布局时最多等待两帧，本地可从手牌锚点生成展示副本；卡雷多哈特打出分支从 Play 卡面生成展示副本并返回手牌区域。真实卡模型始终由原打出、送墓及入堆流程管理。每张卡在演出返回后再次核验自己的关键条件，费用预览不调用演出。杯满的灿幻庄以当前实现为准：在手且本回合尚未打出攻击牌时，将同一玩家卡牌的费用改为 0；其费用查询保持纯函数。
