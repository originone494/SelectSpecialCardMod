using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「送墓」：卡牌**因效果被送入弃牌堆**时触发（原「灵活」，效果不变）。
// 触发点用原版唯一的弃牌事件 AfterCardDiscarded（CardCmd.DiscardAndDraw 内部、入堆之后），
// 因此「回合结束清空手牌」「正常/自动打出后的落堆」「纯搬运（Add 到弃牌堆）」都不会触发。
//
// 两种处理，与关键字文案一一对应：
//   ① 卡自己实现 ISongMuCard（= 送墓后注有效果）→ 触发那些效果，不打出；
//   ② 没实现（= 送墓后注没有效果）→ 默认把它打出。
public interface ISongMuCard
{
    // 这张卡自身的送墓效果（由"被丢弃的那张卡"实现）
    Task SongMuSpecial(PlayerChoiceContext ctx, Player player);
}

// 全局观察者（能力/遗物）：任意一张送墓卡被丢弃时收到通知；trigger 是触发的那张卡，便于按标签筛选
public interface ISongMuObserver
{
    Task OnSongMu(PlayerChoiceContext ctx, Player player, CardModel trigger);
}

public static class SongMuHook
{
    // 连锁深度上限：正常构筑 3~5 层，这里只是防极端循环（A 被丢弃→触发丢弃 B→B 又触发…）的安全阀
    private const int MaxChainDepth = 32;
    private static readonly AsyncLocal<int> ChainDepth = new();

    private sealed class DeferredWindow
    {
        public List<(PlayerChoiceContext Context, CardModel Card)> Pending { get; } = [];
    }

    // 每个异步结算链独立捕获事件；嵌套堆墓不能清空外层尚未处理的事件。
    private static readonly AsyncLocal<DeferredWindow?> Deferred = new();

    // 由卡牌基类的 AfterCardDiscarded 调用，card == 被丢弃的那张卡
    public static async Task OnCardDiscarded(PlayerChoiceContext ctx, CardModel card)
    {
        if (!HasSongMu(card)) return;

        var player = card.Owner;
        if (player is null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;

        // 处于延迟窗口（如整堆送墓）时先记账，窗口结束后再按被丢弃的先后顺序逐一触发。
        if (Deferred.Value is { } window)
        {
            window.Pending.Add((ctx, card));
            return;
        }

        await Trigger(ctx, card, player);
    }

    /// <summary>
    /// 在 body 执行期间挂起所有「送墓」触发并记录；body 正常结束后，
    /// 按记录顺序逐一结算（嵌套调用时由最外层窗口统一结算）。
    /// 用途：让「一次性把整堆牌送入弃牌堆」这类效果先整体生效，
    /// 再处理被丢弃的送墓卡，避免送墓在扫牌中途抽/检索走牌、导致后续被重复搬运。
    /// </summary>
    public static async Task DeferTriggers(Func<Task> body)
    {
        var parent = Deferred.Value;
        var window = new DeferredWindow();
        Deferred.Value = window;
        try
        {
            await body();
        }
        finally
        {
            Deferred.Value = parent;
        }

        if (parent != null)
        {
            parent.Pending.AddRange(window.Pending);
            return;
        }
        foreach (var (ctx, card) in window.Pending)
        {
            if (CombatManager.Instance.IsOverOrEnding) break;
            if (card.Owner is { } player) await Trigger(ctx, card, player);
        }
    }

    private static async Task Trigger(PlayerChoiceContext ctx, CardModel card, Player player)
    {
        if (ChainDepth.Value >= MaxChainDepth)
        {
            Log.Warn($"[SelectSpecialCardMod] 送墓连锁已达 {MaxChainDepth} 层，停止继续触发：{card.Id}");
            return;
        }

        ChainDepth.Value++;
        try
        {
            if (card is ISongMuCard self)
            {
                // 「送墓后注有效果」→ 不打出，改为触发那些效果
                await self.SongMuSpecial(ctx, player);
            }
            else
            {
                // 「送墓后注没有效果」→ 关键字默认行为：把这张卡打出
                await AutoPlayFromDiscard(ctx, card, null);
            }
            await Broadcast(ctx, player, card);
        }
        finally
        {
            ChainDepth.Value--;
        }
    }

    // 与原实现同判据：带送墓关键字或送墓标签
    public static bool HasSongMu(CardModel card)
    {
        return card.Keywords.Contains(SelectKeywords.SongMu) || card.Tags.Contains(SelectTags.SongMu);
    }

    private static async Task Broadcast(PlayerChoiceContext ctx, Player player, CardModel trigger)
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null) return;

        foreach (var model in combatState.IterateHookListeners().OfType<ISongMuObserver>().ToList())
        {
            var abstractModel = (AbstractModel)(object)model;
            ctx.PushModel(abstractModel);
            try
            {
                await model.OnSongMu(ctx, player, trigger);
            }
            finally
            {
                ctx.PopModel(abstractModel);
            }
        }
    }

    // 从弃牌堆打出：AutoPlay 只在 card.Pile == null 时才把牌搬进 Play，
    // 已在牌堆里的卡必须先自己搬——与官方 CardPileCmd.AutoPlayFromDrawPile 同款做法。
    public static async Task AutoPlayFromDiscard(PlayerChoiceContext ctx, CardModel card, Creature? target, bool exhaustOnPlay = false)
    {
        // 该送墓事件排队期间可能已被用作素材或回收，不从新的牌堆擅自打出。
        if (card.Pile?.Type != PileType.Discard || card.HasBeenRemovedFromState) return;
        await CardPileCmd.Add(card, PileType.Play);
        if (card.Pile?.Type != PileType.Play) return;
        if (exhaustOnPlay)
        {
            card.ExhaustOnNextPlay = true;
        }
        await CardCmd.AutoPlay(ctx, card, target);
    }
}