using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class YiXiangMingEMoZhiShengCard : YiXiangMingVoiceCard
{
    protected override Type CounterpartType => typeof(YiXiangMingTianShiZhiShengCard);
    protected override bool CounterOnHealing => false;
    public override int PendulumScale => 5;

    protected override HashSet<CardTag> CanonicalTags => [..base.CanonicalTags, SelectTags.KeDu5];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        ..base.AdditionalHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.KeDu5),
    ];
}
