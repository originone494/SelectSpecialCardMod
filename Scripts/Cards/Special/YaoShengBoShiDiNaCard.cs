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

// 耀圣·波诗-蒂娜。PortraitPath 继承基类，PNG 文件名与本类名严格一致。
public class YaoShengBoShiDiNaCard : SelectSpecialBaseCard
{
    private bool _startedInCenter;
    private ICombatState? _startingCombat;

    public YaoShengBoShiDiNaCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 6;
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou, ..StarTags];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(14m, ValueProp.Move), new BlockVar(10m, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(SelectKeywords.YaoSheng),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        new HoverTip(new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.title"),
            new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.description").GetFormattedText()),
        ..StarHoverTips,
    ];

    // 与露西娜共用原版打出前缀和中央定义；离开手牌后仍按本次起始位置结算。
    internal void CaptureStartingPosition()
    {
        _startingCombat = CombatState;
        _startedInCenter = YaoShengHuaShiLuXiNaCard.IsAtHandCenter(this);
    }

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var player = Owner;
        var combat = CombatState;
        bool fromCenter = _startedInCenter && combat != null && _startingCombat == combat;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
        if (!fromCenter || !CanContinue()) return;

        // 格挡不受检索次数限制；每次从中央打出均可获得。
        await CreatureCmd.GainBlock(player.Creature, DynamicVars.Block, cardPlay);
        if (!CanContinue()) return;

        string key = PerCombatOnce.Key("YaoShengDiNaRetrieve", Id.ToString());
        using var reservation = PlayerEffectState.For(player).Reserve(key, PlayerEffectState.Combat());
        if (reservation == null) return;
        try
        {
            var retrieved = await SelectToolCmd.RetrieverCard(context, player,
                c => c.Id != Id && c.Tags.Contains(SelectTags.YaoShengMoFa),
                amount: 1, source: this, minAmount: 1, requireManualConfirmation: true);
            if (retrieved.Count > 0) reservation.Commit();
        }
        catch
        {
            reservation.Commit();
            throw;
        }
    }
}
