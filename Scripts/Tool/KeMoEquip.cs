using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
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

    // 被装备的卡「离开手牌」→ 挂在它身上的装备卡会被消耗。
    // 打出这张卡时等这一击结算完再消耗（AfterCardPlayed），好让这一击吃到装备加成。
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null || cardPlay.Card != Owner) return;
        await KeMoEquip.ConsumeAllEquipments(new BlockingPlayerChoiceContext(), Owner);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (Owner == null || card != Owner) return;
        if (oldPileType != PileType.Hand) return;
        if (Owner.Pile?.Type == PileType.Play) return;   // 打出交给 AfterCardPlayed 那条
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

    internal static void Unregister(KeMoEquipCapability cap) => EquipCards.Remove(cap);

    /// <summary>「装备堆」里当前躺着的装备卡本体。</summary>
    public static List<CardModel> EquipmentCards(Player player)
        => Entry.EquipPile.GetPile(player)?.Cards.ToList() ?? [];

    /// <summary>把一张卡身上所有装备都消耗掉（被装备的卡离开手牌时用）。</summary>
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
    }

    /// <summary>把一张卡当融合素材消耗掉：如果它正作为装备挂在别人身上，先卸掉那个组件。</summary>
    public static async Task ExhaustMaterial(PlayerChoiceContext ctx, CardModel card)
    {
        var cap = EquipCards.FirstOrDefault(kv => kv.Value == card).Key;
        cap?.RemoveFromOwner();

        if (card.Pile?.Type == Entry.EquipPile)
        {
            await ExhaustEquipment(ctx, card);
            return;
        }
        await CardCmd.Exhaust(ctx, card);
    }

    // 装备卡本体躺在「装备堆」（无界面牌堆），原版消耗特效没地方播。
    // 统一走 SelectToolCmd.ExhaustWithVfx：在固定的手牌区位置播一次原版消耗特效，再静默入堆。
    public static async Task ExhaustEquipment(PlayerChoiceContext ctx, CardModel equipCard)
        => await SelectToolCmd.ExhaustWithVfx(ctx, equipCard);

    // 选装备目标的提示（{CardName} 由调用方注入）
    private static LocString EquipPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_EQUIP");

    // 「连接怪兽」= 带任何 Link 标签的卡。以后新增 Link 标签（Link3…）加到这里即可。
    public static bool IsLinkMonster(CardModel card)
        => card.Tags.Contains(SelectTags.Link1) || card.Tags.Contains(SelectTags.Link2);

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
            prefs: new CardSelectorPrefs(prompt, 1, 1),
            context: ctx,
            player: player,
            filter: CanEquipTo,
            source: source)).FirstOrDefault();
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
        await CardPileCmd.Add(equipCard, Entry.EquipPile, CardPilePosition.Bottom, null, skipVisuals: true);
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
