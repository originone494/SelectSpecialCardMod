using System.Text;
namespace SelectSpecialCardMod.Scripts.Rules;
public static class ChoicePayloadCodec
{
    public const int MaxBytes = 16384;
    public static List<int> Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxBytes) throw new ArgumentException("Choice payload is too large.");
        return bytes.Select(b => (int)b).ToList();
    }
    public static string Decode(IReadOnlyList<int> values)
    {
        if (values.Count > MaxBytes || values.Any(v => v < 0 || v > 255)) throw new ArgumentException("Invalid choice payload.");
        return new UTF8Encoding(false, true).GetString(values.Select(v => (byte)v).ToArray());
    }
}
