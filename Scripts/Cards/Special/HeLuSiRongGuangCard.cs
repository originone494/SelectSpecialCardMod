using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 荷鲁斯·荣光：造成30点伤害。
// 特殊效果（右键手牌发动）：丢弃自身和1张手牌，将1张「王之棺」加入手牌。
// 拥有能力「王之棺」的场合，这张卡可以免费打出。
public class HeLuSiRongGuangCard : SelectSpecialBaseCard, ISpecialEffectCard
{
    public HeLuSiRongGuangCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(30m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.HeLuSiGuaiShou,
        SelectTags.SpecialEffect,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.HeLuSiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    // 拥有能力「王之棺」时，战斗中费用为0
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (!HorusFreePlay.IsAvailable(card)) return false;   // 卡名一回合一次
        modifiedCost = 0m;
        return true;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        HorusFreePlay.Consume(this);
        if (cardPlay.Target == null) return;

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 发动条件：手牌中存在自身以外可以丢弃的牌（没有则右键不发动）
    public bool CanActivateSpecialEffect(Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        return hand != null && hand.Cards.Any(c => c != this);
    }

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        // 丢弃1张手牌（玩家选择，必须选1张；排除自身）
        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: c => c != this,
            source: this)).ToList();
        foreach (var card in selected)
        {
            await CardCmd.Discard(choiceContext, card);
        }

        // 丢弃自身
        await CardCmd.Discard(choiceContext, this);

        // 将1张「王之棺」加入手牌
        CardModel wangZhiGuan = player.Creature.CombatState!.CreateCard<WangZhiGuanCard>(player);
        var addResult = await CardPileCmd.AddGeneratedCardToCombat(wangZhiGuan, PileType.Hand, player);
        CardCmd.PreviewCardPileAdd(addResult, 2f);
    }
}
