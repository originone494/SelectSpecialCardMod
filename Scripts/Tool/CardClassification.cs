using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 额外怪兽是分类的并集，不再维护独立的「额外怪兽」标签。
public static class CardClassification
{
    public static int LinkRating(CardModel card)
    {
        if (card.Tags.Contains(SelectTags.Link4)) return 4;
        if (card.Tags.Contains(SelectTags.Link3)) return 3;
        if (card.Tags.Contains(SelectTags.Link2)) return 2;
        if (card.Tags.Contains(SelectTags.Link1)) return 1;
        return 0;
    }

    public static bool IsExtraMonster(CardModel card)
        => LinkRating(card) > 0
           || card.Tags.Contains(SelectTags.RongHeGuaiShou)
           || card.Tags.Contains(SelectTags.ChaoLiangGuaiShou)
           || card.Tags.Contains(SelectTags.TongBuGuaiShou);
}
