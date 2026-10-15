using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception(name); }
var ledger = new EffectLedger<int>();
Check(!ledger.IsUnavailable("a", 1), "unused effect available");
Check(!ledger.IsCommitted("a", 1), "unused effect not shown as used");
using (var lease = ledger.Reserve("a", 1))
{
    Check(lease != null && ledger.IsUnavailable("a", 1), "reserved effect unavailable");
    Check(!ledger.IsCommitted("a", 1), "selection reservation not shown as used");
    Check(ledger.Reserve("a", 1) == null, "reentry rejected");
    Check(ledger.Reserve("b", 1) is { } other, "different effect independent");
}
Check(!ledger.IsUnavailable("a", 1), "cancel releases reservation");
Check(!ledger.IsCommitted("a", 1), "cancel leaves combat use unspent");
using (var lease = ledger.Reserve("a", 1)) lease!.Commit();
Check(ledger.IsUnavailable("a", 1) && !ledger.IsUnavailable("a", 2), "commit expires next period");
Check(ledger.IsCommitted("a", 1) && !ledger.IsCommitted("a", 2), "used display expires with period");
using (var old = ledger.Reserve("a", 2)) { ledger.Mark("a", 3); }
Check(ledger.IsUnavailable("a", 3), "old lease cannot release newer period");
var utf8 = "珠泪\n怪物技能⚔";
Check(ChoicePayloadCodec.Decode(ChoicePayloadCodec.Encode(utf8)) == utf8, "payload Unicode roundtrip");
Reject(() => ChoicePayloadCodec.Decode([-1]), "negative byte rejected");
Reject(() => ChoicePayloadCodec.Decode([256]), "invalid byte rejected");
Reject(() => ChoicePayloadCodec.Encode(new string('a', ChoicePayloadCodec.MaxBytes + 1)), "oversized payload rejected");
var known = new MonsterMoveEffects { BlockKnown = true, HealKnown = true, SelfKnown = true, PlayerKnown = true, SummonsKnown = true };
var fallback = MonsterMoveEffects.Of(5, 7, [("StrengthPower", 2)], [("WeakPower", 1)], [("MONSTER", 10)]);
var merged = MonsterMoveEffects.Merge(known, fallback);
Check(merged.Block == 0 && merged.Heal == 0 && merged.BlockKnown && merged.HealKnown, "known zeros preserved");
Check(merged.SelfPowers.Count + merged.PlayerPowers.Count + merged.SummonedMonsters.Count == 0, "known empty effects preserved");
var unknown = MonsterMoveEffects.Merge(new MonsterMoveEffects(), fallback);
Check(unknown.Block == 5 && unknown.SelfPowers.Count == 1, "unknown effects use fallback");
var snapshot = MonsterEffectSnapshot.From(merged);
var json = JsonSerializer.Serialize(snapshot);
var restored = JsonSerializer.Deserialize<MonsterEffectSnapshot>(json)!.ToEffects();
Check(restored.BlockKnown && restored.SelfKnown && restored.SelfPowers.Count == 0, "snapshot preserves zero/empty distinction");
var nonempty = MonsterEffectSnapshot.From(fallback);
var again = JsonSerializer.Deserialize<MonsterEffectSnapshot>(JsonSerializer.Serialize(nonempty))!.ToEffects();
Check(again.SelfPowers[0].Power == "StrengthPower" && again.SummonedMonsters[0].MaxHp == 10, "snapshot preserves power and summon values");
var tmp = Path.Combine(AppContext.BaseDirectory, "fixtures", "scm-architecture-" + Guid.NewGuid(), "data.json");
AtomicFile.Write(tmp, "{\"version\":1}");
Check(File.ReadAllText(tmp) == "{\"version\":1}", "atomic initial write");
AtomicFile.Write(tmp, "{\"version\":2}");
Check(File.ReadAllText(tmp) == "{\"version\":2}" && File.ReadAllText(tmp + ".bak") == "{\"version\":1}", "atomic replace retains backup");
Check(!File.Exists(tmp + ".tmp"), "atomic temporary consumed");
var fixtureDirectory = Path.GetFullPath(Path.GetDirectoryName(tmp)!);
if (!fixtureDirectory.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture escaped test output.");
Directory.Delete(fixtureDirectory, true);
var attack = (AttackCommand)RuntimeHelpers.GetUninitializedObject(typeof(AttackCommand));
var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
var frame = AttackDamageNegate.Begin(attack);
Check(AttackDamageNegate.Mark(attack, creature), "attack protection marked");
Check(!AttackDamageNegate.Mark(attack, creature), "attack protection deduplicated");
var nestedAttack = (AttackCommand)RuntimeHelpers.GetUninitializedObject(typeof(AttackCommand));
var inner = AttackDamageNegate.Begin(nestedAttack);
Check(!AttackDamageNegate.Mark(attack, creature), "nested attack isolated");
AttackDamageNegate.Restore(inner);
Check(frame.Targets.Contains(creature), "outer protection survives nested attack");
AttackDamageNegate.Restore(frame);
await AttackDamageNegate.Complete(Task.FromResult(attack), frame);
Check(frame.Targets.Count == 0, "attack protection cleared on completion");


var prompt = new MegaCrit.Sts2.Core.Localization.LocString("card_selection", "material_value");
prompt.Add("MaterialName", "连接怪兽"); prompt.Add("LinkRating", 2m);
var clonedPrompt = CardPrompts.Named(prompt, null);
Check(clonedPrompt.Variables["MaterialName"].Equals("连接怪兽") && clonedPrompt.Variables["LinkRating"].Equals(2m), "prompt clone preserves injected material variables");
clonedPrompt.Add("LinkRating", 3m);
Check(prompt.Variables["LinkRating"].Equals(2m), "prompt clone does not mutate shared variables");
var cacheKey = new object(); int queries = 0;
var cache = new TimedQueryCache<object, int>(100);
Check(cache.Get(cacheKey, 0, 1, () => ++queries) == 1 && cache.Get(cacheKey, 0, 2, () => ++queries) == 1, "UI cache reuses same state");
Check(cache.Get(cacheKey, 1, 3, () => ++queries) == 2, "UI cache invalidates by revision");
Check(cache.Get(cacheKey, 1, 103, () => ++queries) == 3, "UI cache fallback expires");
var scope = new AsyncScope<object>(); var outerObject = new object(); var innerObject = new object();
var outerScope = scope.Enter(outerObject); var innerScope = scope.Enter(innerObject);
Check(ReferenceEquals(scope.Current, innerObject), "nested scope selects inner origin");
scope.Complete(innerScope);
Check(ReferenceEquals(scope.Current, outerObject), "nested scope restores outer origin");
var started = new TaskCompletionSource(); var finish = new TaskCompletionSource();
async Task CaptureScope() { started.SetResult(); await finish.Task; Check(ReferenceEquals(scope.Current, outerObject), "async continuation retains captured scope"); }
var captured = CaptureScope(); await started.Task; scope.Restore(outerScope);
Check(scope.Current == null, "scope restores caller before completion"); finish.SetResult(); await captured; scope.Complete(outerScope);
int firstPayment = 0, secondPayment = 0;
using (EffectActivation.Begin(() => firstPayment++))
{
    using (EffectActivation.Begin(() => secondPayment++)) EffectActivation.PaymentStarted();
    EffectActivation.PaymentStarted();
}
EffectActivation.PaymentStarted();
Check(firstPayment == 1 && secondPayment == 1, "payment scope isolates and restores nested callbacks");
var saved = STS2RitsuLib.RunData.RunSavedDataStore.For("SelectSpecialCardMod").RegisterPerPlayer<ExtraDeckRunData>("extra_deck_configuration");
var run = (MegaCrit.Sts2.Core.Runs.RunState)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
saved.Set(run, 11, new() { Version = 1, InitialCards = ["card.EXTRA", "card.EXTRA"], Migrated = false, EffectSnapshots = true });
saved.Set(run, 22, new() { Version = 1, InitialCards = ["card.OTHER"], Migrated = true });
var slot = saved.GetType().GetField("_slot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(saved)!;
var documentType = typeof(STS2RitsuLib.RunData.RunSavedDataStore).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataDocument")!;
var document = Activator.CreateInstance(documentType, true)!;
slot.GetType().GetMethod("Export")!.Invoke(slot, [run, document]);
var savedJson = documentType.GetMethod("ToRootObject")!.Invoke(document, null)!.ToString()!;
var decodedDocument = documentType.GetMethod("FromJson")!.Invoke(null, [savedJson])!;
var restoredRun = (MegaCrit.Sts2.Core.Runs.RunState)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
slot.GetType().GetMethod("Import")!.Invoke(slot, [restoredRun, decodedDocument]);
Check(saved.Get(restoredRun, 11).InitialCards.SequenceEqual(["card.EXTRA", "card.EXTRA"]), "actual save preserves extra-deck copies");
Check(saved.Get(restoredRun, 11).EffectSnapshots && !saved.Get(restoredRun, 22).EffectSnapshots, "save preserves replay protocol capability separately");
Check(saved.Get(restoredRun, 22).Migrated && saved.Get(restoredRun, 22).InitialCards.SequenceEqual(["card.OTHER"]), "actual save isolates players and migration state");
var freshRun = (MegaCrit.Sts2.Core.Runs.RunState)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
Check(saved.Get(freshRun, 11).Version == 0, "fresh run does not inherit prior extra-deck configuration");

var waiting = new TaskCompletionSource<int>();
var lifetimeWait = RunLifetime.Wait(waiting.Task, freshRun);
RunLifetime.Cancel(freshRun);
bool cancelled = false;
try { await lifetimeWait; } catch (OperationCanceledException) { cancelled = true; }
Check(cancelled, "run cleanup cancels pending remote-choice waits");
Check(!RunLifetime.Token(restoredRun).IsCancellationRequested, "run cancellation is isolated");

var timingCard = (ShiJiCleanupProbeCard)RuntimeHelpers.GetUninitializedObject(typeof(ShiJiCleanupProbeCard));
var timingOwner = (MegaCrit.Sts2.Core.Entities.Players.Player)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
var timingCompletion = new TaskCompletionSource();
int timingFinished = 0;
timingCard.ExecutionFinished += source => { if (ReferenceEquals(source, timingCard)) timingFinished++; };
timingCard.Effect = () => timingCompletion.Task;
var timingTask = ShiJiHook.ActivateShiJiAsync(new BlockingPlayerChoiceContext(), timingOwner, timingCard);
Check(!timingTask.IsCompleted && timingFinished == 0, "timing UI cleanup waits for the whole effect");
timingCompletion.SetResult();
await timingTask;
Check(timingFinished == 1, "timing effect emits native completion without a card-play action");
var timingFailure = new InvalidOperationException("timing fixture failure");
timingCard.Effect = () => Task.FromException(timingFailure);
bool timingFailurePreserved = false;
try { await ShiJiHook.ActivateShiJiAsync(new BlockingPlayerChoiceContext(), timingOwner, timingCard); }
catch (InvalidOperationException e) { timingFailurePreserved = ReferenceEquals(e, timingFailure); }
Check(timingFailurePreserved && timingFinished == 2, "failed timing effect restores UI and propagates its error");
timingCard.Effect = () => Task.FromCanceled(new CancellationToken(true));
bool timingCancelled = false;
try { await ShiJiHook.ActivateShiJiAsync(new BlockingPlayerChoiceContext(), timingOwner, timingCard); }
catch (OperationCanceledException) { timingCancelled = true; }
Check(timingCancelled && timingFinished == 3, "cancelled timing effect restores UI and remains cancelled");

if (args.Contains("--patches"))
{
    var harmony = new Harmony("scm.architecture.integration");
    try
    {
        var runtime = typeof(STS2RitsuLib.Combat.AttackHits.AttackHitHook).Assembly;
        var redirect = runtime.GetType("STS2RitsuLib.Combat.AttackHits.Patches.AttackCommandExecuteHitHookPatch", true)!;
        var execute = AccessTools.Method(typeof(AttackCommand), "Execute", [typeof(PlayerChoiceContext)]);
        var machine = execute.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? typeof(AttackCommand).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Single(t => t.Name.StartsWith("<Execute>d__"));
        var next = AccessTools.Method(machine, "MoveNext");
        harmony.Patch(next, transpiler: new HarmonyMethod(AccessTools.Method(redirect, "Transpiler")));
        Check(Harmony.GetPatchInfo(next)!.Transpilers.Any(), "actual RitsuLib hit redirect installed");
        foreach (var type in new[] { typeof(DrawWindowPatch), typeof(AttackDamageScopePatch), typeof(AttackDamageOriginPatch),
            typeof(ResponseDamageScopePatch), typeof(ResponseDamageBlockPatch), typeof(ResponseHpLossPatch), typeof(ResponseAttackDamageAmountPatch),
            typeof(ExtraDeckNewRunPatch), typeof(RunSetupRoomPatch), typeof(ExtraDeckCombatStartPatch), typeof(ExtraDeckCombatEndPatch), typeof(PetAllyTurnPatch), typeof(MonsterActorScopePatch), typeof(UiRuleInvalidationPatch), typeof(RunLifetimeCleanupPatch), typeof(ChuWaiKeywordPatch) })
        {
            harmony.CreateClassProcessor(type).Patch();
            Check(true, "actual API patch registered: " + type.Name);
        }
        Check(Harmony.GetPatchInfo(next)!.Transpilers.Count == 1, "mod does not compete with framework Execute transpiler");
        var current = PatchProcessor.GetCurrentInstructions(next).ToList();
        var wrapper = AccessTools.Method(typeof(STS2RitsuLib.Combat.AttackHits.AttackHitHook), "DamageWithAttackHitHooks");
        Check(current.Any(i => i.Calls(wrapper)), "framework wrapper retained after mod patches");
        var damageMethod = AccessTools.Method(typeof(CreatureCmd), "Damage", [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>),
            typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay)]);
        harmony.Patch(damageMethod, prefix: new HarmonyMethod(typeof(DamageProbe), "Prefix") { priority = Priority.Last });
        DamageProbe.Target = creature;
        var liveFrame = AttackDamageNegate.Begin(attack);
        AttackDamageNegate.Mark(attack, creature);
        try
        {
            await STS2RitsuLib.Combat.AttackHits.AttackHitHook.DamageWithAttackHitHooks(
                new BlockingPlayerChoiceContext(), [creature], 1, ValueProp.Move, null, null, null, attack, 0, 2);
            await STS2RitsuLib.Combat.AttackHits.AttackHitHook.DamageWithAttackHitHooks(
                new BlockingPlayerChoiceContext(), [creature], 1, ValueProp.Move, null, null, null, attack, 1, 2);
            Check(DamageProbe.ProtectedHits == 2, "real framework damage calls retain whole-attack protection across hits");
            Check(DamageProbe.UnprotectedNested == 2, "nested independent damage does not inherit attack protection");
        }
        finally { AttackDamageNegate.Restore(liveFrame); await AttackDamageNegate.Complete(Task.FromResult(attack), liveFrame); }

    }
    finally { harmony.UnpatchAll(harmony.Id); }
}
Console.WriteLine($"SUCCESS {assertions} assertions");

public sealed class ShiJiCleanupProbeCard : SelectSpecialCardMod.Scripts.Cards.Special.TianZhiYueCard, IShiJiCard
{
    public Func<Task> Effect = null!;
    public IEnumerable<MegaCrit.Sts2.Core.Entities.Cards.PileType> ShiJiPiles => [];
    public bool CanActivateShiJi(MegaCrit.Sts2.Core.Entities.Players.Player player) => true;
    public Task ActivateShiJi(PlayerChoiceContext ctx, MegaCrit.Sts2.Core.Entities.Players.Player player) => Effect();
}

public static class DamageProbe
{
    public static Creature Target = null!;
    public static int ProtectedHits, UnprotectedNested;
    public static bool Prefix(PlayerChoiceContext choiceContext, decimal amount, ref Task<IEnumerable<DamageResult>> __result)
    { __result = Run(choiceContext, amount); return false; }
    private static async Task<IEnumerable<DamageResult>> Run(PlayerChoiceContext ctx, decimal amount)
    {
        if (amount == 1)
        {
            if (ResponseDamageNegate.IsWholeAttackMarked(Target)) ProtectedHits++;
            await CreatureCmd.Damage(ctx, (IEnumerable<Creature>)new[] { Target }, 2, ValueProp.Move, null, null, null);
        }
        else if (!ResponseDamageNegate.IsMarked(Target)) UnprotectedNested++;
        return Array.Empty<DamageResult>();
    }
}
