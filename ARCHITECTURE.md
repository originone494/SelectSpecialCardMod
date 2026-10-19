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

升龙和超龙通过 `IExtraDeckSummonCard` 接入右键额外堆，`SynchroSummon` 分别选择1张龙族调整和1张非调整龙族，读取实例 `StarLevel` 并要求合计等于目标星级。确认两张前不支付，逐张消耗时重查战斗、额外怪兽和未支付素材；灵摆区卡不能作为素材。此入口与打出天杯龙后触发的 `TongDiao` 分开，保留原有触发规则。杯满消耗修改所选手牌副本的伤害后刷新预览，两张灿幻同步的描述通过 `{Damage:diff()}` 读取同一个伤害变量。

手牌选择的 source 决定选中卡面何时恢复：传入模型会等待该模型的 ExecutionFinished。手动召唤、时机装备等独立流程使用空来源，让选牌结束后立即恢复手牌；提示卡名仍由 CardSelectorPrefs 提供。ShiJiHook 在时机效果的 finally 中通知被选中的卡执行结束，覆盖正常返回、异常和取消。新增独立效果须明确负责这条 UI 生命周期，不能依赖之后的牌堆变化碰巧清理。

RunSetup 在原版可等待钩子中依次处理所有玩家；没有本地玩家匹配失败后操作第一位玩家的兜底。宠物攻击加入可等待回合钩子，使用玩家专用模组随机流和同步的实体顺序。

ExtraDeckStore 只表示局外偏好。ExtraDeckRunState 在新局第一次准备时同步各玩家配置并保存为局内快照；旧局没有快照时，把已有额外堆保存为兼容基线。后续局外修改不影响本局。旧数据无法还原的初始配置不作猜测。

ExtraDeckSeries 以每玩家串行门禁补齐，实际入堆失败传播错误。CreateSummonedCopy 按实际持久卡克隆并记录来源，融合、同步及手动召唤共用此入口；返回时恢复对应持久卡。没有来源记录的历史或生成副本保留同类型兼容处理。

新系列通过 SeriesRegistry.Register 注册匹配条件及额外怪兽类型；不再要求修改中央 AllSeries 数组。规则 ID 必须唯一。

额外怪兽统一由 `CardClassification.IsExtraMonster` 根据卡牌分类判断：Link1～Link4、融合怪兽、超量怪兽、同步怪兽任一满足即可。已删除独立的 ExtraMonster 标签/关键字；检索排除、精英奖励、额外卡组设置及同步、系列补给计数、融合素材返回位置、两种壶的随机额外怪兽和灵摆召唤均使用该判定。连接等级由 `CardClassification.LinkRating` 统一读取，刻魔素材计数沿用此结果；额外堆卡框分组也读取怪兽分类标签。

`ChaoLiangGuaiShou`（超量怪兽）是分类标签，`ChaoLiang`（超量）保留为召唤机制；No.41 从 XyzMonsterCard 同时继承二者。两张灿幻同步怪兽增加 `TongBuGuaiShou`（同步怪兽），保留已有 TongBu 标签。珠泪已删除 ZhuLeiXiaJiGuaiShou 标签/关键字及 IsLowerMonster；四处原下级检索改用 IsMonster，再由通用检索入口排除额外怪兽，弃牌堆/消耗堆回收仍按原效果允许珠泪怪兽。

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

卡牌内容设置由 CardAvailability 统一判定。局外 Global 偏好 `card_content_preferences` 默认为关闭；主机在 RunSavedDataLobbyStagingEvent.Committing 写入共享局内槽 `card_content_policy`，本局规则随开局消息与存档固定，局中改设置只影响新局，旧存档缺槽时保持关闭。显式非游戏王类型表只过滤本模组的获取候选，不移除模型注册、图鉴或已有卡。标准特殊卡池、特殊奖励、检索、随机生成、异解补给袋、指定模板批量生成、见卡候选和额外卡组补给共用判定。详细清单与边界见 `CARD_CONTENT_FILTER_REPORT_2026-10-16.md`。

同一局外设置数据另含 `GrantDuelDiskAtStart`，默认开启，只控制本人。共享开局策略 Version=3 标记使用个人发放设置，不再复制主机的发放偏好。DuelDskGrant 在首次发放前使用原版 PlayerChoiceSynchronizer 同步各玩家自己的布尔选择，仅 LocalContext.IsMe 的客户端读取其本机偏好；其余端及新回放读取该玩家的同步记录。每玩家局内槽 `duel_disk_initial_grant` 保存 PreferenceResolved、GrantAtStart、InitialGrantHandled，已解析偏好不重复同步，已处理发放不补发。v1/v2旧局继续使用旧策略，不向旧回放插入新选择。已有死亡讯息-红始终按旧规则迁移为决斗盘。决斗盘资格只限制现有特殊精英奖励，不扩展到检索、随机生成或初始额外卡组。

## 哀唱与驻场演出

珠泪·哀唱通过 SpecialEffectHook 的右键入口发动。EnemyIntentTargetPicker 用当前敌人意图生成临时展示卡，选择结果只映射到战斗实体，不修改怪物意图牌堆。目标在支付前重新查询；ZhuLeiAiChangTargets 以引擎分配的 Creature.CombatId 记录同一战斗内的预占和成功击晕。不同哀唱副本共享账本，切换战斗对象后账本隔离。该账本目前仅在内存中；战斗起点重放与中途快照恢复都尚未验证。

五张保留驻场效果的卡在各自规则入口等待 ZhuChangEffectVfx。演出复用 SpecialEffectVfx 的视觉副本，手牌节点未布局时最多等待两帧，本地可从手牌锚点生成展示副本；卡雷多哈特打出分支从 Play 卡面生成展示副本并返回手牌区域。真实卡模型始终由原打出、送墓及入堆流程管理。每张卡在演出返回后再次核验自己的关键条件，费用预览不调用演出。杯满的灿幻庄以当前实现为准：在手且本回合尚未打出攻击牌时，将同一玩家卡牌的费用改为 0；其费用查询保持纯函数。

玩家可获取的特殊卡由 `SpecialCardUpgrade` 统一补齐为升级等级1：`SelectSpecialBaseCard.AfterCreated` 覆盖奖励、检索、随机生成及持久额外卡组；从已有拥有者实例复制时由 `AfterCloned` 补齐；入堆钩子为遗漏的实例提供兜底。模板转可变实例尚无拥有者时不提前升级，以免原版反序列化按存档次数再升一次。`SpecialCardLoadedUpgradePatch` 在原版 `FromSerializable` 完成后处理等级0的旧存档，等级1保持原样。原版/其他模组普通卡、怪物意图卡、Token及图鉴隐藏机制卡不在自动升级范围内；可打出的 `ShenHuaCompositeCard` 单独纳入外观升级，选择项等其他Token仍排除。

特殊卡的 `OnUpgrade` 在基类固定为空，仅采用原版 `UpgradeInternal` 和 `FinalizeUpgradeInternal` 设置并完成外观升级，不走升级命令演出、地图升级记录或命令级升级钩子。现有费用、效果、关键词和标签保留，重复处理直接返回。珠泪·残响删除旧升级分支的额外4格挡与1虚弱，最终基础数值为6伤害、7格挡、2虚弱。精英奖励改用该统一入口，避免单独升级与其他获取方式产生差异。

开局特殊卡新增两个个人偏好 `ChooseSpecialCardAtStart`、`SpecialCardRewardAtStart`，默认关闭，沿用 `card_content_preferences` 文件。共享开局策略 Version=4 仅标记新功能可用；各玩家本人通过 PlayerChoiceSynchronizer 提交两位开关值，其余端/回放读取相同记录。旧策略版本不增加新的选择、奖励或同步记录。逐玩家局内槽 `start_special_cards` 保存开关快照、自选/奖励各自完成状态、已确认但待入组的卡名、奖励选项及领取前同名卡数量；重入不重复发放，奖励生成失败后重试沿用原选项，不再消耗奖励随机数。若保存/异常发生在入组后等待钩子的阶段，以卡组同名数量增长补记完成；特殊替换/阻止入组遗物及中途读档仍须实测。

`RunStartCardsReadyPatch` 在实际 NTransition.RoomFadeIn（新局）及 FadeIn（继续游戏）完成后启动流程；不在 AfterRoomEntered/EnterRoomInternal 内等待界面，避免阻止房间联机位置更新或屏幕淡入。`RunStartCards.Prepare` 要求非战斗房间已存在，先完成原有 RunSetup，再按 NetId 顺序在各端处理每位玩家的自选、奖励、系列额外补给。正常涅奥开局在初始事件显示；无涅奥模式在初始地图显示。开局奖励使用 CardReward 派生 OpeningCardReward：存在房间历史时完全走原版领取/跳过；空地图没有历史时改用原版三选一卡牌界面同步选择、走原版 CardPileCmd.Add 入组，并省略不存在的房间历史写入，避免原版 CardReward 的空引用。空地图分支不提供常规 CardRewardAlternative 附加选项，原版奖励列表仍可整体跳过。运行结束取消等待，未选临时副本从状态清理。

`SpecialCardRewards` 供开局自选、开局奖励和精英奖励共用主卡组候选：本模组解锁卡、多人约束、不可获取卡排除、非游戏王过滤、额外怪兽排除、按类型去重及按 ModelId 排序。三选一从玩家 Rewards RNG 不重复抽取最多3张，使用原版指定卡牌 CardReward 构造器，避开 Ancient 稀有度随机生成限制。开局奖励走原版 OfferCustom，所有客户端生成并等待所有玩家奖励，仅本人显示；可领取也可跳过。决斗盘资格及每幕首个精英限制仍只留在 EliteSpecialReward 的入口，开局两项不依赖决斗盘。

## 随缘神话合体（旧卡兼容）

本节记录迁移前的合体卡；2026-10-17之后的新生成入口见下文“神话异变系统”。

`SuiYuanShenHuaHeTiCard` 为非游戏王X费技能、先古、消耗；在选择前用原版 `ResolveEnergyXValue` 固定X，从手牌选0～X张合法素材，使用真实消耗命令逐张支付。选择0张不生成；成功支付后生成含X个不同模块的一个技能，与素材数量和身份无关。生成时独立随机基础费用0～3（各25%）及是否拥有消耗（各50%），在入手前固定。灵摆区实体通过现有 `MaterialPayment.CanUseAsMaterial` 排除。支付期间战斗结束／切换或素材移走时不生成；未入堆的生成实体会清理。

`FusionActions` 定义原子动作与候选过滤，`FusionEffectLibrary` 手工编写64个不可变配方，`FusionEffectRunner` 顺序执行模块／动作，`ShenHuaCompositeCard` 负责固定实例、卡面和保存。来源卡类型仅是审核元数据，不调用来源卡OnPlay或复制其功能接口。模块分类标签取24项白名单并集，不继承星级／额外怪兽／灵摆等功能标签。

抽取按固定顺序分组，先等概率选系列、再选模块，一轮优先未使用系列，模块不放回；同系列不同模块可并存。使用局内Niche RNG。`ModuleIdsJson` 以原版SavedProperty保存有序ID，复制和加载不重新随机，未知／重复ID及对已绑定实例的重抽被拒绝。ID应保持稳定；当前未保存配方版本快照，后续修改同ID配方会影响读回的旧实例。

合体卡为战斗Token，不进入奖励、检索、图鉴或永久卡组，只有该生成入口可创建；基础费用随机0～3，随机带有消耗；无消耗时正常弃牌，外观升级。`GeneratedTraits` 以原版SavedProperty整数保存费用与消耗，设置时更新原版自定义基础费用及已缓存的实例关键词，克隆/读档/回合结束不重抽；旧档缺少该属性时沿用0费无消耗。卡名按前两个模块与数量组合，描述逐模块列出动作。所有配方动作预声明独立原版动态变量，绑定模块不清空变量缓存，克隆保持数值修正。单体目标失效后使用CombatTargets RNG选择新目标；玩家死亡、战斗结束或切换时停止后续动作。合体卡与本体共用现有250×351插画，并明确采用先古卡布局。完整规则与首批待审核配方见 `MYTH_COMPOSITE_IMPLEMENTATION_2026-10-16.md`、`MYTH_EFFECT_LIBRARY_REVIEW_2026-10-16.md`。

神圣棺合体模块的条件翻倍由`FusionBlockDamageVar`预览、`FusionBlockDamageScope`范围及合体卡原版`ModifyDamageMultiplicative`共同处理：AsyncLocal隔离UI与异步攻击，匹配具体来源卡与CardPlay，目标当次有格挡则×2；正常／异常结束恢复上层范围，其他模块不自动翻倍。实际攻击仍传基础18，力量等先按原版加算后再乘倍率。魔镌客使用独立KeMoSpell过滤检索刻魔魔法。

## 神话异变系统（2026-10-17）

本体仍为X费、最多消耗X张手牌、0张不生成；成功支付后调用MythCardFactory，只生成一个条件＋一个效果。新MythMutationCard是独立ModCardTemplate，随机类型/费用/卡图/范围数值，继承效果来源的系列分类标签，不自动进行旧特殊卡的外观升级。新卡只通过CombatState.CreateCard创建并加入战斗手牌，不建立RunState原件或DeckVersion，不进入永久卡组，仅本场战斗有效。

三份config/myth JSON分别保存45条件、121效果和105张已验证250×351卡图，资源通过Godot FileAccess加载，导出预设显式包含JSON。条件类别固定70/15/15，同类排序后均匀抽取；费用0～3、类型、卡图和参数独立随机，沿用50%消耗。专用GetModRunRng玩家流myth_mutation支持保存及同步；素材不进入工厂。

SnapshotJson保存完整MythCardSnapshot：模板文字/动作、最终参数、图片ID及路径、类型费用、消耗、升级状态和系列标签符号名称。标签通过26项分类白名单筛选效果来源元数据，含系列子类，排除星级、额外怪兽与功能标记；条件、图片和投入卡不贡献标签。旧快照缺少标签字段时从效果来源补齐一次；后续复制、保存和升级保持已冻结标签。加载及克隆不用当前配置重生成；公开捕获返回深拷贝。动作白名单执行，预声明变量槽位仅绑定/升级改值，GainsBlock按实际动作判定，类型与目标分别处理。条件通过IsPlayable和实际支付双重验证；手牌来源由原生牌堆变更钩子记录，整手支付逐张核验，灵摆区实体不可支付。新专用休战能力持续到敌方回合结束，作用于同战斗全体伤害，不改变旧一时休战。

升级采用模板数值增量或减费，不重抽条件/效果/图片/类型。升级结果写回快照，加载原版升级状态时按快照等级去重；使用原版UpgradeValueBy/UpgradeBy支持预览颜色。旧ShenHuaCompositeCard及64项库保留，其他特殊卡的外观升级策略不变。详见MYTH_MUTATION_REPORT_2026-10-17.md与MYTH_MUTATION_REVIEW_2026-10-17.md。


## 神话完整库接入（2026-10-17后续修订）

开局指定发放设置：ExtraDeckSettings新增默认关闭的“游戏开始时，将1张「随缘神话合体」加入牌组”。偏好存于CardContentPreferences.GrantMythCardAtStart，开局与其他个人选项一起同步为第3位标志；仅新建v5策略局启用。RunStartCardData v2保存个人开关、开始／完成标记与发放前同名张数，支持发放期间保存后的数量核对，且不影响其他开局选项。使用RunState.CreateCard<SuiYuanShenHuaHeTiCard>对应的规范模型生成已升级本体并按原生命令入永久牌组，不调用神话随机工厂。用户明确要求此指定发放优先于非游戏王过滤；不依赖决斗盘。v4局保留原两位协议，已初始化的v1开局快照不补发。

控制台入口修复：游戏日志确认直接生成并打出旧结果模板SHEN_HUA_COMPOSITE_CARD会得到未绑定配方的空卡，不会调用本体生成流程。MythConsoleCardAlias只在原生card命令中将该ID重定向到SUI_YUAN_SHEN_HUA_HE_TI_CARD，保留目标牌堆与原命令数组；模型ID和存档加载不重定向。未绑定旧模板显示说明并禁止手动打出，已绑定旧实例沿用原配方。真正本体名称为“随缘神话合体”，描述为“消耗最多X张牌，将1张拥有随机效果的卡加入手牌。”；X费、至少投入1张才生成及50%消耗等规则保持既有实现。

两份配置逐字节替换为用户桌面的原始JSON，45条件（空1／限制20／支付24）及121效果全部开放，不使用未实现模板过滤。MythLibraryImport支持新根对象、source、text、args、step结构；同时保留原数组格式。新库31种条件动作由MythLibraryConditions处理，83种效果动作由MythLibraryEffects、MythOngoingPower、MythCreaturePowers和MythIntentTheft分发。加载时检查动作名称、参数类型与元数，未知动作明确拒绝。

动态参数解析后连同原参数键保存到完整快照；新旧执行分支独立。原卡OnPlay不参与生成或执行；原卡标签仍只作为分类元数据。延迟打牌保存原生战斗卡编号，复制敌人保存本体CombatId；持续效果每次施加独立实例，避免不同配方互相覆盖。来源仅为数据追溯，不要求场上持有源卡或其系列。

新库未提供upgrade字段，不擅自增加数值升级；原有快照携带的升级增量继续有效。0～3费、三类型、50%消耗、条件70／15／15、仅本战斗及继承效果系列的既有规则继续保留。详细效果清单、实现入口、校验与未执行实测边界见MYTH_FULL_LIBRARY_IMPLEMENTATION_2026-10-17.md。

## 所有卡牌奖励随机化（2026-10-17）

新增默认关闭的 RandomizeAllCardRewards 设置，开局由主机写入v6局策略。MythCardRewards覆盖原版奖励工厂、指定选项奖励及单张指定卡奖励；使用原奖励RNG或事件RNG覆盖，保留选项数量、选择与重掷流程。生成永久RunState神话卡，先固定UpgradeLevel=1，再设置升级外观，避免附加数值升级；领取后进入牌组，和随缘神话合体仍只生成战斗卡的入口独立。

多选奖励使用隐藏且不可获取的MythRewardEnvelopeCard保存完整选项与重掷状态，复用原版完整单卡JSON及网络传输；恢复为CardReward，不依赖外部批次表。内层SerializableCard编码显式包含SavedProperties公共字段。开局未领取奖励另由RunStartCardData.FixedRewardCards保存固定实例，并按配方摘要核对领取。商店商品、检索、战斗生成、开局全卡池自选和指定发放不改。详细实现、覆盖范围与静态／实测边界见RANDOM_CARD_REWARDS_REPORT_2026-10-17.md。

2026-10-17 单人设置修复：RitsuLib大厅事件的IsHost只表示联机主机，单人为false。CardAvailability改为单人／主机均提交，另在RunSavedDataPreparingEvent中仅为缺失快照的单人新局补齐；保留已导入规则，联机客户端及旧存档不从本机偏好改写。增加策略及个人发放诊断日志，详见SETTINGS_STARTUP_FIX_2026-10-17.md。

2026-10-17 新增默认关闭的AllCardRewardsSpecial：v7开局策略记录此开关，新局单人／主机提交和无大厅单人补齐沿用已修复入口。CardRewardReplacement统一两种互斥的替换模式；旧快照若同时为true仍沿用随机模式。仅新开关生效时从SpecialCardRewards.Candidates抽取正常特殊卡，保留原效果和外观升级，排除额外怪兽／机制卡并遵循非游戏王过滤。可用卡足够时同组不重名，不足时允许重复以维持原奖励数量；空池保留原入口并记录日志。沿用已有奖励工厂、指定选项、单卡、重掷和完整选项存档支持，开局待领取奖励亦保存固定选项。v6及更早策略不会启用新模式。

设置页只调整选项顺序，保留card_content与extra_deck两个原有区域，不增加分类标题。顺序：决斗盘、开局全池自选、开局特殊奖励、开局指定神话卡、所有奖励正常特殊卡、所有奖励神话随机卡、非游戏王过滤，最后原有额外卡组入口。所有旧偏好绑定和键名保持不变。

奖励开关后续改为互斥：两个ProjectedModSettingsValueBinding共享CardRewardMode父绑定，开启一个时原子关闭另一个，关闭未启用的选项不影响当前模式，允许两个都关闭；共享父绑定也使另一个勾选同步刷新。配置仍保存原有两个布尔字段，不改文件名或重置其他偏好。注册时将旧的同时开启配置规范为随机模式，新局快照只写一个生效模式，旧局不改写。

2026-10-17 后续规则覆盖：界面统一将随机效果特殊卡称为“神话卡”，删除随缘神话合体的“神话异变”静态悬停说明及旧选素材提示；本体改为0费，OnPlay直接调用战斗神话卡工厂生成1张入手，不选牌、不支付素材，保留本体消耗关键字。生成的神话卡仍只在本场战斗有效；模型ID、完整配方保存和45条件／121效果JSON保持兼容。

开局“获得1组特殊卡奖励”明确发放正常特殊卡，OpeningCardReward实现INormalSpecialCardReward，构造和Populate不会被全局神话奖励设置改写。重掷参数以引用标记强制走正常特殊卡池，完整奖励载体保存KeepSpecialCards以在恢复时保留该行为；普通战斗／精英／事件等原奖励仍按各自全局开关执行。RunStartCards会将旧版本尚未领取、被错误保存成神话卡的开局选项恢复为原先固定的特殊卡ID，已经领取的不补发。

2026-10-17 新增独立的“游戏开始时，获得1组神话卡奖励”，默认关闭，紧邻开局特殊奖励，未新增设置分类。个人偏好为MythCardRewardAtStart，仅v8新局使用开局设置第4位（8）；v7及更早局保留原协议。RunStartCardData v3保存GiveMythReward、MythRewardCompleted、完整FixedMythRewardCards、按配方摘要记录的领取前数量及MythRewardRerolled；已初始化的旧快照不补发。按玩家自己的开关发放，不依赖决斗盘，可以与其余开局发放同时开启。

该奖励通过MythCardRewards.Create生成3张已升级、永久RunState神话卡，使用玩家奖励RNG；选择1张入组或跳过。本体随缘神话合体仍只在战斗生成临时神话卡。开局普通特殊奖励先结算，再结算独立神话奖励；普通特殊卡池为空不会提前终止神话奖励。OpeningCardReward现为共用空地图兼容基类，OpeningSpecialCardReward／OpeningMythCardReward分别带正常特殊卡／神话卡标记；后者强制保留神话选项，不受两个全局奖励转换开关影响。重掷参数引用标记及完整奖励载体KeepMythCards保留这一规则，逐玩家待领奖励同时追踪Populate后的最终配方和重掷使用状态，恢复时不重抽、不按同模板ID误认已领取，也不恢复已使用的重掷资格。无牌堆的预览实例在流程结束或异常时清理。
