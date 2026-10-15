using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 右键特殊效果：选择符合意图的敌人，弃一张其他手牌并击晕；送墓仍检索珠泪下级怪兽。
public class ZhuLeiAiChangCard : SelectSpecialBaseCard, ISongMuCard, ISpecialEffectCard
{
    private static LocString TargetPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_AI_CHANG_TARGET");
    private static LocString DiscardPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_AI_CHANG_DISCARD");

    public ZhuLeiAiChangCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self) { }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei, SelectTags.SongMu, SelectTags.ZhuLeiXianJing, SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Unplayable, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
    ];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    public bool CanActivateSpecialEffect(Player player)
        => Owner == player && CombatState != null && Pile?.Type == PileType.Hand
            && PileType.Hand.GetPile(player).Cards.Any(c => c != this && c.Owner == player)
            && ZhuLeiAiChangTargets.Get(CombatState).Count > 0;

    public async Task ActivateSpecialEffect(PlayerChoiceContext context, Player player)
        => await TryActivateSpecialEffect(context, player);

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player) || CombatState == null) return false;
        var combat = CombatState;
        var target = await EnemyIntentTargetPicker.Pick(context, player, this,
            CardPrefs(this, TargetPrompt, 1, 1));
        if (target == null || CombatState != combat || !ZhuLeiAiChangTargets.IsEligible(combat, target)) return false;

        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardPrompt, 1, 1), context: context, player: player,
            filter: c => c != this && c.Owner == player, source: null!)).FirstOrDefault();
        if (selected == null || selected == this || selected.Owner != player
            || selected.Pile?.Type != PileType.Hand || CombatState != combat
            || Owner != player || Pile?.Type != PileType.Hand
            || !ZhuLeiAiChangTargets.IsEligible(combat, target)) return false;

        using var reservation = ZhuLeiAiChangTargets.Reserve(combat, target);
        if (reservation == null) return false;
        if (CombatManager.Instance.IsOverOrEnding) return false;
        // 以原版弃牌历史事件确认支付。记录发生于送墓钩子之前，
        // 即使连锁将代价牌回手或移往别处也仍算已支付。
        bool paid = false;
        var history = CombatManager.Instance.History;
        void OnHistoryChanged()
        {
            if (!paid && history.Entries.LastOrDefault() is CardDiscardedEntry entry
                && ReferenceEquals(entry.Card, selected))
            {
                paid = true;
                EffectActivation.PaymentStarted();
            }
        }
        history.Changed += OnHistoryChanged;
        try { await CardCmd.Discard(context, selected); }
        finally { history.Changed -= OnHistoryChanged; }
        if (!paid) return false;
        if (CombatManager.Instance.IsOverOrEnding || CombatState != combat
            || !ZhuLeiAiChangTargets.IsEligible(combat, target, includeReserved: true)) return true;

        try { await CreatureCmd.Stun(target); }
        catch
        {
            if (target.IsStunned) reservation.Commit();
            throw;
        }
        if (target.IsStunned) reservation.Commit();
        return true;
    }

    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await SelectToolCmd.RetrieverCard(ctx, player,
            c => ZhuLeiFilter.IsLowerMonster(c), p => p is SelectSpecialCardPool,
            1, source: this);
    }
}
