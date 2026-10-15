using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.RunData;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

public sealed class ExtraDeckRunData
{
    public int Version { get; set; }
    public List<string> InitialCards { get; set; } = [];
    public bool Migrated { get; set; }
    public bool EffectSnapshots { get; set; }
}
public static class ExtraDeckRunState
{
    private static PlayerRunSavedData<ExtraDeckRunData> _saved = null!;
    private static readonly ConditionalWeakTable<RunState, object> NewRuns = new();
    private static bool _initialized;
    public static void Init()
    {
        if (_initialized) return;
        _saved = RunSavedDataStore.For(Entry.ModId).RegisterPerPlayer<ExtraDeckRunData>("extra_deck_configuration");
        _initialized = true;
    }
    public static void NewRun(RunState run) { NewRuns.GetValue(run, _ => new object()); }
    public static bool UsesEffectSnapshots(Player player) => _saved.Get(player).EffectSnapshots;
    public static IReadOnlyList<string> Initial(Player player) => _saved.Get(player).InitialCards;
    public static async Task Ensure(Player player)
    {
        var data = _saved.Get(player);
        if (data.Version != 0) return;
        var run = (RunState)player.RunState;
        RunLifetime.Token(run).ThrowIfCancellationRequested();
        if (!NewRuns.TryGetValue(run, out _))
        {
            // Old saves have no provenance. Preserve their existing pile as a compatibility baseline.
            data.InitialCards = Entry.ExtraPile.GetPile(player).Cards.Where(c => !c.HasBeenRemovedFromState)
                .Select(c => c.Id.ToString()).ToList();
            data.Migrated = true;
        }
        else
        {
            var sync = RunManager.Instance.PlayerChoiceSynchronizer;
            var choice = sync.ReserveChoiceId(player);
            var context = new BlockingPlayerChoiceContext();
            await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
            try
            {
                IReadOnlyList<CardModel> cards;
                if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Replay)
                {
                    cards = ExtraDeckStore.Selected.Select(ExtraDeckStore.FindByKey).OfType<CardModel>().ToList();
                    sync.SyncLocalChoice(player, choice, PlayerChoiceResult.FromCanonicalCards(cards));
                }
                else cards = (await RunLifetime.Wait(sync.WaitForRemoteChoice(player, choice), run)).AsCanonicalCards().ToList();
                data.InitialCards = cards.Where(c => c.Tags.Contains(SelectTags.ExtraMonster))
                    .GroupBy(c => c.Id).SelectMany(g => g.Take(ExtraDeckStore.MaxCopiesPerName))
                    .Take(ExtraDeckStore.MaxCards).Select(c => c.Id.ToString()).ToList();
            }
            finally { await context.SignalPlayerChoiceEnded(); }
        }
        RunLifetime.Token(run).ThrowIfCancellationRequested();
        data.EffectSnapshots = RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Replay;
        data.Version = 1;
        _saved.Set(run, player.NetId, data);
    }
}
[HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
public static class ExtraDeckNewRunPatch
{
    static void Postfix(RunManager __instance)
    { if (__instance.DebugOnlyGetState() is { } run) ExtraDeckRunState.NewRun(run); }
}
