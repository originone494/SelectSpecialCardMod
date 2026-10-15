using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class YiXiangMingTianShiZhiShengCard : YiXiangMingVoiceCard
{
    protected override Type CounterpartType => typeof(YiXiangMingEMoZhiShengCard);
    protected override bool CounterOnHealing => true;
    public override int PendulumScale => 3;

    protected override HashSet<CardTag> CanonicalTags => [..base.CanonicalTags, SelectTags.KeDu3];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        ..base.AdditionalHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.KeDu3),
    ];
}
