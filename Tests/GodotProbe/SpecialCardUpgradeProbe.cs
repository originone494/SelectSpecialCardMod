using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

public static class SpecialCardUpgradeProbe
{
    private static string Values(CardModel card)
        => string.Join(";", card.DynamicVars.OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => v.Key + "=" + v.Value.BaseValue.ToString(System.Globalization.CultureInfo.InvariantCulture)))
           + "|cost=" + card.EnergyCost.Canonical + "/" + card.EnergyCost.GetWithModifiers(CostModifiers.Local)
           + "|keywords=" + string.Join(",", card.GetKeywordsWithSources(KeywordSources.Local).OrderBy(k => k))
           + "|tags=" + string.Join(",", card.Tags.OrderBy(t => t));

    private static void AssertUpgraded(CardModel card)
    {
        if (!card.IsMutable || !card.IsUpgraded || card.CurrentUpgradeLevel != 1 || card.IsUpgradable)
            throw new Exception("A special card is not finalized at cosmetic upgrade level 1: " + card.Id);
    }

    public static void Check()
    {
        if (Harmony.GetPatchInfo(AccessTools.Method(typeof(CardModel), nameof(CardModel.FromSerializable)))?.Postfixes
                .Any(p => p.PatchMethod.DeclaringType == typeof(SpecialCardLoadedUpgradePatch)) != true)
            throw new Exception("Special-card load migration patch is missing.");
        var templates = ModelDb.AllCards.Where(SpecialCardUpgrade.AppliesTo).ToList();
        if (templates.Count == 0) throw new Exception("Special-card coverage inventory is empty.");
        foreach (var template in templates)
        {
            if (template.IsUpgraded) throw new Exception("A shared canonical template was mutated by automatic upgrades.");
            var card = template.ToMutable();
            var before = Values(card);
            var legacySave = card.ToSerializable();
            if (legacySave.CurrentUpgradeLevel != 0) throw new Exception("Fixture no longer represents an old unupgraded save.");
            SpecialCardUpgrade.Ensure(card);
            AssertUpgraded(card);
            if (Values(card) != before) throw new Exception("Cosmetic upgrade changed values, cost, keywords or tags: " + card.Id);
            SpecialCardUpgrade.Ensure(card);
            AssertUpgraded(card);

            var legacy = CardModel.FromSerializable(legacySave);
            AssertUpgraded(legacy);
            if (Values(legacy) != before) throw new Exception("Legacy-save migration changed card effects: " + card.Id);
            var loaded = CardModel.FromSerializable(card.ToSerializable());
            AssertUpgraded(loaded);
            if (Values(loaded) != before) throw new Exception("An upgraded save was upgraded twice or changed values: " + card.Id);
        }

        var player = ArrivalEquipmentProbe.NewPlayer();
        var run = (RunState)player.RunState;
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        // 奖励、初始/补给额外怪兽均使用 RunState.CreateCard。
        var reward = run.CreateCard(ModelDb.Card<YaoShengHuaShiLuXiNaCard>(), player);
        var extra = run.CreateCard(ModelDb.Card<No41NiShuiMoShouCard>(), player);
        AssertUpgraded(reward); AssertUpgraded(extra); AssertUpgraded(run.CloneCard(reward));
        var combat = player.Creature.CombatState!;
        // 检索、随机生成、普通派生和额外召唤副本分别覆盖创建与复制路径。
        var generated = combat.CreateCard(ModelDb.Card<ZhuLeiCanXiangCard>(), player);
        AssertUpgraded(generated);
        AssertUpgraded(combat.CloneCard(generated));
        AssertUpgraded(combat.CloneCard(extra));
        if (generated.DynamicVars.Damage.IntValue != 6 || generated.DynamicVars.Block.IntValue != 7
            || generated.DynamicVars.Weak.IntValue != 2)
            throw new Exception("Residual Tearlaments Metanoise upgrades still add Block or Weak.");
        var temporary = combat.CreateCard(ModelDb.Card<YaoShengHuaShiLuXiNaCard>(), player);
        temporary.EnergyCost.SetThisTurn(0);
        SpecialCardUpgrade.Ensure(temporary);
        if (temporary.EnergyCost.GetWithModifiers(CostModifiers.Local) != 0)
            throw new Exception("Repeated cosmetic upgrades erase a temporary free-cost modifier.");

        foreach (var template in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<EffectOneCard>(), ModelDb.Card<NamedEffectChoiceCard>(), ModelDb.Card<MonsterMoveCard>() })
        {
            var card = template.ToMutable();
            SpecialCardUpgrade.Ensure(card);
            if (card.IsUpgraded) throw new Exception("A vanilla, choice-token or monster-intent card was automatically upgraded.");
        }
        GD.Print($"PASS: {templates.Count} special-card types retain their final effects after cosmetic upgrades and level-0/1 save loads; run/combat creation and clones upgrade once; exclusions and temporary costs remain intact.");
    }
}
