using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 效果就是「失去所有增益。获得5点力量。」
// 施加的「奥利哈刚的结界」是隐形标记能力（无任何效果、不显示），
// 仅供「奥利哈刚第二结界」的免伤条件判定使用。
public class AoLiHaGangDeJieJieCard : SelectSpecialBaseCard
{
    public AoLiHaGangDeJieJieCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<StrengthPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 失去所有增益
        foreach (var power in Owner.Creature.Powers.ToList())
        {
            if (power.Type == PowerType.Buff)
                await PowerCmd.Remove(power);
        }

        // 获得5点力量
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 5, Owner.Creature, this);

        // 施加隐形标记，供第二结界判定
        await PowerCmd.Apply<AoLiHaGangDeJieJiePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}
