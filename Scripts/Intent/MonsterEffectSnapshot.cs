using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Intent;
public sealed class MonsterEffectSnapshot
{
    public int Version { get; set; } = 1;
    public int Block { get; set; }
    public int Heal { get; set; }
    public bool BlockKnown { get; set; }
    public bool HealKnown { get; set; }
    public bool SelfKnown { get; set; }
    public bool PlayerKnown { get; set; }
    public bool SummonsKnown { get; set; }
    public string Seal { get; set; } = "";
    public List<PowerData> Self { get; set; } = [];
    public List<PowerData> Players { get; set; } = [];
    public List<SummonData> Summoned { get; set; } = [];
    public sealed record PowerData(string Power, int Amount);
    public sealed record SummonData(string Kind, int MaxHp);
    internal static MonsterEffectSnapshot From(MonsterMoveEffects fx) => new()
    {
        Block = fx.Block, Heal = fx.Heal, BlockKnown = fx.BlockKnown, HealKnown = fx.HealKnown,
        SelfKnown = fx.SelfKnown, PlayerKnown = fx.PlayerKnown, SummonsKnown = fx.SummonsKnown, Seal = fx.SealJunk,
        Self = fx.SelfPowers.Select(p => new PowerData(p.Power, p.Amount)).ToList(),
        Players = fx.PlayerPowers.Select(p => new PowerData(p.Power, p.Amount)).ToList(),
        Summoned = fx.SummonedMonsters.Select(p => new SummonData(p.MonsterKind, p.MaxHp)).ToList()
    };
    internal MonsterMoveEffects ToEffects()
    {
        if (Version != 1 || Block < 0 || Heal < 0 || Self.Count + Players.Count + Summoned.Count > 256)
            throw new JsonException("Invalid monster effect snapshot.");
        var fx = new MonsterMoveEffects { Block = Block, Heal = Heal, BlockKnown = BlockKnown, HealKnown = HealKnown,
            SelfKnown = SelfKnown, PlayerKnown = PlayerKnown, SummonsKnown = SummonsKnown, SealJunk = Seal };
        fx.SelfPowers.AddRange(Self.Select(p => new PowerEffect(p.Power, p.Amount)));
        fx.PlayerPowers.AddRange(Players.Select(p => new PowerEffect(p.Power, p.Amount)));
        fx.SummonedMonsters.AddRange(Summoned.Select(p => (p.Kind, p.MaxHp)));
        return fx;
    }
    public static async Task Capture(PlayerChoiceContext ctx, Player player, MonsterMoveCard card)
    {
        // Historical replays lack this additional choice. Keep their original execution path.
        if (RunManager.Instance.NetService.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Replay
            && !SelectSpecialCardMod.Scripts.Tool.ExtraDeckRunState.UsesEffectSnapshots(player)) return;
        var run = (RunState)player.RunState;
        SelectSpecialCardMod.Scripts.Tool.RunLifetime.Token(run).ThrowIfCancellationRequested();
        var sync = RunManager.Instance.PlayerChoiceSynchronizer;
        var id = sync.ReserveChoiceId(player);
        await ctx.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        try
        {
            string json;
            if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Replay)
            {
                json = JsonSerializer.Serialize(From(IntentCardMapper.EffectsFor(card) ?? new MonsterMoveEffects()));
                sync.SyncLocalChoice(player, id, PlayerChoiceResult.FromIndexes(ChoicePayloadCodec.Encode(json)));
            }
            else json = ChoicePayloadCodec.Decode((await SelectSpecialCardMod.Scripts.Tool.RunLifetime.Wait(sync.WaitForRemoteChoice(player, id), run)).AsIndexes());
            SelectSpecialCardMod.Scripts.Tool.RunLifetime.Token(run).ThrowIfCancellationRequested();
            card.EffectSnapshot = json;
        }
        finally { await ctx.SignalPlayerChoiceEnded(); }
    }
}
