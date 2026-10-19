using Godot;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythArtManifest
{
    public const string Fallback = "res://SelectSpecialCardMod/images/cards/SuiYuanShenHuaHeTiCard.png";
    private static readonly Lazy<IReadOnlyList<MythArt>> Data = new(() =>
    {
        var list = MythJson.Read<List<MythArt>>(Godot.FileAccess.GetFileAsString(MythConditionRegistry.Root + "myth_art_manifest.json"));
        if (list.Count == 0 || list.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
            throw new InvalidOperationException("Invalid myth art manifest.");
        foreach (var art in list)
            if (!ValidPath(art.Path) || !ResourceLoader.Exists(art.Path))
                throw new InvalidOperationException("Missing myth portrait: " + art.Path);
        return list.OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
    });
    public static IReadOnlyList<MythArt> All => Data.Value;
    public static bool ValidPath(string path) => path.StartsWith("res://SelectSpecialCardMod/images/cards/", StringComparison.Ordinal)
        && !path.Contains("..") && path.EndsWith(".png", StringComparison.Ordinal);
}
