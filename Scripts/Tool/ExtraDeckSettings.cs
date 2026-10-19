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
        CardAvailability.Register();
        RitsuLibFramework.RegisterModSettings(Entry.ModId, page => page
            .WithVisibleOnHostSurfaces(ModSettingsHostSurface.MainMenu | ModSettingsHostSurface.RunPause)
            .AddSection("card_content", section => section
                .WithTitle(Text("SELECT_SPECIAL_CARD_MOD_CARD_CONTENT_TITLE"))
                .AddToggle("grant_duel_disk", Text("SELECT_SPECIAL_CARD_MOD_GRANT_DUEL_DISK"), CardAvailability.DuelDiskPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_GRANT_DUEL_DISK_DESC"))
                .AddToggle("choose_special_card_at_start", Text("SELECT_SPECIAL_CARD_MOD_START_CARD"), CardAvailability.StartCardPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_START_CARD_DESC"))
                .AddToggle("special_card_reward_at_start", Text("SELECT_SPECIAL_CARD_MOD_START_REWARD"), CardAvailability.StartRewardPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_START_REWARD_DESC"))
                .AddToggle("myth_card_reward_at_start", Text("SELECT_SPECIAL_CARD_MOD_START_MYTH_REWARD"), CardAvailability.StartMythRewardPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_START_MYTH_REWARD_DESC"))
                .AddToggle("grant_myth_card_at_start", Text("SELECT_SPECIAL_CARD_MOD_START_MYTH_CARD"), CardAvailability.MythCardPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_START_MYTH_CARD_DESC"))
                .AddToggle("all_card_rewards_special", Text("SELECT_SPECIAL_CARD_MOD_SPECIAL_CARD_REWARDS"), CardAvailability.SpecialRewardsPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_SPECIAL_CARD_REWARDS_DESC"))
                .AddToggle("randomize_all_card_rewards", Text("SELECT_SPECIAL_CARD_MOD_RANDOM_CARD_REWARDS"), CardAvailability.RandomRewardsPreference,
                    Text("SELECT_SPECIAL_CARD_MOD_RANDOM_CARD_REWARDS_DESC"))
                .AddToggle("only_yu_gi_oh", Text("SELECT_SPECIAL_CARD_MOD_ONLY_YU_GI_OH"), CardAvailability.Preference,
                    Text("SELECT_SPECIAL_CARD_MOD_ONLY_YU_GI_OH_DESC")))
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
