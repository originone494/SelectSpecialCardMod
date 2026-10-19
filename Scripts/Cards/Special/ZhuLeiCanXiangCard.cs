using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·残响（陷阱）
//   打出：丢弃1张攻击卡，对目标敌人造成6点伤害、获得7点格挡、给予目标2层虚弱
//   送墓：从消耗堆将1张「珠泪怪兽」卡加入手牌
public class ZhuLeiCanXiangCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiCanXiangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6m, ValueProp.Move),
        new BlockVar(7m, ValueProp.Move),
        new PowerVar<WeakPower>(2),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.SongMu,
        SelectTags.ZhuLeiXianJing,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<WeakPower>(),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
    ];

    private bool CanDiscardAttack => Owner != null && CombatState != null
        && PileType.Hand.GetPile(Owner).Cards.Any(c => c.Owner == Owner && c.Type == CardType.Attack);

    protected override bool IsPlayable => CanDiscardAttack;

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
        => card != this || CanDiscardAttack;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!CanDiscardAttack || cardPlay.Target is not { IsAlive: true } enemy) return;
        var combat = CombatState;

        var discarded = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: c => c.Owner == Owner && c.Type == CardType.Attack,
            source: this)).FirstOrDefault();
        if (discarded == null || discarded.Owner != Owner || discarded.Type != CardType.Attack
            || discarded.Pile?.Type != PileType.Hand || CombatState != combat
            || CombatManager.Instance.IsOverOrEnding) return;

        // 弃牌事件发生在送墓连锁之前，即使代价牌被连锁回收，也仍算实际支付。
        bool paid = false;
        var history = CombatManager.Instance.History;
        void OnHistoryChanged()
        {
            if (history.Entries.LastOrDefault() is CardDiscardedEntry entry
                && ReferenceEquals(entry.Card, discarded)) paid = true;
        }
        history.Changed += OnHistoryChanged;
        try { await CardCmd.Discard(choiceContext, discarded); }
        finally { history.Changed -= OnHistoryChanged; }
        if (!paid || CombatState != combat || CombatManager.Instance.IsOverOrEnding
            || !Owner.Creature.IsAlive) return;

        if (enemy.IsAlive)
            await CreatureCmd.Damage(choiceContext, enemy, DynamicVars.Damage.BaseValue,
                ValueProp.Move, Owner.Creature, this, cardPlay);
        if (CombatManager.Instance.IsOverOrEnding || !Owner.Creature.IsAlive) return;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
        if (enemy.IsAlive && !CombatManager.Instance.IsOverOrEnding)
            await PowerCmd.Apply<WeakPower>(choiceContext, enemy, DynamicVars.Weak.IntValue, Owner.Creature, this);
    }

    // 送墓：从消耗堆将1张「珠泪怪兽」卡加入手牌（按效果文本口径 = 含融合怪兽）
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(ZhuLeiFilter.IsMonster)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1),
            filter: ZhuLeiFilter.IsMonster)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }

}
