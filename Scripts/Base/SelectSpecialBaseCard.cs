using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Base;

[RegisterCard(typeof(SelectSpecialCardPool), Inherit = true)]
public abstract class SelectSpecialBaseCard(int energyCost, CardType type, CardRarity rarity, TargetType targetType, bool shouldShowInCardLibrary = true)
    : ModCardTemplate(energyCost, type, rarity, targetType, shouldShowInCardLibrary)
{
    // ── 星级 ──
    // 有星级的卡覆写它（0 = 没有星级）。基类据此**自动**挂上对应的「N星」标签与悬停提示，
    // 卡本身不用再手写星级标签/提示；同调、超量这类"按星级算"的条件也统一读这个数字。
    public virtual int StarLevel => 0;

    private static readonly (int Level, CardTag Tag, CardKeyword Keyword)[] StarTable =
    [
        (1, SelectTags.Xing1, SelectKeywords.Xing1),
        (2, SelectTags.Xing2, SelectKeywords.Xing2),
        (3, SelectTags.Xing3, SelectKeywords.Xing3),
        (4, SelectTags.Xing4, SelectKeywords.Xing4),
        (5, SelectTags.Xing5, SelectKeywords.Xing5),
        (6, SelectTags.Xing6, SelectKeywords.Xing6),
        (7, SelectTags.Xing7, SelectKeywords.Xing7),
        (8, SelectTags.Xing8, SelectKeywords.Xing8),
        (9, SelectTags.Xing9, SelectKeywords.Xing9),
        (10, SelectTags.Xing10, SelectKeywords.Xing10),
        (11, SelectTags.Xing11, SelectKeywords.Xing11),
        (12, SelectTags.Xing12, SelectKeywords.Xing12),
    ];

    private (int Level, CardTag Tag, CardKeyword Keyword)? StarEntry
    {
        get
        {
            if (StarLevel <= 0) return null;
            foreach (var entry in StarTable)
            {
                if (entry.Level == StarLevel) return entry;
            }
            return null;
        }
    }

    /// <summary>本卡星级对应的标签（无星级时为空）。卡在 CanonicalTags 里写上 ..StarTags 即可。</summary>
    protected IEnumerable<CardTag> StarTags => StarEntry is { } entry ? [entry.Tag] : [];

    /// <summary>本卡星级对应的悬停提示（无星级时为空）。卡在 AdditionalHoverTips 里写上 ..StarHoverTips 即可。</summary>
    protected IEnumerable<IHoverTip> StarHoverTips
        => StarEntry is { } entry ? [HoverTipFactory.FromKeyword(entry.Keyword)] : [];

    /// <summary>取某个星级对应的标签（运行时改变星级时用，如「幻禄」登场后 3星→4星）。</summary>
    protected static CardTag StarTag(int level)
        => FindStar(level) is { } entry ? entry.Tag : default;

    /// <summary>取某个星级对应的关键字。</summary>
    protected static CardKeyword StarKeyword(int level)
        => FindStar(level) is { } entry ? entry.Keyword : default;

    private static (int Level, CardTag Tag, CardKeyword Keyword)? FindStar(int level)
    {
        if (level <= 0) return null;
        foreach (var entry in StarTable)
        {
            if (entry.Level == level) return entry;
        }
        return null;
    }

    // 原版靠 CardModel.GainsBlock 判断一张牌能否被「格挡类附魔」影响：
    // 灵活(Nimble) 的 CanEnchant 直接读这个属性，而它默认 false、需要逐卡声明。
    // 这里改为按 CanonicalVars 是否声明了 BlockVar 自动判定，避免每张格挡牌漏声明。
    public override bool GainsBlock => base.GainsBlock || CanonicalVars.Any(v => v is BlockVar);

    // 「登场」：卡因效果加入手牌时触发（抽牌不算，DrawWindowPatch 区分）
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card != this) return;
        await DengChangHook.OnCardAddedToHand(card);
    }

    // 「送墓」（原「灵活」）：本卡因效果被送入弃牌堆时触发。
    // 用原版唯一的弃牌事件（CardCmd.DiscardAndDraw 内部、入堆之后），
    // 因此回合结束清空手牌、正常/自动打出后的落堆都不会触发。
    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        await base.AfterCardDiscarded(choiceContext, card);
        if (card != this) return;
        await SongMuHook.OnCardDiscarded(choiceContext, card);
    }

    // 选卡提示统一注入卡名：提示文本以 {CardName} 开头（渲染为「卡名：」）。
    // 克隆传入的 LocString（table+key 不变），避免向调用方的共享静态实例注入变量。
    protected static CardSelectorPrefs CardPrefs(CardModel source, LocString prompt, int min, int max)
    {
        var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
        if (source != null) p.Add("CardName", source.Title);
        // 必选流程即使候选数量刚好够，也必须由玩家选择并确认。
        return new CardSelectorPrefs(p, min, max) { RequireManualConfirmation = min > 0 };
    }

    // 包括可选流程也要求手动确认；保留现有调用入口。
    protected static CardSelectorPrefs CardPrefsConfirm(CardModel source, LocString prompt, int min, int max)
    {
        var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
        if (source != null) p.Add("CardName", source.Title);
        return new CardSelectorPrefs(p, min, max) { RequireManualConfirmation = true };
    }

    // 原版通用提示的"带卡名"版本（文本含 {CardName}，见 card_selection.json）
    protected static LocString DiscardNamedPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_DISCARD");
    public static LocString RetrieveNamedPrompt() => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_RETRIEVE");
    public static LocString ForeseeNamedPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_FORESEE");

    // 卡图资源：路径前缀用本 mod 的挂载目录 SelectSpecialCardMod
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://SelectSpecialCardMod/images/cards/{GetType().Name}.png"
    );
}
