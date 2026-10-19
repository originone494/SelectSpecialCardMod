using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;

namespace SelectSpecialCardMod.Scripts.Myth;

// 旧“神话合体”是需要绑定配方的结果模板；控制台直接生成它只会得到空卡。
// 仅重定向card命令，不改模型ID或存档反序列化，已生成的旧卡继续保留原配方。
[HarmonyPatch(typeof(CardConsoleCmd), nameof(CardConsoleCmd.Process))]
public static class MythConsoleCardAlias
{
    public const string LegacyTemplateId = "SELECT_SPECIAL_CARD_MOD_CARD_SHEN_HUA_COMPOSITE_CARD";
    public const string SourceCardId = "SELECT_SPECIAL_CARD_MOD_CARD_SUI_YUAN_SHEN_HUA_HE_TI_CARD";
    public static void Prefix(ref string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], LegacyTemplateId, StringComparison.OrdinalIgnoreCase)) return;
        args = (string[])args.Clone();
        args[0] = SourceCardId;
        Entry.Logger.Info("[Myth] Console card alias: legacy result template -> 随缘神话合体.");
    }
}
