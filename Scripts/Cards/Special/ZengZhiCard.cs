using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class ZengZhiCard : SelectSpecialBaseCard
{
    public ZengZhiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 消耗 → 虚无：打出后回手，回合结束时（还在手上）才被虚无消耗掉
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 打出后将这张卡返回手牌。
        // OnPlayWrapper 只在卡仍位于 Play 堆时才把它送去结果堆（CardModel.cs:1989-2004），
        // 所以先把它搬回手牌，结算完就不会再被送去弃牌堆/消耗堆。
        await CardPileCmd.Add(this, PileType.Hand);

        if (cardPlay.Target?.Monster == null || CombatState == null) return;

        var source = cardPlay.Target;
        var monster = source.Monster.CanonicalInstance.ToMutable();

        // 落点：优先占空闲槽位；入场后强制检查"不许挡住任何敌人"（见 CopySpawnSlot 注释）
        string? slot = CopySpawnSlot.PickSlot(CombatState);

        var copy = await CreatureCmd.Add(
            monster,
            CombatState,
            CombatSide.Enemy,
            slot);

        CopySpawnSlot.EnsureClearOf(copy, source);

        await CreatureCmd.SetMaxHp(copy, source.MaxHp);
        await CreatureCmd.SetCurrentHp(copy, source.CurrentHp);
        await CreatureCmd.Stun(copy);

        // 链接能力挂在**复制品自己**身上（Owner = 复制品），不需要再持有引用
        var power = ModelDb.Power<ZengZhiPower>().ToMutable();
        await PowerCmd.Apply(choiceContext, power, copy, 1, Owner.Creature, this);
    }
}
