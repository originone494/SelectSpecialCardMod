using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 邻家割草：将抽牌堆的所有卡送入弃牌堆。
// 整堆搬运期间挂起「送墓」触发，搬运完再逐一结算（与 yuno-mod 原版同款，
// 否则某张送墓卡在搬运中途把快照里的牌抽走，循环到它时又会把它从手牌送入弃牌堆）。
public class LinJiaGeCaoCard : SelectSpecialBaseCard
{
    public LinJiaGeCaoCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 将抽牌堆的所有卡送入弃牌堆
        var drawPile = PileType.Draw.GetPile(Owner);
        var cards = drawPile.Cards.ToList();

        await SongMuHook.DeferTriggers(() => CardCmd.Discard(choiceContext, cards));
    }
}
