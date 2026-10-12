using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 「羊衍生物」：无法打出，抽到时消耗，按费用次数获得4点格挡，然后抽1张牌。
// 由「替罪羊」按费用 1~4 生成，本卡自身不通过任何随机获取途径发放（Token 稀有度 + 奖励候选过滤）。
public class YangYanShengWuCard : SelectSpecialBaseCard
{
    private const string _repeatKey = "Repeat";

    public YangYanShengWuCard() : base(0, CardType.Attack, CardRarity.Token, TargetType.Self)
    {
    }

    // 无法打出
    protected override bool IsPlayable => false;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        // 卡面直接显示了每次获得的格挡数值（{Block:diff()}），属于「看得到就声明」，
        // 因此这里用 BlockVar 而不是普通 DynamicVar。
        new BlockVar(4m, ValueProp.Move),      // 每次获得的格挡
        new DynamicVar(_repeatKey, 1m),        // 重复次数，由替罪羊按费用设置
    ];

    // 抽到时触发（该钩子对每张被抽的卡都会调用，必须判断是本卡）
    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card != this) return;
        if (Owner == null) return;

        await CardCmd.Exhaust(choiceContext, this);
        for (int i = 0; i < DynamicVars[_repeatKey].IntValue; i++)
        {
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, null);
        }
        await CardPileCmd.Draw(choiceContext, 1, Owner);
    }
}
