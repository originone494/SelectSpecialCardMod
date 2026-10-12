using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

// 王之棺：回合开始时随机将等同于层数的「荷鲁斯怪兽」卡送入弃牌堆，之后将弃牌堆的荷鲁斯怪兽全部加入手牌；
// 「荷鲁斯怪兽」卡进入弃牌堆时（随机送入/主动弃牌/打出后落弃/回合末冲牌等所有路径），对随机敌人造成8点伤害。
[RegisterPower]
public class WangZhiGuanPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player.Creature != Owner) return;
        if (CombatState == null) return;

        Flash();

        // ① 按层数随机将 Amount 张「荷鲁斯怪兽」卡送入弃牌堆（不弹选择界面）
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            c => c.Tags.Contains(SelectTags.HeLuSiGuaiShou),
            null,
            Amount,
            isDiscard: true,
            isRandom: true);

        // ② 将弃牌堆的「荷鲁斯怪兽」卡全部加入手牌（含①送入的那张，经弃牌堆"过一手"后回手）
        foreach (CardModel card in PileType.Discard.GetPile(player).Cards
            .Where(c => c.Tags.Contains(SelectTags.HeLuSiGuaiShou))
            .ToList())
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        // 只处理"进入弃牌堆"且带荷鲁斯怪兽标签的卡（打出、丢弃等所有路径都会走到这里）
        if (card.Pile?.Type != PileType.Discard) return;
        if (!card.Tags.Contains(SelectTags.HeLuSiGuaiShou)) return;
        if (CombatState == null) return;

        Flash();

        Creature? enemy = Owner.Player!.RunState.Rng.CombatTargets.NextItem(CombatState.HittableEnemies);
        if (enemy == null) return;

        for (int i = 0; i < Amount; i++)
        {
            // 逐次 await，保证多段伤害的结算顺序在锁步下两端一致
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), enemy, 8m, ValueProp.Move, Owner, null, null);
        }
    }
}