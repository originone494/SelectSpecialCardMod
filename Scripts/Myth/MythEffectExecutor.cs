using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythEffectExecutor
{
    private static readonly IReadOnlyDictionary<string, FusionActionKind> Operations = new Dictionary<string, FusionActionKind>(StringComparer.Ordinal)
    {
        ["attack"] = FusionActionKind.Damage, ["attack_vs_block"] = FusionActionKind.DamageVsBlock,
        ["block"] = FusionActionKind.Block, ["draw"] = FusionActionKind.Draw, ["mill"] = FusionActionKind.Mill,
        ["heal"] = FusionActionKind.Heal, ["strength"] = FusionActionKind.Strength, ["dexterity"] = FusionActionKind.Dexterity,
        ["weak"] = FusionActionKind.Weak, ["vulnerable"] = FusionActionKind.Vulnerable,
        ["discard"] = FusionActionKind.Discard, ["exhaust_discard"] = FusionActionKind.ExhaustDiscard,
        ["recover_exile"] = FusionActionKind.RecoverExile, ["retrieve"] = FusionActionKind.Retrieve,
        ["random_exile"] = FusionActionKind.RandomExile,
    };
    private static readonly HashSet<string> Special = ["multi_attack", "random_exhaust", "truce_all", "clear_buffs", "exhaust_all_hand", "mill_all"];
    public static string Key(int index, string op) => $"Mutation_{index}_" + (op.StartsWith("lib_")
        ? op == "lib_custom_attack_double_vs_block" ? "library_block_damage" : MythLibraryEffects.IsDamage(op[4..]) ? "library_damage"
            : op is "lib_gain_block" or "lib_custom_gain_block_repeated" ? "library_block" : "library_value" : op);
    public static string RepeatKey(int index) => $"MutationRepeat_{index}";
    public static bool RequiresEnemy(MythResolvedAction a) => a.Op == "multi_attack" || (Operations.TryGetValue(a.Op, out var k) && new FusionAction(k, a.Amount).RequiresEnemy);
    public static bool IsBlock(MythResolvedAction a) => a.Op is "block" or "lib_gain_block" or "lib_custom_gain_block_repeated";
    public static IEnumerable<IHoverTip> HoverTips(IEnumerable<MythResolvedAction> actions)
    {
        foreach (var action in actions)
        {
            if (Operations.TryGetValue(action.Op, out var kind))
                foreach (var tip in new FusionAction(kind, action.Amount).HoverTips()) yield return tip;
            if (action.Op == "truce_all") yield return HoverTipFactory.FromPower<MythTrucePower>();
        }
    }
    public static void Validate(MythResolvedAction a)
    {
        if (a.Op.StartsWith("lib_")) { MythLibraryEffects.Validate(a); return; }
        if ((!Operations.ContainsKey(a.Op) && !Special.Contains(a.Op)) || a.Amount is < 1 or > 1000
            || a.Repeat is < 1 or > 16 || !Enum.TryParse<FusionCardFilter>(a.Filter, out var filter) || !Enum.IsDefined(filter))
            throw new InvalidOperationException("Unsupported myth action: " + a.Op);
    }
    // 固定槽位可在规范模板克隆前声明，之后绑定只修改数值，避免私有缓存重建/丢失附魔。
    public static IEnumerable<DynamicVar> Variables()
    {
        for (int i = 0; i < 6; i++)
        {
            foreach (var pair in Operations.OrderBy(p => p.Key, StringComparer.Ordinal)) yield return new FusionAction(pair.Value, 0).CreateVar(Key(i, pair.Key));
            yield return new DamageVar(Key(i, "multi_attack"), 0, ValueProp.Move);
            foreach (string op in Special.Where(s => s != "multi_attack").OrderBy(s => s, StringComparer.Ordinal)) yield return new DynamicVar(Key(i, op), 0);
            yield return new DynamicVar(RepeatKey(i), 1);
            yield return new DamageVar(Key(i, "lib_attack"), 0, ValueProp.Move);
            yield return new FusionBlockDamageVar(Key(i, "lib_custom_attack_double_vs_block"), 0);
            yield return new BlockVar(Key(i, "lib_gain_block"), 0, ValueProp.Move);
            yield return new DynamicVar(Key(i, "lib_draw"), 0);
            for (int arg = 0; arg < 4; arg++) yield return new DynamicVar(MythLibraryEffects.ArgKey(i, arg), 0);
        }
    }
    public static async Task Execute(MythMutationCard card, PlayerChoiceContext context, CardPlay play)
    {
        var ctx = new FusionActionContext(card, context, play);
        if (!await MythConditionEvaluator.Pay(card, ctx)) return;
        var actions = card.FixedSnapshot!.Actions;
        for (int i = 0; i < actions.Count; i++)
        {
            if (!ctx.CanContinue) return;
            var a = actions[i]; string key = Key(i, a.Op);
            if (a.Op.StartsWith("lib_")) { await MythLibraryEffects.Execute(card, ctx, a, i); continue; }
            if (Operations.TryGetValue(a.Op, out var kind))
            {
                await FusionActions.Execute(new FusionAction(kind, a.Amount, Enum.Parse<FusionCardFilter>(a.Filter)), key, ctx);
                continue;
            }
            switch (a.Op)
            {
                case "multi_attack":
                    var target = ctx.Target();
                    if (target != null) await DamageCmd.Attack(card.DynamicVars[key].BaseValue)
                        .WithHitCount(card.DynamicVars[RepeatKey(i)].IntValue).FromCard(card, play).Targeting(target).Execute(context);
                    break;
                case "random_exhaust":
                    for (int n = 0; n < card.DynamicVars[key].IntValue && ctx.CanContinue; n++)
                    {
                        var options = PileType.Hand.GetPile(card.Owner).Cards.Where(c => c != card && MaterialPayment.CanUseAsMaterial(c)).ToList();
                        if (options.Count == 0) break;
                        var selected = MythCardFactory.RngFor(card.Owner).NextItem(options)!;
                        await MaterialPayment.Exhaust(context, card.Owner, [selected], 1, PileType.Hand);
                    }
                    break;
                case "exhaust_all_hand":
                    var hand = PileType.Hand.GetPile(card.Owner).Cards.Where(c => c != card && MaterialPayment.CanUseAsMaterial(c)).ToList();
                    foreach (var item in hand)
                    {
                        if (!ctx.CanContinue || item.Pile?.Type != PileType.Hand) return;
                        if (!await MaterialPayment.Exhaust(context, card.Owner, [item], 1, PileType.Hand)) return;
                    }
                    break;
                case "clear_buffs":
                    foreach (var power in card.Owner.Creature.Powers.Where(p => p.Type == PowerType.Buff).ToList())
                    { if (!ctx.CanContinue) return; await PowerCmd.Remove(power); }
                    break;
                case "mill_all":
                    var draw = PileType.Draw.GetPile(card.Owner).Cards.ToList();
                    await SelectSpecialCardMod.Scripts.Hook.SongMuHook.DeferTriggers(() => CardCmd.Discard(context, draw));
                    break;
                case "truce_all":
                    // 整个当前轮次到敌方回合结束，包含宠物；不沿用旧休战对玩家过早到期的行为。
                    await PowerCmd.Apply<MythTrucePower>(context, ctx.Combat!.Creatures.Where(c => c.IsAlive).ToList(), 1, card.Owner.Creature, card);
                    break;
                default: throw new InvalidOperationException("Unsupported myth action.");
            }
        }
    }
}
