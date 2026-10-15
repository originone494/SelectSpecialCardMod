using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「荷鲁斯怪兽」统一的免费打出规则：
//   **卡名一回合一次**，拥有能力「王之棺」的场合，这张卡可以免费打出。
//
// 用 PerTurnOnce 按"卡名"记账：同名卡的多份拷贝共享一次机会；
// 换回合 / 换战斗时键自然失效，不需要任何重置钩子。
public static class HorusFreePlay
{
    private const string Scope = "horus_free";

    private static string KeyOf(CardModel card) => PerTurnOnce.Key(Scope, card.Id.ToString());

    // 现在能不能免费打出（有「王之棺」，且这个卡名本回合还没免费打出过）
    public static bool IsAvailable(CardModel card)
    {
        var owner = card.Owner;
        if (owner == null) return false;
        if (!owner.Creature.HasPower<WangZhiGuanPower>()) return false;
        return !PerTurnOnce.IsUsed(owner, KeyOf(card));
    }

    // 这张卡真正打出去时记账（有「王之棺」才计；同名卡再打也不受影响，重复记账是幂等的）
    public static void Consume(CardModel card, CardPlay cardPlay)
    {
        if (cardPlay.IsAutoPlay) return;
        var owner = card.Owner;
        if (owner == null) return;
        if (!owner.Creature.HasPower<WangZhiGuanPower>()) return;
        PerTurnOnce.Mark(owner, KeyOf(card));
    }
}
