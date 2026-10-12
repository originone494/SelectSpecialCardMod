using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 荷鲁斯·先导哈比：获得16点格挡，从弃牌堆或消耗堆将2张卡加入手牌。
// 是/否选择：「是」从消耗堆选、「否」从弃牌堆选；
// 拥有能力「王之棺」的场合，这张卡免费打出。
public class HeLuSiXianDaoHaBiCard : SelectSpecialBaseCard
{
    private const string _retrieveCardCount = "RetrieveCardCount";

    public HeLuSiXianDaoHaBiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(16m, ValueProp.Move),
        new DynamicVar(_retrieveCardCount, 2),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.HeLuSiGuaiShou,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.HeLuSiGuaiShou),
    ];

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
        // 获得16点格挡
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Move, cardPlay);

        // 「是」→ 从消耗堆选，「否」→ 从弃牌堆选；只有一堆有卡时直接从那堆选（不询问）。
        // 将2张卡加入手牌（牌堆不足2张时按实际数量选）——通用工具会按实际张数收缩。
        int want = DynamicVars[_retrieveCardCount].IntValue;
        var selectedCards = await SelectToolCmd.PickFromTwoPiles(
            choiceContext, Owner, _ => true,
            PileType.Exhaust, PileType.Discard,
            ChoicePrompt, SelectionPrompt,
            this, minSelect: want, maxSelect: want);

        foreach (var card in selectedCards)
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    private static LocString ChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_XIAN_DAO_HA_BI_CHOICE");
    private static LocString SelectionPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_XIAN_DAO_HA_BI_SELECT");
}
