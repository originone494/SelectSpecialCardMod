using System.Collections.Generic;
using System.Threading.Tasks;
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

public class ZhuLeiXiaoMeiCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiXiaoMeiCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.Self)
    {
    }

    // 只用于声明「这张牌能给格挡」：原版菲涅尔透镜的灵活(Nimble)附魔靠 CardModel.GainsBlock
    // 判定资格，而基类按 CanonicalVars 里是否存在 BlockVar 自动推导该属性。
    // 实际格挡值仍在 OnPlay 里直接传入——BlockVar 的附魔加成只作用于预览，不会与实际值重复计算。
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(10m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiRongHe,
        SelectTags.YingDui,
    ];

    // 卡面不再注入「珠泪融合」关键字（送墓文案直接写在描述里）；机制判据走标签，悬停里保留关键字说明
    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiRongHe),
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        // 「应对」只在这里出现：卡面靠"应对：…"纯文本，悬停时给出这个关键字的说明
        HoverTipFactory.FromKeyword(SelectKeywords.YingDui),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 获得10点格挡
        await CreatureCmd.GainBlock(Owner.Creature, 10m, ValueProp.Move, cardPlay);
    }

    // 「受到敌人攻击前，将这张卡打出」——参考天下独步的大义贼的 BeforeDamageReceived 写法。
    // 差别在于大义贼是"从弃牌堆返回抽牌堆 + 获得格挡"，本卡是"从手牌打出"（走自己的 OnPlay）。
    // 打出后它离开手牌、结算完落进弃牌堆，所以同一份拷贝不会在一次攻击里重复触发；
    // 配合「保留」关键字，没被打出时会留在手上等下一次攻击。
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (cardSource != null) return;                 // 只要"受到敌人攻击"，排除卡牌/能力造成的伤害
        if (dealer == null || !dealer.IsMonster) return;

        // AutoPlay 只在 card.Pile == null 时才把牌搬进 Play（CardCmd.cs:114-117），
        // 手牌里的卡必须先自己搬——与官方 CardPileCmd.AutoPlayFromDrawPile 同款做法。
        await CardPileCmd.Add(this, PileType.Play);
        await CardCmd.AutoPlay(choiceContext, this, null);

        // 应对的额外部分：从抽牌堆顶将3张卡送入弃牌堆
        await SelectToolCmd.DuiMu(choiceContext, Owner, 3);
    }

    // 送墓：进行一次珠泪融合；触发过珠泪融合的情况，将这张卡打出
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        // 触发过珠泪融合（同名卡一场战斗一次已用掉）→ 将这张卡打出（本卡目标为自身，无需敌人）
        if (ZhuLeiFusion.HasFusedThisCombat(this))
        {
            await SongMuHook.AutoPlayFromDiscard(ctx, this, null);
            return;
        }

        // 进行一次珠泪融合
        await ZhuLeiFusion.TryFusion(ctx, player, this);
    }
}
