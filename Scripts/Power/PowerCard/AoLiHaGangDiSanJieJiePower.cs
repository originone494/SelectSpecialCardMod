using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.HandSize;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 奥利哈刚第三结界：
// - 手牌数量没有上限
// - 若同时拥有「奥利哈刚的结界」和「奥利哈刚第二结界」，回合开始时清除自身负面能力、清除所有敌人的力量
[RegisterPower]
public class AoLiHaGangDiSanJieJiePower : SelectSpecialBasePower, IMaxHandSizeModifier
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 手牌数量没有上限
    public int ModifyMaxHandSize(Player player, int currentMaxHandSize)
    {
        if (player != Owner.Player) return currentMaxHandSize;
        return 999;
    }

    public int ModifyMaxHandSizeLate(Player player, int currentMaxHandSize)
    {
        return currentMaxHandSize;
    }

    // 回合开始时，若双结界齐备：清除自身负面能力、清除所有敌人的力量
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != Owner.Side) return;
        if (!Owner.HasPower<AoLiHaGangDeJieJiePower>()) return;
        if (!Owner.HasPower<AoLiHaGangDiErJieJiePower>()) return;

        Flash();
        var ctx = new ThrowingPlayerChoiceContext();

        // 清除自身负面能力
        foreach (var power in Owner.Powers.Where(p => p.Type == PowerType.Debuff).ToList())
        {
            await PowerCmd.Remove(power);
        }

        // 清除所有敌人的力量
        foreach (Creature enemy in Owner.CombatState!.HittableEnemies)
        {
            var strength = enemy.GetPower<StrengthPower>();
            if (strength != null)
            {
                await PowerCmd.Remove(strength);
            }
        }
    }
}
