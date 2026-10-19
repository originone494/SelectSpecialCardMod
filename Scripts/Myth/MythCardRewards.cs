using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Pool;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Myth;

public sealed class MythRewardBatch
{
    public List<string> Cards { get; set; } = [];
    public int OptionCount { get; set; }
    public bool CanReroll { get; set; }
    public bool CanSkip { get; set; }
    public CardCreationSource Source { get; set; }
    public CardRarityOddsType RarityOdds { get; set; }
    public CardCreationFlags Flags { get; set; }
    public List<string> PoolIds { get; set; } = [];
    public MythRewardRng? RngOverride { get; set; }
    public bool KeepSpecialCards { get; set; }
    public bool KeepMythCards { get; set; }
}

// 仅作为原版 SerializableReward 的完整载体，不进入任何牌堆或候选。
// 原版单卡奖励原生支持完整卡片及其 SavedProperty 的 JSON/网络传输。
[RegisterCard(typeof(SelectSpecialCardPool))]
public sealed class MythRewardEnvelopeCard() : ModCardTemplate(0, CardType.Skill, CardRarity.Token, TargetType.Self, showInCardLibrary: false)
{
    [SavedProperty] public string RewardJson { get; set; } = "";
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    protected override bool IsPlayable => false;
    public override CardAssetProfile AssetProfile => new(PortraitPath: MythArtManifest.Fallback);
}

// 使用属性存储，避免依赖保存器的 IncludeFields 配置。
public sealed record MythRewardRng(int Counter, ulong State0, ulong State1, ulong State2, ulong State3)
{
    public static MythRewardRng Capture(Rng rng)
    {
        var s = rng.ToSerializable();
        return new(s.counter, s.state0, s.state1, s.state2, s.state3);
    }
    public Rng Restore() => new(new SerializableRng
        { counter = Counter, state0 = State0, state1 = State1, state2 = State2, state3 = State3 });
}

// 仅奖励使用永久 RunState 实体；随缘神话合体的 CombatState 生成流程保持本场战斗有效。
public static class MythCardRewards
{
    private static readonly JsonSerializerOptions CardJson = new() { IncludeFields = true };

    public static MythMutationCard Create(Player player, Rng rng)
    {
        var snapshot = MythCardFactory.Generate(rng);
        var card = (MythMutationCard)player.RunState.CreateCard(ModelDb.Card<MythMutationCard>(), player);
        try
        {
            // 先固定最终配方等级，令 OnUpgrade 不再叠加配方升级增量。
            // 奖励只获得升级外观，不改变这次随机到的费用、数值或效果。
            card.Bind(snapshot with { UpgradeLevel = 1 });
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
            return card;
        }
        catch { if (card.Pile == null && !card.HasBeenRemovedFromState) card.RemoveFromState(); throw; }
    }

    // 原版 SavedProperties 使用公共字段。先按完整原版结构编码，外层只保存字符串，
    // 防止模组保存器未开启 IncludeFields 时丢失随机配方或附魔。
    public static List<string> CaptureCards(IEnumerable<CardModel> cards)
        => cards.Select(c => JsonSerializer.Serialize(c.ToSerializable(), CardJson)).ToList();

    internal static bool ContainsMythCards(IEnumerable<string> saves)
        => saves.Any(save => JsonSerializer.Deserialize<SerializableCard>(save, CardJson)?.Id == ModelDb.Card<MythMutationCard>().Id);

    public static List<CardModel> RestoreCards(Player player, IEnumerable<string> saves)
    {
        var cards = new List<CardModel>();
        try
        {
            foreach (var save in saves)
            {
                var serialized = JsonSerializer.Deserialize<SerializableCard>(save, CardJson)
                    ?? throw new JsonException("Missing myth reward card.");
                var card = CardModel.FromSerializable(serialized);
                cards.Add(card);
                player.RunState.AddCard(card, player);
            }
            return cards;
        }
        catch
        {
            foreach (var card in cards)
                if (card.Pile == null && !card.HasBeenRemovedFromState) card.RemoveFromState();
            throw;
        }
    }

    internal static bool IsFixed(CardModel card) => card is MythMutationCard { FixedSnapshot: not null };
    internal static Rng RewardRng(CardReward reward)
        => ((CardCreationOptions)AccessTools.Property(typeof(CardReward), "RerollOptions").GetValue(reward)!).RngOverride
            ?? reward.Player.PlayerRng.Rewards;

    internal static SerializableReward Save(CardReward reward)
    {
        var options = (CardCreationOptions)AccessTools.Property(typeof(CardReward), "RerollOptions").GetValue(reward)!;
        var batch = new MythRewardBatch
        {
            Cards = CaptureCards(reward.Cards),
            OptionCount = (int)AccessTools.Property(typeof(CardReward), "OptionCount").GetValue(reward)!,
            CanReroll = reward.CanReroll, CanSkip = reward.CanSkip,
            Source = options.Source, RarityOdds = options.RarityOdds, Flags = options.Flags,
            PoolIds = options.CardPools.Select(p => p.Id.ToString()).ToList(),
            RngOverride = options.RngOverride == null ? null : MythRewardRng.Capture(options.RngOverride),
            KeepSpecialCards = reward is INormalSpecialCardReward,
            KeepMythCards = reward is IMythCardReward,
        };
        var envelope = (MythRewardEnvelopeCard)ModelDb.Card<MythRewardEnvelopeCard>().ToMutable();
        envelope.RewardJson = JsonSerializer.Serialize(batch);
        // 自包含载体随原版房间保存/同步，不依赖另一个保存回调的执行顺序。
        return new SerializableReward { RewardType = RewardType.SpecialCard, SpecialCard = envelope.ToSerializable() };
    }

    internal static CardReward Restore(SerializableReward save, Player player)
    {
        var envelope = (MythRewardEnvelopeCard)CardModel.FromSerializable(save.SpecialCard!);
        var batch = JsonSerializer.Deserialize<MythRewardBatch>(envelope.RewardJson)
            ?? throw new JsonException("Missing fixed myth reward.");
        var options = new CardCreationOptions(batch.PoolIds.Select(id => ModelDb.GetById<CardPoolModel>(ModelId.Deserialize(id))),
            batch.Source, batch.RarityOdds).WithFlags(batch.Flags);
        if (batch.RngOverride != null) options.WithRngOverride(batch.RngOverride.Restore());
        var cards = RestoreCards(player, batch.Cards);
        var reward = batch.KeepMythCards ? SpecialCardRewards.CreateOpeningMythReward(cards, player, options, batch.CanSkip)
            : batch.KeepSpecialCards ? SpecialCardRewards.CreateOpeningReward(cards, player, options, batch.CanSkip)
            : new CardReward(cards, CardCreationSource.Other, player, options) { CanSkip = batch.CanSkip };
        reward.CanReroll = batch.CanReroll;
        AccessTools.Field(typeof(CardReward), "<OptionCount>k__BackingField").SetValue(reward, batch.OptionCount);
        // 选项已保存并已应用奖励修改钩子，恢复时不能重复修改。
        AccessTools.Field(typeof(CardReward), "_hasBeenRerolled").SetValue(reward, true);
        return reward;
    }
}

// 原版普通奖励、事件/遗物奖励及重掷共用此工厂；在界面刷新之前产生最终卡面。
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.CreateForReward),
    [typeof(Player), typeof(int), typeof(CardCreationOptions)])]
public static class MythRandomRewardFactoryPatch
{
    static bool Prefix(Player player, int cardCount, CardCreationOptions options, ref IEnumerable<CardCreationResult> __result)
    {
        bool forceSpecial = CardRewardReplacement.KeepsSpecialOptions(options);
        bool forceMyth = CardRewardReplacement.KeepsMythOptions(options);
        if (!forceSpecial && !forceMyth && !CardAvailability.ReplacesCardRewards(player.RunState)) return true;
        if (!forceMyth && (forceSpecial || !CardAvailability.RandomizesCardRewards(player.RunState)) && SpecialCardRewards.Candidates(player).Count == 0)
        {
            Entry.Logger.Warn("[CardRewards] No obtainable special cards; using the original reward factory.");
            return true;
        }
        var rng = options.RngOverride ?? player.PlayerRng.Rewards;
        var cards = new List<CardCreationResult>();
        try
        {
            cards = CardRewardReplacement.Roll(player, cardCount, rng, forceSpecial, forceMyth);
            if (!options.Flags.HasFlag(CardCreationFlags.NoModifyHooks)
                && GameHook.TryModifyCardRewardOptions(player.RunState, player, cards, options, out var modifiers))
                TaskHelper.RunSafely(GameHook.AfterModifyingCardRewardOptions(player.RunState, modifiers));
            CardRewardReplacement.Replace(player, cards, rng, forceSpecial, forceMyth);
            __result = cards;
            return false;
        }
        catch
        {
            foreach (var result in cards)
                if (result.Card.Pile == null && !result.Card.HasBeenRemovedFromState) result.Card.RemoveFromState();
            throw;
        }
    }
}

// 指定选项构造器覆盖教程奖励、本模组奖励及其他模组的标准 CardReward。
[HarmonyPatch(typeof(CardReward), MethodType.Constructor,
    [typeof(IEnumerable<CardModel>), typeof(CardCreationSource), typeof(Player), typeof(CardCreationOptions),
        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer)])]
public static class MythManualRewardPatch
{
    static void Postfix(CardReward __instance, Player player, CardCreationOptions rerollOptions, List<CardCreationResult> ____cards)
    {
        if (__instance is not INormalSpecialCardReward && __instance is not IMythCardReward && CardAvailability.ReplacesCardRewards(player.RunState))
            CardRewardReplacement.Replace(player, ____cards, rerollOptions.RngOverride ?? player.PlayerRng.Rewards);
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.Populate))]
public static class MythRewardPopulatePatch
{
    static void Postfix(CardReward __instance, List<CardCreationResult> ____cards)
    {
        if (__instance is INormalSpecialCardReward or IMythCardReward || CardAvailability.ReplacesCardRewards(__instance.Player.RunState))
            CardRewardReplacement.Replace(__instance.Player, ____cards, MythCardRewards.RewardRng(__instance),
                forceSpecial: __instance is INormalSpecialCardReward, forceMyth: __instance is IMythCardReward);
    }
}

// 单张指定卡奖励也纳入开关，沿用原版的直接领取、预览与完整单卡序列化。
[HarmonyPatch(typeof(SpecialCardReward), MethodType.Constructor, [typeof(CardModel), typeof(Player)])]
public static class MythSingleRewardPatch
{
    static void Prefix(ref CardModel card, Player player)
    {
        if (CardAvailability.ReplacesCardRewards(player.RunState))
        {
            var cards = new List<CardCreationResult> { new(card) };
            CardRewardReplacement.Replace(player, cards, player.PlayerRng.Rewards);
            card = cards[0].Card;
        }
    }
}

[HarmonyPatch(typeof(CardReward), nameof(CardReward.ToSerializable))]
public static class MythRewardSavePatch
{
    static bool Prefix(CardReward __instance, ref SerializableReward __result)
    {
        if (!__instance.Cards.Any() || (__instance is not INormalSpecialCardReward && !__instance.Cards.Any(MythCardRewards.IsFixed)
            && !CardAvailability.ReplacesCardRewards(__instance.Player.RunState))) return true;
        __result = MythCardRewards.Save(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(Reward), nameof(Reward.FromSerializable))]
public static class MythRewardRestorePatch
{
    static bool Prefix(SerializableReward save, Player player, ref Reward __result)
    {
        if (save.RewardType != RewardType.SpecialCard || save.SpecialCard?.Id != ModelDb.Card<MythRewardEnvelopeCard>().Id) return true;
        __result = MythCardRewards.Restore(save, player);
        return false;
    }
}
