using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 命运怀抱的爆弹：应对——消耗手牌的这张卡，获得10点格挡，对进行攻击的敌人造成等额伤害，
// 卡组存在「现世与冥界的逆转」的场合，自身获得与伤害等额的格挡。
// 关键字：不可打出、保留（无消耗）。
public class MingYunHuaiBaoDeBaoDanCard : SelectSpecialBaseCard
{
    public MingYunHuaiBaoDeBaoDanCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    // 明确声明「不可被格挡类附魔」：本卡的格挡在「敌人攻击前」的反应里获得，
    // CreatureCmd.GainBlock 只能传 null cardPlay，而引擎取 cardSource = cardPlay?.Card，
    // 所以灵活(Nimble)的 +格挡 加不上。若沿用基类的自动判定（有 BlockVar → true），
    // 菲涅尔透镜会把它附上、卡面还会预览出附魔后的数值，但实际给的格挡不变——显示与结算不符。
    public override bool GainsBlock => false;

    // 卡面直接写了「获得10点格挡」，格挡数值是看得见的，因此声明 Block 动态变量。
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(10m, ValueProp.Move),
    ];

    // 这张卡无法打出
    protected override bool IsPlayable => false;

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Unplayable, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
    ];

    // 敌人攻击前：消耗手牌的这张卡，获得10点格挡，对进行攻击的敌人造成等额伤害
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (CombatState == null) return;
        if (cardSource != null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (dealer == null) return;
        if (!dealer.IsMonster) return;

        // 消耗手牌的这张卡
        await CardCmd.Exhaust(choiceContext, this);

        // 获得10点格挡
        await CreatureCmd.GainBlock(Owner.Creature, 10m, ValueProp.Move, null);

        // 对进行攻击的敌人造成等额伤害
        await CreatureCmd.Damage(choiceContext, dealer, amount, ValueProp.Move, Owner.Creature, this, null);

        // 卡组存在「现世与冥界的逆转」时，自身获得与伤害等额的格挡
        if (Owner.Deck.Cards.Any(c => c is XianShiYuMingJieDeNiZhuanCard))
        {
            await CreatureCmd.GainBlock(Owner.Creature, amount, ValueProp.Move, null);
        }
    }
}
