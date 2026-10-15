using System.Text;
namespace SelectSpecialCardMod.Scripts.Rules;
public static class AtomicFile
{
    public static void Write(string absolute, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var temporary = absolute + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite)) stream.Flush(true);
        if (File.Exists(absolute)) File.Replace(temporary, absolute, absolute + ".bak", true);
        else File.Move(temporary, absolute);
    }
}
