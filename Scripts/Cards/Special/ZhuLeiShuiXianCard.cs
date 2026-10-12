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
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·水仙人鱼（融合怪兽）
//   打出：造成14点伤害
//   登场：「检索」1张除「珠泪融合怪兽」以外的「珠泪」卡，选择加入手牌或丢弃
//         （由 DengChangHook 在"本卡因效果加入手牌"时触发；有后注效果 → 不打出，改为触发它）
//   特殊效果（右键手牌发动）：从手牌选择1张攻击卡送入弃牌堆（可以选自身），
//         将弃牌堆1张「珠泪怪兽」加入手牌，那张卡在这个回合可以免费打出
//         （发动条件：手牌有攻击卡，且弃牌堆有「珠泪怪兽」）
//   送墓：从抽牌堆顶将5张卡送入弃牌堆
public class ZhuLeiShuiXianCard : SelectSpecialBaseCard, ISongMuCard, IDengChangCard, ISpecialEffectCard
{
    public ZhuLeiShuiXianCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 14 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(14m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.RongHeGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.DengChang,
        SelectTags.SpecialEffect,
        SelectTags.ExtraMonster,
    ];

    // 「登场」「特殊效果」不进关键字列表（卡面用纯文本写"登场：…""特殊效果：…"，关键字只在悬停里给说明）
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
    ];

    // 「检索」后问是否加入手牌（是→加入手牌，否→丢弃）
    private static LocString RetrieveChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_SHUI_XIAN_RETRIEVE");

    // 特殊效果：从手牌选择1张攻击卡送入弃牌堆
    private static LocString SpecialCostPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_SHUI_XIAN_SPECIAL_COST");

    // 特殊效果：从弃牌堆选1张「珠泪怪兽」加入手牌
    private static LocString SpecialRetrievePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_SHUI_XIAN_SPECIAL_RETRIEVE");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成14点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    // 登场：同名卡一回合一次。「检索」1张除「珠泪融合怪兽」以外的「珠泪」卡，选择加入手牌或丢弃
    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        // 「检索」本身是 0~1 的选择（RetrieverCard 内部用 min 0）：玩家可以先决定不检索，
        // 此时整段结束，不会有任何卡进入手牌。
        // 选中的卡此时已按生成卡加入了手牌（RetrieverCard 的默认形态）。
        var retrievedList = await SelectToolCmd.RetrieverCard(
            ctx,
            player,
            ZhuLeiFilter.IsCardExceptFusionMonster,
            p => p is SelectSpecialCardPool,
            1, source: this);
        if (retrievedList.Count == 0) return;

        CardModel retrieved = retrievedList[0];

        // 「选择加入手牌或丢弃」= 是/否，必须选一个（是 → 留在手牌，否 → 丢弃）
        if (!await SelectToolCmd.AskYesNo(ctx, player, RetrieveChoicePrompt, source: this))
        {
            // 选「否」→ 丢弃（走真正的弃牌语义，会触发被弃卡的送墓）
            await CardCmd.Discard(ctx, retrieved);
        }
    }

    // 特殊效果发动条件：① 手牌有攻击卡（可以选自身）；② 弃牌堆有「珠泪怪兽」（没有就不能发动）
    public bool CanActivateSpecialEffect(Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        if (hand == null || !hand.Cards.Any(c => c.Type == CardType.Attack)) return false;
        var discard = PileType.Discard.GetPile(player);
        return discard != null && discard.Cards.Any(ZhuLeiFilter.IsMonster);
    }

    // 特殊效果：从手牌选择1张攻击卡才能发动，
    //           从弃牌堆将1张「珠泪怪兽」加入手牌，那张卡在这个回合可以免费打出，
    //           之后，将选择的卡送入弃牌堆
    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        // ① 从手牌选择1张攻击卡（可以选自身）；先记下，最后才送入弃牌堆
        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, SpecialCostPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: c => c.Type == CardType.Attack,
            source: this)).ToList();
        if (selected.Count == 0) return;
        CardModel cost = selected[0];

        // ② 从弃牌堆将1张「珠泪怪兽」加入手牌
        var options = PileType.Discard.GetPile(player).Cards.Where(ZhuLeiFilter.IsMonster).ToList();
        if (options.Count == 0) return;

        var picked = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            options,
            player,
            CardPrefs(this, SpecialRetrievePrompt, 1, 1))).FirstOrDefault();
        if (picked == null) return;

        await CardPileCmd.Add(picked, PileType.Hand);

        // ③ 那张卡在这个回合可以免费打出（与死者苏生同款机制）
        picked.EnergyCost.SetThisTurn(0);

        // ④ 之后，将选择的卡送入弃牌堆（走真正的弃牌语义，会触发被弃卡的送墓）
        await CardCmd.Discard(choiceContext, cost);
    }

    // 送墓：从抽牌堆顶将5张卡送入弃牌堆
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await SelectToolCmd.DuiMu(ctx, player, 5);
    }
}
