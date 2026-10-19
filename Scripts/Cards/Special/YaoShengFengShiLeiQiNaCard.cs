using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 耀圣·风诗-蕾绮娜。PortraitPath 继承基类，以准确类名作为 PNG 文件名。
public class YaoShengFengShiLeiQiNaCard : SelectSpecialBaseCard
{
    private bool _startedInCenter;
    private ICombatState? _startingCombat;

    public YaoShengFengShiLeiQiNaCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 6;
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou, ..StarTags];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(18m, ValueProp.Move), new DynamicVar("Draw", 3m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(SelectKeywords.YaoSheng),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        new HoverTip(new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.title"),
            new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.description").GetFormattedText()),
        ..StarHoverTips,
    ];

    internal void CaptureStartingPosition()
    {
        _startingCombat = CombatState;
        _startedInCenter = YaoShengHuaShiLuXiNaCard.IsAtHandCenter(this);
    }

    internal static CardPlay? PreviousOwnPlay(CardPlay current)
    {
        CardPlay? previous = null;
        // 只读取本人手动开始打出的顺序；自动打出不替代最近一次手动打出。
        // 遇到当前记录即停止，既不把自身算成上一张，也不读入之后的连锁。
        foreach (var entry in CombatManager.Instance.History.CardPlaysStarted)
        {
            if (ReferenceEquals(entry.CardPlay, current)) return previous;
            if (entry.CardPlay.Player == current.Player && !entry.CardPlay.IsAutoPlay) previous = entry.CardPlay;
        }
        return null;
    }

    internal static void MakeFreeForTurn(CardModel card)
    {
        card.EnergyCost.SetThisTurn(0);
        card.SetStarCostThisTurn(0);
        card.InvokeEnergyCostChanged();
    }

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var player = Owner;
        var combat = CombatState;
        bool fromCenter = _startedInCenter && combat != null && _startingCombat == combat;
        // 伤害和抽牌可能引发自动打出，先固定当前打出的上一张牌。
        bool previousWasYaoSheng = PreviousOwnPlay(cardPlay)?.Card.Tags.Contains(SelectTags.YaoSheng) == true;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
        if (!fromCenter || !CanContinue()) return;

        // 抽牌不要求上一张是耀圣，也不受检索的同名次数限制。
        await CardPileCmd.Draw(context, DynamicVars["Draw"].IntValue, player);
        if (!CanContinue() || !previousWasYaoSheng) return;

        string key = PerCombatOnce.Key("YaoShengLeiQiNaRetrieve", Id.ToString());
        using var reservation = PlayerEffectState.For(player).Reserve(key, PlayerEffectState.Combat());
        if (reservation == null) return;
        try
        {
            var retrieved = await SelectToolCmd.RetrieverCard(context, player,
                c => c.Id != Id && c.Tags.Contains(SelectTags.YaoShengGuaiShou),
                amount: 1, source: this, minAmount: 1, requireManualConfirmation: true);
            if (retrieved.Count == 0) return;
            reservation.Commit();
            if (!CanContinue()) return;
            foreach (var card in retrieved.Where(c => c.Owner == player && c.CombatState == combat && !c.HasBeenRemovedFromState))
                MakeFreeForTurn(card);
        }
        catch
        {
            reservation.Commit();
            throw;
        }
    }
}
