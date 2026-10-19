using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Intent;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;

// 保留真实IllusionPower.AfterDeath、REVIVE_MOVE和MonsterCardEffects执行链；只替换演出及全局监听器边界。
public static class MonsterRevivalProbe
{
    private static readonly Type Deck = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterDeck", true)!;
    private static readonly Type Effects = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterCardEffects", true)!;
    private static int _heals;

    public static async Task Check()
    {
        var harmony = new Harmony("scm.monster-revival.zero-hp-probe");
        CallDeck("ClearAll");
        _heals = 0;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim)),
                prefix: new HarmonyMethod(typeof(MonsterRevivalProbe), nameof(Animation)));
            harmony.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Heal), [typeof(Creature), typeof(decimal), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(MonsterRevivalProbe), nameof(Heal)));
            harmony.Patch(AccessTools.Method(typeof(GameHook), nameof(GameHook.ShouldCreatureBeRemovedFromCombatAfterDeath)),
                prefix: new HarmonyMethod(typeof(MonsterRevivalProbe), nameof(RemovalPolicy)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(RunState), "_players").SetValue(player.RunState, new List<Player> { player });
            var combat = player.Creature.CombatState!;
            var eye = NewEye("normal-eye");
            var illusion = (IllusionPower)ModelDb.Power<IllusionPower>().ToMutable();
            illusion.ApplyInternal(eye, 1, true);
            illusion.FollowUpStateId = "DISTRACT_MOVE";

            SetHp(eye, 0);
            await illusion.AfterDeath(new BlockingPlayerChoiceContext(), eye, false, 0);
            if (!illusion.IsReviving || eye.Monster!.NextMove.Id != "REVIVE_MOVE")
                throw new Exception("Native Illusion did not put the defeated eye into its revival move.");
            CallDeck("PrepareTurn", eye);
            var revival = (MonsterMoveCard)((IReadOnlyList<CardModel>)CallDeck("HandOf", eye)!).Single();
            revival.MonsterKind = ""; // 不把测试数据写入玩家的招式观测库。
            await (Task)CallDeck("PlayTurn", eye)!;
            if (eye.CurrentHp != 6 || illusion.IsReviving || _heals != 1)
                throw new Exception("The real native revival move was skipped at zero HP or did not restore the eye to 6 HP.");

            // 再次倒下仍能重新复活；但之前的旧卡不能趁机执行。
            SetHp(eye, 0);
            await illusion.AfterDeath(new BlockingPlayerChoiceContext(), eye, false, 0);
            await Play(eye, revival);
            if (_heals != 1 || eye.CurrentHp != 0) throw new Exception("A stale revival card executed instead of the current native state.");
            var current = Card(eye, eye.Monster.NextMove);
            var enemies = (IList<Creature>)AccessTools.Field(typeof(CombatState), "_enemies").GetValue(combat)!;
            enemies.Remove(eye);
            await Play(eye, current);
            if (_heals != 1) throw new Exception("An already removed creature executed a revival move.");
            enemies.Add(eye);
            await Play(eye, current);
            if (_heals != 2 || eye.CurrentHp != 6 || illusion.IsReviving)
                throw new Exception("The eye could not revive again through the native move.");

            // 普通死亡怪／无原能力复制体不会因放宽复活检查而继续行动。
            var ordinary = NewEye("ability-free-copy");
            int actions = 0;
            var ordinaryMove = new MoveState("ORDINARY_MOVE", _ => { actions++; return Task.CompletedTask; }, new BuffIntent());
            ordinary.Monster!.SetMoveImmediate(ordinaryMove, forceTransition: true);
            var ordinaryCard = Card(ordinary, ordinaryMove);
            SetHp(ordinary, 0);
            await Play(ordinary, ordinaryCard);
            var fallback = Card(ordinary, null);
            fallback.InitMove(null, [new HealIntent()]);
            await Play(ordinary, fallback);
            if (actions != 0 || _heals != 2) throw new Exception("A dead creature without revival abilities was allowed to act.");
            SetHp(ordinary, 6);
            await Play(ordinary, ordinaryCard);
            if (actions != 1) throw new Exception("Normal living-creature native moves stopped executing.");
            GD.Print("PASS: real EyeWithTeeth Illusion revival restores 0 to 6 HP, repeats, rejects stale/removed/dead ordinary actors and preserves normal living moves.");

            Creature NewEye(string slot)
            {
                var actor = new Creature(ModelDb.Monster<EyeWithTeeth>().ToMutable(), CombatSide.Enemy, slot) { CombatState = combat };
                actor.Monster!.SetUpForCombat();
                actor.Monster.SetMoveImmediate((MoveState)actor.Monster.MoveStateMachine.States["DISTRACT_MOVE"], forceTransition: true);
                combat.AddCreature(actor);
                return actor;
            }
            MonsterMoveCard Card(Creature actor, MoveState? move)
            {
                var card = combat.CreateCard<MonsterMoveCard>(player);
                card.InitMove(move, move?.Intents ?? Array.Empty<AbstractIntent>());
                return card;
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); CallDeck("ClearAll"); }
    }

    private static object? CallDeck(string name, params object[] args) => AccessTools.Method(Deck, name).Invoke(null, args);
    private static Task Play(Creature actor, CardModel card) => (Task)AccessTools.Method(Effects, "Play").Invoke(null, [actor, card])!;
    private static void SetHp(Creature actor, int hp) => actor.SetCurrentHpInternal(hp);
    public static bool Animation(ref Task __result) { __result = Task.CompletedTask; return false; }
    public static bool Heal(Creature creature, decimal amount, ref Task __result)
    {
        _heals++;
        creature.HealInternal(amount);
        __result = Task.CompletedTask;
        return false;
    }
    public static bool RemovalPolicy(Creature creature, ref bool __result)
    {
        __result = creature.GetPower<IllusionPower>()?.ShouldCreatureBeRemovedFromCombatAfterDeath(creature) ?? true;
        return false;
    }
}
