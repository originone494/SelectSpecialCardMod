using System.Text.Json;

namespace SelectSpecialCardMod.Scripts.Myth;

// 外部设计库只转换数据，不执行文档中的命令或调用源卡效果。
public static class MythLibraryImport
{
    public static List<MythConditionTemplate> Conditions(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array) return MythJson.Read<List<MythConditionTemplate>>(json);
        var root = document.RootElement;
        Header(root);
        var weights = root.GetProperty("kind_weights_percent");
        if (weights.GetProperty("none").GetInt32() != 70 || weights.GetProperty("restriction").GetInt32() != 15
            || weights.GetProperty("payment").GetInt32() != 15 || !root.GetProperty("allow_same_source_for_condition_and_effect").GetBoolean())
            throw new InvalidOperationException("Unsupported myth condition distribution.");
        return root.GetProperty("modules").EnumerateArray().Select(m => new MythConditionTemplate
        {
            Id = m.GetProperty("id").GetString()!, Kind = m.GetProperty("kind").GetString()!, SourceCard = Source(m),
            NamePart = m.GetProperty("name_part").GetString()!, Text = m.GetProperty("text").GetString()!,
            Op = m.GetProperty("kind").GetString() == "none" ? "none" : "library",
            Parameters = Parameters(m), Actions = Actions(m),
        }).ToList();
    }
    public static List<MythEffectTemplate> Effects(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array) return MythJson.Read<List<MythEffectTemplate>>(json);
        var root = document.RootElement;
        Header(root);
        var costs = root.GetProperty("energy_cost_range").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        if (!root.GetProperty("allow_all_three_card_types").GetBoolean() || !costs.SequenceEqual(new[] { 0, 3 }))
            throw new InvalidOperationException("Unsupported myth cost/type distribution.");
        return document.RootElement.GetProperty("modules").EnumerateArray().Select(m => new MythEffectTemplate
        {
            Id = m.GetProperty("id").GetString()!, SourceCard = Source(m), NamePart = m.GetProperty("name_part").GetString()!,
            Target = m.GetProperty("target").GetString()!, Description = m.GetProperty("text").GetString()!,
            Parameters = Parameters(m), Actions = Actions(m),
        }).ToList();
    }
    private static void Header(JsonElement root)
    { if (root.GetProperty("schema_version").GetInt32() != 1) throw new InvalidOperationException("Unsupported myth library schema."); }
    private static string Source(JsonElement m) => m.GetProperty("source").ValueKind == JsonValueKind.Null ? "" : m.GetProperty("source").GetProperty("class").GetString()!;
    private static Dictionary<string, MythRange> Parameters(JsonElement m) => m.GetProperty("parameters").EnumerateObject().ToDictionary(p => p.Name, p =>
    {
        if (p.Value.GetProperty("step").GetInt32() != 1) throw new InvalidOperationException("Unsupported myth parameter step.");
        return new MythRange(p.Value.GetProperty("min").GetInt32(), p.Value.GetProperty("max").GetInt32());
    });
    private static List<MythActionTemplate> Actions(JsonElement m) => m.GetProperty("actions").EnumerateArray().Select(a =>
    {
        var args = a.GetProperty("args").EnumerateArray().Select(v => v.GetString()!).ToArray();
        string first = args.FirstOrDefault(v => v.StartsWith('$') || int.TryParse(v, out _)) ?? "1";
        return new MythActionTemplate { Op = "lib_" + a.GetProperty("op").GetString(), Amount = first.TrimStart('$'), Arguments = MythJson.Write(args) };
    }).ToList();
    public static MythResolvedAction Resolve(MythActionTemplate action, Dictionary<string, int> values)
    {
        var keys = MythJson.Read<string[]>(action.Arguments);
        var args = keys.Select(k => k.StartsWith('$') ? values[k[1..]].ToString(System.Globalization.CultureInfo.InvariantCulture) : k).ToArray();
        string amount = action.Amount;
        return new(action.Op, values.GetValueOrDefault(amount, int.TryParse(amount, out int number) ? number : 1), 1, "Any", amount, "1", MythJson.Write(args), action.Arguments);
    }
}
