using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 吞食百万的暴食兽：向除外堆加入5张随机卡，之后造成等同于消耗堆数量的伤害。
public class TunShiBaiWanDeBaoShiShouCard : SelectSpecialBaseCard
{
    private const int _randomCards = 5;

    public TunShiBaiWanDeBaoShiShouCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target == null) return;

        // 向除外堆加入5张随机卡
        await SelectToolCmd.AddRandomCardsToPile(Owner, Entry.ChuWaiPile, _randomCards);

        // 之后造成等同于消耗堆数量的伤害
        int exhaustCount = Entry.ChuWaiPile.GetPile(Owner).Cards.Count;
        await DamageCmd.Attack(exhaustCount)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(choiceContext);
    }
}