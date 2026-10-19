using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Settings;

public static class CardAvailabilityProbe
{
    private static HashSet<Type> _offered = [];
    private static RunState? _activeRun;
    private static bool _randomPolicy = true;
    private static int _captures;

    public static async Task Check()
    {
        CardAvailability.Register(); CardAvailability.Register();
        CheckRewardToggleExclusivity();
        CheckPolicyLifecycle();
        if (new CardContentPreferences().OnlyYuGiOh || new CardContentRunPolicy().OnlyYuGiOh)
            throw new Exception("New preferences or legacy saves unexpectedly enable filtering.");
        Type[] excluded = [typeof(ChangShiMingZiCard), typeof(CiShiBiKeZhiRenCard), typeof(DaiZuiADaiCard),
            typeof(DangNianJiaSheShanHaiTuCard), typeof(JiaShiZhenJieYuQuShenCard), typeof(YiCunJiaCard), typeof(XingBaoQiLiuZhanCard), typeof(SuiYuanShenHuaHeTiCard)];
        if (!excluded.ToHashSet().SetEquals(CardAvailability.NonYuGiOhCards))
            throw new Exception("Non-Yu-Gi-Oh classification changed; review the list explicitly.");
        Type[] allowed = [typeof(StrikeIronclad), typeof(XiaoShiJieCard), typeof(TianZhiYueCard),
            typeof(ChangShiDuanLiCard), typeof(XianShiDuanLiCard), typeof(YouShiDuanLiCard),
            typeof(JueDuiMoFaShengJieJieCard), typeof(ShengJieJieDeWuNvCard), typeof(EffectOneCard), typeof(MonsterMoveCard)];
        foreach (var type in allowed)
            if (!CardAvailability.IsAllowedType(type, true)) throw new Exception("Excluded an unrelated or infrastructure card: " + type);
        foreach (var type in excluded)
            if (CardAvailability.IsAllowedType(type, true) || !CardAvailability.IsAllowedType(type, false))
                throw new Exception("The toggle does not restore the full card pool: " + type);
        // 注册表完整保留，确保旧存档仍能解析这8张卡。
        foreach (var type in excluded)
            if (!ModelDb.AllCards.Any(c => c.GetType() == type)) throw new Exception("Filtering removed a registered card model.");

        var policy = (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
        var enabled = ArrivalEquipmentProbe.NewPlayer();
        var disabled = ArrivalEquipmentProbe.NewPlayer();
        var legacy = ArrivalEquipmentProbe.NewPlayer();
        policy.Set((RunState)enabled.RunState, new() { Version = 1, OnlyYuGiOh = true });
        policy.Set((RunState)disabled.RunState, new() { Version = 1, OnlyYuGiOh = false });
        var sample = ModelDb.Card<YiCunJiaCard>();
        if (CardAvailability.IsAllowed(sample, enabled.RunState) || !CardAvailability.IsAllowed(sample, disabled.RunState)
            || !CardAvailability.IsAllowed(sample, legacy.RunState))
            throw new Exception("Per-run snapshots or legacy-save compatibility failed.");

        var h = new Harmony("scm.card-content.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"),
                prefix: new HarmonyMethod(typeof(CardAvailabilityProbe), nameof(Choose)));
            h.Patch(AccessTools.Method(typeof(RunManager), "DebugOnlyGetState"),
                prefix: new HarmonyMethod(typeof(CardAvailabilityProbe), nameof(ActiveRun)));
            var candidates = excluded.Concat([typeof(XiaoShiJieCard), typeof(StrikeIronclad)]).ToHashSet();
            foreach (var player in new[] { enabled, disabled, legacy })
            {
                _activeRun = (RunState)player.RunState;
                _offered = [];
                await SelectToolCmd.RetrieverCard(new BlockingPlayerChoiceContext(), player, c => candidates.Contains(c.GetType()));
                var expected = player == enabled ? candidates.Except(excluded).ToHashSet() : candidates;
                if (!_offered.SetEquals(expected)) throw new Exception("Actual retrieval ignored this run's policy.");
                var pool = ModelDb.GetById<CardPoolModel>(ModelDb.GetId(typeof(SelectSpecialCardPool)));
                var unlocked = pool.GetUnlockedCards(null!, CardMultiplayerConstraint.SingleplayerOnly).Select(c => c.GetType()).ToHashSet();
                if (player == enabled ? unlocked.Overlaps(excluded) : !excluded.All(unlocked.Contains))
                    throw new Exception("Standard unlocked pool bypassed the run's policy.");
            }
            // 末端防线：即使调用方直接交入被过滤的模板，也不得创建新的副本。
            int before = PileType.Hand.GetPile(enabled).Cards.Count;
            var results = await (Task<IReadOnlyList<CardPileAddResult>>)AccessTools.Method(typeof(SelectToolCmd), "AddGeneratedCopiesToPile")
                .Invoke(null, [enabled, new List<CardModel> { sample }, PileType.Hand, 0f])!;
            if (results.Count != 0 || PileType.Hand.GetPile(enabled).Cards.Count != before)
                throw new Exception("Direct generated-copy entry bypassed card-content filtering.");
            GD.Print("PASS: card-content classification, enabled/disabled/legacy run isolation, real retrieval candidates and direct generation guard; models and infrastructure remain registered.");
        }
        finally { h.UnpatchAll(h.Id); _offered = []; _activeRun = null; }
    }

    private static void CheckPolicyLifecycle()
    {
        var policy = (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
        var h = new Harmony("scm.card-content.lifecycle.godot-framework");
        StartRunLobby? lobby = null;
        try
        {
            // 只替换偏好读取，避免探针改写玩家的真实配置；提交、导出和导入均使用真实框架。
            h.Patch(AccessTools.Method(typeof(CardAvailability), "CapturePolicy"),
                prefix: new HarmonyMethod(typeof(CardAvailabilityProbe), nameof(CapturePolicy)));
            _randomPolicy = true; _captures = 0;
            lobby = new StartRunLobby(GameMode.Standard, new NetSingleplayerGameService(), null!, 1);
            AccessTools.Method(typeof(RunSavedDataLobby), "PublishStagingEvent")
                .Invoke(null, [lobby, RunSavedDataLobbyStagingReason.Committing]);
            if (!policy.Lobby.TryGet(lobby, out var staged) || staged.Version != 8 || !staged.OnlyYuGiOh
                || !staged.RandomizeAllCardRewards || staged.AllCardRewardsSpecial || _captures != 1)
                throw new Exception("A real single-player lobby with IsHost=false failed to stage its settings.");

            var registry = typeof(RunSavedDataStore).Assembly.GetType("STS2RitsuLib.RunData.RunSavedDataRegistry", true)!;
            string payload = (string)AccessTools.Method(registry, "BuildLobbyStagingPayload").Invoke(null, [lobby])!;
            var importedRun = (RunState)ArrivalEquipmentProbe.NewPlayer().RunState;
            AccessTools.Method(registry, "ImportPayloadIntoRun").Invoke(null, [importedRun, payload]);
            if (!CardAvailability.UsesPersonalDuelDiskSettings(importedRun) || !CardAvailability.UsesStartCardSettings(importedRun)
                || !CardAvailability.UsesMythStartCardSetting(importedRun) || !CardAvailability.RandomizesCardRewards(importedRun)
                || !CardAvailability.UsesMythStartRewardSetting(importedRun)
                || CardAvailability.UsesSpecialCardRewards(importedRun)
                || CardAvailability.IsAllowed(ModelDb.Card<YiCunJiaCard>(), importedRun))
                throw new Exception("Lobby settings did not activate startup, filtering and random rewards after import.");

            _randomPolicy = false;
            Prepare(importedRun, multiplayer: false);
            if (!policy.Get(importedRun).RandomizeAllCardRewards || _captures != 1)
                throw new Exception("New-run preparation overwrote an authoritative lobby snapshot.");

            var noLobby = (RunState)ArrivalEquipmentProbe.NewPlayer().RunState;
            policy.Get(noLobby); // 其他初始化代码可能已经读取过默认v0值。
            Prepare(noLobby, multiplayer: false);
            if (policy.Get(noLobby).Version != 8 || policy.Get(noLobby).RandomizeAllCardRewards
                || !CardAvailability.UsesSpecialCardRewards(noLobby) || _captures != 2)
                throw new Exception("A new single-player run without lobby staging did not capture its settings.");

            var multiplayer = (RunState)ArrivalEquipmentProbe.NewPlayer().RunState;
            Prepare(multiplayer, multiplayer: true);
            if (policy.Get(multiplayer).Version != 0 || _captures != 2)
                throw new Exception("Multiplayer preparation used local preferences in place of host data.");
            AccessTools.Method(typeof(CardAvailability), "StagePolicy").Invoke(null,
                [new RunSavedDataLobbyStagingEvent(lobby, true, false, RunSavedDataLobbyStagingReason.Committing, DateTimeOffset.UtcNow)]);
            if (!policy.Lobby.GetOrCreate(lobby).RandomizeAllCardRewards || _captures != 2)
                throw new Exception("A client overwrote the host's staged settings.");

            var legacy = (RunState)ArrivalEquipmentProbe.NewPlayer().RunState;
            policy.Set(legacy, new() { Version = 1, OnlyYuGiOh = false });
            Prepare(legacy, multiplayer: false);
            if (policy.Get(legacy).Version != 1 || CardAvailability.UsesStartCardSettings(legacy) || _captures != 2)
                throw new Exception("Existing legacy policy was upgraded from current preferences.");
        }
        finally
        {
            if (lobby != null) { policy.Lobby.Remove(lobby); lobby.CleanUp(disconnectSession: false); }
            h.UnpatchAll(h.Id); _randomPolicy = true;
        }
        GD.Print("PASS: real single-player IsHost=false lobby commit/export/import, new-run fallback, authoritative snapshot preservation, client write isolation and legacy policy.");

        static void Prepare(RunState run, bool multiplayer) => AccessTools.Method(typeof(CardAvailability), "PreparePolicy")
            .Invoke(null, [new RunSavedDataPreparingEvent(run, multiplayer, DateTimeOffset.UtcNow)]);
    }

    public static bool CapturePolicy(ref CardContentRunPolicy __result)
    {
        _captures++;
        __result = new() { Version = 8, OnlyYuGiOh = true, RandomizeAllCardRewards = _randomPolicy, AllCardRewardsSpecial = !_randomPolicy };
        return false;
    }

    private static void CheckRewardToggleExclusivity()
    {
        // 使用真实投影绑定及生产转换逻辑；内存父绑定避免探针触碰玩家配置文件。
        var parent = ModSettingsBindings.InMemory("SelectSpecialCardMod", "reward_mode_probe", CardRewardMode.None);
        ProjectedModSettingsValueBinding<CardRewardMode, bool> Toggle(CardRewardMode mode)
            => (ProjectedModSettingsValueBinding<CardRewardMode, bool>)AccessTools.Method(typeof(CardAvailability), "CreateRewardToggle")
                .Invoke(null, [parent, mode, mode.ToString()])!;
        var special = Toggle(CardRewardMode.Special); var random = Toggle(CardRewardMode.Random);
        if (special.Read() || random.Read()) throw new Exception("Reward toggles did not start disabled.");
        special.Write(true);
        if (!special.Read() || random.Read()) throw new Exception("Enabling normal rewards also enabled random rewards.");
        random.Write(true);
        if (!random.Read() || special.Read()) throw new Exception("Random rewards did not switch normal rewards off.");
        special.Write(false);
        if (!random.Read()) throw new Exception("Disabling an inactive toggle disabled the active mode.");
        special.Write(true);
        if (!special.Read() || random.Read()) throw new Exception("Normal rewards did not switch random rewards off.");
        special.Write(false);
        if (random.Read() || special.Read()) throw new Exception("The active reward mode could not be disabled.");
        if (special.ExtraBindingsToMarkDirtyForUi.Single() != parent || random.ExtraBindingsToMarkDirtyForUi.Single() != parent)
            throw new Exception("Reward toggles do not share UI refresh dependencies.");

        var legacy = new CardContentPreferences { AllCardRewardsSpecial = true, RandomizeAllCardRewards = true, OnlyYuGiOh = true };
        var mode = (CardRewardMode)AccessTools.Method(typeof(CardAvailability), "ReadRewardMode").Invoke(null, [legacy])!;
        AccessTools.Method(typeof(CardAvailability), "WriteRewardMode").Invoke(null, [legacy, mode]);
        if (!legacy.RandomizeAllCardRewards || legacy.AllCardRewardsSpecial || !legacy.OnlyYuGiOh)
            throw new Exception("Legacy conflicting settings lost their effective mode or unrelated preferences.");
        GD.Print("PASS: mutually exclusive projected reward toggles, both switch directions, inactive disable, both-off, shared UI refresh and legacy conflict normalization; no user configuration writes.");
    }

    public static bool Choose(IReadOnlyList<CardModel> cardsIn, ref Task<IEnumerable<CardModel>> __result)
    {
        _offered = cardsIn.Select(c => c.GetType()).ToHashSet();
        __result = Task.FromResult<IEnumerable<CardModel>>([]);
        return false;
    }

    public static bool ActiveRun(ref RunState? __result) { __result = _activeRun; return false; }
}
