using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Rules;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class MaterialPayment
{
    // 灵摆区仍属于手牌堆；只有因效果放置在两侧的卡不能作为素材。
    public static bool CanUseAsMaterial(CardModel card)
        => !card.HasBeenRemovedFromState && !YiXiangMingHandPlacement.IsPlayLocked(card);

    public static async Task<bool> Exhaust(PlayerChoiceContext ctx, Player owner, IReadOnlyList<CardModel> chosen, int minimum, PileType source)
    {
        if (chosen.Count < minimum || chosen.Distinct().Count() != chosen.Count
            || chosen.Any(c => c.Owner != owner || c.Pile?.Type != source || !CanUseAsMaterial(c))) return false;
        EffectActivation.PaymentStarted();
        foreach (var card in chosen)
        {
            if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding) return false;
            if (card.Owner != owner || card.Pile?.Type != source || !CanUseAsMaterial(card)) return false;
            var result = await CardCmd.Exhaust(ctx, card);
            if (result?.success != true) throw new InvalidOperationException($"Material payment failed: {card.Id}");
        }
        return true;
    }
}
