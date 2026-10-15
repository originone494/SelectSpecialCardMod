using Godot;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class AtomicJsonStore
{
    public static void Write(string path, string text)
        => SelectSpecialCardMod.Scripts.Rules.AtomicFile.Write(ProjectSettings.GlobalizePath(path), text);
}
