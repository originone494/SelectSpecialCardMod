using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「燦幻超龍トランセンド・ドラギオン」（Sangenpai Transcendent Dragion，站内 sc_name「灿幻超龙 三极天龙」）：
//   打出：造成30点伤害
//   登场（一场战斗一次，因效果加入手牌时触发）：将手牌所有的攻击卡打出，打出后立刻返回手牌
//   时机（一回合一次，右键弃牌堆按钮发动）：攻击牌打出3次以上的回合，
//         将这张卡从弃牌堆加入手牌，本回合可以免费打出，之后可以丢弃1张手牌
public class CanHuanChaoLongSanJiTianLongCard : SelectSpecialBaseCard, IDengChangCard, IShiJiCard, IExtraDeckSummonCard
{
    public CanHuanChaoLongSanJiTianLongCard() : base(3, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(30m, ValueProp.Move),
    ];

    public override int StarLevel => 10;

    public bool CanSummonFromExtraDeck(Player player) => SynchroSummon.CanSummon(this, player);
    public Task<bool> PaySummonCost(PlayerChoiceContext context, Player player)
        => SynchroSummon.PayMaterials(context, this, player);

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.LongZu,
        ..StarTags,
        SelectTags.CanHuanGuaiShou,
        SelectTags.TongBu,
        SelectTags.TongBuGuaiShou,
        SelectTags.DengChang,
        SelectTags.ShiJi,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
        ..StarHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.TongBu),
        HoverTipFactory.FromKeyword(SelectKeywords.TongBuGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.CanHuanGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成30点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 登场：将手牌所有的攻击卡打出，打出后立刻返回手牌
    // 使用自动打出的资格；CanPlay 还检查手动出牌资源，不适用于这里。
    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        var hand = PileType.Hand.GetPile(player).Cards
            .Where(c => c.Type == CardType.Attack && !c.Keywords.Contains(CardKeyword.Unplayable))
            .ToList();
        if (hand.Count == 0) return;

        // 快照后逐张：打出 → 立刻返回手牌 → 下一张
        foreach (var card in hand)
        {
            if (card.Pile?.Type != PileType.Hand) continue;
            if (CombatManager.Instance.IsOverOrEnding) return;
            if (!MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(player.Creature.CombatState!, card,
                out _, AutoPlayType.Default)) continue;
            Creature? target = player.RunState.Rng.CombatTargets.NextItem(player.Creature.CombatState!.HittableEnemies);
            if (target == null) return;

            await CardPileCmd.Add(card, PileType.Play);
            await CardCmd.AutoPlay(ctx, card, target);

            // 打出后立刻返回手牌（只回落在弃牌堆的；被消耗等其它去向不回）
            if (card.Pile?.Type == PileType.Discard)
            {
                await CardPileCmd.Add(card, PileType.Hand);
            }
        }
    }

    // 广播钩子：向时机计数器上报本回合打出的攻击卡
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);
    }

    // 时机：一回合一次（按这张卡自己记）；只能从弃牌堆发动；条件：本回合打出过3张以上攻击牌
    private (CombatId? Combat, int Turn)? _shiJiUsedTurn;
    public IEnumerable<PileType> ShiJiPiles => [PileType.Discard];

    // 时机发动后「本回合免费打出」的标记（按战斗+回合号记录，回合结束自动过期）
    private (CombatId? Combat, int Turn)? _freePlayTurn;

    public bool CanActivateShiJi(Player player)
        => ShiJiTracker.AttacksPlayedThisTurn(player) >= 3
           && _shiJiUsedTurn != ShiJiTrackerKey(player);

    private static (CombatId? Combat, int Turn) ShiJiTrackerKey(Player player)
        => (CombatManager.Instance.CurrentCombatId, player.PlayerCombatState?.TurnNumber ?? 0);

    // 本回合免费打出：时机发动后，这张卡在本回合费用为 0
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (Owner == null || _freePlayTurn != ShiJiTrackerKey(Owner)) return false;
        modifiedCost = 0m;
        return true;
    }

    // 时机发动：将这张卡从弃牌堆加入手牌，本回合可以免费打出，之后可以丢弃1张手牌（0~1，可选）
    // 注意：FromHandForDiscard 只负责选牌，弃牌必须显式调用 CardCmd.Discard（原版杂技同款写法）
    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        _shiJiUsedTurn = ShiJiTrackerKey(player);
        await CardPileCmd.Add(this, PileType.Hand);
        _freePlayTurn = ShiJiTrackerKey(player);

        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 0, 1),
            context: ctx,
            player: player,
            filter: null,
            // 时机独立于普通打出，选择完成后立即恢复手牌节点。
            source: null!)).ToList();
        foreach (var card in selected)
        {
            await CardCmd.Discard(ctx, card);
        }
    }
}
