using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.HandSize;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

// 每次施加保留独立参数，避免不同异变效果在同一个能力ID下相互覆盖。
public class MythOngoingPower : SelectSpecialBasePower, IMaxHandSizeModifier
{
    [SavedProperty] public string Operation { get; set; } = "";
    [SavedProperty] public string EffectName { get; set; } = "神话卡";
    [SavedProperty] public string EffectText { get; set; } = "";
    [SavedProperty] public string DisplaySnapshotJson { get; set; } = "";
    private string _loadedDisplayJson = "";
    private MythCardSnapshot? _displaySnapshot;
    private MythCardSnapshot? DisplaySnapshot
    {
        get
        {
            if (DisplaySnapshotJson != _loadedDisplayJson)
            {
                _displaySnapshot = DisplaySnapshotJson.Length == 0 ? null : MythJson.Read<MythCardSnapshot>(DisplaySnapshotJson);
                _loadedDisplayJson = DisplaySnapshotJson;
            }
            return _displaySnapshot;
        }
    }
    [SavedProperty] public int A { get; set; }
    [SavedProperty] public int B { get; set; }
    [SavedProperty] public int Counter { get; set; }
    // 延迟打出记住的目标卡，用「模型ID」而不是"本场战斗的临时卡编号"：
    // 临时编号在战斗开始时从 0 重新分配、按牌堆扫描顺序发号，读档后同一个数字可能指到手里另一张牌
    // （曾经会把玩家选的「打击」自动打出成「防御」）。模型ID跨存档稳定，同名牌在效果上等价。
    [SavedProperty] public string ChosenCardEntry { get; set; } = "";
    // 旧存档曾保存临时卡编号，保留字段只为兼容反序列化，不再写入也不再读取。
    [SavedProperty] public int ChosenCardId { get; set; } = -1;
    private bool _resolving;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerAssetProfile AssetProfile => new(IconPath: MythArtManifestPower.Icon, BigIconPath: MythArtManifestPower.Icon);
    public override LocString Title { get { var s = new LocString("powers", "SELECT_SPECIAL_CARD_MOD_POWER_MYTH_ONGOING.title"); s.Add("Name", DisplaySnapshot is { } snapshot ? MythCardText.Title(snapshot) + (EffectName.EndsWith('+') ? "+" : "") : EffectName); return s; } }
    public override LocString Description { get { var s = new LocString("powers", "SELECT_SPECIAL_CARD_MOD_POWER_MYTH_ONGOING.description"); s.Add("Text", DisplaySnapshot is { } snapshot ? MythCardText.EffectDescription(snapshot) : EffectText); return s; } }
    public static async Task Grant(FusionActionContext ctx, string op, int[] values, Creature? target = null, CardModel? chosen = null)
    {
        if (!ctx.CanContinue) return;
        var power = (MythOngoingPower)ModelDb.Power<MythOngoingPower>().ToMutable();
        power.Operation = op; power.A = values.ElementAtOrDefault(0); power.B = values.ElementAtOrDefault(1);
        // SavedProperty 必须与客户端语言无关；显示时再按当前语言渲染。
        power.EffectName = ctx.Source is MythMutationCard myth
            ? MythCardText.CanonicalTitle(myth.FixedSnapshot!) + (myth.IsUpgraded ? "+" : "") : ctx.Source.Title;
        if (ctx.Source is MythMutationCard source) power.DisplaySnapshotJson = source.SnapshotJson;
        power.EffectText = ctx.Source is MythMutationCard c ? System.Text.RegularExpressions.Regex.Replace(c.FixedSnapshot!.Effect.Description,
            "\\{([a-z_]+)\\}", m => c.FixedSnapshot.EffectValues[m.Groups[1].Value].ToString()) : "";
        if (chosen != null) power.ChosenCardEntry = chosen.Id.ToString();
        await PowerCmd.Apply(ctx.ChoiceContext, power, target ?? ctx.Source.Owner.Creature, 1, ctx.Source.Owner.Creature, ctx.Source);
    }
    private bool Active => Owner.IsAlive && CombatState != null && !CombatManager.Instance.IsOverOrEnding;
    public int ModifyMaxHandSize(Player player, int currentMaxHandSize) => currentMaxHandSize;
    public int ModifyMaxHandSizeLate(Player player, int currentMaxHandSize)
        => Operation == "custom_power_unlimited_hand" && player == Owner.Player ? int.MaxValue : currentMaxHandSize;
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card.Owner?.Creature != Owner) return false;
        bool free = Operation == "custom_next_attack_free" && card.Type == CardType.Attack
            || Operation == "custom_power_free_until_attack" && Counter == 0 && card.Type != CardType.Attack;
        if (free) modifiedCost = 0;
        return free;
    }
    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner?.Creature == Owner && cardPlay.Card.Type == CardType.Attack)
        {
            if (Operation == "custom_power_free_until_attack") Counter = 1;
            if (Operation == "custom_next_attack_free") return PowerCmd.Remove(this);
        }
        return Task.CompletedTask;
    }
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (Operation == "nullify" && (target == Owner || dealer == Owner)) return 0;
        if (Operation == "truce" && dealer?.CombatState == CombatState) return 0;
        if (Operation == "custom_power_multienemy_disable_damage" && dealer?.IsEnemy == true
            && dealer.CombatState == CombatState && CombatState!.HittableEnemies.Count(e => e.IsAlive) >= 2) return 0;
        decimal multiplier = 1;
        if (Operation == "custom_gain_double_damage_half_taken_this_turn" && dealer == Owner) multiplier *= 2;
        if (Operation is "custom_gain_half_damage_taken_this_turn" or "custom_gain_double_damage_half_taken_this_turn" && target == Owner) multiplier *= 0.5m;
        return multiplier;
    }
    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay play)
    {
        if (!Active || play.Card.Owner?.Creature != Owner || play.Card.Type != CardType.Attack) return;
        switch (Operation)
        {
            case "custom_power_on_attack_damage": await RandomDamage(context, A); break;
            case "custom_power_attack_mills_draw_pile": await Mill(context, A); break;
            case "custom_power_attack_mills_and_weakens":
                await Mill(context, A);
                foreach (var enemy in Enemies()) { if (!Active) return; await PowerCmd.Apply<YaoShengLuXiNaTempDownPower>(context, enemy, B, Owner, null); } break;
        }
    }
    private List<Creature> Enemies() => CombatState?.HittableEnemies.Where(c => c.IsAlive).ToList() ?? [];
    private async Task RandomDamage(PlayerChoiceContext context, int amount)
    {
        if (!Active || Owner.Player == null) return;
        var enemy = Owner.Player.RunState.Rng.CombatTargets.NextItem(Enemies());
        if (enemy != null) await CreatureCmd.Damage(context, enemy, amount, ValueProp.Move, Owner, null, null);
    }
    private async Task Mill(PlayerChoiceContext context, int amount)
    {
        if (!Active || Owner.Player == null || _resolving) return;
        _resolving = true;
        try
        {
            // 只移动本批牌顶，禁止“送墓→洗回→再次送墓”的无限连锁。
            var draw = PileType.Draw.GetPile(Owner.Player);
            foreach (var card in draw.Cards.Take(amount).ToList())
            { if (!Active) return; if (card.Pile == draw) await CardCmd.Discard(context, card); }
        }
        finally { _resolving = false; }
    }
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (!Active || card.Owner?.Creature != Owner || card.Pile?.Type != PileType.Discard || oldPileType == PileType.Discard) return;
        // Mill自身的弃牌不能在延后执行时绕过原有防重入标记。
        if (Operation == "custom_power_on_discard_mill" && _resolving) return;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            if (!Active || CombatState != combat || !Owner.Powers.Contains(this)) return;
            var context = new BlockingPlayerChoiceContext();
            if (Operation == "custom_power_on_discard_mill") await Mill(context, A);
            if (Operation == "custom_power_on_discard_pile_enter_damage") await RandomDamage(context, A);
        });
    }
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? source)
    {
        if (!Active || Operation != "custom_power_on_buff_count_attack_all" || power == this || power.Owner != Owner
            || power.Type != PowerType.Buff || amount <= 0) return;
        Counter++;
        if (Counter < A) return;
        Counter -= A;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            if (!Active || CombatState != combat || !Owner.Powers.Contains(this)) return;
            foreach (var enemy in Enemies()) { if (!Active) return; await CreatureCmd.Damage(context, enemy, B, ValueProp.Move, Owner, null, null); }
        });
    }
    public override async Task AfterShuffle(PlayerChoiceContext context, Player player)
    {
        if (!Active || Operation != "custom_power_shuffle_retrieve_banished" || player.Creature != Owner) return;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            if (!Active || CombatState != combat || !Owner.Powers.Contains(this)) return;
            for (int n = 0; n < A && Active; n++)
            {
                var cards = Entry.ChuWaiPile.GetPile(player).Cards.Where(MythLibraryChoices.Movable).ToList();
                if (cards.Count == 0) break;
                await CardPileCmd.Add(MythCardFactory.RngFor(player).NextItem(cards)!, PileType.Hand);
            }
        });
    }
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!Active || side != Owner.Side || !participants.Contains(Owner) || Owner.Player is not { } player) return;
        var context = new BlockingPlayerChoiceContext();
        switch (Operation)
        {
            case "custom_power_turn_start_artifact": await PowerCmd.Apply<ArtifactPower>(context, Owner, A, Owner, null); break;
            case "custom_power_turn_start_retrieve_and_attack":
                var discard = PileType.Discard.GetPile(player).Cards.Where(MythLibraryChoices.Movable).ToList();
                if (discard.Count != 0) await CardPileCmd.Add(MythCardFactory.RngFor(player).NextItem(discard)!, PileType.Hand);
                if (Active) await RandomDamage(context, A); break;
            case "custom_power_turn_start_banish_and_play_attack":
            case "custom_power_turn_start_banish_and_retrieve":
                string type = Operation.EndsWith("play_attack") ? "Attack" : "Any";
                var pool = MythLibraryEffects.RandomPool(player, type).ToList(); var generated = new List<CardModel>();
                for (int n = 0; n < A && Active && pool.Count > 0; n++)
                {
                    var entries = await SelectToolCmd.AddGeneratedCopiesToPile(player, [MythCardFactory.RngFor(player).NextItem(pool)!], Entry.ChuWaiPile);
                    generated.AddRange(entries.Where(e => e.success && e.cardAdded != null).Select(e => e.cardAdded!));
                }
                var eligible = generated.Where(c => c.Pile?.Type == Entry.ChuWaiPile && !c.HasBeenRemovedFromState).ToList();
                if (Active && eligible.Count > 0)
                {
                    var chosen = MythCardFactory.RngFor(player).NextItem(eligible)!;
                    if (type == "Attack") await CardCmd.AutoPlay(context, chosen, null); else await CardPileCmd.Add(chosen, PileType.Hand);
                } break;
            case "custom_power_countdown_execute_all":
                Counter++;
                if (Counter >= A) { foreach (var enemy in Enemies()) { if (!Active) return; await CreatureCmd.Kill(enemy); } if (Owner.Powers.Contains(this)) await PowerCmd.Remove(this); } break;
        }
    }
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!Active || side != Owner.Side || !participants.Contains(Owner) || Owner.Player is not { } player) return;
        var hand = PileType.Hand.GetPile(player);
        if (Operation == "custom_power_end_turn_retain")
        {
            int count = Math.Min(A, hand.Cards.Count);
            if (count == 0) return;
            var cards = (await CardSelectCmd.FromHand(context, player,
                new CardSelectorPrefs(new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_MYTH_LIBRARY_RETAIN"), count, count) { RequireManualConfirmation = true }, filter: null, source: null!)).ToList();
            if (Active && cards.Count == count && cards.Distinct().Count() == count)
                foreach (var c in cards.Where(c => c.Owner == player && c.Pile == hand)) c.AddKeyword(CardKeyword.Retain);
        }
        if (Operation == "delayed")
        {
            // 按模型ID回找手牌：读档后实体换了也照样能找到同名的那张（同名牌效果等价）。
            CardModel? chosen = ChosenCardEntry.Length == 0
                ? null
                : hand.Cards.FirstOrDefault(c => c.Id.ToString() == ChosenCardEntry && MythLibraryChoices.Movable(c));
            ChosenCardEntry = "";
            await PowerCmd.Remove(this);
            if (chosen?.Owner == player && chosen.Pile == hand) await CardCmd.AutoPlay(context, chosen, null);
        }
        if (Operation == "custom_discard_all_at_turn_end")
        {
            await PowerCmd.Remove(this);
            await CardCmd.Discard(context, hand.Cards.Where(MythLibraryChoices.Movable).ToList());
        }
    }
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!Active) return;
        if (Operation == "custom_power_end_turn_block" && side == Owner.Side && participants.Contains(Owner))
            await CreatureCmd.GainBlock(Owner, A, ValueProp.Move, null);
        bool enemyExpiry = Operation is "nullify" or "truce" or "custom_gain_half_damage_taken_this_turn" or "custom_gain_double_damage_half_taken_this_turn";
        bool playerExpiry = Operation is "custom_next_attack_free" or "custom_power_free_until_attack";
        if (enemyExpiry && side == CombatSide.Enemy || playerExpiry && side == Owner.Side && participants.Contains(Owner)) await PowerCmd.Remove(this);
    }
    protected override void AfterCloned() { base.AfterCloned(); _resolving = false; }
}

internal static class MythArtManifestPower
{
    public const string Icon = "res://SelectSpecialCardMod/images/powers/YiShiXiuZhanPower.png";
}
