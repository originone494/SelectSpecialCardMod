using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Base;

public abstract class XyzMonsterCard(int cost) : SelectSpecialBaseCard(cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy),
    IExtraDeckSummonCard, IExtraDeckSummonStaging
{
    public abstract int XyzRank { get; }
    public abstract int RequiredMaterialCount { get; }
    [SavedProperty] public int MaterialGroupId { get; set; }
    [SavedProperty] public string MaterialOwnerKey { get; set; } = "";
    [SavedProperty] public bool MaterialsStaged { get; set; }
    public IReadOnlyList<CardModel> XyzMaterialCards => XyzMaterials.Cards(this);

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.ExtraMonster, SelectTags.ChaoLiang];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips
    {
        get
        {
            yield return HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster);
            yield return HoverTipFactory.FromKeyword(SelectKeywords.ChaoLiang);
            if (CombatState == null) yield break;
            var title = new LocString("cards", "SELECT_SPECIAL_CARD_MOD_XYZ_MATERIALS_TITLE");
            title.Add("Count", XyzMaterialCards.Count);
            yield return new HoverTip(title, XyzMaterialCards.Count == 0
                ? new LocString("cards", "SELECT_SPECIAL_CARD_MOD_XYZ_MATERIALS_EMPTY").GetFormattedText()
                : string.Join("\n", XyzMaterialCards.Select(c => c.Title)));
        }
    }

    public abstract bool IsValidMaterial(CardModel card);
    private static LocString MaterialPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_XYZ_SUMMON");

    public bool CanSummonFromExtraDeck(Player player)
        => (!FeatureStatus.Checked || FeatureStatus.Xyz) && Owner == player && player.Creature.IsAlive
           && player.Creature.CombatState != null && !CombatManager.Instance.IsOverOrEnding
           && Pile?.Type == Entry.ExtraPile && MaterialGroupId == 0
           && PileType.Hand.GetPile(player).Cards.Count(IsValidMaterial) >= RequiredMaterialCount;

    public async Task<bool> PaySummonCost(PlayerChoiceContext context, Player player)
    {
        if (!CanSummonFromExtraDeck(player)) return false;
        var combat = player.Creature.CombatState;
        var prompt = new LocString(MaterialPrompt.LocTable, MaterialPrompt.LocEntryKey);
        prompt.Add("Count", RequiredMaterialCount);
        // 一次选齐并确认；取消不会叠放。独立召唤流程使用空来源恢复手牌节点。
        var selected = (await CardSelectCmd.FromHand(context, player,
            CardPrefsConfirm(this, prompt, RequiredMaterialCount, RequiredMaterialCount) with { Cancelable = true },
            IsValidMaterial, source: null!)).ToList();
        if (player.Creature.CombatState != combat || !CanSummonFromExtraDeck(player)
            || selected.Count != RequiredMaterialCount || selected.Distinct().Count() != selected.Count
            || selected.Any(c => !IsValidMaterial(c))) return false;
        return await XyzMaterials.Stage(this, selected);
    }

    public void TransferSummonState(CardModel summonedCopy)
    {
        if (summonedCopy is not XyzMonsterCard copy || copy.Owner != Owner || !MaterialsStaged
            || XyzMaterialCards.Count != RequiredMaterialCount || copy.MaterialGroupId != 0)
            throw new InvalidOperationException("Invalid Xyz summon material transfer.");
        copy.MaterialGroupId = MaterialGroupId;
        copy.MaterialOwnerKey = MaterialOwnerKey;
        copy.MaterialsStaged = false;
        MaterialGroupId = 0;
        MaterialOwnerKey = "";
        MaterialsStaged = false;
    }

    public async Task CancelSummonState(PlayerChoiceContext context, Player player, CardModel? summonedCopy)
    {
        if (summonedCopy is XyzMonsterCard copy && copy.MaterialGroupId > 0) await XyzMaterials.Cancel(copy);
        await XyzMaterials.Cancel(this);
        if (Pile?.Type == Entry.ExtraUsedPile && player.RunState.ContainsCard(this))
            ExtraDeckSeries.RestorePersistentExtraCard(player, this, Entry.ExtraPile.GetPile(player));
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this && !MaterialsStaged && MaterialGroupId > 0
            && Pile?.Type != PileType.Hand && Pile?.Type != PileType.Play && !CombatManager.Instance.IsOverOrEnding)
            await XyzMaterials.ExhaustAll(new BlockingPlayerChoiceContext(), this);
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        // 真正的召唤由 TransferSummonState 交接；一般复制不继承他人的实体素材。
        MaterialGroupId = 0;
        MaterialOwnerKey = "";
        MaterialsStaged = false;
    }
}
