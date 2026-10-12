using STS2RitsuLib;
using STS2RitsuLib.Settings;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「额外卡组」的局外入口：挂在模组设置页（主菜单和局内暂停都能打开）。
// 真正选卡在独立的大图界面里做（ExtraDeckPickerScreen），这里只放一个按钮和说明。
public static class ExtraDeckSettings
{
    private const string LocTable = "card_selection";

    public static void Register()
    {
        RitsuLibFramework.RegisterModSettings(Entry.ModId, page => page
            .WithVisibleOnHostSurfaces(ModSettingsHostSurface.MainMenu | ModSettingsHostSurface.RunPause)
            .AddSection("extra_deck", section => section
                .WithTitle(Text("SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_SETTINGS_TITLE"))
                .AddParagraph(
                    "extra_deck_desc",
                    Text("SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_SETTINGS_TITLE"),
                    Text("SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_SETTINGS_DESC"))
                .AddButton(
                    "open_extra_deck_picker",
                    Text("SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_OPEN"),
                    Text("SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_OPEN_DESC"),
                    () => ExtraDeckPickerScreen.Open(),
                    ModSettingsButtonTone.Accent,
                    null)));
    }

    // 文案一律走本地化（card_selection.json）；取不到时兜底显示键名，方便发现漏翻。
    private static ModSettingsText Text(string key)
        => ModSettingsText.LocString(LocTable, key, key);
}
