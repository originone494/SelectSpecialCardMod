using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace SelectSpecialCardMod.Scripts.Intent;

// 文案模板：正文放在本地化文件（localization/zhs/cards.json）里，代码只引用键。
// 想加语言只要复制一份 localization/<lang>/cards.json，不用改代码。
internal static class MonsterText
{
    internal const string Table = "cards";
    internal const string Damage = "SELECT_SPECIAL_CARD_MOD_TEXT_DAMAGE";
    internal const string DamageMulti = "SELECT_SPECIAL_CARD_MOD_TEXT_DAMAGE_MULTI";
    internal const string Block = "SELECT_SPECIAL_CARD_MOD_TEXT_BLOCK";
    internal const string Heal = "SELECT_SPECIAL_CARD_MOD_TEXT_HEAL";
    internal const string GainPower = "SELECT_SPECIAL_CARD_MOD_TEXT_GAIN_POWER";
    internal const string ApplyPower = "SELECT_SPECIAL_CARD_MOD_TEXT_APPLY_POWER";
    internal const string PlaceholderDamage = "SELECT_SPECIAL_CARD_MOD_TEXT_PLACEHOLDER_DAMAGE";
    // 意图兜底文案（游戏自己的意图文案读不到时用）
    internal const string IntentLabelPrefix = "SELECT_SPECIAL_CARD_MOD_INTENT_LABEL_";
    // 卡名兜底（招式名读不到、用意图类型拼卡名时用）
    internal const string IntentNamePrefix = "SELECT_SPECIAL_CARD_MOD_INTENT_NAME_";
    // 怪物头顶的费用标签
    internal const string EnergyLabel = "SELECT_SPECIAL_CARD_MOD_TEXT_ENERGY_LABEL";
    internal const string IntentSeparator = "\n";

    internal static string Of(string key, params (string Name, string Value)[] args)
    {
        try
        {
            var loc = new LocString(Table, key);
            foreach (var (name, value) in args) loc.Add(name, value);
            return loc.GetFormattedText();
        }
        catch
        {
            return key;   // 本地化取不到时兜底返回键名，至少不会崩
        }
    }

    // 纯文本模板（无占位符）：取不到时用传入的兜底（一般给个非中文的标识，方便发现漏翻译）
    internal static string OrDefault(string key, string fallback)
    {
        try
        {
            var loc = new LocString(Table, key);
            return loc.Exists() ? loc.GetFormattedText() : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

// 一条「能力效果」：能力种类 + 层数。
// Power 存 PowerModel 的类名（如 StrengthPower / VulnerablePower / DexterityPower …）——
// 强化不一定是力量、削弱不一定是虚弱，所以能力种类必须照搬，不能写死。
internal readonly struct PowerEffect
{
    internal readonly string Power;
    internal readonly int Amount;

    internal PowerEffect(string power, int amount)
    {
        Power = power;
        Amount = amount;
    }
}

// 一个招式「真实施加了多少」的数值集合。
// 「观测记录」（MonsterEffectRecord）与「静态表」（MonsterEffectTable）共用同一个结构，
// 调用方按 观测 → 静态表 → 占位值 的顺序取用；数值 0 / 空列表表示没有值（退回占位值）。
internal sealed class MonsterMoveEffects
{
    internal int Block;
    internal int Heal;
    internal readonly List<PowerEffect> SelfPowers = new();    // 怪物给自己加的能力（强化）
    internal readonly List<PowerEffect> PlayerPowers = new();  // 怪物给玩家加的能力（多为减益）
    // 这一招真正召唤出来的怪物（种类 + 生命）：只能是观测到的，静态表给不出
    internal readonly List<(string MonsterKind, int MaxHp)> SummonedMonsters = new();
    // 非空 = 打出这张副本时改为"给目标怪塞一张指定废牌"（值："status" / "curse" / "knowledge"）
    internal string SealJunk = "";

    // 便利构造（静态表用；对象初始化器无法给只读 List 做嵌套集合初始化）
    internal static MonsterMoveEffects Of(int block = 0, int heal = 0,
        (string Power, int Amount)[]? selfPowers = null,
        (string Power, int Amount)[]? playerPowers = null,
        (string MonsterKind, int MaxHp)[]? summoned = null)
    {
        var fx = new MonsterMoveEffects { Block = block, Heal = heal };
        if (selfPowers != null)
            foreach (var (power, amount) in selfPowers) fx.SelfPowers.Add(new PowerEffect(power, amount));
        if (playerPowers != null)
            foreach (var (power, amount) in playerPowers) fx.PlayerPowers.Add(new PowerEffect(power, amount));
        if (summoned != null)
            foreach (var (kind, hp) in summoned) fx.SummonedMonsters.Add((kind, hp));
        return fx;
    }

    // 逐字段合并：观测值优先（数值 >0 / 列表非空），否则用静态表；两者都没有就保持 0 / 空。
    // 结算与卡面描述都走这一份逻辑，保证"看到的 = 打出的"。
    internal static MonsterMoveEffects Merge(MonsterMoveEffects? observed, MonsterMoveEffects? fromTable)
    {
        var fx = new MonsterMoveEffects
        {
            Block = observed is { Block: > 0 } ? observed.Block : (fromTable?.Block ?? 0),
            Heal = observed is { Heal: > 0 } ? observed.Heal : (fromTable?.Heal ?? 0),
        };
        MergeList(fx.SelfPowers, observed?.SelfPowers, fromTable?.SelfPowers);
        MergeList(fx.PlayerPowers, observed?.PlayerPowers, fromTable?.PlayerPowers);
        MergeList(fx.SummonedMonsters, observed?.SummonedMonsters, fromTable?.SummonedMonsters);
        fx.SealJunk = !string.IsNullOrEmpty(observed?.SealJunk)
            ? observed!.SealJunk
            : (fromTable?.SealJunk ?? "");
        return fx;

        static void MergeList<T>(List<T> into, List<T>? a, List<T>? b)
        {
            var src = a is { Count: > 0 } ? a : b;
            if (src != null) into.AddRange(src);
        }
    }
}

// 按类名找能力的规范实例（供把记录到的能力原样施加出去）。
internal static class MonsterPowerLookup
{
    private static readonly Dictionary<string, PowerModel?> Cache = new();

    internal static PowerModel? FindCanonical(string? className)
    {
        if (string.IsNullOrEmpty(className)) return null;
        if (Cache.TryGetValue(className, out var cached)) return cached;

        PowerModel? found = null;
        foreach (var power in ModelDb.AllPowers)
        {
            if (power.GetType().Name == className)
            {
                found = power;
                break;
            }
        }
        Cache[className] = found;
        return found;
    }
}
