using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models.Capabilities;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「刻魔装备」（组件版）：挂在被装备的卡上，提供伤害加成。
//
// 用 RitsuLib 的「组件（ModelCapability）」而不是游戏本体的附魔 —— 因为一张卡要能**同时挂多件装备**
//（本体附魔一张卡只能有一个）。同类组件默认不合并，所以多件装备会各自留一份、加成叠加。
// 生命周期用 UntilCombatEndCapability：战斗结束自动卸掉（装备卡本体随战斗销毁）。
[RegisterModelCapability]
public class KeMoEquipCapability : UntilCombatEndCapability<CardModel>, ICardDescriptionContributor
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("EquipDamage", 0m)];

    /// <summary>这件装备的连接怪兽标记数（Link1=1 / Link2=2 / Link3=3；非连接怪兽装备 = 0）。</summary>
    public int LinkRating { get; set; }

    /// <summary>是否是「刻魔·神圣棺」那件装备（额外获得"攻击拥有格挡的敌人时造成双倍伤害"）。</summary>
    public bool DoubleVsBlock { get; set; }

    /// <summary>装备来源的卡名（展示用）。</summary>
    public string EquipName { get; set; } = "";

    // 伤害加成 = 6 × 这件装备自己的连接怪兽标记数。
    // 每件装备各自返回自己那一份，加起来正好等于「刻魔·神圣棺」① 的口径：
    //   卡的伤害上升 = 给该卡装备的连接怪兽标记合计 × 6。
    // （所以 镇魂棺 Link1 = +6、大圣棺 Link2 = +12、神圣棺 Link3 = +18，两件叠起来自然相加。）
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (Owner == null || cardSource != Owner) return 0m;
        return 6m * LinkRating;
    }

    // 「刻魔·神圣棺」②：这张卡攻击拥有格挡的敌人时，造成双倍伤害。
    // 多件神圣棺叠加不会变成 4 倍——只让"第一件带这个效果的装备"生效。
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (Owner == null || cardSource != Owner) return 1m;
        if (!DoubleVsBlock || !IsFirstDoubleVsBlock()) return 1m;
        if (target == null || target.Block <= 0) return 1m;
        if (!props.IsPoweredAttack()) return 1m;
        return 2m;
    }

    private bool IsFirstDoubleVsBlock()
    {
        var caps = Owner!.Capabilities().All.OfType<KeMoEquipCapability>().Where(c => c.DoubleVsBlock).ToList();
        return caps.Count > 0 && ReferenceEquals(caps[0], this);
    }

    protected override void OnDetach(CardModel card) => KeMoEquip.Unregister(this);

    // 进入弃牌堆（包括打出后的落堆）保留装备，洗牌与再次回手也保留。
    // 其他离手/离场仍消耗装备；非弃牌去向的打出在 AfterCardPlayed 结算完再消耗。
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null || cardPlay.Card != Owner) return;
        if (Owner.Pile?.Type == PileType.Discard) return;
        // 原版此时通常还在 Play；实际进堆前不能提前消耗将保留的装备。
        if (Owner.Pile?.Type == PileType.Play && cardPlay.ResultPile == PileType.Discard) return;
        await KeMoEquip.ConsumeAllEquipments(new BlockingPlayerChoiceContext(), Owner);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (Owner == null || card != Owner) return;
        var destination = Owner.Pile?.Type;
        if (destination == PileType.Discard || destination == PileType.Play) return;
        if ((oldPileType == PileType.Discard || oldPileType == PileType.Draw)
            && (destination == PileType.Draw || destination == PileType.Hand))
        {
            // 重新抽回/回收到手牌时重新计算装备加伤的卡面预览。
            if (destination == PileType.Hand) KeMoEquip.RefreshEquipmentPreview(Owner);
            return;
        }
        // 包含此前留在弃牌堆/抽牌堆的宿主被消耗、除外或移出战斗。
        if (oldPileType != PileType.Hand && oldPileType != PileType.Play
            && oldPileType != PileType.Discard && oldPileType != PileType.Draw) return;
        await KeMoEquip.ConsumeAllEquipments(new BlockingPlayerChoiceContext(), Owner);
    }

    // 卡面追加一行「装备：…」——列出这张卡身上的全部装备名（多件用「、」隔开）。
    // 一张卡挂着 N 件装备就有 N 个本组件，这一行只能出一次，所以只交给**第一件**去写。
    public IEnumerable<CardDescriptionFragment> GetDescriptionFragments(CardDescriptionContext context)
    {
        if (Owner == null) return [];

        var caps = Owner.Capabilities().All.OfType<KeMoEquipCapability>().ToList();
        if (caps.Count == 0 || !ReferenceEquals(caps[0], this)) return [];

        var names = caps.Select(c => c.EquipName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (names.Count == 0) return [];

        var text = new LocString("cards", "SELECT_SPECIAL_CARD_MOD_EQUIP_LINE");
        text.Add("EquipNames", string.Join("、", names));
        return [new CardDescriptionFragment(text)];
    }
}

// 「刻魔装备」的通用操作：选目标、挂组件、把装备卡本体移出消耗堆。
public static class KeMoEquip
{
    // 组件 → 它的"装备卡本体"（运行时表，不参与存档；组件卸下时自动清理）
    private static readonly Dictionary<KeMoEquipCapability, CardModel> EquipCards = new();

    public static void Reset() => EquipCards.Clear();
    internal static void Unregister(KeMoEquipCapability cap) => EquipCards.Remove(cap);

    /// <summary>「装备堆」里当前躺着的装备卡本体。</summary>
    public static List<CardModel> EquipmentCards(Player player)
        => Entry.EquipPile.GetPile(player)?.Cards.ToList() ?? [];

    /// <summary>把一张卡身上所有装备都消耗掉（非保留去向的离手/离场时用）。</summary>
    public static async Task ConsumeAllEquipments(PlayerChoiceContext ctx, CardModel target)
    {
        foreach (var cap in target.Capabilities().All.OfType<KeMoEquipCapability>().ToList())
        {
            if (EquipCards.TryGetValue(cap, out var equipCard))
            {
                cap.RemoveFromOwner();
                if (equipCard.Pile?.Type == Entry.EquipPile) await ExhaustEquipment(ctx, equipCard);
            }
            else
            {
                cap.RemoveFromOwner();
            }
        }
        RefreshEquipmentPreview(target);
    }

    /// <summary>选择完成后先统一卸下作为素材的装备，避免宿主离手时提前消耗它们。</summary>
    public static void DetachMaterialEquipment(CardModel card)
    {
        var caps = EquipCards.Where(kv => kv.Value == card).Select(kv => kv.Key).ToList();
        var targets = caps.Select(cap => cap.Owner).OfType<CardModel>().Distinct().ToList();
        foreach (var cap in caps)
            cap.RemoveFromOwner();
        foreach (var target in targets) RefreshEquipmentPreview(target);
    }

    /// <summary>把一张卡当融合素材消耗掉：如果它正作为装备挂在别人身上，先卸掉那个组件。</summary>
    public static async Task<bool> ExhaustMaterial(PlayerChoiceContext ctx, CardModel card)
    {
        if (!MaterialPayment.CanUseAsMaterial(card)) return false;
        DetachMaterialEquipment(card);
        // 其他素材的消耗连锁已经支付了这张卡时，不重复广播消耗事件。
        if (card.Pile?.Type == PileType.Exhaust) return true;

        var result = card.Pile?.Type == Entry.EquipPile
            ? await ExhaustEquipment(ctx, card)
            : await CardCmd.Exhaust(ctx, card);
        return result?.success == true;
    }

    // 装备卡本体躺在「装备堆」（无界面牌堆），原版消耗特效没地方播。
    // 统一走 SelectToolCmd.ExhaustWithVfx：在固定的手牌区位置播一次原版消耗特效，再静默入堆。
    public static async Task<CardPileAddResult?> ExhaustEquipment(PlayerChoiceContext ctx, CardModel equipCard)
        => await SelectToolCmd.ExhaustWithVfx(ctx, equipCard);

    // 选装备目标的提示（{CardName} 由调用方注入）
    private static LocString EquipPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_EQUIP");

    /// <summary>连接等级；非连接怪兽为 0。连接识别和素材计数共用此定义。</summary>
    public static int LinkRating(CardModel card) => CardClassification.LinkRating(card);

    public static bool IsLinkMonster(CardModel card) => LinkRating(card) > 0;

    // 能当装备目标：带「光恶魔」且不是连接怪兽
    public static bool CanEquipTo(CardModel card)
        => card.Tags.Contains(SelectTags.GuangEMo) && !IsLinkMonster(card);

    // 这张卡现在挂着几件装备（震怒之日的「装备数」用这个）
    public static int EquipCount(CardModel card)
        => card.Capabilities().All.OfType<KeMoEquipCapability>().Count();

    /// <summary>
    /// 把 equipCard 当作装备，装到手牌里选中的一张「光恶魔（连接怪兽除外）」上。
    /// linkRating = 这件装备的连接怪兽标记数（镇魂棺 1 / 大圣棺 2 / 神圣棺 3）。
    /// </summary>
    public static async Task Equip(PlayerChoiceContext ctx, Player player, CardModel equipCard,
        int linkRating, CardModel source, bool doubleVsBlock = false)
    {
        var hand = PileType.Hand.GetPile(player);
        if (hand == null || !hand.Cards.Any(CanEquipTo)) return;

        var prompt = new LocString(EquipPrompt.LocTable, EquipPrompt.LocEntryKey);
        prompt.Add("CardName", source.Title);
        var picked = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(prompt, 1, 1) { RequireManualConfirmation = true },
            context: ctx,
            player: player,
            filter: CanEquipTo,
            // 装备目标仍留在手牌，选择结束时立即恢复卡面，不等待装备卡的打出通知。
            source: null!)).FirstOrDefault();
        if (picked == null) return;

        await EquipTo(equipCard, picked, linkRating, doubleVsBlock);
    }

    /// <summary>纯挂载：给 target 挂上 equipCard 这件装备（不管目标是从哪个牌堆来的）。</summary>
    public static async Task EquipTo(CardModel equipCard, CardModel target, int linkRating, bool doubleVsBlock = false)
    {
        // 装备演出：装备卡从它现在的位置 → 飞到"被装备的那张卡"那里。
        // （装备堆是无界面牌堆，直接让原版播入堆位移会飞向屏幕中央，玩家看不出装备给了谁）
        PlayEquipFly(equipCard, target);

        // 挂组件：同类组件不合并，所以同一张卡可以叠多件装备
        var cap = ModelCapabilityRegistry.Create<KeMoEquipCapability>();
        cap.LinkRating = linkRating;
        cap.DoubleVsBlock = doubleVsBlock;
        cap.EquipName = equipCard.Title;
        target.AddCapability(cap);
        EquipCards[cap] = equipCard;

        // 装备卡本体离开消耗堆 → 「装备堆」（静默入堆，演出由上面的飞行负责）
        CardPileAddResult result;
        try
        {
            result = await CardPileCmd.Add(equipCard, Entry.EquipPile, CardPilePosition.Bottom, null, skipVisuals: true);
            if (!result.success || equipCard.Pile?.Type != Entry.EquipPile)
                throw new InvalidOperationException("Equipment card did not enter the equipment pile.");
        }
        catch
        {
            cap.RemoveFromOwner();
            RefreshEquipmentPreview(target);
            throw;
        }
        // 显示层失败不能撤销已经完成的装备移动与伤害组件。
        CompleteSilentEquipmentMove(result);
        RefreshEquipmentPreview(target);
    }

    internal static void RefreshEquipmentPreview(CardModel target)
    {
        if (target.HasBeenRemovedFromState || target.Pile?.Type != PileType.Hand) return;
        // 只重新计算预览，保留基础伤害；实际伤害仍由同一套原版/组件钩子计算。
        target.DynamicVars.ClearPreview();
        target.UpdateDynamicVarPreview(CardPreviewMode.Normal, target.CurrentTarget, target.DynamicVars);
        var node = NCard.FindOnTable(target, PileType.Hand);
        if (node != null && GodotObject.IsInstanceValid(node)) node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
    }

    internal static void CompleteSilentEquipmentMove(CardPileAddResult result)
    {
        if (!result.success || result.cardAdded.Pile?.Type != Entry.EquipPile) return;
        var oldPile = result.oldPile;
        if (oldPile == null || ReferenceEquals(result.cardAdded.Pile, oldPile)) return;
        // skipVisuals 同时抑制了来源牌堆的移除通知；自定义飞行不会补发它们。
        oldPile.InvokeCardRemoved(result.cardAdded);
        oldPile.InvokeCardRemoveFinished();
        oldPile.InvokeContentsChanged();

        if (oldPile.Type != PileType.Hand || !LocalContext.IsMe(result.cardAdded.Owner)) return;
        var hand = NPlayerHand.Instance;
        var holder = hand?.GetCardHolder(result.cardAdded);
        if (holder == null) return;
        var node = holder.CardNode;
        hand!.Remove(result.cardAdded);
        if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFreeSafely();
    }

    // 装备飞行演出：造一张卡面副本，从"装备卡现在的位置"飞到"被装备的那张卡"的位置，然后收掉。
    private static void PlayEquipFly(CardModel equipCard, CardModel target)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;

        var room = NCombatRoom.Instance;
        if (room?.Ui == null) return;

        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == equipCard.GetType());
        var node = template != null ? NCard.Create(template.ToMutable(), ModelVisibility.Visible) : null;
        if (node == null) return;

        room.Ui.AddChildSafely(node);
        node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);

        // 起点：装备卡自己若有卡面就用它的位置（例如神圣棺从手牌装备）；
        // 否则它来自消耗堆（无节点）→ 用消耗堆按钮的位置。
        var fromNode = NCard.FindOnTable(equipCard, null);
        node.GlobalPosition = fromNode != null
            ? fromNode.GlobalPosition
            : PileType.Exhaust.GetTargetPosition(node);

        // 终点：被装备的那张卡（在手牌里，有卡面节点）
        var toNode = NCard.FindOnTable(target, null);
        if (toNode == null)
        {
            node.QueueFreeSafely();
            return;
        }
        var to = toNode.GlobalPosition;

        var tween = node.CreateTween();
        tween.TweenProperty(node, "global_position", to, 0.35f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() => node.QueueFreeSafely()));
    }
}
