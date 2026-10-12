using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异解·福音：获得被动（每回合免费打出1张异解怪兽；回合开始时灌注除外堆并可打出1张异解怪兽）
public class YiJieFuYinCard : SelectSpecialBaseCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieMoXian),
    ];

    public YiJieFuYinCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieMoXian];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 获得被动能力
        await PowerCmd.Apply<YiJieFuYinPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}