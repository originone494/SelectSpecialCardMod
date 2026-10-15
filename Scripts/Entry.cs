using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using Godot.Bridge;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;
using STS2RitsuLib;
using STS2RitsuLib.CardPiles;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Scaffolding.Cards.HandOutline;
using MegaCrit.Sts2.Core.Nodes.Cards;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    // modid：必须与 SelectSpecialCardMod.json 里的 id 保持一致
    public const string ModId = "SelectSpecialCardMod";
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModId);

    // 除外堆（ChuWaiPile）：注册后拿到的运行时 PileType 标识。
    // 之后移动卡牌用它，例如：await CardPileCmd.Add(card, ChuWaiPile);
    // 遍历用：ChuWaiPile.GetPile(player).Cards
    public static PileType ChuWaiPile;

    // 额外堆（ExtraPile）：和除外堆同类，只是位置在抽牌堆正上方。
    // 用法同上：await CardPileCmd.Add(card, ExtraPile);
    public static PileType ExtraPile;

    // 「额外堆·已用」：战斗中从额外堆取走的副本临时放这里（不显示任何界面），
    // 随战斗一起销毁；额外堆在战斗结束/开始时按规则补齐，所以打完就恢复原样。
    public static PileType ExtraUsedPile;

    // 「装备堆」（EquipPile）：被当作"装备"装出去的卡（刻魔·镇魂棺／刻魔·大圣棺）本体放这里——
    // 无界面、随战斗销毁。卡不能凭空消失，装备后必须有个落脚的牌堆，否则会留在消耗堆里被反复装备。
    public static PileType EquipPile;

    // 超量素材保留实际卡牌实例，存于独立的战斗内隐藏牌堆。
    public static PileType XyzMaterialPile;

    private static bool _initialized;
    public static void Init()
    {
        if (_initialized) return;
        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);

        // 让 RitsuLib 扫描本程序集里的注册特性（RegisterCard / RegisterRelic 等）
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        // 图鉴过滤器：Special 卡池默认不会出现在卡牌图鉴里，必须注册过滤器才能浏览
        ModContentRegistry.For(ModId)
            .RegisterCardLibraryCompendiumSharedPoolFilter<SelectSpecialCardPool>(
                "select_special_card_pool",
                "res://icon.png");

        // 图鉴过滤器：怪物卡池（意图卡牌化的卡面模板，图鉴可见供玩家查看）
        ModContentRegistry.For(ModId)
            .RegisterCardLibraryCompendiumSharedPoolFilter<MonsterCardPool>(
                "monster_card_pool",
                "res://icon.png");

        RegisterChuWaiPile();
        RegisterExtraPile();
        RegisterExtraUsedPile();
        RegisterEquipPile();
        RegisterXyzMaterialPile();
        MegaCrit.Sts2.Core.Combat.CombatManager.Instance.CombatEnded += _ => KeMoEquip.Reset();

        // 「时机」系统：攻击卡计数器 + 弃牌堆按钮右键挂接
        ShiJiTracker.Init();

        // 「特殊效果」系统：手牌右键发动特殊效果（RitsuLib 右键交互）
        SpecialEffectHook.Init();
        // 「特殊效果」可发动时的手牌金色泛光：走 RitsuLib 的手牌描边系统
        // （Switch + refreshEveryFrame：每帧重算，条件一变立刻亮/灭——不会再出现"用完了还黄着"）
        ModCardHandOutlineRegistry.Register<SelectSpecialBaseCard>(
            ModCardHandOutlineRules.Switch(
                card => UiConditions.SpecialEffect(card) ? NCardHighlight.gold : null,
                priority: 10,
                visibleWhenUnplayable: true,
                refreshEveryFrame: true));

        // 已放置的灵摆卡默认蓝边，仅主动灵摆效果可发动时为金边；被动效果不改变颜色。
        ModCardHandOutlineRegistry.Register<YiXiangMingVoiceCard>(
            ModCardHandOutlineRules.Switch(
                card => (card as YiXiangMingVoiceCard)?.PendulumOutlineColor,
                priority: 20,
                visibleWhenUnplayable: true,
                refreshEveryFrame: true));

        // 本局「见过的卡」记录（NCard 渲染时登记，供「当年假设山海图」等卡使用）
        RunSeenCards.Init();

        // 意图卡牌化：怪物意图拆成迷你卡悬浮在怪物头上
        IntentCardHook.Init();

        // 逃跑副本卡：玩家遁走结算（EndCombatInternal + 空奖励）
        EscapeState.Init();

        // 召唤副本卡：宠物帮打（玩家回合开始按意图快照伤害攻击）
        PetAlly.Init();

        // 击败每层第一个精英：Special 卡池 3 选 1 奖励（前提：持有「决斗盘」，移植自 yuno mod）
        EliteSpecialReward.Register();

        // 开局发放「决斗盘」，并兼容替换旧存档的「死亡讯息-红」。
        DuelDskGrant.Register();

        // 额外卡组：设置页入口（局外选「额外怪兽」）+ 开局把选好的卡放进额外堆
        ExtraDeckSettings.Register();
        ExtraDeckGrant.Register();
        // 系列卡补给：获得珠泪 / 天杯龙·灿幻系列的卡时，把对应额外怪兽加入额外堆（同名上限 3）
        ExtraDeckSeries.Register();
        // 额外堆手动召唤：额外怪兽满足召唤条件时高亮额外堆，右键可召唤
        ExtraDeckSummonHook.Register();
        PendulumSummon.Register();

        // Harmony 补丁（DrawWindowPatch 等）：实例 ID 用本 mod 自己的，避免与其他 mod 冲突。
        // 不用 PatchAll：它遇到单个补丁类应用失败会整体抛异常中断，排在后面的补丁类
        // 全部丢失且无任何提示（v0.111.0 下 AddToCombatAndPreview<T> 的泛型状态机
        // 局部变量让 MonoMod 导入崩溃，曾导致路线 A 全部补丁静默失效）。
        // 逐类打补丁 + try/catch 隔离：坏一个只坏一个，并在日志里写明哪个类坏了。
        var harmony = new Harmony("com.SelectSpecialCardMod.patch");
        foreach (var type in AccessTools.GetTypesFromAssembly(assembly))
        {
            if (type.GetCustomAttribute<HarmonyPatch>() == null) continue;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                FeatureStatus.AppliedPatch(type);
            }
            catch (Exception ex)
            {
                GD.PushError($"[SelectSpecialCardMod] 补丁类 {type.FullName} 应用失败：{ex.GetType().Name}: {ex.Message}");
                Logger.Info($"[Patch] 补丁类 {type.FullName} 应用失败：{ex.Message}");
            }
        }

        FeatureStatus.Complete();
        _initialized = true;
        Logger.Info("[Init] SelectSpecialCardMod 初始化完成");
    }

    // 除外堆：战斗内把卡牌移出战斗的区域。
    // 必须在初始化阶段注册，模型初始化后牌堆注册会冻结。
    private static void RegisterChuWaiPile()
    {
        ChuWaiPile = ModCardPileRegistry.For(ModId)
            .RegisterOwned("chu_wai_pile", new ModCardPileSpec
            {
                // 每场战斗创建、战斗结束销毁（除外不跨战斗保留）
                Scope = ModCardPileScope.CombatOnly,
                // 挂在战斗界面右下（消耗堆旁边）
                Style = ModCardPileUiStyle.BottomRight,
                // 直接引用官方消耗堆的图标资源，随游戏版本自动同步
                IconPath = "res://images/packed/combat_ui/exhaust_pile.png",
                // BottomRightPrimary 自动排在消耗堆左侧（-100,0），偏移 (+100,-100)
                // 抵消横移后正好落在消耗堆正上方（按钮 80 高 + 20 间距）
                Anchor = new ModCardPileAnchor(ModCardPileAnchorKind.BottomRightPrimary, new Vector2(100f, -100f)),
                // 点击牌堆按钮打开原版样式的查看界面
                OnOpen = ctx => ctx.ShowDefaultPileScreen(),
                VisibleWhen = ctx => ctx.Player != null,
                // 查看界面默认按卡名排序：同名的卡自然排在一起
                //（只影响显示顺序，不改变牌堆本身顺序）
                View = new ModCardPileViewSpec
                {
                    DefaultSorting = [SortingOrders.AlphabetAscending],
                },
            }).PileType;

        Logger.Info($"[CardPiles] 除外堆注册完成: 0x{(int)ChuWaiPile:X8}");
    }

    // 额外堆：与除外堆同类（战斗内牌堆、有按钮、可点开查看），位置放在**抽牌堆正上方**。
    // 设计定位 = 游戏王的「额外卡组」：本局一直带着的第二个牌堆，装「额外怪兽」（融合怪兽等）。
    // 与除外堆的关键差别：
    //   · 除外堆是 CombatOnly（一场战斗用完就没）；额外堆是 RunPersistent（跟卡组一样跨战斗保留）；
    //   · 额外堆里的卡不会被抽到，取出方式交给融合/同调这类"召唤"效果去写。
    private static void RegisterExtraPile()
    {
        ExtraPile = ModCardPileRegistry.For(ModId)
            .RegisterOwned("e_wai_pile", new ModCardPileSpec
            {
                // 额外卡组是"本局的资产"：跨战斗保留（和卡组一样）。
                Scope = ModCardPileScope.RunPersistent,
                // 挂在战斗界面左下（抽牌堆那一侧）
                Style = ModCardPileUiStyle.BottomLeft,
                // 图标与消耗堆相同
                IconPath = "res://images/packed/combat_ui/exhaust_pile.png",
                // BottomLeftPrimary = 从抽牌堆按钮向右延伸的那一行；偏移 (-100,-100)
                // 抵掉横移，正好落在抽牌堆正上方（与除外堆落在消耗堆正上方是同一套做法）。
                Anchor = new ModCardPileAnchor(ModCardPileAnchorKind.BottomLeftPrimary, new Vector2(-100f, -100f)),
                // 点击牌堆按钮打开原版样式的查看界面
                OnOpen = ctx => ctx.ShowDefaultPileScreen(),
                VisibleWhen = ctx => ctx.Player != null,
            }).PileType;

        Logger.Info($"[CardPiles] 额外堆注册完成: 0x{(int)ExtraPile:X8}");
    }

    // 「额外堆·已用」：只存"这一场战斗里从额外堆取走"的副本，无界面、战斗结束销毁。
    private static void RegisterExtraUsedPile()
    {
        ExtraUsedPile = ModCardPileRegistry.For(ModId)
            .RegisterOwned("e_wai_yong_guo_pile", new ModCardPileSpec
            {
                Scope = ModCardPileScope.CombatOnly,
                Style = ModCardPileUiStyle.Headless,
            }).PileType;

        Logger.Info($"[CardPiles] 额外堆·已用 注册完成: 0x{(int)ExtraUsedPile:X8}");
    }

    // 「装备堆」：被当作装备装出去的卡本体，无界面、战斗结束销毁。
    private static void RegisterEquipPile()
    {
        EquipPile = ModCardPileRegistry.For(ModId)
            .RegisterOwned("ke_mo_equip_pile", new ModCardPileSpec
            {
                Scope = ModCardPileScope.CombatOnly,
                Style = ModCardPileUiStyle.Headless,
            }).PileType;

        Logger.Info($"[CardPiles] 装备堆注册完成: 0x{(int)EquipPile:X8}");
    }

    private static void RegisterXyzMaterialPile()
    {
        XyzMaterialPile = ModCardPileRegistry.For(ModId)
            .RegisterOwned("xyz_material_pile", new ModCardPileSpec
            {
                Scope = ModCardPileScope.CombatOnly,
                Style = ModCardPileUiStyle.Headless,
            }).PileType;
    }
}
