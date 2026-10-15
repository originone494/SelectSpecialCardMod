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
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「特殊效果」：右键手牌中带特殊效果的卡，发动这张卡自己的特殊效果。
// 由卡实现本接口提供具体效果；右键的分发/多人同步由 RitsuLib 的 ModRightClickRegistry 负责。
public interface ISpecialEffectCard
{
    // 能否发动（不满足则右键不触发）
    bool CanActivateSpecialEffect(Player player);

    // 发动特殊效果
    Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player);

    async Task<ActivationResult> ExecuteSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return new(ActivationOutcome.ConditionChanged);
        return new(await TryActivateSpecialEffect(context, player) ? ActivationOutcome.Activated : ActivationOutcome.NotActivated);
    }

    // 可取消的效果覆写此方法；只有真正发动才消耗同名次数。
    Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player);

}

public static class SpecialEffectHook
{
    // 右键绑定的句柄（本 mod 生命周期内常驻，无需注销）
    private static IDisposable? _binding;

    private static string EffectKey(CardModel card) => "SpecialEffect:" + card.Id;
    private static bool HasUsedThisTurn(Player player, CardModel card)
        => PlayerEffectState.For(player).IsUnavailable(EffectKey(card), PlayerEffectState.Turn(player));

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

    internal static async Task<ActivationResult> ExecuteSpecialEffectAsync(CardModel card, PlayerChoiceContext context, Player player)
    {
        if (card is not ISpecialEffectCard effect || card.Owner != player || card.Pile?.Type != PileType.Hand)
            return new(ActivationOutcome.ConditionChanged);
        var combat = card.CombatState;
        if (!await SpecialEffectVfx.Play(card)) return new(ActivationOutcome.NotActivated);
        // 演出期间退出战斗或移走卡牌时，不继续执行已失效的效果。
        if (card.CombatState != combat || card.Owner != player || card.Pile?.Type != PileType.Hand)
            return new(ActivationOutcome.ConditionChanged);
        return await effect.ExecuteSpecialEffect(context, player);
    }

    public static void Init()
    {
        if (_binding != null) return;
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
                if (ctx.Model is not CardModel card || card is not ISpecialEffectCard) return;
                if (ctx.PlayerChoiceContext is null) return;
                // 执行阶段再判一次：入队与真正执行之间手牌可能已变化/已被同名卡用掉
                if (card.Owner != ctx.Player || !CanActivate(card)) return;
                using var reservation = PlayerEffectState.For(ctx.Player).Reserve(EffectKey(card), PlayerEffectState.Turn(ctx.Player));
                if (reservation == null) return;
                using var payment = EffectActivation.Begin(reservation.Commit);
                try
                {
                    if ((await ExecuteSpecialEffectAsync(card, ctx.PlayerChoiceContext, ctx.Player)).Activated) reservation.Commit();
                }
                catch
                {
                    // A failed command may already have paid costs. Never enable an uncertain retry.
                    reservation.Commit();
                    throw;
                }
                finally { SelectSpecialCardMod.Scripts.Tool.UiConditions.Invalidate(); }
            });
    }
}
