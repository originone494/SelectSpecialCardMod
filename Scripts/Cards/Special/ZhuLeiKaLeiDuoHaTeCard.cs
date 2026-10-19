using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·卡雷多哈特（融合怪兽）
//   打出：造成15点伤害
//   驻场：登场时、或「珠泪怪兽」触发送墓时 → 给予所有敌人2层虚弱
//   送墓：同名卡一回合一次 → 打出这张卡 → 加入手牌 → 「检索」并丢弃1张「珠泪」卡
public class ZhuLeiKaLeiDuoHaTeCard : SelectSpecialBaseCard, ISongMuCard, ISongMuObserver
{
    public ZhuLeiKaLeiDuoHaTeCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 15 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.RongHeGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiGuaiShou,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成15点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    // 驻场的登场分支：每次因效果加入手牌时生效，抽牌不算。
    // 不走有同名战斗次数限制的「登场」关键字后注，文案没有给此驻场分支次数限制。
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card != this || oldPileType == PileType.Hand || Pile?.Type != PileType.Hand
            || DrawWindowPatch.IsDrawing(this)) return;
        if (CombatState?.HittableEnemies.Any() != true) return;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(this, async () =>
        {
            if (!await ZhuChangEffectVfx.Play(this, card)) return;
            if (CombatState != combat || Pile?.Type != PileType.Hand) return;
            await GiveWeakToAllEnemies(new BlockingPlayerChoiceContext());
        }, () => Pile?.Type == PileType.Hand);
    }

    // 驻场的送墓分支：本卡在手上时，给予所有敌人2层虚弱。
    // 自身送墓时不在手牌；回手的登场分支已生效，不追溯响应自身送墓的末尾广播。
    public async Task OnSongMu(PlayerChoiceContext ctx, Player player, CardModel trigger)
    {
        if (player != Owner || trigger == this) return;
        if (!trigger.Tags.Contains(SelectTags.ZhuLeiGuaiShou)) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;

        if (CombatState?.HittableEnemies.Any() != true) return;
        if (!await ZhuChangEffectVfx.Play(this, trigger)) return;
        if (CombatState == null || Pile?.Type != PileType.Hand) return;
        await GiveWeakToAllEnemies(ctx);
    }

    private async Task GiveWeakToAllEnemies(PlayerChoiceContext ctx)
    {
        if (CombatState == null) return;
        foreach (Creature enemy in CombatState.HittableEnemies.ToList())
        {
            await PowerCmd.Apply<WeakPower>(ctx, enemy, 2, Owner.Creature, this);
        }
    }

    // 送墓：同名卡一回合一次 → 打出这张卡 → 将这张卡加入手牌
    //       → 「检索」并丢弃1张「珠泪」卡
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        // 同名卡一回合一次（按卡名：多张同名卡共享一次）
        string onceKey = PerTurnOnce.Key("SongMu", Id.ToString());
        if (PerTurnOnce.IsUsed(player, onceKey)) return;
        // 先记账：连锁里再丢掉一张同名卡时，它的送墓不会二次发动
        PerTurnOnce.Mark(player, onceKey);

        // ① 打出这张卡（随机敌人）
        Creature? enemy = player.RunState.Rng.CombatTargets.NextItem(player.Creature.CombatState!.HittableEnemies);
        if (enemy != null)
        {
            await SongMuHook.AutoPlayFromDiscard(ctx, this, enemy);
        }

        // ② 将这张卡加入手牌
        await CardPileCmd.Add(this, PileType.Hand);

        // ③ 「检索」并丢弃1张「珠泪」卡；通用检索入口会排除额外怪兽。
        await SelectToolCmd.RetrieverCard(ctx, player, ZhuLeiFilter.IsCard,
            p => p is SelectSpecialCardPool, 1, true, source: this,
            minAmount: 1, requireManualConfirmation: true);
    }
}
