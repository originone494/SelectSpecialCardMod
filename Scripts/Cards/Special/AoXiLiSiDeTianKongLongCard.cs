using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「奥西里斯之天空龙」（Slifer the Sky Dragon）：
// 对所有敌人造成等同于手牌数×10的伤害，之后，击杀生命值在20及以下的敌人。
public class AoXiLiSiDeTianKongLongCard : SelectSpecialBaseCard
{
    private const int _damagePerHandCard = 10;
    private const int _executeThreshold = 20;

    public AoXiLiSiDeTianKongLongCard() : base(3, CardType.Attack, CardRarity.Ancient, TargetType.AllEnemies)
    {
    }

    // 消耗：打出后进入消耗堆（复苏的天空神的时机条件「奥西里斯的天空龙」存在消耗堆 依赖于此）
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 对所有敌人造成等同于手牌数×10的伤害（+1 是打出的这张本身）
        int handCount = PileType.Hand.GetPile(Owner).Cards.Count + 1;
        await DamageCmd.Attack(handCount * _damagePerHandCard)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(CombatState!)
            .WithHitFx("vfx/vfx_attack_lightning")
            .Execute(choiceContext);

        // 击杀生命值在 20 及以下的所有敌人（伤害结算后再判定）
        var executeTargets = CombatState!.HittableEnemies
            .Where(e => e.IsAlive && e.CurrentHp <= _executeThreshold)
            .ToList();
        foreach (var enemy in executeTargets)
        {
            await CreatureCmd.Kill(enemy);
        }
    }
}
