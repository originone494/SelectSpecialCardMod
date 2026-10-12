using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Keywords;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「登场」：卡牌**因效果加入手牌**时触发（一场战斗一次，按卡名记）。
//
// 抽牌与"效果加入手牌"走同一条入堆路径，用 DrawWindowPatch 的"抽牌窗口"区分：
// 窗口内的入堆 = 抽牌，不触发登场。
public interface IDengChangCard
{
    // 卡自己的后注登场效果
    Task DengChangSpecial(PlayerChoiceContext ctx, Player player);
}

public static class DengChangHook
{
    // 防循环②：跨卡连锁上限（A 的登场效果检索 B、B 的登场效果又检索 A …）
    private const int MaxChainDepth = 32;
    private static int _chainDepth;

    // 防循环①（核心）：正在结算登场的卡实例。
    // "打出后立刻返回手牌"本身就是一次"因效果加入手牌"，
    // 不屏蔽就会 打出 → 返回手牌 → 再触发 → 再打出 …… 无限循环。
    private static readonly HashSet<CardModel> Resolving = new();

    // 关键字 或 标签 都算（标签用于"卡面不显示登场关键字、只在悬停里给说明"的写法）
    public static bool HasDengChang(CardModel card)
        => card.Keywords.Contains(SelectKeywords.DengChang) || card.Tags.Contains(SelectTags.DengChang);

    // 由卡牌基类的 AfterCardChangedPiles 调用（card == 刚换堆的那张卡）
    public static async Task OnCardAddedToHand(CardModel card)
    {
        if (card.Pile?.Type != PileType.Hand) return;
        if (!HasDengChang(card)) return;

        // 抽牌窗口内的入堆 = 抽牌，不算"因效果加入手牌"
        if (DrawWindowPatch.IsInsideDraw(card.Owner)) return;

        await Trigger(card);
    }

    private static async Task Trigger(CardModel card)
    {
        var player = card.Owner;
        if (player is null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;

        // 防循环①：这张卡正在结算登场 → 这次的"加入手牌"就是它自己的返回动作，直接放过
        if (!Resolving.Add(card)) return;

        if (_chainDepth >= MaxChainDepth)
        {
            Resolving.Remove(card);
            Log.Warn($"[SelectSpecialCardMod] 登场连锁已达 {MaxChainDepth} 层，停止继续触发：{card.Id}");
            return;
        }

        _chainDepth++;
        try
        {
            // 登场只触发后注效果；没有后注的卡不做任何事
            if (card is not IDengChangCard self) return;

            // 一场战斗一次：按卡名记（PerCombatOnce 换战斗自动失效）
            string onceKey = PerCombatOnceKey(card);
            if (PerCombatOnce.IsUsed(onceKey)) return;
            PerCombatOnce.Mark(onceKey);

            // AfterCardChangedPiles 没有 PlayerChoiceContext，而打出的卡可能弹选择界面，
            // 用原版专为这种场合准备的 BlockingPlayerChoiceContext
            var ctx = new BlockingPlayerChoiceContext();

            await self.DengChangSpecial(ctx, player);
        }
        catch (Exception e)
        {
            Log.Warn($"[SelectSpecialCardMod] 登场结算异常，已跳过本次：{card.Id} {e.Message}");
        }
        finally
        {
            _chainDepth--;
            Resolving.Remove(card);
        }
    }

    private static string PerCombatOnceKey(CardModel card) => "DengChang:" + card.Id.Entry;
}
