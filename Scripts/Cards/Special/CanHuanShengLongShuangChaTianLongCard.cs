using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「燦幻昇龍バイデント・ドラギオン」（Sangenpai Bident Dragion，站内 sc_name「灿幻升龙 双戟天龙」）：
//   打出：造成20点伤害
//   登场：从消耗堆将1张「天杯龙」或「灿幻怪兽」加入手牌
//   时机（一回合一次，右键弃牌堆按钮发动）：攻击牌打出3次以上的回合，
//         将这张卡从弃牌堆加入手牌，本回合可以免费打出，之后可以从手牌消耗1张技能牌
public class CanHuanShengLongShuangChaTianLongCard : SelectSpecialBaseCard, IDengChangCard, IShiJiCard
{
    public CanHuanShengLongShuangChaTianLongCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(20m, ValueProp.Move),
    ];

    public override int StarLevel => 7;

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.LongZu,
        ..StarTags,
        SelectTags.TiaoZheng,
        SelectTags.CanHuanGuaiShou,
        SelectTags.DengChang,
        SelectTags.TongBu,
        SelectTags.ShiJi,
        SelectTags.ExtraMonster,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
        ..StarHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.TongBu),
        HoverTipFactory.FromKeyword(SelectKeywords.TiaoZheng),
        HoverTipFactory.FromKeyword(SelectKeywords.CanHuanGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成20点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 登场：从消耗堆将1张「天杯龙」或「灿幻怪兽」加入手牌
    private static LocString DengChangPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_CAN_HUAN_SHENG_LONG_DENG_CHANG");

    private static bool IsTianBeiLongOrCanHuanGuaiShou(CardModel c)
        => c.Tags.Contains(SelectTags.TianBeiLong) || c.Tags.Contains(SelectTags.CanHuanGuaiShou);

    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(IsTianBeiLongOrCanHuanGuaiShou)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefs(this, DengChangPrompt, 1, 1),
            filter: IsTianBeiLongOrCanHuanGuaiShou)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
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
           && _shiJiUsedTurn != ShiJiKey(player);

    private static (CombatId? Combat, int Turn) ShiJiKey(Player player)
        => (CombatManager.Instance.CurrentCombatId, player.PlayerCombatState?.TurnNumber ?? 0);

    // 本回合免费打出：时机发动后，这张卡在本回合费用为 0
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (Owner == null || _freePlayTurn != ShiJiKey(Owner)) return false;
        modifiedCost = 0m;
        return true;
    }

    // 时机发动：将这张卡从弃牌堆加入手牌，本回合可以免费打出，之后可以消耗1张技能牌（0~1，可选）
    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        _shiJiUsedTurn = ShiJiKey(player);
        await CardPileCmd.Add(this, PileType.Hand);
        _freePlayTurn = ShiJiKey(player);

        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, CardSelectorPrefs.ExhaustSelectionPrompt, 0, 1),
            context: ctx,
            player: player,
            filter: c => c.Type == CardType.Skill,
            source: this)).ToList();
        foreach (var card in selected)
        {
            await CardCmd.Exhaust(ctx, card);
        }
    }
}
