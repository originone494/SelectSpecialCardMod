using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「怠慢之壶」（Pot of Sloth）：战斗开始时加入手牌，依据敌人数量抽取卡牌。
// "战斗开始时加入手牌"：在首回合起手抽牌之前，把自己从抽牌堆移入手牌（不占用起手抽牌张数）。
// 引擎在每个回合设置时、抽牌之前广播 BeforeHandDraw，这里只在首回合（TurnNumber == 1）行动。
public class DaiManZhiHuCard : SelectSpecialBaseCard
{
    public DaiManZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    // 首回合起手抽牌前：把自己移入手牌
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (Owner == null || player != Owner) return;
        if (player.PlayerCombatState?.TurnNumber != 1) return;
        if (Pile?.Type != PileType.Draw) return;

        await CardPileCmd.Add(this, PileType.Hand);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 依据敌人数量抽取卡牌
        int enemyCount = CombatState!.HittableEnemies.Count;
        if (enemyCount > 0)
        {
            await CardPileCmd.Draw(choiceContext, enemyCount, Owner);
        }
    }
}
