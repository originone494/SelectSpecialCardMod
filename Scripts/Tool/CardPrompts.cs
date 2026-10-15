using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class CardPrompts
{
    public static LocString Named(LocString prompt, CardModel? source)
    {
        var copy = new LocString(prompt.LocTable, prompt.LocEntryKey);
        copy.AddVariablesFrom(prompt);
        if (source != null) copy.Add("CardName", source.Title);
        return copy;
    }
}
