using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·神异-风暴恶魔。PortraitPath 继承基类，按本类名寻找 PNG。
public class YiXiangMingShenYiFengBaoEMoCard : YiXiangMingLinkCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [..base.CanonicalVars, new BlockVar(10m, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        ..base.AdditionalHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.ChuWai),
    ];

    public override async Task DengChangSpecial(PlayerChoiceContext context, Player player)
    {
        var combat = CombatState;
        int repeats = Counters(player);
        for (int i = 0; i < repeats && CombatState == combat && CanResolve(player); i++)
            await CreatureCmd.GainBlock(player.Creature, DynamicVars.Block, null);
    }

    private static bool IsSpellOrTrap(CardModel card)
        => card.Tags.Contains(SelectTags.YiXiangMingMoFa) || card.Tags.Contains(SelectTags.YiXiangMingXianJing);
    private static bool IsInEffectPile(CardModel card)
        => card.Pile?.Type == PileType.Exhaust || card.Pile?.Type == Entry.ChuWaiPile;
    private static bool IsCandidate(CardModel card, Player player)
        => card.Owner == player && IsSpellOrTrap(card) && IsInEffectPile(card) && !card.HasBeenRemovedFromState
           && !card.Keywords.Contains(CardKeyword.Unplayable)
           && MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(player.Creature.CombatState!, card, out _, AutoPlayType.Default);
    private static IEnumerable<CardModel> Candidates(Player player)
        => PileType.Exhaust.GetPile(player).Cards.Concat(Entry.ChuWaiPile.GetPile(player).Cards)
            .Where(c => IsCandidate(c, player));

    public override bool CanActivateSpecialEffect(Player player)
        => CanUseSpecialEffect(player) && Candidates(player).Any();
    private static LocString PlayPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_YI_XIANG_MING_FENG_BAO_PLAY");

    public override async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return false;
        var combat = CombatState;
        var chosen = (await CardSelectCmd.FromSimpleGrid(context, Candidates(player).ToList(), player,
            CardPrefs(this, PlayPrompt, 1, 1))).FirstOrDefault();
        if (chosen == null || CombatState != combat || !CanUseSpecialEffect(player) || !IsCandidate(chosen, player)) return false;
        var result = await CardPileCmd.Add(chosen, PileType.Play);
        if (!result.success || chosen.Pile?.Type != PileType.Play) return false;
        EffectActivation.PaymentStarted();
        await CardCmd.AutoPlay(context, chosen, null);
        return true;
    }
}
