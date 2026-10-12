using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

// 灵魂召集：打出攻击牌时，从抽牌堆顶将3张卡送去弃牌堆
[RegisterPower]
public class LingHunZhaoJiPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 只有打出的卡是攻击牌时触发
        if (cardPlay.Card.Type != CardType.Attack) return Task.CompletedTask;
        // 只响应自己打出的攻击牌（多人下队友的攻击不磨自己的抽牌堆）
        if (cardPlay.Card.Owner != Owner.Player) return Task.CompletedTask;
        if (CombatState == null) return Task.CompletedTask;

        Flash();
        return SelectToolCmd.DuiMu(choiceContext, Owner.Player!, 3);
    }
}