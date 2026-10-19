using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace SelectSpecialCardMod.Scripts.Tool;

public enum CardRewardMode { None, Special, Random }

public sealed class CardContentPreferences
{
    public bool OnlyYuGiOh { get; set; }
    public bool GrantDuelDiskAtStart { get; set; } = true;
    public bool ChooseSpecialCardAtStart { get; set; }
    public bool SpecialCardRewardAtStart { get; set; }
    public bool MythCardRewardAtStart { get; set; }
    public bool GrantMythCardAtStart { get; set; }
    public bool RandomizeAllCardRewards { get; set; }
    public bool AllCardRewardsSpecial { get; set; }
}

public sealed class CardContentRunPolicy
{
    public int Version { get; set; }
    public bool OnlyYuGiOh { get; set; }
    public bool RandomizeAllCardRewards { get; set; }
    public bool AllCardRewardsSpecial { get; set; }
    // 旧存档没有此字段时保持原有的自动发放行为。
    public bool GrantDuelDiskAtStart { get; set; } = true;
}

// 保留模型注册、图鉴和已有卡牌，只在产生新候选/副本的入口过滤。
public static class CardAvailability
{
    private static RunSavedData<CardContentRunPolicy>? _runPolicy;
    private static bool _registered;
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> Preference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.OnlyYuGiOh, static (data, value) => data.OnlyYuGiOh = value);
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> DuelDiskPreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.GrantDuelDiskAtStart, static (data, value) => data.GrantDuelDiskAtStart = value);
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> StartCardPreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.ChooseSpecialCardAtStart, static (data, value) => data.ChooseSpecialCardAtStart = value);
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> StartRewardPreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.SpecialCardRewardAtStart, static (data, value) => data.SpecialCardRewardAtStart = value);
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> MythCardPreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.GrantMythCardAtStart, static (data, value) => data.GrantMythCardAtStart = value);
    public static readonly ModSettingsValueBinding<CardContentPreferences, bool> StartMythRewardPreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        static data => data.MythCardRewardAtStart, static (data, value) => data.MythCardRewardAtStart = value);
    // 共用一个父绑定，写入时原子更新两个旧字段；子绑定共享刷新依赖，两个勾选同步变化。
    private static readonly ModSettingsValueBinding<CardContentPreferences, CardRewardMode> RewardModePreference = new(
        Entry.ModId, "card_content_preferences", SaveScope.Global,
        ReadRewardMode, WriteRewardMode);
    public static readonly ProjectedModSettingsValueBinding<CardRewardMode, bool> RandomRewardsPreference
        = CreateRewardToggle(RewardModePreference, CardRewardMode.Random, "randomize_all_card_rewards");
    public static readonly ProjectedModSettingsValueBinding<CardRewardMode, bool> SpecialRewardsPreference
        = CreateRewardToggle(RewardModePreference, CardRewardMode.Special, "all_card_rewards_special");

    private static CardRewardMode ReadRewardMode(CardContentPreferences data)
        => data.RandomizeAllCardRewards ? CardRewardMode.Random : data.AllCardRewardsSpecial ? CardRewardMode.Special : CardRewardMode.None;

    private static void WriteRewardMode(CardContentPreferences data, CardRewardMode mode)
    {
        data.RandomizeAllCardRewards = mode == CardRewardMode.Random;
        data.AllCardRewardsSpecial = mode == CardRewardMode.Special;
    }

    internal static ProjectedModSettingsValueBinding<CardRewardMode, bool> CreateRewardToggle(
        IModSettingsValueBinding<CardRewardMode> parent, CardRewardMode selected, string key)
        => ModSettingsBindings.Project(parent, key, mode => mode == selected,
            (mode, enabled) => enabled ? selected : mode == selected ? CardRewardMode.None : mode);

    // 明确列举来源，不能把未匹配游戏王卡库的简称、异译或动画卡自动判为非游戏王。
    public static IReadOnlyList<Type> NonYuGiOhCards { get; } = Array.AsReadOnly<Type>(
    [
        typeof(ChangShiMingZiCard),
        typeof(CiShiBiKeZhiRenCard),
        typeof(DaiZuiADaiCard),
        typeof(DangNianJiaSheShanHaiTuCard),
        typeof(JiaShiZhenJieYuQuShenCard),
        typeof(YiCunJiaCard),
        typeof(XingBaoQiLiuZhanCard),
        typeof(SuiYuanShenHuaHeTiCard),
    ]);
    private static readonly HashSet<Type> ExcludedTypes = NonYuGiOhCards.ToHashSet();

    public static void Register()
    {
        if (_registered) return;
        ModDataStore.For(Entry.ModId).Register<CardContentPreferences>(
            key: "card_content_preferences", fileName: "card_content.json", scope: SaveScope.Global,
            defaultFactory: static () => new(), autoCreateIfMissing: true);
        var preferences = ModDataStore.For(Entry.ModId).Get<CardContentPreferences>("card_content_preferences");
        if (preferences.RandomizeAllCardRewards && preferences.AllCardRewardsSpecial)
        {
            // 旧版本两个开关可同时保存为true；保留当时实际生效的随机模式。
            RewardModePreference.Write(CardRewardMode.Random);
            RewardModePreference.Save();
        }
        _runPolicy = RunSavedDataStore.For(Entry.ModId).Register<CardContentRunPolicy>(
            "card_content_policy", defaultFactory: static () => new(),
            options: new RunSavedDataOptions { WritePolicy = RunSavedDataWritePolicy.WhenSet });

        // IsHost 仅指联机主机；单人游戏 IsHost=false，仍须提交本局策略。
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataLobbyStagingEvent>(StagePolicy);
        // 无大厅的单人开局也会发出 Preparing；仅在新局缺少策略时补齐，读档不走这里。
        RitsuLibFramework.SubscribeLifecycle<RunSavedDataPreparingEvent>(PreparePolicy);
        _registered = true;
    }

    private static CardContentRunPolicy CapturePolicy()
    {
        var mode = RewardModePreference.Read();
        return new()
        {
            Version = 8,
            OnlyYuGiOh = Preference.Read(),
            RandomizeAllCardRewards = mode == CardRewardMode.Random,
            AllCardRewardsSpecial = mode == CardRewardMode.Special,
        };
    }

    private static void StagePolicy(RunSavedDataLobbyStagingEvent evt)
    {
        if (evt.Reason != RunSavedDataLobbyStagingReason.Committing || (evt.IsMultiplayer && !evt.IsHost)) return;
        var policy = CapturePolicy();
        _runPolicy!.Lobby.Set(evt.Lobby, policy);
        Entry.Logger.Info($"[CardSettings] Staged v{policy.Version}; multiplayer={evt.IsMultiplayer}; host={evt.IsHost}; onlyYuGiOh={policy.OnlyYuGiOh}; specialRewards={policy.AllCardRewardsSpecial}; randomRewards={policy.RandomizeAllCardRewards}.");
    }

    private static void PreparePolicy(RunSavedDataPreparingEvent evt)
    {
        // 联机客户端不能用本机偏好改写权威策略；已提交的策略也不能被局外设置覆盖。
        if (!evt.IsMultiplayer && (!_runPolicy!.TryGet(evt.RunState, out var existing) || existing.Version == 0))
            _runPolicy.Set(evt.RunState, CapturePolicy());
        var policy = _runPolicy!.Get(evt.RunState);
        Entry.Logger.Info($"[CardSettings] New run v{policy.Version}; multiplayer={evt.IsMultiplayer}; onlyYuGiOh={policy.OnlyYuGiOh}; specialRewards={policy.AllCardRewardsSpecial}; randomRewards={policy.RandomizeAllCardRewards}.");
    }

    public static bool IsAllowedType(Type type, bool onlyYuGiOh)
        => !onlyYuGiOh || !ExcludedTypes.Contains(type);

    public static bool IsAllowed(CardModel card, IRunState? run = null)
        => IsAllowedType(card.GetType(), run);

    public static bool IsAllowedType(Type type, IRunState? run = null)
    {
        // 大部分卡直接返回，不读取设置；原版/其他模组以及机制卡都不会进入排除表。
        if (!ExcludedTypes.Contains(type)) return true;
        var activeRun = run as RunState ?? RunManager.Instance?.DebugOnlyGetState();
        // 无策略的旧存档保持原规则，不能用本机局外配置改变已进行的游戏。
        bool enabled = activeRun != null
            ? _runPolicy?.Get(activeRun).OnlyYuGiOh == true
            : _registered && Preference.Read();
        return !enabled;
    }

    public static bool IsAllowedInConfiguration(CardModel card)
        => IsAllowedType(card.GetType(), _registered && Preference.Read());

    public static bool ShouldGrantDuelDiskAtStart(IRunState run)
        => run is not RunState state || _runPolicy?.Get(state).GrantDuelDiskAtStart != false;

    // v1/v2 旧局沿用旧策略，不能向其回放插入新的玩家选择记录。
    public static bool UsesPersonalDuelDiskSettings(IRunState run)
        => run is RunState state && _runPolicy?.Get(state).Version >= 3;

    // 只对加入本功能之后的新局启用，旧存档/旧回放不插入新的选择与奖励记录。
    public static bool UsesStartCardSettings(IRunState run)
        => run is RunState state && _runPolicy?.Get(state).Version >= 4;

    // v4及更早的存档/回放保留两位设置协议，不插入新的发放或改变已录制的选择。
    public static bool UsesMythStartCardSetting(IRunState run)
        => run is RunState state && _runPolicy?.Get(state).Version >= 5;

    // 旧局继续使用原有的三位开局设置协议，不补发新增的神话奖励。
    public static bool UsesMythStartRewardSetting(IRunState run)
        => run is RunState state && _runPolicy?.Get(state).Version >= 8;

    // 奖励在各客户端同步生成，采用开局保存的主机规则，不能读取各端实时配置。
    public static bool RandomizesCardRewards(IRunState run)
        => run is RunState state && _runPolicy?.Get(state) is { Version: >= 6, RandomizeAllCardRewards: true };

    public static bool UsesSpecialCardRewards(IRunState run)
        => !RandomizesCardRewards(run)
            && run is RunState state && _runPolicy?.Get(state) is { Version: >= 7, AllCardRewardsSpecial: true };

    public static bool ReplacesCardRewards(IRunState run)
        => RandomizesCardRewards(run) || UsesSpecialCardRewards(run);
}
