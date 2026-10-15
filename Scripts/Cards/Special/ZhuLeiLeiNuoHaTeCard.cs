using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class ZhuLeiLeiNuoHaTeCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiLeiNuoHaTeCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 12 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiXiaJiGuaiShou,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成12点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);

        // 「检索」并丢弃1张「珠泪·雷诺哈特」以外的「珠泪下级怪兽」卡
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => ZhuLeiFilter.IsLowerMonster(c)
                 && c is not ZhuLeiLeiNuoHaTeCard,
            p => p is SelectSpecialCardPool,
            1, true, source: this);
    }

    private static LocString SongMuChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_LEI_NUO_HA_TE_SONG_MU");

    // 送墓：同名卡一回合一次。若手牌有「珠泪」卡，可以选择1张「珠泪」卡丢弃，将这张卡打出。
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        // ① 同名卡一回合一次
        string onceKey = PerTurnOnce.Key("SongMu", Id.ToString());
        if (PerTurnOnce.IsUsed(player, onceKey)) return;

        // ② 条件：手牌有「珠泪」卡（任意珠泪卡，含融合怪兽）
        if (!PileType.Hand.GetPile(player).Cards.Any(ZhuLeiFilter.IsCard)) return;

        // ③ 是/否询问（网格 + 提示文本，复用「是」「否」辅助卡）
        var shi = player.Creature.CombatState!.CreateCard<ShiCard>(player);
        var fou = player.Creature.CombatState!.CreateCard<FouCard>(player);
        CardModel? picked = (await CardSelectCmd.FromSimpleGrid(
            ctx,
            new List<CardModel> { shi, fou },
            player,
            CardPrefs(this, SongMuChoicePrompt, 1, 1))).FirstOrDefault();
        if (picked is not ShiCard) return; // 否/取消 → 不发动

        // ④ 玩家一确认就记账：这样连锁里再丢掉一张同名卡时，它的送墓不会二次发动
        PerTurnOnce.Mark(player, onceKey);

        // ⑤ 选择1张「珠泪」卡丢弃
        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: ctx,
            player: player,
            filter: ZhuLeiFilter.IsCard,
            source: this)).ToList();
        if (selected.Count == 0) return;

        await CardCmd.Discard(ctx, selected[0]);

        // ⑥ 将这张卡打出（本卡此刻在弃牌堆里，走"从弃牌堆自动打出"）
        await SongMuHook.AutoPlayFromDiscard(ctx, this, null);
    }
}