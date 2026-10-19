using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Myth;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.RunData;

// 保留真实奖励工厂、固定快照、原版重掷与原版保存/恢复，只替换运行局的钩子枚举。
public static class MythCardRewardsProbe
{
    private static RunSavedData<CardContentRunPolicy> Policy
        => (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
    private static Player NewPlayer(bool enabled = true, int version = 6)
    {
        var p = (Player)AccessTools.Method(typeof(RunStartCardsProbe), "NewPlayer").Invoke(null, [version, 101UL])!;
        Policy.Set((RunState)p.RunState, new() { Version = version, RandomizeAllCardRewards = enabled });
        return p;
    }
    private static CardCreationOptions Options() => new CardCreationOptions([], CardCreationSource.Other, CardRarityOddsType.Uniform)
        .WithFlags(CardCreationFlags.NoModifyHooks | CardCreationFlags.NoUpgradeRoll);
    private static string Fingerprint(IEnumerable<CardModel> cards)
        => string.Join("\n", cards.Cast<MythMutationCard>().Select(c => c.SnapshotJson));
    private static void CheckCards(IEnumerable<CardModel> input, Player p, int count)
    {
        var cards = input.ToList();
        if (cards.Count != count || cards.Any(c => c is not MythMutationCard m || m.SnapshotJson.Length == 0
            || !c.IsUpgraded || c.Owner != p || c.CardScope != p.RunState || c.Pile != null
            || c.Rarity != CardRarity.Ancient || c.EnergyCost.Canonical is < 0 or > 3))
            throw new Exception("Random rewards lost count, ownership, fixed effects, upgrade, cost or permanent scope.");
    }

    public static void Check()
    {
        CardAvailability.Register();
        var defaults = JsonSerializer.Deserialize<CardContentPreferences>("{}")!;
        if (defaults.RandomizeAllCardRewards || defaults.AllCardRewardsSpecial)
            throw new Exception("Random rewards must default to disabled.");
        var h = new Harmony("scm.myth-rewards.godot-framework");
        try
        {
            EquipmentHookProbe.Models = [];
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));

            foreach (var p in new[] { NewPlayer(false), NewPlayer(true, 5) })
            {
                var original = p.RunState.CreateCard(ModelDb.Card<StrikeIronclad>(), p);
                string state = p.PlayerRng.Rewards.ToSerializable().ToString();
                var plain = new CardReward([original], CardCreationSource.Other, p, Options());
                if (CardAvailability.RandomizesCardRewards(p.RunState) || plain.Cards.Single() != original
                    || state != p.PlayerRng.Rewards.ToSerializable().ToString())
                    throw new Exception("Disabled/legacy policy converted an ordinary reward or consumed RNG.");
            }

            var player = NewPlayer();
            var expectedRng = new Rng(player.PlayerRng.Rewards.ToSerializable());
            var expected = Enumerable.Range(0, 4).Select(_ => MythJson.Write(MythCardFactory.Generate(expectedRng) with { UpgradeLevel = 1 })).ToList();
            // 空卡池也可以生成，避免先古卡经过原版稀有度过滤导致空候选。
            var results = CardFactory.CreateForReward(player, 4, Options()).ToList();
            var cards = results.Select(r => r.Card).ToList();
            CheckCards(cards, player, 4);
            if (!cards.Cast<MythMutationCard>().Select(c => c.SnapshotJson).SequenceEqual(expected))
                throw new Exception("Cosmetic reward upgrades changed the rolled values/cost or consumed extra RNG.");
            var envelope = ModelDb.Card<MythRewardEnvelopeCard>();
            if (envelope.ShouldShowInCardLibrary || envelope.CanBeGeneratedInCombat || envelope.CanBeGeneratedByModifiers
                || !((bool)AccessTools.Method(typeof(SelectToolCmd), "IsNonObtainable").Invoke(null, [envelope])!))
                throw new Exception("The reward save envelope became an obtainable card.");
            var reward = new CardReward(cards, CardCreationSource.Other, player, Options()) { CanReroll = true, CanSkip = false };
            string before = Fingerprint(reward.Cards), rng = player.PlayerRng.Rewards.ToSerializable().ToString();
            reward.Populate(); reward.Populate();
            if (before != Fingerprint(reward.Cards) || rng != player.PlayerRng.Rewards.ToSerializable().ToString())
                throw new Exception("Repopulating a fixed reward rerolled cards.");

            var marker = reward.ToSerializable();
            // 原版保存器的 JSON 选项包含 SavedProperties 公共字段及原版 ModelId 转换器。
            marker = JsonSerializer.Deserialize<SerializableReward>(JsonSerializer.Serialize(marker,
                MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.Options), MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.Options)!;
            var loaded = (CardReward)Reward.FromSerializable(marker, player);
            loaded.Populate();
            CheckCards(loaded.Cards, player, 4);
            if (before != Fingerprint(loaded.Cards) || !loaded.CanReroll || loaded.CanSkip
                || rng != player.PlayerRng.Rewards.ToSerializable().ToString())
                throw new Exception("Reward restore changed effects, flags or RNG.");
            if (loaded.ToSerializable().SpecialCard?.Id != marker.SpecialCard?.Id)
                throw new Exception("Re-saving a restored reward lost its envelope.");

            var writer = new PacketWriter(); marker.Serialize(writer);
            var reader = new PacketReader(); reader.Reset(writer.Buffer);
            var wire = new SerializableReward(); wire.Deserialize(reader);
            if (wire.SpecialCard?.Id != ModelDb.Card<MythRewardEnvelopeCard>().Id)
                throw new Exception("Native reward packet lost the fixed batch reference.");
            var wireLoaded = (CardReward)Reward.FromSerializable(wire, player);
            if (before != Fingerprint(wireLoaded.Cards)) throw new Exception("Reward packet restore rerolled effects.");

            var run = (RunState)player.RunState;
            AccessTools.Field(typeof(RunState), "_mapPointHistory").SetValue(run,
                new List<List<MapPointHistoryEntry>> { new() { new() { PlayerStats = [new() { PlayerId = player.NetId }] } } });
            bool refreshedWithFinalCards = false;
            loaded.AfterGenerated += () => refreshedWithFinalCards = loaded.Cards.Count() == 4 && loaded.Cards.All(c => c is MythMutationCard);
            loaded.Reroll();
            CheckCards(loaded.Cards, player, 4);
            if (loaded.CanReroll || !refreshedWithFinalCards || before == Fingerprint(loaded.Cards))
                throw new Exception("Reroll lost count, refreshed too early or reused old effects.");

            var manualOriginal = player.RunState.CreateCard(ModelDb.Card<YiXiangMingJueZeCard>(), player);
            var manual = new CardReward([manualOriginal], CardCreationSource.Other, player, Options());
            CheckCards(manual.Cards, player, 1);
            if (!manualOriginal.HasBeenRemovedFromState) throw new Exception("Replaced preview card leaked into run state.");

            var single = new SpecialCardReward(player.RunState.CreateCard(ModelDb.Card<StrikeIronclad>(), player), player);
            var singleCopy = (SpecialCardReward)Reward.FromSerializable(single.ToSerializable(), player);
            var a = (CardModel)AccessTools.Field(typeof(SpecialCardReward), "_card").GetValue(single)!;
            var b = (CardModel)AccessTools.Field(typeof(SpecialCardReward), "_card").GetValue(singleCopy)!;
            CheckCards([a, b], player, 2);
            if (Fingerprint([a]) != Fingerprint([b])) throw new Exception("Single-card reward restore rerolled effects.");

            var peer1 = NewPlayer(); var peer2 = NewPlayer();
            string Generate(Player p) => Fingerprint(CardFactory.CreateForReward(p, 3, Options()).Select(r => r.Card));
            if (Generate(peer1) != Generate(peer2) || peer1.PlayerRng.Rewards.ToSerializable() != peer2.PlayerRng.Rewards.ToSerializable())
                throw new Exception("Independent peers generated different reward cards or RNG states.");
            var overridden = NewPlayer();
            string untouched = overridden.PlayerRng.Rewards.ToSerializable().ToString();
            var overrideOptions = Options().WithRngOverride(new Rng(4567));
            var overrideCards = CardFactory.CreateForReward(overridden, 2, overrideOptions).ToList();
            if (untouched != overridden.PlayerRng.Rewards.ToSerializable().ToString())
                throw new Exception("Event RNG override consumed the player's reward RNG.");

            // 非奖励生成和指定发放没有接入本功能。
            if (player.RunState.CreateCard(ModelDb.Card<SuiYuanShenHuaHeTiCard>(), player) is not SuiYuanShenHuaHeTiCard)
                throw new Exception("A fixed card grant was replaced.");
            var permanent = loaded.Cards.First();
            PileType.Deck.GetPile(player).AddInternal(permanent);
            var clone = permanent.CreateClone();
            if (clone is not MythMutationCard mutation || mutation.SnapshotJson != ((MythMutationCard)permanent).SnapshotJson)
                throw new Exception("Battle/deck cloning lost the recipe.");
            CheckSpecialRewards(h);
        }
        finally { h.UnpatchAll(h.Id); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: random-reward defaults/legacy policy, permanent upgraded scope, count, stable previews, fixed save and packet references, rerolls, manual/single rewards, peer RNG and event RNG override. Runtime probe; no real UI/network transport.");
    }

    private static void CheckSpecialRewards(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(CardPoolModel), "GetUnlockedCards"),
            prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(RunStartCardsProbe.Pool)));
        var p = NewPlayer(false, 7);
        Policy.Set((RunState)p.RunState, new() { Version = 7, AllCardRewardsSpecial = true, OnlyYuGiOh = true });
        var cards = CardFactory.CreateForReward(p, 4, Options()).Select(c => c.Card).ToList();
        Standard(cards, 4);
        if (cards.DistinctBy(c => c.GetType()).Count() != 3 || cards.Any(c => c is YiCunJiaCard))
            throw new Exception("Special rewards did not respect filtering, de-duplication or a count larger than the pool.");
        var reward = new CardReward(cards, CardCreationSource.Other, p, Options()) { CanReroll = true };
        string rng = p.PlayerRng.Rewards.ToSerializable().ToString();
        reward.Populate();
        if (!reward.Cards.SequenceEqual(cards) || rng != p.PlayerRng.Rewards.ToSerializable().ToString())
            throw new Exception("Existing special reward options were unnecessarily replaced.");
        var save = reward.ToSerializable();
        var loaded = (CardReward)Reward.FromSerializable(save, p);
        loaded.Populate(); Standard(loaded.Cards, 4);
        if (!loaded.Cards.Select(c => c.Id).SequenceEqual(cards.Select(c => c.Id))
            || rng != p.PlayerRng.Rewards.ToSerializable().ToString())
            throw new Exception("Special reward save/restore rerolled options.");
        AccessTools.Field(typeof(RunState), "_mapPointHistory").SetValue(p.RunState,
            new List<List<MapPointHistoryEntry>> { new() { new() { PlayerStats = [new() { PlayerId = p.NetId }] } } });
        loaded.Reroll(); Standard(loaded.Cards, 4);
        if (rng == p.PlayerRng.Rewards.ToSerializable().ToString() || loaded.CanReroll)
            throw new Exception("Special reward reroll did not use the special pool or consume its use.");

        var original = p.RunState.CreateCard(ModelDb.Card<StrikeIronclad>(), p);
        var manual = new CardReward([original], CardCreationSource.Other, p, Options());
        Standard(manual.Cards, 1);
        if (!original.HasBeenRemovedFromState) throw new Exception("A replaced vanilla preview leaked.");
        var single = new SpecialCardReward(p.RunState.CreateCard(ModelDb.Card<StrikeIronclad>(), p), p);
        Standard([(CardModel)AccessTools.Field(typeof(SpecialCardReward), "_card").GetValue(single)!], 1);

        Policy.Set((RunState)p.RunState, new() { Version = 7, AllCardRewardsSpecial = true });
        var unfiltered = CardFactory.CreateForReward(p, 4, Options()).Select(c => c.Card).ToList();
        Standard(unfiltered, 4);
        if (!unfiltered.Any(c => c is YiCunJiaCard)) throw new Exception("Disabling the content filter did not restore its special cards.");
        // 只模拟互斥规则加入前的旧快照；新设置界面不再允许保存这个组合。
        Policy.Set((RunState)p.RunState, new() { Version = 7, AllCardRewardsSpecial = true, RandomizeAllCardRewards = true });
        CheckCards(CardFactory.CreateForReward(p, 3, Options()).Select(c => c.Card), p, 3);

        // 明确指定的开局特殊奖励始终为正常特殊卡，包括原版重掷及完整存档恢复。
        foreach (int mode in new[] { 0, 1, 2 })
        {
            Policy.Set((RunState)p.RunState, new() { Version = 7, OnlyYuGiOh = true,
                AllCardRewardsSpecial = mode == 1, RandomizeAllCardRewards = mode == 2 });
            var opening = (CardReward)AccessTools.Method(typeof(SpecialCardRewards), "Create")
                .Invoke(null, [p, null, true, null])!;
            opening.CanReroll = true;
            opening.Populate(); Standard(opening.Cards, 3);
            string fixedIds = string.Join(",", opening.Cards.Select(c => c.Id));
            var restored = (CardReward)Reward.FromSerializable(opening.ToSerializable(), p);
            restored.Populate(); Standard(restored.Cards, 3);
            if (fixedIds != string.Join(",", restored.Cards.Select(c => c.Id)))
                throw new Exception("Opening special reward restore changed its fixed options.");
            restored.Reroll(); Standard(restored.Cards, 3);

            // 显式开局神话奖励在三种全局模式下均保留神话配方、原版保存与重掷。
            var mythCards = Enumerable.Range(0, 3).Select(_ => MythCardRewards.Create(p, p.PlayerRng.Rewards)).ToList();
            var mythOpening = (CardReward)AccessTools.Method(typeof(SpecialCardRewards), "CreateOpeningMythReward")
                .Invoke(null, [mythCards, p, Options(), true])!;
            mythOpening.CanReroll = true;
            string mythFingerprint = Fingerprint(mythOpening.Cards);
            string mythRng = p.PlayerRng.Rewards.ToSerializable().ToString();
            mythOpening.Populate(); CheckCards(mythOpening.Cards, p, 3);
            var mythRestored = (CardReward)Reward.FromSerializable(mythOpening.ToSerializable(), p);
            mythRestored.Populate(); CheckCards(mythRestored.Cards, p, 3);
            if (!mythRestored.CanReroll || !mythRestored.CanSkip || mythFingerprint != Fingerprint(mythRestored.Cards)
                || mythRng != p.PlayerRng.Rewards.ToSerializable().ToString())
                throw new Exception("Opening myth reward restore changed its recipe, flags or RNG under a global mode.");
            var writer = new PacketWriter(); mythOpening.ToSerializable().Serialize(writer);
            var reader = new PacketReader(); reader.Reset(writer.Buffer);
            var wire = new SerializableReward(); wire.Deserialize(reader);
            var mythWire = (CardReward)Reward.FromSerializable(wire, p);
            if (Fingerprint(mythWire.Cards) != mythFingerprint) throw new Exception("Opening myth reward packet lost fixed recipes.");
            bool refreshedWithMyth = false;
            mythRestored.AfterGenerated += () => refreshedWithMyth = mythRestored.Cards.All(c => c is MythMutationCard);
            mythRestored.Reroll(); CheckCards(mythRestored.Cards, p, 3);
            if (!refreshedWithMyth || mythRestored.CanReroll || mythFingerprint == Fingerprint(mythRestored.Cards))
                throw new Exception("Opening myth reward reroll used the global normal pool or reused old recipes.");

            int updates = 0; bool rerolled = false; string tracked = "";
            AccessTools.Method(typeof(SpecialCardRewards), "TrackOpeningMythReward").Invoke(null,
                [mythOpening, false, (Action<CardReward, bool>)((updated, used) =>
                    { updates++; rerolled = used; tracked = Fingerprint(updated.Cards); })]);
            mythOpening.Populate(); mythOpening.Reroll();
            if (updates != 2 || !rerolled || tracked != Fingerprint(mythOpening.Cards) || tracked == mythFingerprint)
                throw new Exception("Pending opening myth options were not updated after reroll.");
            var resumed = (CardReward)AccessTools.Method(typeof(SpecialCardRewards), "CreateOpeningMythReward")
                .Invoke(null, [MythCardRewards.RestoreCards(p, MythCardRewards.CaptureCards(mythOpening.Cards)), p, Options(), true])!;
            AccessTools.Method(typeof(SpecialCardRewards), "TrackOpeningMythReward").Invoke(null,
                [resumed, true, (Action<CardReward, bool>)((_, used) => { if (!used) throw new Exception("Lost reroll use."); })]);
            resumed.CanReroll = true; // 模拟遗物重新应用奖励钩子，已消耗的重掷资格仍不得恢复。
            resumed.Populate();
            if (resumed.CanReroll || Fingerprint(resumed.Cards) != tracked)
                throw new Exception("Pending opening myth reward restore reset its reroll use or options.");
        }

        var old = NewPlayer(false, 6);
        Policy.Set((RunState)old.RunState, new() { Version = 6, AllCardRewardsSpecial = true });
        var legacyOriginal = old.RunState.CreateCard(ModelDb.Card<StrikeIronclad>(), old);
        var legacy = new CardReward([legacyOriginal], CardCreationSource.Other, old, Options());
        if (legacy.Cards.Single() != legacyOriginal || CardAvailability.UsesSpecialCardRewards(old.RunState))
            throw new Exception("A pre-v7 policy activated the new setting.");
        GD.Print("PASS: standard special rewards, actual candidate filtering, oversized counts, cosmetic upgrades, stable previews, save/restore, rerolls, single/manual entry points, random-mode precedence and old policies.");

        void Standard(IEnumerable<CardModel> input, int count)
        {
            var list = input.ToList();
            if (list.Count != count || list.Any(c => c is not SelectSpecialCardMod.Scripts.Base.SelectSpecialBaseCard
                || !c.IsUpgraded || c.Owner != p || c.CardScope != p.RunState || c.Pile != null
                || !CardAvailability.IsAllowed(c, p.RunState) || CardClassification.IsExtraMonster(c)
                || c.Rarity != CardRarity.Ancient || !c.ShouldShowInCardLibrary
                || c.EnergyCost.Canonical != c.CanonicalInstance.EnergyCost.Canonical))
                throw new Exception("Special reward cards lost their original card rules or candidate restrictions.");
        }
    }
}
