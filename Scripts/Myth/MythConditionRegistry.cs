using Godot;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythConditionRegistry
{
    internal const string Root = "res://SelectSpecialCardMod/config/myth/";
    private static readonly Lazy<IReadOnlyList<MythConditionTemplate>> Data = new(() =>
    {
        var list = MythLibraryImport.Conditions(Godot.FileAccess.GetFileAsString(Root + "myth_conditions.json"));
        if (list.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != list.Count
            || list.Count(c => c.Kind == "none") != 1) throw new InvalidOperationException("Invalid myth conditions.");
        foreach (var c in list) MythCardFactory.ValidateCondition(c);
        foreach (string kind in new[] { "none", "restriction", "payment" })
            if (!list.Any(c => c.Kind == kind)) throw new InvalidOperationException("Missing myth condition category.");
        return list.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
    });
    public static IReadOnlyList<MythConditionTemplate> All => Data.Value;
}
