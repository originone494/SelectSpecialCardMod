using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

// 奥利哈刚第二结界（效果与 yuno-mod 原版不同）：
// - 回合结束时，选择1张手牌获得永久「保留」
// - 若拥有「奥利哈刚的结界」，敌人攻击时可以选弃1张手牌，使那次攻击伤害为0
//   （原版是随机丢弃且每跳一张；这里改为攻击前可选、选1张整次攻击归零，不选则照常受伤）
[RegisterPower]
public class AoLiHaGangDiErJieJiePower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 回合结束时，选择1张手牌获得永久「保留」
    // 用 BeforeSideTurnEnd（"清空手牌"之前）：用 AfterSideTurnEnd 的话手牌已经被清掉，
    // 没有「保留」的牌都不在了，这条效果几乎选不到东西。
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != Owner.Side) return;
        Flash();

        var handCards = PileType.Hand.GetPile(Owner.Player!).Cards.ToList();
        if (handCards.Count == 0) return;

        var selected = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(SelectSelectorPrefs.RetainSelectionPrompt, 1, 1) { RequireManualConfirmation = true },
            context: choiceContext,
            player: Owner.Player!,
            filter: null,
            // 能力触发不经过卡牌打出，选中的卡应立即回到手牌显示。
            source: null!)).ToList();
        if (selected.Count == 0) return;

        // 永久获得「保留」关键字（不是临时保留一回合）
        selected[0].AddKeyword(CardKeyword.Retain);
    }

    // 敌人攻击前询问一次：可以选择弃1张手牌，整次攻击对我造成的伤害归零。
    // 保护绑定该攻击命令，伤害和格挡由统一补丁处理。
    public override async Task BeforeAttack(AttackCommand command)
    {
        if (FeatureStatus.Checked && !FeatureStatus.AttackProtection) return;
        if (command.Attacker == null || !command.Attacker.IsMonster) return;
        if (command.TargetSide != Owner.Side) return;
        if (!Owner.HasPower<AoLiHaGangDeJieJiePower>()) return;
        if (Owner.Player!.PlayerCombatState!.Hand.Cards.Count == 0) return;

        // 可选选择：0~1 张，不选则本次照常受伤
        var selected = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(SelectSelectorPrefs.BarrierDiscardPrompt, 0, 1),
            context: new BlockingPlayerChoiceContext(),
            player: Owner.Player,
            filter: null,
            source: null!)).ToList();
        if (selected.Count == 0 || selected[0].Pile?.Type != PileType.Hand) return;

        if (!AttackDamageNegate.Mark(command, Owner)) return;
        Flash();
        await CardCmd.Discard(new BlockingPlayerChoiceContext(), selected[0]);
    }

}
