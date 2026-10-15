using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Tool;

public static class SelectToolCmd
{
    /// <summary>
    /// 消耗若干张卡。正常情况下原版 CardCmd.Exhaust 就够（它自带演出）；
    /// 但它的失败是**静默的**（内部 CardPileCmd.Add 返回 success=false 时没人看），
    /// 会出现"卡没被消耗、也没任何提示"。这里加一层校验：没进消耗堆就用普通入堆兜底，并记一条日志。
    /// </summary>
    public static async Task ExhaustCards(PlayerChoiceContext ctx, IEnumerable<CardModel> cards)
    {
        foreach (var card in cards)
        {
            await CardCmd.Exhaust(ctx, card);
            if (card.Pile?.Type != PileType.Exhaust)
            {
                Entry.Logger.Warn(
                    $"[消耗兜底] {card.Id.Entry} 原版 Exhaust 后仍在 {card.Pile?.Type.ToString() ?? "null"}，改用普通入堆");
                await CardPileCmd.Add(card, PileType.Exhaust);
            }
        }
    }

    // 「消耗」一张**没有卡面节点的卡**（例如躺在隐藏牌堆「装备堆」里的装备卡）并补一次原版消耗特效。
    //
    // 注意：普通消耗一律用原版 CardCmd.Exhaust 就够了——原版自己有演出：
    //   从手牌消耗 → NCardExhaustQuickVfx（快速版）+ card_exhaust.mp3；
    //   其他来源   → NCardExhaustVfx（完整侵蚀）。
    // 只有"卡在无界面牌堆里、根本没有卡面节点"这种原版播不了的情况，才需要这里补一次特效。
    public static async Task<CardPileAddResult?> ExhaustWithVfx(PlayerChoiceContext ctx, CardModel card)
    {
        PlayExhaustVfx(card);
        return await CardCmd.Exhaust(ctx, card, false, skipVisuals: true);
    }

    /// <summary>只播原版「消耗」特效，不移动卡。</summary>
    public static void PlayExhaustVfx(CardModel card)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;

        var room = NCombatRoom.Instance;
        if (room?.Ui == null || card.Owner == null) return;

        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == card.GetType());
        if (template == null) return;

        // 用一张"副本卡面"来播特效：手牌卡放到它自己的位置，牌堆里的卡放到固定的手牌区位置。
        // 这样不会把手牌里那张真卡面从手牌节点里抢走。
        var node = NCard.Create(template.ToMutable(), ModelVisibility.Visible);
        if (node == null) return;

        var existing = NCard.FindOnTable(card, null);
        room.Ui.AddChildSafely(node);
        node.UpdateVisuals(PileType.Exhaust, CardPreviewMode.Normal);
        if (existing != null)
        {
            node.GlobalPosition = existing.GlobalPosition;
        }
        else
        {
            var vp = room.Ui.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
            node.GlobalPosition = new Vector2(vp.X * 0.5f, vp.Y * 0.88f);   // 手牌区固定位置
        }

        var vfx = NCardExhaustVfx.Create(node);
        if (vfx == null)
        {
            node.QueueFreeSafely();
            return;
        }
        room.Ui.AddChildSafely(vfx);
        NDebugAudioManager.Instance?.Play("card_exhaust.mp3");
        TaskHelper.RunSafely(vfx.PlayAnimation());
    }

    // 「从两个牌堆取卡」的通用流程（荷鲁斯·献祭哈比、刻魔·落泪之日、刻魔·赫赫君王 共用）：
    //   · 只有一堆有符合条件的卡 → 直接从那一堆选（**不询问**）；
    //   · 两堆都有 → 询问 choicePrompt：是 → primaryPile，否 → secondaryPile。
    // 两堆都没有符合条件的卡 → 不询问、不弹界面，返回空列表。
    // 选择张数按 minSelect~maxSelect（不足时自动按实际张数收缩）；并且**必须手动确认**，
    // 候选只剩最少张数时也不会替玩家自动选。
    public static async Task<List<CardModel>> PickFromTwoPiles(
        PlayerChoiceContext ctx, Player player,
        Func<CardModel, bool> filter,
        PileType primaryPile, PileType secondaryPile,
        LocString choicePrompt, LocString pickPrompt,
        CardModel? source = null, int minSelect = 1, int? maxSelect = null)
    {
        var primary = primaryPile.GetPile(player).Cards.Where(filter).ToList();
        var secondary = secondaryPile.GetPile(player).Cards.Where(filter).ToList();
        if (primary.Count == 0 && secondary.Count == 0) return [];

        bool fromPrimary;
        if (primary.Count == 0) fromPrimary = false;
        else if (secondary.Count == 0) fromPrimary = true;
        else fromPrimary = await AskYesNo(ctx, player, choicePrompt, source);

        var candidates = fromPrimary ? primary : secondary;
        int min = Math.Min(minSelect, candidates.Count);
        int max = Math.Min(maxSelect ?? minSelect, candidates.Count);

        var p = new LocString(pickPrompt.LocTable, pickPrompt.LocEntryKey);
        if (source != null) p.Add("CardName", source.Title);
        var prefs = new CardSelectorPrefs(p, min, max) { RequireManualConfirmation = true };
        return (await CardSelectCmd.FromSimpleGrid(ctx, candidates, player, prefs)).ToList();
    }

    // 「从消耗堆·除外堆取卡」时，两堆都有卡的情况下询问的那句话
    public static LocString ExhaustOrChuWaiChoicePrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_EXHAUST_OR_CHU_WAI");

    // 「把牌从牌堆返回另一个牌堆」（消耗堆 → 抽牌堆·额外堆）的统一演出。
    // 牌堆里的卡没有常驻节点，原版对"牌堆→牌堆"的移动未必会播"这张卡飞过去"的效果
    // （尤其进 mod 牌堆「额外堆」时完全没有演出），所以统一走这里：
    // 已在手牌或 Play 的卡走原版移动，原卡面随移动清理；隐藏牌堆的卡
    // 才静默入堆，再补「卡出现在屏幕中央 → 飞向目标牌堆」的飞行特效。
    public static async Task ReturnCardToPile(CardModel card, PileType target,
        CardPilePosition position = CardPilePosition.Bottom)
    {
        // 跳过 Play/手牌的原版显示处理会留下原卡面；额外创建飞行副本无法清理它。
        bool useNativeVisuals = card.Pile?.Type is PileType.Hand or PileType.Play;
        var result = await CardPileCmd.Add(card, target, position, null, skipVisuals: !useNativeVisuals);
        if (!useNativeVisuals && result.success && card.Pile?.Type == target)
            await PlayCardFlyToPile(card, target);
    }

    /// <summary>只播"卡飞向目标牌堆"的原版飞行特效，不移动卡。</summary>
    public static void PlayFlyToPile(CardModel card, PileType target)
        => _ = PlayCardFlyToPile(card, target);

    private static async Task PlayCardFlyToPile(CardModel card, PileType target)
    {
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;

        var room = NCombatRoom.Instance;
        if (room?.Ui == null || card.Owner == null) return;

        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == card.GetType());
        var node = template != null ? NCard.Create(template.ToMutable(), ModelVisibility.Visible) : null;
        if (node == null) return;

        room.Ui.AddChildSafely(node);
        node.UpdateVisuals(target, CardPreviewMode.Normal);
        var vp = room.Ui.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        node.GlobalPosition = new Vector2(vp.X * 0.5f, vp.Y * 0.5f);   // 屏幕中央

        var vfx = NCardFlyVfx.Create(node, target, true, card.Owner.Character.TrailPath);
        if (vfx == null)
        {
            node.QueueFreeSafely();
            return;
        }
        room.Ui.AddChildSafely(vfx);
        if (vfx.SwooshAwayCompletion != null) await vfx.SwooshAwayCompletion.Task;
    }

    // 「检索」：候选 = 全卡池中同时满足 filter 与 poolFilter 的卡（按 Id 去重），
    // 不可获取卡判据：衍生/机制卡（Token 稀有度，如「是」「否」）、图鉴隐藏卡、
    // 怪物卡模板（意图卡牌化）——它们不应被玩家通过检索/随机入堆等通用机制获得。
    // 另外，「检索」本身不再取「额外怪兽」卡（额外怪兽属于额外卡组，只能从额外堆召唤）；
    // 这条只作用于 RetrieverCard，随机入堆（AddRandomCardsToPile）仍可取额外怪兽（金满/强欲之壶要用）。
    // 检索（RetrieverCard）与随机入堆（AddRandomCardsToPile）都从这里统一排除，
    // 调用方无需各自过滤（「是/否」选择直接 CreateCard 实例化，不走这两条路径，不受影响）。
    internal static bool IsNonObtainable(CardModel c) =>
        c.Rarity == CardRarity.Token
        || !c.ShouldShowInCardLibrary
        || c is IMonsterDisplayCard;

    // 生成战斗副本后弹选牌界面，选中的加入手牌。
    // isRandom = true 时不弹界面，从候选中不重复地随机取最多 amount 张；
    // isDiscard = true 时改为送入弃牌堆（并结算"被丢弃"语义）；
    // isExhaust = true 时改为送入消耗堆（并结算"被消耗"语义）。
    // minAmount = 至少要选几张（默认 0 = 可以什么都不选就退出）；
    // requireManualConfirmation = true 时即使候选数 ≤ 最少选择数（例如只有 1 张）也必须玩家自己点。
    public static Task<List<CardModel>> RetrieverCard(
        PlayerChoiceContext choiceContext,
        Player player,
        Func<CardModel, bool> filter,
        Func<CardPoolModel, bool>? poolFilter = null,
        int amount = 1, bool isDiscard = false, bool isRandom = false, LocString? prompt = null,
        CardModel? source = null, bool isExhaust = false,
        int minAmount = 0, bool requireManualConfirmation = false)
    {
        if (isDiscard && isExhaust) throw new ArgumentException("Retrieval destination cannot be both Discard and Exhaust.");
        return CardRetrieval.Retrieve(choiceContext, player, filter,
            new RetrievalOptions(amount, minAmount,
                isExhaust ? RetrievalDestination.Exhaust : isDiscard ? RetrievalDestination.Discard : RetrievalDestination.Hand,
                isRandom ? RetrievalChoice.Random : RetrievalChoice.Player, requireManualConfirmation), poolFilter, prompt, source);
    }

    internal static async Task<List<CardModel>> RetrieveCore(
        PlayerChoiceContext choiceContext,
        Player player,
        Func<CardModel, bool> filter,
        Func<CardPoolModel, bool>? poolFilter = null,
        int amount = 1, bool isDiscard = false, bool isRandom = false, LocString? prompt = null,
        CardModel? source = null, bool isExhaust = false,
        int minAmount = 0, bool requireManualConfirmation = false)
    {
        if (player == null || amount < 1) return [];

        var cards = ModelDb.AllCards
            .Where(filter)
            .Where(c => !IsNonObtainable(c))
            .Where(c => !c.Tags.Contains(SelectTags.ExtraMonster))
            .Where(c => poolFilter == null || (c.Pool != null && poolFilter(c.Pool)))
            .GroupBy(c => c.Id)
            .Select(g => g.First()).OrderBy(c => c.Id)
            .ToList();
        if (cards.Count == 0) return [];

        var combatCopies = cards
            .Select(c => player.Creature.CombatState!.CreateCard(c, player))
            .ToList();

        List<CardModel> selectCards;
        if (isRandom)
        {
            // 随机抽取：从候选中不重复地随机取最多 amount 张，不弹选择界面
            selectCards = new List<CardModel>();
            for (int i = 0; i < amount && combatCopies.Count > 0; i++)
            {
                var picked = player.RunState.Rng.Niche.NextItem(combatCopies)!;
                combatCopies.Remove(picked);
                selectCards.Add(picked);
            }
        }
        else
        {
            // 提示文本：调用方自带 > 带卡名的 mod 检索提示（source 非空）
            var usedPrompt = prompt ?? (source != null
                ? SelectSpecialBaseCard.RetrieveNamedPrompt()
                : SelectSelectorPrefs.RetrieverSelectionPrompt);


            usedPrompt = CardPrompts.Named(usedPrompt, source);
            var prefs = new CardSelectorPrefs(usedPrompt, minAmount, amount)
            {
                RequireManualConfirmation = requireManualConfirmation || minAmount > 0,
            };
            selectCards = (await CardSelectCmd.FromSimpleGrid(
                choiceContext,
                combatCopies,
                player,
                prefs
            )).ToList();
        }

        if (selectCards.Count > 0) SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();
        if (isExhaust)
        {
            // 「检索并消耗」：与「检索并丢弃」同款做法，只是进的是消耗堆、结算的是"被消耗"语义。
            foreach (var card in selectCards)
            {
                var combatState = card.CombatState ?? player.Creature.CombatState;
                if (combatState == null) continue;
                if (CombatManager.Instance.IsOverOrEnding) continue;

                // 生成卡记录（与 AddGeneratedCopiesToPile 同款：先补记录，再入堆，最后广播生成）
                CombatManager.Instance.History.CardGenerated(combatState, card, player);

                var addResult = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Exhaust, player);

                // 兜底：上面那条万一没把卡放进消耗堆，用普通 Add 强制放一次
                if (card.Pile?.Type != PileType.Exhaust)
                {
                    await CardPileCmd.Add(card, PileType.Exhaust);
                }
                if (card.Pile?.Type != PileType.Exhaust) continue;

                await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardGeneratedForCombat(combatState, card, player);

                if (SaveManager.Instance.PrefsSave.FastMode != FastModeType.Instant)
                {
                    var previewFinished = CardCmd.Preview(card, DiscardPreviewSeconds);
                    if (previewFinished != null) await previewFinished.Task;
                }

                if (CombatManager.Instance.IsOverOrEnding) continue;
                CombatManager.Instance.History.CardExhausted(combatState, card);
                await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardExhausted(combatState, choiceContext, card, false);

                Entry.Logger.Info($"[检索并消耗] {card.Id.Entry}（消耗堆现有 {PileType.Exhaust.GetPile(player).Cards.Count} 张，addSuccess={addResult.success}）");
            }
        }
        else if (isDiscard)
        {
            // 「检索并丢弃」要同时满足两件事：① 保留"生成卡"记录；② 走真正的弃牌语义（触发弃牌相关效果）。
            // 做法：先按生成卡入堆（入的是弃牌堆），再手动结算"被丢弃"（不用 CardCmd.Discard，
            // 否则会多出一次"弃牌堆 → 弃牌堆"的入堆和它的重复特效）。
            foreach (var card in selectCards)
            {
                var addResult = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Discard, player);
                if (!addResult.success) continue;

                // 弃牌特效：卡从屏幕中央出现、停留片刻、带拖尾飞进弃牌堆。
                if (SaveManager.Instance.PrefsSave.FastMode != FastModeType.Instant)
                {
                    var previewFinished = CardCmd.Preview(card, DiscardPreviewSeconds);
                    if (previewFinished != null) await previewFinished.Task;
                }

                if (CombatManager.Instance.IsOverOrEnding) continue;
                var combatState = card.CombatState ?? player.Creature.CombatState;
                if (combatState == null) continue;
                CombatManager.Instance.History.CardDiscarded(combatState, card);
                await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardDiscarded(combatState, choiceContext, card);
            }
        }
        else
        {
            foreach (var card in selectCards)
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
        }

        return selectCards;
    }

    // 「检索并丢弃」时，卡在屏幕中央停留多久（之后才带拖尾飞进弃牌堆）。
    // 按快进模式缩放；Instant 模式下整个特效都会被跳过。
    private static float DiscardPreviewSeconds =>
        SaveManager.Instance.PrefsSave.FastMode switch
        {
            FastModeType.Fast => 0.35f,
            _ => 0.8f,
        };

    // 「是/否」选择：弹出「是」「否」两张标记卡让玩家点选（原版 yuno-mod 同款机制）
    public static async Task<bool> AskYesNo(PlayerChoiceContext choiceContext, Player player, LocString prompt, CardModel? source = null)
    {
        prompt = CardPrompts.Named(prompt, source);

        var shi = player.Creature.CombatState!.CreateCard<ShiCard>(player);
        var fou = player.Creature.CombatState!.CreateCard<FouCard>(player);

        CardModel? picked = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            new List<CardModel> { shi, fou },
            player,
            new CardSelectorPrefs(prompt, 1, 1))).FirstOrDefault();

        return picked is ShiCard;
    }

    // 向指定牌堆加入 amount 张随机卡（从全卡池随机生成战斗副本）。
    // 原版战斗牌堆：走游戏的批量生成入堆 + 屏幕中央预览动画；
    // mod 牌堆（除外堆等）：AddGeneratedCardsToCombat 只允许原版战斗牌堆，
    // 这里手动补"生成记录 + 逐张入堆 + 生成广播"（飞行动画由 RitsuLib 的 mod 牌堆系统处理）。
    public static async Task<IReadOnlyList<CardPileAddResult>> AddRandomCardsToPile(
        Player player, PileType targetPile, int amount, float previewTime = 2f, Func<CardModel, bool>? filter = null)
    {
        if (amount <= 0) return Array.Empty<CardPileAddResult>();

        var combatState = player.Creature.CombatState;
        if (combatState == null) return Array.Empty<CardPileAddResult>();

        var candidates = ModelDb.AllCards.Where(c => !IsNonObtainable(c));
        if (filter != null) candidates = candidates.Where(filter);
        var candidateList = candidates.ToList();
        if (candidateList.Count == 0) return Array.Empty<CardPileAddResult>();

        var defs = new List<CardModel>();
        for (int i = 0; i < amount; i++)
        {
            var randomCanonical = player.RunState.Rng.Niche.NextItem(candidateList);
            if (randomCanonical == null) continue;
            defs.Add(randomCanonical);
        }
        if (defs.Count == 0) return Array.Empty<CardPileAddResult>();

        return await AddGeneratedCopiesToPile(player, defs, targetPile, previewTime);
    }

    // 把卡牌定义列表生成为战斗副本并加入指定牌堆。
    // 原版战斗牌堆：走游戏的批量生成入堆 + 屏幕中央预览动画；
    // mod 牌堆（除外堆等）：AddGeneratedCardsToCombat 只允许原版战斗牌堆，
    // 这里手动补"生成记录 + 逐张入堆 + 生成广播"（飞行动画由 RitsuLib 的 mod 牌堆系统处理）。
    internal static async Task<IReadOnlyList<CardPileAddResult>> AddGeneratedCopiesToPile(
        Player player, List<CardModel> definitions, PileType targetPile, float previewTime = 2f)
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null || definitions.Count == 0) return Array.Empty<CardPileAddResult>();

        var copies = new List<CardModel>();
        foreach (var def in definitions)
        {
            copies.Add(combatState.CreateCard(def, player));
        }
        if (copies.Count == 0) return Array.Empty<CardPileAddResult>();

        if (targetPile.IsCombatPile())
        {
            var results = await CardPileCmd.AddGeneratedCardsToCombat(copies, targetPile, player);
            PreviewPileAdd(results, previewTime);
            return results;
        }

        var pile = targetPile.GetPile(player);
        var manualResults = new List<CardPileAddResult>();
        foreach (var card in copies)
        {
            CombatManager.Instance.History.CardGenerated(combatState, card, player);
            manualResults.Add(await CardPileCmd.Add(card, pile));
            await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardGeneratedForCombat(combatState, card, player);
        }
        return manualResults;
    }

    // 播放"卡入堆"预览：卡从屏幕中央出现、停留 time 秒后飞向目标牌堆（引擎自带的 PreviewCardPileAdd）。
    // 超过5张时横排会铺出屏幕，故改用凌乱布局。
    private static void PreviewPileAdd(IReadOnlyList<CardPileAddResult> results, float time)
    {
        if (results.Count == 0) return;

        var style = results.Count > 5 ? CardPreviewStyle.MessyLayout : CardPreviewStyle.HorizontalLayout;
        CardCmd.PreviewCardPileAdd(results, time, style);
    }

    // 「预知」：看抽牌堆顶 amount 张牌，选 1 张加入手牌。
    // remainingToDiscard = true 时剩余的送入弃牌堆（与 yuno-mod 的预知一致）；
    // false 时剩余的留在抽牌堆顶（它们从未离开过抽牌堆，无需搬回）。
    public static async Task<IEnumerable<CardModel>> Foresee(PlayerChoiceContext choiceContext, Player player, int amount, CardModel? source = null, bool remainingToDiscard = true)
    {
        if (amount <= 0) return Array.Empty<CardModel>();

        var drawPile = PileType.Draw.GetPile(player);
        if (drawPile.Cards.Count == 0)
        {
            await CardPileCmd.ShuffleIfNecessary(choiceContext, player);
            drawPile = PileType.Draw.GetPile(player);
        }

        var cardsToScry = drawPile.Cards.Take(amount).ToList();
        if (cardsToScry.Count == 0) return Array.Empty<CardModel>();

        // 预视提示：source 非空时用带卡名的 mod 提示（文本含 {CardName}），否则用通用提示
        LocString foreseePrompt = SelectSelectorPrefs.ForeseeSelectionPrompt;
        if (source != null)
        {
            foreseePrompt = SelectSpecialBaseCard.ForeseeNamedPrompt;
            foreseePrompt = CardPrompts.Named(foreseePrompt, source);
        }

        var prefs = new CardSelectorPrefs(foreseePrompt, 1, 1) { RequireManualConfirmation = true };
        var selectedCards = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            cardsToScry,
            player,
            prefs)).ToList();

        var result = new List<CardModel>();

        // 选中的1张加入手牌
        foreach (var card in selectedCards)
        {
            await CardPileCmd.Add(card, PileType.Hand);
            result.Add(card);
        }

        // 剩余牌：按调用方的选择送入弃牌堆，或留在抽牌堆顶
        if (remainingToDiscard)
        {
            foreach (var card in cardsToScry)
            {
                if (!selectedCards.Contains(card))
                {
                    await CardCmd.Discard(choiceContext, card);
                }
            }
        }

        return result;
    }

    // 「送墓」：将抽牌堆顶的 amount 张卡送入弃牌堆（抽牌堆为空时先把弃牌堆洗回抽牌堆），
    // 并把送入的卡展示给玩家（与 AddRandomCardsToPile / AddRandomCardsToExhaust 同款表现）。
    public static async Task<IEnumerable<CardModel>> DuiMu(PlayerChoiceContext choiceContext, Player player, int amount)
    {
        var list = new List<CardModel>();
        var results = new List<CardPileAddResult>();

        if (amount <= 0 || CombatManager.Instance.IsOverOrEnding) return list;
        var draw = PileType.Draw.GetPile(player);
        var batch = draw.Cards.Take(amount).ToList();
        // 不足时只从本批开始前的弃牌堆补足，不把刚送墓的卡洗回并重复计数。
        if (batch.Count < amount && PileType.Discard.GetPile(player).Cards.Count > 0)
        {
            await CardPileCmd.Shuffle(choiceContext, player);
            batch.AddRange(draw.Cards.Where(c => !batch.Contains(c)).Take(amount - batch.Count));
        }
        batch = batch.Where(c => c.Pile?.Type == PileType.Draw && !c.HasBeenRemovedFromState).ToList();
        await SongMuHook.DeferTriggers(async () =>
        {
            if (batch.Count > 0) await CardCmd.Discard(choiceContext, batch);
        });
        foreach (var card in batch)
        {
            results.Add(new CardPileAddResult { success = true, cardAdded = card, targetPile = PileType.Discard });
            list.Add(card);
        }

        PreviewPileAdd(results, 2f);

        return list;
    }
}
