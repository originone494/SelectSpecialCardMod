using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
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

// 珠泪·俱舍（下级怪兽）
//   打出：造成20点伤害，从抽牌堆顶将3张卡送入弃牌堆
//   特殊效果（右键手牌发动）：消耗弃牌堆的1张卡 → 这张卡在这个回合可以免费打出
//   送墓：从抽牌堆顶将3张卡送入弃牌堆
public class ZhuLeiJuSheCard : SelectSpecialBaseCard, ISongMuCard, ISpecialEffectCard
{
    public ZhuLeiJuSheCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 20 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(20m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.SongMu,
        SelectTags.JuShe,
        SelectTags.ZhuLeiXiaJiGuaiShou,
        SelectTags.SpecialEffect,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.JuShe),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 造成20点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);

        // ② 从抽牌堆顶将3张卡送入弃牌堆
        await SelectToolCmd.DuiMu(choiceContext, Owner, 3);
    }

    // 特殊效果发动条件：弃牌堆至少有1张卡可消耗
    public bool CanActivateSpecialEffect(Player player)
        => PileType.Discard.GetPile(player).Cards.Count > 0;

    // 特殊效果：消耗弃牌堆的1张卡，然后这张卡在这个回合可以免费打出
    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        var discardPile = PileType.Discard.GetPile(player);
        if (discardPile.Cards.Count == 0) return false;

        // 由玩家自己选（范围 1~1，发动后必须选一张）：无有效选项时防御性退出
        var chosen = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            discardPile.Cards.ToList(),
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1) with { RequireManualConfirmation = true })).FirstOrDefault();
        if (chosen == null || chosen.Owner != player || chosen.Pile?.Type != PileType.Discard) return false;
        SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();

        var payment = await CardCmd.Exhaust(choiceContext, chosen);
        if (payment?.success != true) return false;
        EnergyCost.SetThisTurn(0);
        return true;
    }

    // 送墓：从抽牌堆顶将3张卡送入弃牌堆（触发效果而非打出）
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await SelectToolCmd.DuiMu(ctx, player, 3);
    }
}
