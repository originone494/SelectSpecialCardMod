using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 耀圣·花诗-露西娜。PortraitPath 继承基类，使用准确类名作为 PNG 文件名。
public class YaoShengHuaShiLuXiNaCard : SelectSpecialBaseCard
{
    private bool _startedInCenter;
    private ICombatState? _startingCombat;

    public YaoShengHuaShiLuXiNaCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 6;
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou, ..StarTags];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(25m, ValueProp.Move), new DynamicVar("StrengthDown", 8m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(SelectKeywords.YaoSheng),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromPower<StrengthPower>(),
        new HoverTip(new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.title"),
            new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.description").GetFormattedText()),
        ..StarHoverTips,
    ];

    // 由 OnPlayWrapper 前缀调用：原版在 OnPlay/BeforeCardPlayed 之前已经把卡移出手牌。
    internal void CaptureStartingPosition()
    {
        _startingCombat = CombatState;
        _startedInCenter = IsAtHandCenter(this);
    }

    internal static bool IsAtHandCenter(CardModel card)
    {
        if (card.Owner == null || card.Pile?.Type != PileType.Hand) return false;
        var hand = PileType.Hand.GetPile(card.Owner).Cards;
        // 奇数张的两个索引重合；偶数张分别取中央左、右两张。
        return hand.Count > 0 && (ReferenceEquals(hand[(hand.Count - 1) / 2], card)
            || ReferenceEquals(hand[hand.Count / 2], card));
    }

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var player = Owner;
        var combat = CombatState;
        // 固定本次打出的起始位置，伤害及其他连锁改变手牌后不追溯改变判定。
        bool fromCenter = _startedInCenter && combat != null && _startingCombat == combat;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
        if (!fromCenter || !CanContinue()) return;

        if (cardPlay.Target.IsAlive)
            await PowerCmd.Apply<YaoShengLuXiNaTempDownPower>(context, cardPlay.Target,
                DynamicVars["StrengthDown"].IntValue, player.Creature, this);
        if (!CanContinue()) return;

        string key = PerCombatOnce.Key("YaoShengLuXiNaRetrieve", Id.ToString());
        using var reservation = PlayerEffectState.For(player).Reserve(key, PlayerEffectState.Combat());
        if (reservation == null) return;
        try
        {
            var retrieved = await SelectToolCmd.RetrieverCard(context, player,
                c => c.Id != Id && c.Tags.Contains(SelectTags.YaoShengGuaiShou),
                amount: 1, source: this, minAmount: 1, requireManualConfirmation: true);
            if (retrieved.Count > 0) reservation.Commit();
        }
        catch
        {
            // 入手连锁可能已发生，异常时保留次数，防止同名卡重复取得资源。
            reservation.Commit();
            throw;
        }
    }
}
