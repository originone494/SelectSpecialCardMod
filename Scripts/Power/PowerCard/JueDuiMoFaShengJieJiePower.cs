using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Power;

// 绝对魔法圣结界（能力）：
//  回合开始时：① 若没有人工制品则获得1层；② 卡组里有「圣结界的巫女」则 8 伤×3 / 清自身减益 / 1层缓冲；
//              ③ 按"拥有的能力数量"累计结算（自身算1；编号 ≤ 数量 的条目全部生效，上限 4）
//  回合结束时：按手牌中存在的卡牌类型结算（攻击/技能/能力三种各自独立判定）
[RegisterPower]
public class JueDuiMoFaShengJieJiePower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    private static LocString AddOnePrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_JUE_DUI_MO_FA_ADD_ONE");
    private static LocString ExhaustOnePrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_JUE_DUI_MO_FA_EXHAUST_ONE");
    private static LocString DiscardAnyPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_JUE_DUI_MO_FA_DISCARD_ANY");
    private static LocString FromExhaustPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_JUE_DUI_MO_FA_FROM_EXHAUST");

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player) return;
        if (Owner.CombatState == null) return;

        Flash();
        var ctx = choiceContext;

        // ① 没有人工制品则获得 1 层
        if (!Owner.HasPower<ArtifactPower>())
        {
            await PowerCmd.Apply<ArtifactPower>(ctx, Owner, 1, Owner, null);
        }

        // ② 卡组里存在「圣结界的巫女」时的三条件效果
        if (HasMaidenInDeck(player))
        {
            for (int i = 0; i < 3; i++)
            {
                Creature? enemy = player.RunState.Rng.CombatTargets.NextItem(Owner.CombatState.HittableEnemies);
                if (enemy == null) break;
                await CreatureCmd.Damage(ctx, enemy, 8m, ValueProp.Move, Owner, null, null);
            }

            foreach (var power in Owner.Powers.Where(p => p.Type == PowerType.Debuff).ToList())
            {
                await PowerCmd.Remove(power);
            }

            await PowerCmd.Apply<BufferPower>(ctx, Owner, 1, Owner, null);
        }

        // ③ 按拥有的能力数量"累计"适用（自身算 1 层；编号 ≤ 能力数量 的条目全部结算，上限 4）
        int powerCount = Math.Min(Owner.Powers.Count(), 4);
        var drawPile = PileType.Draw.GetPile(player);

        // [1] 可以从抽牌堆选择 1 张卡加入手牌
        if (powerCount >= 1)
        {
            foreach (var card in await CardSelectCmd.FromCombatPile(
                         ctx, drawPile, player, new CardSelectorPrefs(AddOnePrompt, 0, 1)))
            {
                await CardPileCmd.Add(card, PileType.Hand);
            }
        }

        // [2] 可以从抽牌堆消耗 1 张卡，对所有敌人造成 6 点伤害
        if (powerCount >= 2)
        {
            foreach (var card in await CardSelectCmd.FromCombatPile(
                         ctx, drawPile, player, new CardSelectorPrefs(ExhaustOnePrompt, 0, 1)))
            {
                await CardCmd.Exhaust(ctx, card);
                await CreatureCmd.Damage(ctx, Owner.CombatState.HittableEnemies, 6m, ValueProp.Move, Owner, null, null);
            }
        }

        // [3] 可以从抽牌堆选择任意张卡送去弃牌堆
        if (powerCount >= 3)
        {
            var toDiscard = (await CardSelectCmd.FromCombatPile(
                ctx, drawPile, player, new CardSelectorPrefs(DiscardAnyPrompt, 0, 99))).ToList();
            if (toDiscard.Count > 0)
            {
                await CardCmd.Discard(ctx, toDiscard);
            }
        }

        // [4] 可以从消耗堆将 1 张卡加入手牌
        if (powerCount >= 4)
        {
            var exhaustPile = PileType.Exhaust.GetPile(player);
            if (exhaustPile.Cards.Count > 0)
            {
                foreach (var card in await CardSelectCmd.FromCombatPile(
                             ctx, exhaustPile, player, new CardSelectorPrefs(FromExhaustPrompt, 0, 1)))
                {
                    await CardPileCmd.Add(card, PileType.Hand);
                }
            }
        }
    }

    // 回合结束时按"手牌里有哪些种类的牌"结算
    // 用 BeforeSideTurnEnd（"清空手牌"之前）：用 AfterSideTurnEnd 的话手牌已经被清掉，
    // 手牌里几乎只剩带「保留」的牌，这三条判定基本不会成立。
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != Owner.Side) return;
        if (Owner.CombatState == null || Owner.Player == null) return;

        Flash();
        var hand = PileType.Hand.GetPile(Owner.Player).Cards;
        var ctx = new ThrowingPlayerChoiceContext();

        // 手牌中存在攻击牌：对所有敌人造成 6 点伤害
        if (hand.Any(c => c.Type == CardType.Attack))
        {
            await CreatureCmd.Damage(ctx, Owner.CombatState.HittableEnemies, 6m, ValueProp.Move, Owner, null, null);
        }

        // 手牌中存在技能牌：获得 7 点格挡
        if (hand.Any(c => c.Type == CardType.Skill))
        {
            await CreatureCmd.GainBlock(Owner, 7m, ValueProp.Move, null);
        }

        // 手牌中存在能力牌：恢复 1 点生命
        if (hand.Any(c => c.Type == CardType.Power))
        {
            await CreatureCmd.Heal(Owner, 1m);
        }
    }

    // 卡组里是否有「圣结界的巫女」
    private static bool HasMaidenInDeck(Player player)
    {
        return PileType.Deck.GetPile(player).Cards.Any(c => c is ShengJieJieDeWuNvCard);
    }
}