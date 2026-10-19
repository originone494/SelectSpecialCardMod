using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythConditionEvaluator
{
    public static bool CanPlay(MythMutationCard card)
    {
        var s = card.FixedSnapshot;
        if (s == null || card.Owner == null) return s != null;
        if (s.Condition.Actions != null) return MythLibraryConditions.CanPlay(card);
        var owner = card.Owner;
        if (owner.Creature.CombatState == null) return true; // 非战斗卡组/升级预览不读取不存在的手牌堆。
        var hand = PileType.Hand.GetPile(owner).Cards;
        var others = hand.Where(c => c != card).ToList();
        // OnPlay时本卡已进入Play；判定口径仍为打出前包含本卡的手牌。
        bool includeSelf = hand.Contains(card) || card.Pile?.Type == PileType.Play && card.PlayedFromHand;
        int count = others.Count + (includeSelf ? 1 : 0);
        return s.Condition.Op switch
        {
            "none" => true,
            "hand_at_least" => count >= s.ConditionValues["count"],
            "hand_at_most" => count <= s.ConditionValues["count"],
            "has_attack" => includeSelf && card.Type == CardType.Attack || others.Any(c => c.Type == CardType.Attack),
            "has_skill" => includeSelf && card.Type == CardType.Skill || others.Any(c => c.Type == CardType.Skill),
            "has_block" => owner.Creature.Block > 0,
            "hp_below_half" => owner.Creature.CurrentHp * 2 < owner.Creature.MaxHp,
            "pay_hp" => owner.Creature.CurrentHp > s.ConditionValues["hp"]
                && MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(owner.RunState, card.CombatState, owner.Creature, owner.Creature,
                    s.ConditionValues["hp"], ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                    card, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.Normal, out _) >= s.ConditionValues["hp"],
            "pay_all_hand" or "pay_discard_hand" => others.Count > 0 && others.All(MaterialPayment.CanUseAsMaterial),
            _ => false,
        };
    }
    public static async Task<bool> Pay(MythMutationCard card, FusionActionContext ctx)
    {
        if (!ctx.CanContinue || !CanPlay(card)) return false;
        var s = card.FixedSnapshot!;
        if (s.Condition.Actions != null) return await MythLibraryConditions.Pay(card, ctx);
        if (s.Condition.Kind != "payment") return true;
        var owner = card.Owner;
        if (s.Condition.Op == "pay_hp")
        {
            int amount = s.ConditionValues["hp"];
            var paid = await CreatureCmd.Damage(ctx.ChoiceContext, owner.Creature, amount,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, ctx.CardPlay);
            // 支付后的回血连锁不撤销已经发生的生命损失；按伤害结果而非净HP差判定。
            return ctx.CanContinue && paid.Where(r => r.Receiver == owner.Creature).Sum(r => r.UnblockedDamage) >= amount;
        }
        var chosen = PileType.Hand.GetPile(owner).Cards.Where(c => c != card).ToList();
        if (chosen.Count == 0 || chosen.Any(c => !MaterialPayment.CanUseAsMaterial(c))) return false;
        // 按最初支付清单逐张验证；连锁移走后续卡或结束战斗时不免费发动效果。
        foreach (var item in chosen)
        {
            if (!ctx.CanContinue || item.Pile?.Type != PileType.Hand || !MaterialPayment.CanUseAsMaterial(item)) return false;
            if (s.Condition.Op == "pay_all_hand")
            {
                if (!await MaterialPayment.Exhaust(ctx.ChoiceContext, owner, [item], 1, PileType.Hand)) return false;
            }
            else
            {
                await CardCmd.Discard(ctx.ChoiceContext, item);
                if (item.Pile?.Type == PileType.Hand) return false;
            }
        }
        return ctx.CanContinue;
    }
}
