using Godot;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythEffectRegistry
{
    private static readonly Lazy<IReadOnlyList<MythEffectTemplate>> Data = new(() =>
    {
        var list = MythLibraryImport.Effects(Godot.FileAccess.GetFileAsString(MythConditionRegistry.Root + "myth_effects.json"));
        if (list.Count == 0 || list.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException("Invalid myth effect IDs.");
        foreach (var effect in list) MythCardFactory.ValidateEffect(effect);
        return list.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
    });
    public static IReadOnlyList<MythEffectTemplate> All => Data.Value;
}
