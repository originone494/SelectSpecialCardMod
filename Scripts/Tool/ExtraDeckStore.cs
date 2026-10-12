using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;
using FileAccess = Godot.FileAccess;
using Json = Godot.Json;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「额外卡组」的局外选择数据。
// 玩家在主菜单的模组设置里打开大图选卡界面，勾选本局要带的「额外怪兽」；
// 选择存在 user://SelectSpecialCardMod/extra_deck.json（全局一份，跨存档沿用），
// 开局时由 ExtraDeckGrant 把这些卡放进「额外堆」。
public static class ExtraDeckStore
{
    /// <summary>初始额外卡组总份数上限（游戏王是 15 张）。</summary>
    public const int MaxCards = 15;

    /// <summary>同一张卡最多能带几份（游戏王同名卡上限 3 张）。</summary>
    public const int MaxCopiesPerName = 3;

    private const string SaveDir = "user://SelectSpecialCardMod";
    private const string SavePath = "user://SelectSpecialCardMod/extra_deck.json";

    // 存的是卡牌类型的全名（如 SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiShuiXianCard）：
    // 比 ModelId 好读，也不受本地化/翻译变动影响。
    private static List<string>? _selected;

    public static IReadOnlyList<string> Selected => _selected ??= Load();

    public static string KeyOf(CardModel card) => card.GetType().FullName ?? card.GetType().Name;

    /// <summary>这张卡已经选了几份。</summary>
    public static int CountOf(CardModel card)
    {
        string key = KeyOf(card);
        return Selected.Count(k => k == key);
    }

    public static bool IsSelected(CardModel card) => CountOf(card) > 0;

    /// <summary>加一份。超过"同一张 3 份"或"总数 15 份"时拒绝并返回 false。</summary>
    public static bool Add(CardModel card)
    {
        var list = _selected ??= Load();
        if (CountOf(card) >= MaxCopiesPerName) return false;
        if (list.Count >= MaxCards) return false;
        list.Add(KeyOf(card));
        Save();
        return true;
    }

    /// <summary>减一份。本来就没有这份时返回 false。</summary>
    public static bool Remove(CardModel card)
    {
        var list = _selected ??= Load();
        if (!list.Remove(KeyOf(card))) return false;
        Save();
        return true;
    }

    public static void Clear()
    {
        (_selected ??= Load()).Clear();
        Save();
    }

    /// <summary>所有可选的「额外怪兽」（规范卡实例，按卡名排序）。</summary>
    public static List<CardModel> AvailableExtraMonsters()
        => ModelDb.AllCards
            .Where(c => c.Tags.Contains(SelectTags.ExtraMonster))
            .GroupBy(c => c.GetType())
            .Select(g => g.First())
            .OrderBy(c => c.Title, StringComparer.Ordinal)
            .ToList();

    /// <summary>按存的类型名找回规范卡实例；找不到（例如那个卡被删了）返回 null。</summary>
    public static CardModel? FindByKey(string key)
        => ModelDb.AllCards.FirstOrDefault(c => KeyOf(c) == key);

    private static List<string> Load()
    {
        var list = new List<string>();
        try
        {
            if (!FileAccess.FileExists(SavePath)) return list;
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (file == null) return list;
            var parsed = Json.ParseString(file.GetAsText());
            if (parsed.VariantType != Variant.Type.Array) return list;
            foreach (var item in parsed.AsGodotArray())
            {
                if (item.VariantType == Variant.Type.String) list.Add(item.AsString());
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组读取失败（当作空）：{e.Message}");
        }
        return list.Take(MaxCards).ToList();
    }

    private static void Save()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(SaveDir);
            var arr = new Godot.Collections.Array();
            foreach (var key in _selected ?? new List<string>()) arr.Add(key);
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
            file?.StoreString(Json.Stringify(arr, "  "));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组保存失败：{e.Message}");
        }
    }
}
