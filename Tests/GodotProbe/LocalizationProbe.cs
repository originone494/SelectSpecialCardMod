using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using SelectSpecialCardMod.Scripts.Myth;

// 运行时检查需使用包含新本地化文件的PCK；静态语义检查不等于执行通过。
public static class LocalizationProbe
{
    public static void Check()
    {
        var manager = LocManager.Instance;
        string previous = manager.Language;
        var effect = MythEffectRegistry.All.First(e => e.Parameters.Count > 0);
        var condition = MythConditionRegistry.All.First(c => c.Kind == "payment");
        var snapshot = (MythCardSnapshot)AccessTools.Method(typeof(MythFullLibraryProbe), "Snapshot")
            .Invoke(null, [effect, condition, false])!;
        string saved = MythJson.Write(snapshot);
        try
        {
            var signatures = new HashSet<string>();
            foreach (string language in new[] { "eng", "jpn", "zhs" })
            {
                manager.SetLanguage(language);
                foreach (var c in MythConditionRegistry.All)
                    CheckText(c.Id, c.Text, c.Parameters.Keys);
                foreach (var e in MythEffectRegistry.All)
                    CheckText(e.Id, e.Description, e.Parameters.Keys);
                string title = MythCardText.Title(snapshot);
                string description = MythCardText.EffectDescription(snapshot);
                if (Regex.IsMatch(description, "\\{[a-z_]+\\}") || saved != MythJson.Write(snapshot)
                    || MythCardText.CanonicalTitle(snapshot) != condition.NamePart + "的" + effect.NamePart)
                    throw new Exception("Changing locale modified a saved recipe or left unresolved parameters.");
                if (language == "eng" && Regex.IsMatch(title + description, "[\\u3400-\\u9fff]"))
                    throw new Exception("The current myth library still renders Chinese in English.");
                signatures.Add(title + description);
                if (language == "zhs" && title != condition.NamePart + "的" + effect.NamePart)
                    throw new Exception("Chinese saved display text changed.");
            }
            if (signatures.Count != 3) throw new Exception("Myth display did not follow the active locale.");
            // 较旧快照若仍沿用ID但参数改变，译文必须回退，不能索引新库参数。
            manager.SetLanguage("eng");
            var legacy = snapshot with { Effect = snapshot.Effect with { Description = "旧效果{legacy_value}" },
                EffectValues = new() { ["legacy_value"] = 7 } };
            if (MythCardText.EffectDescription(legacy) != "旧效果7")
                throw new Exception("An old template with different parameters did not preserve its saved text.");
        }
        finally { manager.SetLanguage(previous); }
        GD.Print("PASS: English/Japanese/Chinese myth localization lookup, module parameters, live locale selection, immutable snapshots and legacy fallback. No card layout or network assertion.");

        static void CheckText(string id, string original, IEnumerable<string> parameters)
        {
            var text = LocString.GetIfExists("cards", "SELECT_SPECIAL_CARD_MOD_MYTH_" + id + ".text")
                ?? throw new Exception("Missing myth text: " + id);
            string raw = text.GetRawText();
            var keys = Regex.Matches(raw, "\\{([a-z_]+)\\}").Select(m => m.Groups[1].Value).ToHashSet();
            var expected = Regex.Matches(original, "\\{([a-z_]+)\\}").Select(m => m.Groups[1].Value).ToHashSet();
            if (!keys.SetEquals(expected) || !keys.IsSubsetOf(parameters))
                throw new Exception("Translated myth parameters differ: " + id);
        }
    }
}
