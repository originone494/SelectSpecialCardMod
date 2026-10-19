using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 小世界：消耗1张手牌A。之后，消耗抽牌堆的1张卡B，
// 若B的费用·类型·稀罕度与A只有一个相同，
// 则「检索」1张费用·类型·稀罕度只有一个是和B相同的卡。
public class XiaoShiJieCard : SelectSpecialBaseCard
{
    private static LocString ExhaustPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_EXHAUST");

    public XiaoShiJieCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var combat = CombatState;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && !CombatManager.Instance.IsOverOrEnding && player.Creature.IsAlive;
        if (!CanContinue()) return;

        // ① 消耗1张手牌A（强制选1张，选不了则结束）
        var hand = PileType.Hand.GetPile(player);
        bool IsInPile(CardModel card, CardPile pile) => card.Owner == player && card.Pile == pile
            && !card.HasBeenRemovedFromState;
        if (hand.Cards.Count == 0) return;

        CardModel? selectedA = (await CardSelectCmd.FromHand(prefs: CardPrefs(this, ExhaustPrompt, 1, 1), context: choiceContext, player: player, filter: null, source: this)).FirstOrDefault();

        if (!CanContinue() || selectedA == null || !IsInPile(selectedA, hand)) return;
        var cardA = selectedA;
        var exhaustedA = await CardCmd.Exhaust(choiceContext, cardA);
        if (exhaustedA?.success != true || !CanContinue()) return;

        // ② 消耗抽牌堆的1张卡B（强制选1张，抽牌堆为空则停止处理）
        var drawPile = PileType.Draw.GetPile(player);
        if (drawPile.Cards.Count == 0) return;
        var selectedB = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, drawPile.Cards.ToList(), player, CardPrefs(this, SelectionScreenPrompt, 1, 1))).ToList();
        if (!CanContinue() || selectedB.Count == 0 || !IsInPile(selectedB[0], drawPile)) return;
        var cardB = selectedB[0];
        var exhaustedB = await CardCmd.Exhaust(choiceContext, cardB);
        if (exhaustedB?.success != true || !CanContinue()) return;

        // ③ 判断 A 与 B 的费用·类型·稀有度是否恰好1个相同
        if (CountSameProperties(cardA, cardB) != 1) return;

        // ④ 检索1张与 B 恰好1个属性相同的卡，不限制角色卡池
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            c => CountSameProperties(c, cardB) == 1,
            amount: 1, source: this, minAmount: 1, requireManualConfirmation: true);
    }

    // 统计两张卡在 费用 / 类型 / 稀有度 上相同的个数
    private static int CountSameProperties(CardModel a, CardModel b)
    {
        int same = 0;
        if (a.EnergyCost.GetWithModifiers(CostModifiers.None) == b.EnergyCost.GetWithModifiers(CostModifiers.None)) same++;
        if (a.Type == b.Type) same++;
        if (a.Rarity == b.Rarity) same++;
        return same;
    }
}
