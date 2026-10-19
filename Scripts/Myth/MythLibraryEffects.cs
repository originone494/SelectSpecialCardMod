using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.HandSize;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public static partial class MythLibraryEffects
{
    // Arity由审核过的121条配方生成；执行入口仍是下方明确的C#动作分支。
    private static readonly IReadOnlyDictionary<string, int> Arity = BuildArity();
    private static partial IReadOnlyDictionary<string, int> BuildArity();
    public static bool IsDamage(string op) => op is "attack" or "attack_all" or "custom_attack_double_vs_block" or "custom_attack_scaled_other_hand"
        or "custom_attack_by_distinct_discard" or "custom_multiattack_scaled_attacks_played" or "custom_attack_all_per_hand" or "custom_attack_all_repeated";
    public static void Validate(MythResolvedAction action)
    {
        var args = MythJson.Read<string[]>(action.Arguments); var keys = MythJson.Read<string[]>(action.ArgumentKeys);
        string op = action.Op[4..];
        if (args.Length > 4 || !Arity.TryGetValue(op, out int mask) || (mask & (1 << args.Length)) == 0 || keys.Length != args.Length)
            throw new InvalidOperationException("Unsupported library effect: " + action.Op);
        for (int i = 0; i < args.Length; i++)
        {
            bool valid = op == "custom_apply_damage_nullification" ? args[i] == (i == 0 ? "Target" : "EndOfTurn")
                : i == 0 && (op.StartsWith("generate_") || op is "retrieve_exhaust_type" or "retrieve_exhaust_type_free") ? args[i] is "Any" or "Attack" or "Skill" or "Power"
                : i == 2 && op == "generate_random_to_hand" ? args[i] == "FreeThisTurn"
                : int.TryParse(args[i], out int n) && n is >= 1 and <= 1000;
            if (!valid) throw new InvalidOperationException("Invalid library effect argument: " + op);
        }
    }
    public static string[] Args(MythResolvedAction action) => MythJson.Read<string[]>(action.Arguments);
    public static string ArgKey(int action, int arg) => $"MutationArg_{action}_{arg}";
    public static IReadOnlyList<CardModel> RandomPool(MegaCrit.Sts2.Core.Entities.Players.Player owner, string type = "Any")
        => ModelDb.AllCards.Where(c => !SelectToolCmd.IsNonObtainable(c) && c.CanBeGeneratedInCombat
            && c is not MythMutationCard && c is not SuiYuanShenHuaHeTiCard && c is not SelectSpecialCardMod.Scripts.Cards.Other.ShenHuaCompositeCard
            && !CardClassification.IsExtraMonster(c) && CardAvailability.IsAllowed(c, owner.RunState) && MythLibraryChoices.TypeMatches(c, type))
            .OrderBy(c => c.Id).ToList();
    public static async Task<List<CardModel>> Generate(FusionActionContext ctx, string type, int count, PileType pile, bool free = false)
    {
        var pool = RandomPool(ctx.Source.Owner, type); var result = new List<CardModel>();
        if (pool.Count == 0) return result;
        for (int n = 0; n < count && ctx.CanContinue; n++)
        {
            var template = MythCardFactory.RngFor(ctx.Source.Owner).NextItem(pool.ToList())!;
            var generated = ctx.Combat!.CreateCard(template, ctx.Source.Owner);
            try
            {
                if (free) MythLibraryChoices.Free(generated);
                CardPileAddResult added;
                if (pile.IsCombatPile()) added = await CardPileCmd.AddGeneratedCardToCombat(generated, pile, ctx.Source.Owner);
                else
                {
                    CombatManager.Instance.History.CardGenerated(ctx.Combat!, generated, ctx.Source.Owner);
                    added = await CardPileCmd.Add(generated, pile);
                    await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardGeneratedForCombat(ctx.Combat!, generated, ctx.Source.Owner);
                }
                if (!added.success) continue;
                result.Add(generated);
                if (pile.IsCombatPile()) CardCmd.PreviewCardPileAdd([added], 1f);
                // 入堆已广播一次；补弃牌/消耗语义，不再次搬运同一张卡。
                if (!ctx.CanContinue || generated.HasBeenRemovedFromState || generated.Pile?.Type != pile) continue;
                if (pile == PileType.Discard)
                {
                    CombatManager.Instance.History.CardDiscarded(ctx.Combat!, generated);
                    await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardDiscarded(ctx.Combat!, ctx.ChoiceContext, generated);
                }
                else if (pile == PileType.Exhaust)
                {
                    CombatManager.Instance.History.CardExhausted(ctx.Combat!, generated);
                    await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardExhausted(ctx.Combat!, ctx.ChoiceContext, generated, false);
                }
            }
            finally { if (generated.Pile == null && !generated.HasBeenRemovedFromState) generated.RemoveFromState(); }
        }
        return result;
    }
    public static async Task Execute(MythMutationCard card, FusionActionContext ctx, MythResolvedAction action, int index)
    {
        string op = action.Op[4..]; var args = Args(action);
        int N(int p = 0) => int.Parse(args[p]);
        decimal Amount() => card.DynamicVars[MythEffectExecutor.Key(index, action.Op)].BaseValue;
        var owner = card.Owner; var choice = ctx.ChoiceContext;
        List<Creature> Enemies() => ctx.Combat!.HittableEnemies.Where(c => c.IsAlive).ToList();
        List<CardModel> Pile(PileType p) => p.GetPile(owner).Cards.Where(c => c != card && MythLibraryChoices.Movable(c)).ToList();
        async Task Attack(decimal amount, bool all = false, int hits = 1)
        {
            if (!ctx.CanContinue || Enemies().Count == 0 || hits <= 0) return;
            var command = DamageCmd.Attack(amount).FromCard(card, ctx.CardPlay).WithHitCount(hits);
            if (all) await command.TargetingAllOpponents(ctx.Combat!).Execute(choice);
            else if (ctx.Target() is { } target) await command.Targeting(target).Execute(choice);
        }
        async Task Recover(IEnumerable<CardModel> candidates, int count, bool free = false, PileType? destination = null)
        {
            var initial = candidates.ToList(); var originalPiles = initial.ToDictionary(c => c, c => c.Pile); var selected = await MythLibraryChoices.Select(ctx, initial, count);
            foreach (var c in selected)
            {
                if (!ctx.CanContinue || !initial.Contains(c) || c.Pile != originalPiles[c] || !MythLibraryChoices.Movable(c)) return;
                await CardPileCmd.Add(c, destination ?? PileType.Hand, destination == PileType.Draw ? CardPilePosition.Random : CardPilePosition.Bottom);
                if (free && c.Pile?.Type == PileType.Hand) MythLibraryChoices.Free(c);
            }
        }
        if (op.StartsWith("custom_power_") || op is "custom_next_attack_free" or "custom_discard_all_at_turn_end"
            or "custom_gain_half_damage_taken_this_turn" or "custom_gain_double_damage_half_taken_this_turn")
        { await MythOngoingPower.Grant(ctx, op, args.Where(a => int.TryParse(a, out _)).Select(int.Parse).ToArray()); return; }
        switch (op)
        {
            case "attack": await Attack(Amount()); break;
            case "attack_all": await Attack(Amount(), true); break;
            case "gain_block": await CreatureCmd.GainBlock(owner.Creature, (MegaCrit.Sts2.Core.Localization.DynamicVars.BlockVar)card.DynamicVars[MythEffectExecutor.Key(index, action.Op)], ctx.CardPlay); break;
            case "draw": await CardPileCmd.Draw(choice, N(), owner); break;
            case "heal": await CreatureCmd.Heal(owner.Creature, N()); break;
            case "gain_strength": await PowerCmd.Apply<StrengthPower>(choice, owner.Creature, N(), owner.Creature, card); break;
            case "gain_artifact": await PowerCmd.Apply<ArtifactPower>(choice, owner.Creature, N(), owner.Creature, card); break;
            case "apply_weak":
                if (ctx.Target() is { } weakTarget) await PowerCmd.Apply<WeakPower>(choice, weakTarget, N(), owner.Creature, card); break;
            case "apply_weak_all":
                foreach (var enemy in Enemies()) { if (!ctx.CanContinue) break; await PowerCmd.Apply<WeakPower>(choice, enemy, N(), owner.Creature, card); } break;
            case "apply_temporary_strength_loss_all":
                foreach (var enemy in Enemies()) { if (!ctx.CanContinue) break; await PowerCmd.Apply<YaoShengLuXiNaTempDownPower>(choice, enemy, N(), owner.Creature, card); } break;
            case "mill_top": await SelectToolCmd.DuiMu(choice, owner, N()); break;
            case "mill_entire_draw_pile": await SongMuHook.DeferTriggers(() => CardCmd.Discard(choice, Pile(PileType.Draw))); break;
            case "generate_random_to_hand": await Generate(ctx, args[0], N(1), PileType.Hand, args.Length == 3); break;
            case "generate_random_to_discard": await Generate(ctx, args[0], N(1), PileType.Discard); break;
            case "generate_random_to_exhaust": await Generate(ctx, args[0], N(1), PileType.Exhaust); break;
            case "add_random_banished": await Generate(ctx, "Any", N(), Entry.ChuWaiPile); break;
            case "generate_choose_random_to_hand":
            {
                // 池子只算一次。原来把 RandomPool(...) 写在过滤器里，导致每评估一张候选
                // 都要重建并排序整个卡池（几百次全排序），选牌前会白卡几十毫秒；
                // 换成一次性集合后候选与顺序完全一致。
                var poolSet = RandomPool(owner, args[0]).ToHashSet();
                await SelectToolCmd.RetrieverCard(choice, owner, poolSet.Contains, amount: N(1), minAmount: N(1), source: card, requireManualConfirmation: true);
                break;
            }
            case "retrieve_exhaust": await Recover(Pile(PileType.Exhaust), N()); break;
            case "retrieve_discard": await Recover(Pile(PileType.Discard), N()); break;
            case "retrieve_discard_or_exhaust": await Recover(Pile(PileType.Discard).Concat(Pile(PileType.Exhaust)), N()); break;
            case "retrieve_exhaust_type": await Recover(Pile(PileType.Exhaust).Where(c => MythLibraryChoices.TypeMatches(c, args[0])), N(1)); break;
            case "retrieve_exhaust_type_free": await Recover(Pile(PileType.Exhaust).Where(c => MythLibraryChoices.TypeMatches(c, args[0])), N(1), true); break;
            case "return_exhaust_to_draw": await Recover(Pile(PileType.Exhaust), N(), destination: PileType.Draw); break;
            case "choose_draw_top_to_hand": await PickTop(ctx, N(), N(1), false); break;
            case "custom_pick_draw_top_discard_rest": await PickTop(ctx, N(), N(1), true); break;
            case "draw_to_hand_limit": await DrawUntil(ctx, MaxHandSizeCalculator.Calculate(owner)); break;
            case "custom_draw_until_hand_size": await DrawUntil(ctx, N()); break;
            case "custom_draw_per_enemy": await CardPileCmd.Draw(choice, N() * Enemies().Count, owner); break;
            case "custom_draw_hand_count_multiplier": await CardPileCmd.Draw(choice, N() * PileType.Hand.GetPile(owner).Cards.Count(c => c != card), owner); break;
            case "exhaust_from_discard_up_to":
                var exhaust = await MythLibraryChoices.Select(ctx, Pile(PileType.Discard), N(), true);
                if (exhaust.Count > 0) await MaterialPayment.Exhaust(choice, owner, exhaust, exhaust.Count, PileType.Discard); break;
            case "swap_exhaust_and_discard":
                var fromExhaust = Pile(PileType.Exhaust); var fromDiscard = Pile(PileType.Discard);
                // 原始实体清单，不把已移动或被连锁移走的牌再次交换。
                foreach (var c in fromDiscard) { if (!ctx.CanContinue) return; if (c.Pile?.Type == PileType.Discard) await CardPileCmd.Add(c, PileType.Exhaust); }
                await SongMuHook.DeferTriggers(async () => { foreach (var c in fromExhaust) { if (!ctx.CanContinue) return; if (c.Pile?.Type == PileType.Exhaust) await CardPileCmd.Add(c, PileType.Discard); } });
                break;
            case "apply_truce_all_until_turn_end": await MythOngoingPower.Grant(ctx, "truce", []); break;
            case "custom_apply_damage_nullification":
                var chosenTarget = ctx.CardPlay.Target;
                if (chosenTarget?.IsAlive == true && chosenTarget.CombatState == ctx.Combat) await MythOngoingPower.Grant(ctx, "nullify", [], chosenTarget); break;
            case "execute_enemies_hp_at_most":
                foreach (var enemy in Enemies()) { if (!ctx.CanContinue) break; if (enemy.CurrentHp <= N()) await CreatureCmd.Kill(enemy); } break;
            case "custom_execute_target_hp_percent":
                if (ctx.Target() is { } executionTarget && (long)executionTarget.CurrentHp * 100 <= (long)executionTarget.MaxHp * N()) await CreatureCmd.Kill(executionTarget); break;
            case "custom_attack_double_vs_block":
                using (var scope = new FusionBlockDamageScope(card, ctx.CardPlay)) await Attack(Amount()); break;
            case "custom_attack_scaled_other_hand": await Attack(Amount() + PileType.Hand.GetPile(owner).Cards.Count(c => c != card) * N(1)); break;
            case "custom_attack_by_distinct_discard": await Attack(Amount() + Pile(PileType.Discard).Select(c => c.Id).Distinct().Count() * N(1)); break;
            case "custom_attack_by_banished_count": await Attack(Entry.ChuWaiPile.GetPile(owner).Cards.Count * N()); break;
            case "custom_attack_all_per_hand": await Attack(Amount() * PileType.Hand.GetPile(owner).Cards.Count(c => c != card), true); break;
            case "custom_attack_all_repeated": await Attack(Amount(), true, N(1)); break;
            case "custom_multiattack_scaled_attacks_played": await Attack(Amount() + MythLibraryConditions.Attacks(card) * N(2), hits: N(1)); break;
            case "custom_gain_block_repeated":
                for (int n = 0; n < N(1) && ctx.CanContinue; n++)
                    await CreatureCmd.GainBlock(owner.Creature, (MegaCrit.Sts2.Core.Localization.DynamicVars.BlockVar)card.DynamicVars[MythEffectExecutor.Key(index, action.Op)], ctx.CardPlay); break;
            case "custom_temporary_strength_loss_by_distinct_discard":
                if (ctx.Target() is { } lossTarget) await PowerCmd.Apply<YaoShengLuXiNaTempDownPower>(choice, lossTarget, Pile(PileType.Discard).Select(c => c.Id).Distinct().Count() * N(), owner.Creature, card); break;
            case "custom_auto_play_hand_attacks":
            case "custom_optional_play_costly_hand":
                var playable = Pile(PileType.Hand).Where(c => op == "custom_auto_play_hand_attacks" ? c.Type == CardType.Attack : MythLibraryChoices.Cost(c) >= N()).ToList();
                var played = await MythLibraryChoices.Select(ctx, playable, op == "custom_auto_play_hand_attacks" ? N() : 1, true);
                foreach (var c in played) { if (!ctx.CanContinue) return; if (c.Pile?.Type == PileType.Hand && MythLibraryChoices.Movable(c)) await CardCmd.AutoPlay(choice, c, null); } break;
            case "custom_play_skill_from_exhaust_free":
                var skills = await MythLibraryChoices.Select(ctx, Pile(PileType.Exhaust).Where(c => c.Type == CardType.Skill), N());
                foreach (var c in skills) { if (!ctx.CanContinue) return; if (c.Pile?.Type == PileType.Exhaust) await CardCmd.AutoPlay(choice, c, null); } break;
            case "custom_berserker_draw_until_non_attack":
                while (ctx.CanContinue)
                {
                    var drawn = (await CardPileCmd.Draw(choice, 1, owner)).ToList();
                    if (drawn.Count == 0 || drawn[0].Type != CardType.Attack || drawn[0].Pile?.Type != PileType.Hand || !MythLibraryChoices.Movable(drawn[0])) break;
                    await CardCmd.AutoPlay(choice, drawn[0], null);
                } break;
            case "custom_exhaust_top_and_damage_by_cost":
                var top = PileType.Draw.GetPile(owner).Cards.Take(N()).ToList(); int cost = top.Sum(MythLibraryChoices.Cost);
                if (top.Count > 0 && await MaterialPayment.Exhaust(choice, owner, top, top.Count, PileType.Draw) && ctx.CanContinue) await Attack(cost * N(1), true); break;
            case "custom_exhaust_all_copies_of_chosen":
                var selected = await MythLibraryChoices.Select(ctx, Pile(PileType.Hand), 1);
                if (selected.Count == 0) break;
                var copies = Pile(PileType.Hand).Concat(Pile(PileType.Draw)).Concat(Pile(PileType.Discard)).Where(c => c.Id == selected[0].Id).ToList();
                foreach (var c in copies) { if (!ctx.CanContinue) return; if (MythLibraryChoices.Movable(c)) await MaterialPayment.Exhaust(choice, owner, [c], 1, c.Pile!.Type); } break;
            case "custom_exchange_exhaust_banish":
                var banish = await MythLibraryChoices.Select(ctx, Pile(PileType.Exhaust), N(), true);
                foreach (var c in banish) { if (!ctx.CanContinue) return; if (c.Pile?.Type == PileType.Exhaust) await CardPileCmd.Add(c, Entry.ChuWaiPile); }
                var recover = await MythLibraryChoices.Select(ctx, Pile(Entry.ChuWaiPile), N(), true);
                foreach (var c in recover) { if (!ctx.CanContinue) return; if (c.Pile?.Type == Entry.ChuWaiPile) await CardPileCmd.Add(c, PileType.Exhaust); } break;
            case "custom_recall_played_cards_random_cost":
                var recalled = CombatManager.Instance.History.CardPlaysFinished.Where(e => e.HappenedThisTurn(ctx.Combat!) && e.CardPlay.Player == owner)
                    .Select(e => e.CardPlay.Card).Where(c => c != card).Distinct().ToList();
                foreach (var c in recalled)
                {
                    if (!ctx.CanContinue) return;
                    if (!c.HasBeenRemovedFromState && !MythLibraryChoices.Movable(c)) continue;
                    var item = c.HasBeenRemovedFromState ? ctx.Combat!.CloneCard(c) : c;
                    try { await CardPileCmd.Add(item, PileType.Hand); if (item.Pile?.Type == PileType.Hand) { item.EnergyCost.SetThisTurn(MythCardFactory.RngFor(owner).NextInt(4)); item.InvokeEnergyCostChanged(); } }
                    finally { if (item.Pile == null && !item.HasBeenRemovedFromState) item.RemoveFromState(); }
                } break;
            case "custom_return_self_to_hand_once_per_turn":
                if (card.CanReturnThisTurn()) await CardPileCmd.Add(card, PileType.Hand); break;
            case "custom_delayed_play_chosen_hand":
                var delayed = await MythLibraryChoices.Select(ctx, Pile(PileType.Hand), N());
                if (delayed.Count != 0) await MythOngoingPower.Grant(ctx, "delayed", [], chosen: delayed[0]); break;
            case "custom_transform_seen_card":
                var transform = await MythLibraryChoices.Select(ctx, Pile(PileType.Hand).Where(c => c.IsTransformable), N());
                var seen = await RunSeenCards.SynchronizeCandidates(choice, owner);
                var seenPool = seen.Where(c => c is not MythMutationCard && !SelectToolCmd.IsNonObtainable(c)).OrderBy(c => c.Id).ToList();
                foreach (var c in transform)
                {
                    if (!ctx.CanContinue || c.Pile?.Type != PileType.Hand || seenPool.Count == 0) return;
                    var replacement = ctx.Combat!.CreateCard(MythCardFactory.RngFor(owner).NextItem(seenPool)!, owner);
                    try { await CardCmd.Transform(c, replacement); }
                    finally { if (replacement.Pile == null && !replacement.HasBeenRemovedFromState) replacement.RemoveFromState(); }
                } break;
            case "custom_create_sheep_tokens_to_draw":
                for (int n = 0; n < N() && ctx.CanContinue; n++)
                {
                    var sheep = ctx.Combat!.CreateCard<YangYanShengWuCard>(owner); sheep.DynamicVars["Repeat"].BaseValue = 1;
                    try { await CardPileCmd.AddGeneratedCardToCombat(sheep, PileType.Draw, owner, CardPilePosition.Random); }
                    finally { if (sheep.Pile == null && !sheep.HasBeenRemovedFromState) sheep.RemoveFromState(); }
                } break;
            case "custom_purge_enemy_block_and_positive_powers":
                if (ctx.Target() is { } purge)
                {
                    if (purge.Block > 0) await CreatureCmd.LoseBlock(choice, purge, purge.Block, card.Owner.Creature);
                    foreach (var power in purge.Powers.Where(p => p.Type == PowerType.Buff).ToList()) { if (!ctx.CanContinue) return; await PowerCmd.Remove(power); }
                } break;
            case "custom_stun_enemy":
                if (ctx.Target() is { } stun) await MythStunPower.Apply(ctx, stun, N()); break;
            case "custom_stun_random_enemies":
                var targets = Enemies();
                for (int n = 0; n < N() && targets.Count > 0 && ctx.CanContinue; n++)
                { var target = MythCardFactory.RngFor(owner).NextItem(targets)!; targets.Remove(target); await MythStunPower.Apply(ctx, target, 1); } break;
            case "custom_clone_enemy_no_intent":
            case "custom_clone_enemy_mirror_damage": await MythClonePower.Spawn(ctx, ctx.Target(), op.EndsWith("mirror_damage")); break;
            case "custom_steal_enemy_intent_cards": await MythIntentTheft.Execute(ctx, N()); break;
            default: throw new InvalidOperationException("Missing library effect handler: " + op);
        }
    }
    private static async Task DrawUntil(FusionActionContext ctx, int count)
    {
        // 无上限手牌时仍只请求当前能抽到的实体，避免以int.MaxValue建立无意义循环。
        int available = PileType.Draw.GetPile(ctx.Source.Owner).Cards.Count + PileType.Discard.GetPile(ctx.Source.Owner).Cards.Count;
        int missing = Math.Min(available, Math.Max(0, count - PileType.Hand.GetPile(ctx.Source.Owner).Cards.Count));
        if (missing > 0 && ctx.CanContinue) await CardPileCmd.Draw(ctx.ChoiceContext, missing, ctx.Source.Owner);
    }
    private static async Task PickTop(FusionActionContext ctx, int reveal, int take, bool discardRest)
    {
        var owner = ctx.Source.Owner; var pile = PileType.Draw.GetPile(owner);
        if (pile.Cards.Count == 0) await CardPileCmd.ShuffleIfNecessary(ctx.ChoiceContext, owner);
        if (!ctx.CanContinue) return;
        var top = pile.Cards.Take(reveal).ToList(); var selected = await MythLibraryChoices.Select(ctx, top, take);
        foreach (var c in selected) { if (!ctx.CanContinue) return; if (c.Pile == pile) await CardPileCmd.Add(c, PileType.Hand); }
        if (discardRest) await SongMuHook.DeferTriggers(async () =>
        { foreach (var c in top.Except(selected)) { if (!ctx.CanContinue) return; if (c.Pile == pile) await CardCmd.Discard(ctx.ChoiceContext, c); } });
    }
}
