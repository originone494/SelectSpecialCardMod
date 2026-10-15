using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Creatures;
using FileAccess = Godot.FileAccess;   // 与 System.IO.FileAccess 重名，显式取 Godot 的

namespace SelectSpecialCardMod.Scripts.Intent;

// 「观测记录」：把怪物实际出招时**真正发出的效果**抄下来，冻结为"原始数值"给强夺副本使用。
//
// 数值不是靠"出招前后差值"猜的，而是**拦截效果命令读参数**
// （PowerCmd.Apply / CreatureCmd.GainBlock / CreatureCmd.Heal）。所以反击（如玩家身上的荆棘）、
// 自伤、受击/攻击触发的能力都不会被误算进这一招；格挡/治疗记的也是命令声明的值。
// 只有"这一招召唤出了哪些怪"用敌方怪物列表的前后对比识别（没有归因问题）。
//
// 落地方式：
//   · **全局持久化** —— 写到 user://SelectSpecialCardMod/monster_effects.json，重启游戏仍在；
//   · 每条记录带**游戏版本号**（ReleaseInfoManager 的语义版本）；
//   · 同一招式：游戏版本号**更新**时覆盖旧记录，版本相同则保持第一次的记录（冻结）。
//   · 拿不到版本号时保持冻结，不覆盖。
//
// 边界：这只怪从没出过这招 → 没有记录 → 调用方退回静态表 / 占位值。
internal static class MonsterEffectRecord
{
    private const string SaveDir = "user://SelectSpecialCardMod";
    private const string SavePath = SaveDir + "/monster_effects.json";

    // 落盘形态的一条记录
    private sealed class Entry
    {
        internal string Version = "";
        internal bool Complete;
        internal int Block;
        internal int Heal;
        internal readonly List<PowerEffect> SelfPowers = new();
        internal readonly List<PowerEffect> PlayerPowers = new();
        internal readonly List<(string Kind, int MaxHp)> Summoned = new();
    }

    private static Dictionary<string, Entry>? _store;                                  // 惰性加载
    private static readonly Dictionary<string, MonsterMoveEffects> Converted = new();   // 运行期缓存
    private static string? _gameVersion;

    // ── 当前记录状态（MonsterCardEffects.Play 在出招前后开/关）──
    private sealed class Recording
    {
        internal bool Active;
        internal string Kind = "", MoveId = "";
        internal Creature? Actor;
        internal MonsterMoveEffects? Draft;
        internal List<(string Kind, int MaxHp)> EnemiesBefore = [];
        internal Recording? Parent;
    }
    private static readonly System.Threading.AsyncLocal<Recording?> Current = new();
    private static bool _active { get => Current.Value?.Active == true; set => Current.Value!.Active = value; }
    private static string _kind { get => Current.Value!.Kind; set => Current.Value!.Kind = value; }
    private static string _moveId { get => Current.Value!.MoveId; set => Current.Value!.MoveId = value; }
    private static Creature? _actor { get => Current.Value?.Actor; set => Current.Value!.Actor = value; }
    private static MonsterMoveEffects? _draft { get => Current.Value?.Draft; set => Current.Value!.Draft = value; }
    private static List<(string Kind, int MaxHp)> _enemiesBefore { get => Current.Value!.EnemiesBefore; set => Current.Value!.EnemiesBefore = value; }

    private static string Key(string monsterKind, string moveId) => monsterKind + "|" + moveId;

    private static string GameVersion
    {
        get
        {
            if (_gameVersion != null) return _gameVersion;
            try
            {
                var info = ReleaseInfoManager.Instance;
                _gameVersion = info?.SemVer is { } semver
                    ? $"{semver.Major}.{semver.Minor}.{semver.Patch}"
                    : (info?.ReleaseInfo?.Version ?? "");
            }
            catch
            {
                _gameVersion = "";
            }
            return _gameVersion;
        }
    }

    // 是否该记录/覆盖：没记录 → 记；版本更新 → 覆盖；版本相同或读不到 → 保持冻结
    private static bool ShouldRecord(Entry? existing)
    {
        if (existing == null) return true;
        if (!SemanticVersion.TryFromString(existing.Version, out var stored) || stored == null) return true;
        if (!SemanticVersion.TryFromString(GameVersion, out var current) || current == null) return false;
        return current.CompareTo(stored) > 0;
    }

    internal static void BeginRecord(Creature actor, string? monsterKind, string? moveId)
    {
        Current.Value = new Recording { Parent = Current.Value };
        if (string.IsNullOrEmpty(monsterKind) || string.IsNullOrEmpty(moveId)) return;
        if (!ShouldRecord(GetEntry(monsterKind, moveId))) return;

        _active = true;
        _actor = actor;
        _kind = monsterKind;
        _moveId = moveId;
        _draft = new MonsterMoveEffects { BlockKnown = true, HealKnown = true, SelfKnown = true, PlayerKnown = true, SummonsKnown = true };
        _enemiesBefore = Enemies(actor);
    }

    internal static void CommitRecord()
    {
        if (_active && _draft != null)
        {
            AddSummons(_enemiesBefore, Enemies(_actor), _draft.SummonedMonsters);

            var entry = new Entry { Version = GameVersion, Complete = true, Block = _draft.Block, Heal = _draft.Heal };
            entry.SelfPowers.AddRange(_draft.SelfPowers);
            entry.PlayerPowers.AddRange(_draft.PlayerPowers);
            entry.Summoned.AddRange(_draft.SummonedMonsters);

            string key = Key(_kind, _moveId);
            Store()[key] = entry;
            Converted[key] = _draft;
            Save();
        }
        if (Current.Value != null) Current.Value.Active = false;
    }

    // ── 以下三个由效果命令的补丁调用；不在记录期间时全部空转 ──

    internal static void NotePower(string? powerName, decimal amount, Creature? target, Creature? applier)
    {
        if (!_active || !ReferenceEquals(applier, _actor) || _draft == null || target == null || amount <= 0m || string.IsNullOrEmpty(powerName)) return;
        if (ReferenceEquals(target, _actor)) AddPower(_draft.SelfPowers, powerName, (int)amount);
        else if (target.IsPlayer) AddPower(_draft.PlayerPowers, powerName, (int)amount);
    }

    internal static void NoteBlock(decimal amount, Creature? target)
    {
        if (!_active || _draft == null || amount <= 0m || !ReferenceEquals(target, _actor)) return;
        _draft.Block += (int)amount;
    }

    internal static void NoteHeal(decimal amount, Creature? target)
    {
        if (!_active || _draft == null || amount <= 0m || !ReferenceEquals(target, _actor)) return;
        _draft.Heal += (int)amount;
    }

    // 没记录返回 null（调用方退回静态表 / 占位值）
    internal static MonsterMoveEffects? Get(string? monsterKind, string? moveId)
    {
        if (string.IsNullOrEmpty(monsterKind) || string.IsNullOrEmpty(moveId)) return null;
        string key = Key(monsterKind, moveId);
        if (Converted.TryGetValue(key, out var cached)) return cached;
        var entry = GetEntry(monsterKind, moveId);
        if (entry == null) return null;

        var fx = new MonsterMoveEffects { Block = entry.Block, Heal = entry.Heal, BlockKnown = entry.Complete || entry.Block > 0, HealKnown = entry.Complete || entry.Heal > 0, SelfKnown = entry.Complete || entry.SelfPowers.Count > 0, PlayerKnown = entry.Complete || entry.PlayerPowers.Count > 0, SummonsKnown = entry.Complete || entry.Summoned.Count > 0 };
        fx.SelfPowers.AddRange(entry.SelfPowers);
        fx.PlayerPowers.AddRange(entry.PlayerPowers);
        fx.SummonedMonsters.AddRange(entry.Summoned);
        Converted[key] = fx;
        return fx;
    }

    private static Entry? GetEntry(string monsterKind, string moveId)
        => Store().TryGetValue(Key(monsterKind, moveId), out var entry) ? entry : null;

    private static void Reset() { if (Current.Value != null) Current.Value = Current.Value.Parent; }
    internal static void AbortRecord() => Reset();

    // 同一招重复施加同一种能力时把层数累加
    private static void AddPower(List<PowerEffect> into, string power, int amount)
    {
        for (int i = 0; i < into.Count; i++)
        {
            if (into[i].Power != power) continue;
            into[i] = new PowerEffect(power, into[i].Amount + amount);
            return;
        }
        into.Add(new PowerEffect(power, amount));
    }

    private static List<(string Kind, int MaxHp)> Enemies(Creature? actor)
    {
        var list = new List<(string, int)>();
        var enemies = actor?.CombatState?.Enemies;
        if (enemies == null) return list;
        foreach (var enemy in enemies)
            list.Add((enemy.Monster?.Id.Entry ?? "", enemy.MaxHp));
        return list;
    }

    private static void AddSummons(List<(string Kind, int MaxHp)> before, List<(string Kind, int MaxHp)> after,
        List<(string Kind, int MaxHp)> into)
    {
        var remaining = new Dictionary<string, int>();
        foreach (var (kind, _) in before)
            remaining[kind] = remaining.TryGetValue(kind, out int c) ? c + 1 : 1;

        foreach (var entry in after)
        {
            if (remaining.TryGetValue(entry.Kind, out int count) && count > 0)
            {
                remaining[entry.Kind] = count - 1;
                continue;
            }
            into.Add(entry);
        }
    }

    // ── 落盘 / 读盘（用 Godot 自带 JSON，避免打包裁剪时反射序列化被禁用）──

    private static Dictionary<string, Entry> Store()
    {
        if (_store != null) return _store;
        _store = Load();
        Converted.Clear();
        return _store;
    }

    private static Dictionary<string, Entry> Load()
    {
        var store = new Dictionary<string, Entry>();
        try
        {
            if (!FileAccess.FileExists(SavePath)) return store;
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (file == null) return store;
            var parsed = Json.ParseString(file.GetAsText());
            if (parsed.VariantType != Variant.Type.Dictionary) return store;

            var root = parsed.AsGodotDictionary();
            foreach (var key in root.Keys)
            {
                var value = root[key];
                if (value.VariantType != Variant.Type.Dictionary) continue;
                var entry = FromVariant(value.AsGodotDictionary());
                if (entry != null) store[key.AsString()] = entry;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 观测记录读取失败（当作空表继续）：{e.Message}");
        }
        return store;
    }

    private static void Save()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
            var root = new Godot.Collections.Dictionary();
            foreach (var pair in _store!) root[pair.Key] = ToVariant(pair.Value);
            SelectSpecialCardMod.Scripts.Tool.AtomicJsonStore.Write(SavePath, Json.Stringify(root, "  "));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 观测记录写入失败：{e.Message}");
        }
    }

    private static Godot.Collections.Dictionary ToVariant(Entry entry)
    {
        var self = new Godot.Collections.Array();
        foreach (var p in entry.SelfPowers) self.Add(new Godot.Collections.Array { p.Power, p.Amount });
        var players = new Godot.Collections.Array();
        foreach (var p in entry.PlayerPowers) players.Add(new Godot.Collections.Array { p.Power, p.Amount });
        var summoned = new Godot.Collections.Array();
        foreach (var (kind, hp) in entry.Summoned) summoned.Add(new Godot.Collections.Array { kind, hp });

        return new Godot.Collections.Dictionary
        {
            { "version", entry.Version },
            { "complete", entry.Complete },
            { "schema", 2 },
            { "block", entry.Block },
            { "heal", entry.Heal },
            { "self", self },
            { "players", players },
            { "summoned", summoned },
        };
    }

    private static Entry? FromVariant(Godot.Collections.Dictionary d)
    {
        var entry = new Entry
        {
            Version = d.TryGetValue("version", out var version) ? version.AsString() : "",
            Complete = d.TryGetValue("complete", out var complete) && complete.AsBool(),
            Block = ReadInt(d, "block"),
            Heal = ReadInt(d, "heal"),
        };
        ReadPowers(d, "self", entry.SelfPowers);
        ReadPowers(d, "players", entry.PlayerPowers);

        if (d.TryGetValue("summoned", out var summoned) && summoned.VariantType == Variant.Type.Array)
        {
            foreach (var item in summoned.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Array) continue;
                var pair = item.AsGodotArray();
                if (pair.Count < 2) continue;
                entry.Summoned.Add((pair[0].AsString(), (int)pair[1].AsInt64()));
            }
        }
        return entry;
    }

    private static int ReadInt(Godot.Collections.Dictionary d, string key)
        => d.TryGetValue(key, out var value) ? (int)value.AsInt64() : 0;

    private static void ReadPowers(Godot.Collections.Dictionary d, string key, List<PowerEffect> into)
    {
        if (!d.TryGetValue(key, out var raw) || raw.VariantType != Variant.Type.Array) return;
        foreach (var item in raw.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Array) continue;
            var pair = item.AsGodotArray();
            if (pair.Count < 2) continue;
            string power = pair[0].AsString();
            int amount = (int)pair[1].AsInt64();
            if (!string.IsNullOrEmpty(power) && amount > 0) into.Add(new PowerEffect(power, amount));
        }
    }
}
