using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelectSpecialCardMod.Scripts.Myth;

public sealed record MythRange(int Min, int Max);
public sealed record MythActionTemplate
{
    public string Op { get; init; } = "";
    public string Amount { get; init; } = "1";
    public string Repeat { get; init; } = "1";
    public string Filter { get; init; } = "Any";
    public string Arguments { get; init; } = "";
}
public sealed record MythConditionTemplate
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "none";
    public string SourceCard { get; init; } = "";
    public string NamePart { get; init; } = "";
    public string Text { get; init; } = "";
    public string Op { get; init; } = "none";
    public Dictionary<string, MythRange> Parameters { get; init; } = [];
    public List<MythActionTemplate>? Actions { get; init; }
}
public sealed record MythEffectTemplate
{
    public string Id { get; init; } = "";
    public string SourceCard { get; init; } = "";
    public string NamePart { get; init; } = "";
    public string Target { get; init; } = "self";
    public string Description { get; init; } = "";
    public Dictionary<string, MythRange> Parameters { get; init; } = [];
    public Dictionary<string, int> Upgrade { get; init; } = [];
    public int UpgradeCostDelta { get; init; }
    public List<MythActionTemplate> Actions { get; init; } = [];
}
public sealed record MythArt(string Id, string Path);
public sealed record MythResolvedAction(string Op, int Amount, int Repeat, string Filter, string AmountKey, string RepeatKey, string Arguments = "", string ArgumentKeys = "");

// 保存完整条件/配方/文字/资源路径和已随机数值，旧卡不再依赖后来修改的JSON模板。
public sealed record MythCardSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public required MythConditionTemplate Condition { get; init; }
    public required MythEffectTemplate Effect { get; init; }
    public required Dictionary<string, int> ConditionValues { get; init; }
    public required Dictionary<string, int> EffectValues { get; init; }
    public required List<MythResolvedAction> Actions { get; init; }
    public required string CardType { get; init; }
    public required int EnergyCost { get; init; }
    public required MythArt Art { get; init; }
    public int UpgradeLevel { get; init; }
    // 独立随机，不从来源卡继承。沿用此前确认的50%消耗。
    public bool Exhaust { get; init; }
    // 保存符号名称，避免自定义CardTag枚举的运行时编号影响读档；null兼容旧快照。
    public List<string>? SeriesTags { get; init; }
}

public static class MythJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options)
        ?? throw new JsonException("Missing myth data.");
}
