using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythCardText
{
    public static string CanonicalTitle(MythCardSnapshot s)
        => s.Condition.Kind == "none" ? s.Effect.NamePart + "·神话" : s.Condition.NamePart + "的" + s.Effect.NamePart;

    public static string Title(MythCardSnapshot s)
    {
        if (LocManager.Instance?.Language is not ("eng" or "jpn"))
            return CanonicalTitle(s);
        var title = new LocString("cards", "SELECT_SPECIAL_CARD_MOD_MYTH_" + (s.Condition.Kind == "none" ? "title_none" : "title_pair"));
        title.Add("A", Localized(s.Condition.Id, "name", s.Condition.NamePart));
        title.Add("B", Localized(s.Effect.Id, "name", s.Effect.NamePart));
        return title.GetFormattedText();
    }

    // 只替换显示文字，不改完整配方、参数、随机数或旧存档；中文继续显示已保存的原文。
    private static string Localized(string id, string suffix, string fallback)
    {
        if (LocManager.Instance?.Language is not ("eng" or "jpn")) return fallback;
        string text = LocString.GetIfExists("cards", "SELECT_SPECIAL_CARD_MOD_MYTH_" + id + "." + suffix)?.GetRawText() ?? fallback;
        if (!fallback.Contains('{') && !text.Contains('{')) return text;
        // 老模板若沿用ID却更改了参数，不能用新译文索引不存在的旧数值。
        var keys = Regex.Matches(fallback, "\\{([a-z_]+)\\}").Select(m => m.Groups[1].Value).ToHashSet();
        return keys.SetEquals(Regex.Matches(text, "\\{([a-z_]+)\\}").Select(m => m.Groups[1].Value)) ? text : fallback;
    }

    public static string EffectDescription(MythCardSnapshot s)
        => Replace(Localized(s.Effect.Id, "text", s.Effect.Description), k => s.EffectValues[k].ToString());
    public static string Description(MythMutationCard card)
    {
        var s = card.FixedSnapshot;
        if (s == null) return "";
        string condition = Replace(Localized(s.Condition.Id, "text", s.Condition.Text), k => s.ConditionValues[k].ToString());
        string effect = Replace(Localized(s.Effect.Id, "text", s.Effect.Description), k =>
        {
            for (int i = 0; i < s.Actions.Count; i++)
            {
                if (s.Actions[i].Op.StartsWith("lib_"))
                {
                    var keys = MythJson.Read<string[]>(s.Actions[i].ArgumentKeys);
                    int arg = Array.IndexOf(keys, "$" + k);
                    if (arg >= 0) return card.DynamicVars[arg == 0 ? MythEffectExecutor.Key(i, s.Actions[i].Op) : MythLibraryEffects.ArgKey(i, arg)].ToHighlightedString(false);
                }
                if (s.Actions[i].AmountKey == k) return card.DynamicVars[MythEffectExecutor.Key(i, s.Actions[i].Op)].ToHighlightedString(false);
                if (s.Actions[i].RepeatKey == k) return card.DynamicVars[MythEffectExecutor.RepeatKey(i)].ToHighlightedString(false);
            }
            return s.EffectValues[k].ToString();
        });
        return string.IsNullOrWhiteSpace(condition) ? effect : condition + "\n" + effect;
    }
    private static string Replace(string text, Func<string, string> value)
        => Regex.Replace(text, "\\{([a-z_]+)\\}", m => value(m.Groups[1].Value));
}
