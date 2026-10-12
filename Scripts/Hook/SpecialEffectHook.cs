using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using STS2RitsuLib.Interactions.RightClick;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「特殊效果」：右键手牌中带特殊效果的卡，发动这张卡自己的特殊效果。
// 由卡实现本接口提供具体效果；右键的分发/多人同步由 RitsuLib 的 ModRightClickRegistry 负责。
public interface ISpecialEffectCard
{
    // 能否发动（不满足则右键不触发）
    bool CanActivateSpecialEffect(Player player);

    // 发动特殊效果
    Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player);
}

public static class SpecialEffectHook
{
    // 右键绑定的句柄（本 mod 生命周期内常驻，无需注销）
    private static IDisposable? _binding;

    // 「同名卡一回合一次」：记录每（玩家, 卡名）最近一次发动所在的回合键。
    // 与本回合键相同即表示本回合该卡名的特殊效果已发动过，右键不再触发；
    // 回合号变化后键自然不再相等，无需手动清理。
    private static readonly Dictionary<(Player Player, string CardName), (CombatId? Combat, int Turn)> LastUsedTurn = [];

    private static (CombatId? Combat, int Turn)? CurrentTurnKey(Player player)
    {
        var combat = CombatManager.Instance;
        if (combat == null) return null;
        return ((CombatId?)combat.CurrentCombatId, player.PlayerCombatState?.TurnNumber ?? 0);
    }

    private static bool HasUsedThisTurn(Player player, CardModel card)
    {
        var key = CurrentTurnKey(player);
        return key != null && LastUsedTurn.TryGetValue((player, card.Title), out var used) && used == key.Value;
    }

    private static void MarkUsedThisTurn(Player player, CardModel card)
    {
        var key = CurrentTurnKey(player);
        if (key == null) return;
        LastUsedTurn[(player, card.Title)] = key.Value;
    }

    // 某张牌当前是否「可以右键发动特殊效果」——与右键注册用的判据完全一致：
    // 在手牌中 + 带「特殊效果」标签 + 本回合该卡名未发动过 + 卡自己判定条件成立。
    // 供 SpecialEffectGlowPatch 复用，保证「发金光 = 真的能点」。
    public static bool CanActivate(CardModel? card)
    {
        if (card == null) return false;
        if (card.Pile?.Type != PileType.Hand) return false;
        if (!card.Tags.Contains(SelectTags.SpecialEffect)) return false;
        Player player = card.Owner;
        if (player == null) return false;
        if (HasUsedThisTurn(player, card)) return false;
        return card is ISpecialEffectCard effect && effect.CanActivateSpecialEffect(player);
    }

    public static void Init()
    {
        _binding = ModRightClickRegistry.Register<CardModel>(
            Entry.ModId,
            "special_effect",
            canHandle: ctx => ctx.Model is CardModel card
                              && card.Pile?.Type == PileType.Hand
                              && card.Tags.Contains(SelectTags.SpecialEffect)
                              && !HasUsedThisTurn(ctx.Player, card)
                              && card is ISpecialEffectCard effect && effect.CanActivateSpecialEffect(ctx.Player),
            execute: async ctx =>
            {
                if (ctx.Model is not CardModel card || card is not ISpecialEffectCard effect) return;
                if (ctx.PlayerChoiceContext is null) return;
                // 执行阶段再判一次：入队与真正执行之间手牌可能已变化/已被同名卡用掉
                if (!effect.CanActivateSpecialEffect(ctx.Player)) return;
                if (HasUsedThisTurn(ctx.Player, card)) return;
                await effect.ActivateSpecialEffect(ctx.PlayerChoiceContext, ctx.Player);
                MarkUsedThisTurn(ctx.Player, card);
            });
    }
}
