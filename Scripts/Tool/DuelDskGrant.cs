using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Relics;
using STS2RitsuLib.RunData;
namespace SelectSpecialCardMod.Scripts.Tool;
public sealed class DuelDskGrantState
{
    public bool InitialGrantHandled { get; set; }
    public bool PreferenceResolved { get; set; }
    public bool GrantAtStart { get; set; }
}
public static class DuelDskGrant
{
    private static PlayerRunSavedData<DuelDskGrantState>? _saved;
    public static void Register()
    {
        _saved ??= RunSavedDataStore.For(Entry.ModId).RegisterPerPlayer<DuelDskGrantState>("duel_disk_initial_grant");
    }
    public static async Task Grant(Player player)
    {
        Register();
        var state = _saved!.Get(player);
        var disk = player.GetRelic<DuelDskRelic>();
        foreach (var legacy in player.Relics.OfType<DeadEndRedRelic>().ToList())
        {
            if (disk == null)
            {
                // 原版 Replace 保留遗物栏位置；迁移后也保留原来的获得楼层。
                int floor = legacy.FloorAddedToDeck;
                disk = (DuelDskRelic)await RelicCmd.Replace(legacy, ModelDb.Relic<DuelDskRelic>().ToMutable());
                disk.FloorAddedToDeck = floor;
            }
            else
            {
                // 已有决斗盘时只清理兼容遗物，不重复发放。
                await RelicCmd.Remove(legacy);
            }
        }
        // 兼容遗物迁移始终执行；配置只决定本局是否自动发放新的决斗盘。
        if (state.InitialGrantHandled) return;
        if (disk == null)
        {
            if (!state.PreferenceResolved)
            {
                state.GrantAtStart = CardAvailability.UsesPersonalDuelDiskSettings(player.RunState)
                    ? await SynchronizePreference(player)
                    : CardAvailability.ShouldGrantDuelDiskAtStart(player.RunState);
                state.PreferenceResolved = true;
                _saved.Set((RunState)player.RunState, player.NetId, state);
                Entry.Logger.Info($"[DuelDisk] Player {player.NetId} initial preference: grant={state.GrantAtStart}.");
            }
            if (state.GrantAtStart) await RelicCmd.Obtain<DuelDskRelic>(player);
        }
        state.InitialGrantHandled = true;
        _saved.Set((RunState)player.RunState, player.NetId, state);
    }

    private static async Task<bool> SynchronizePreference(Player player)
    {
        var run = (RunState)player.RunState;
        RunLifetime.Token(run).ThrowIfCancellationRequested();
        var sync = RunManager.Instance.PlayerChoiceSynchronizer;
        uint choice = sync.ReserveChoiceId(player);
        var context = new BlockingPlayerChoiceContext();
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        try
        {
            int value;
            // 只有玩家本人读取本机开关。其他客户端等待其同步结果，不能代替队友作选择。
            if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay)
            {
                value = CardAvailability.DuelDiskPreference.Read() ? 1 : 0;
                sync.SyncLocalChoice(player, choice, PlayerChoiceResult.FromIndex(value));
            }
            else
                value = (await RunLifetime.Wait(sync.WaitForRemoteChoice(player, choice), run)).AsIndex();
            RunLifetime.Token(run).ThrowIfCancellationRequested();
            if (value is not (0 or 1)) throw new InvalidOperationException("Invalid personal Duel Disk preference.");
            return value == 1;
        }
        finally { await context.SignalPlayerChoiceEnded(); }
    }
}
