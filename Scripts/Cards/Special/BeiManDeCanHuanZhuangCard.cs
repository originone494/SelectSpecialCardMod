using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「盃満ちる燦幻荘」（杯满的灿幻庄）：
//   一场战斗只能打出一次，打出后，返回手牌。
//   打出：「检索」1张「天杯龙」，之后，丢弃1张自身以外的手牌。
//   消耗时：选择手牌1张龙族同调卡，使其伤害翻倍。
//
// "打出后返回手牌"：AfterCardPlayed 里移回手牌，流水线末尾的落堆步骤
// 因卡已不在打牌区而自动跳过（与 yuno-mod 同款手法）。
public class BeiManDeCanHuanZhuangCard : SelectSpecialBaseCard
{
    public BeiManDeCanHuanZhuangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    // 一场战斗只能打出一次：按**这张卡自己**记
    private CombatId? _playedCombat;
    private bool _zhuChangShown;
    private bool _zhuChangPending;
    private CombatId? _zhuChangCombat;
    private bool PlayedThisCombat => Owner != null && _playedCombat == CombatManager.Instance.CurrentCombatId;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _zhuChangShown = false;
        _zhuChangPending = false;
        _zhuChangCombat = null;
    }

    private bool ZhuChangApplies => Owner != null && CombatState != null
        && Pile?.Type == PileType.Hand && ShiJiTracker.AttacksPlayedThisTurn(Owner) == 0;

    private async Task RefreshZhuChang(object trigger)
    {
        var combat = CombatManager.Instance.CurrentCombatId;
        if (_zhuChangCombat != combat)
        {
            _zhuChangShown = false;
            _zhuChangCombat = combat;
        }
        if (!ZhuChangApplies)
        {
            _zhuChangShown = false;
            return;
        }
        if (_zhuChangShown || _zhuChangPending) return;
        _zhuChangPending = true;
        try
        {
            if (!await ZhuChangEffectVfx.Play(this, trigger)) return;
            if (ZhuChangApplies) _zhuChangShown = true;
        }
        finally { _zhuChangPending = false; }
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card == this) await RefreshZhuChang(card);
    }

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        await base.BeforeSideTurnStart(choiceContext, side, participants, combatState);
        if (side == CombatSide.Player) await RefreshZhuChang(combatState);
    }

    // 龙族同步怪兽：龙族 + 同步怪兽 标记
    private static Func<CardModel, bool> IsDragonSynchroCard
        => c => c.Tags.Contains(SelectTags.LongZu) && c.Tags.Contains(SelectTags.TongBuGuaiShou);

    private static LocString BuffPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_BEI_MAN_DE_CAN_HUAN_ZHUANG_BUFF");

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.CanHuanMoFa,
        SelectTags.ZhuChang,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.CanHuanMoFa),
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuChang),
    ];

    // 驻场：这个回合没打出过攻击卡的情况下，可以免费将卡打出
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card.Owner != this.Owner) return false;
        if (Owner == null || Pile?.Type != PileType.Hand) return false;
        if (ShiJiTracker.AttacksPlayedThisTurn(Owner) > 0) return false;
        modifiedCost = 0;
        return true;
    }

    // 一场战斗只能打出一次：本场战斗已经打出过 → 直接把这张卡变成"不可打出"
    protected override bool IsPlayable => !PlayedThisCombat;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 兜底：万一被效果绕过 IsPlayable 打出来，第二次也不结算
        if (PlayedThisCombat) return;

        // 「检索」1张「天杯龙」
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => c.Tags.Contains(SelectTags.TianBeiLong),
            p => p is SelectSpecialCardPool,
            1, source: this);

        // 之后，丢弃1张自身以外的手牌（FromHandForDiscard 只选不弃，弃牌显式调用 CardCmd.Discard）
        var discarded = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: c => c != this,
            source: this)).ToList();
        foreach (var card in discarded)
        {
            await CardCmd.Discard(choiceContext, card);
        }
    }

    // 打出后，返回手牌（一场战斗一次，按这张卡自己记）
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 向攻击卡计数器上报（驻场条件要用；多张卡重复上报有去重）
        ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);
        await RefreshZhuChang(cardPlay);

        if (cardPlay.Card != this) return;
        if (PlayedThisCombat) return;
        _playedCombat = CombatManager.Instance.CurrentCombatId;

        await CardPileCmd.Add(this, PileType.Hand);
        await RefreshZhuChang(cardPlay);
    }

    // 被消耗时：选择手牌1张龙族同调卡，使其伤害翻倍
    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        await base.AfterCardExhausted(choiceContext, card, causedByEthereal);
        if (card != this) return;
        await BuffDragonSynchro(choiceContext);
    }

    private async Task BuffDragonSynchro(PlayerChoiceContext choiceContext)
    {
        if (Owner == null || CombatState == null || CombatManager.Instance.IsOverOrEnding) return;
        var player = Owner;
        var combat = CombatState;
        var hand = PileType.Hand.GetPile(player);
        bool IsTarget(CardModel candidate) => candidate.Owner == player && candidate.CombatState == combat
            && candidate.Pile == hand && !candidate.HasBeenRemovedFromState && IsDragonSynchroCard(candidate);
        if (!hand.Cards.Any(IsTarget)) return;

        CardModel? target = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, BuffPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: IsTarget,
            // 被消耗触发不保证之后还会有本卡的打出结束通知。
            source: null!)).FirstOrDefault();

        if (target == null || CombatState != combat || player.Creature.CombatState != combat
            || CombatManager.Instance.IsOverOrEnding || !IsTarget(target)) return;

        // 修改实际打出读取的伤害，同时立即刷新卡面预览。
        target.DynamicVars.Damage.BaseValue *= 2m;
        target.DynamicVars.ClearPreview();
        target.UpdateDynamicVarPreview(CardPreviewMode.Normal, target.CurrentTarget, target.DynamicVars);
        var node = NCard.FindOnTable(target, PileType.Hand);
        if (node != null && GodotObject.IsInstanceValid(node)) node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
    }
}
