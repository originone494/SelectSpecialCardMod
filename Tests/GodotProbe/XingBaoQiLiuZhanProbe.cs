using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;

public static class XingBaoQiLiuZhanProbe
{
    private static readonly List<AttackCommand> Commands = [];

    public static async Task Check()
    {
        var h = new Harmony("scm.starburst.godot-framework");
        var history = CombatManager.Instance.History;
        history.Clear();
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            // 替换攻击执行以免加载游戏场景；实际 OnPlay 和 AttackCommand 构造全部保留。
            h.Patch(AccessTools.Method(typeof(AttackCommand), "Execute", [typeof(PlayerChoiceContext)]), prefix: new HarmonyMethod(typeof(XingBaoQiLiuZhanProbe), nameof(Capture)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
            var combat = player.Creature.CombatState!;
            var card = (XingBaoQiLiuZhanCard)ArrivalEquipmentProbe.AddCard(typeof(XingBaoQiLiuZhanCard), player, PileType.Hand);
            var target = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
            target.CombatState = combat;
            AccessTools.Field(typeof(Creature), "_currentHp").SetValue(target, 1000);
            EquipmentHookProbe.Models = [card];
            if (card.Type != CardType.Attack || card.Rarity != CardRarity.Ancient || card.TargetType != TargetType.AnyEnemy
                || card.EnergyCost.Canonical != 3 || !card.Keywords.Contains(CardKeyword.Exhaust)
                || card.DynamicVars["Hits"].IntValue != 16)
                throw new Exception("Starburst card metadata changed.");
            AssertDamage(card, 1);
            var nativeResult = (CardLocation)AccessTools.Method(typeof(CardModel), "GetResultLocationForCardPlay").Invoke(card, null)!;
            if (nativeResult.pileType != PileType.Exhaust) throw new Exception("Starburst does not exhaust after play.");

            var ownPlay = MakePlay(card, player, target);
            history.CardPlayStarted(combat, ownPlay);
            AssertDamage(card, 1); // 当前自身开始打出尚未完成，不增加伤害。
            await InvokePlay(card, ownPlay, 1);

            var attack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Discard);
            var skill = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Discard);
            history.CardPlayFinished(combat, MakePlay(attack, player, target));
            history.CardPlayFinished(combat, MakePlay(attack, player, target, automatic: true));
            history.CardPlayFinished(combat, MakePlay(attack, player, target));
            history.CardPlayFinished(combat, MakePlay(skill, player, null));
            var other = ArrivalEquipmentProbe.NewPlayer();
            var otherAttack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), other, PileType.Discard);
            history.CardPlayFinished(combat, MakePlay(otherAttack, other, target));
            AssertDamage(card, 4); // 只计本人的三个已完成攻击牌，技能与其他玩家的攻击不计。

            var lateCopy = (XingBaoQiLiuZhanCard)ArrivalEquipmentProbe.AddCard(typeof(XingBaoQiLiuZhanCard), player, PileType.Hand);
            AssertDamage(lateCopy, 4); // 后获得的卡也能读取之前的攻击记录。
            await InvokePlay(card, MakePlay(card, player, target, automatic: true), 4);
            history.CardPlayFinished(combat, ownPlay);
            AssertDamage(lateCopy, 5); // 已经完成的同名卡也算一张之前的攻击牌。
            combat.RoundNumber++;
            AssertDamage(card, 1);
            history.CardPlayFinished(combat, MakePlay(attack, player, target));
            AssertDamage(card, 2);
            history.Clear();
            AssertDamage(card, 1);
            var canonical = new XingBaoQiLiuZhanCard();
            if (canonical.DynamicVars.CalculatedDamage.Calculate(null) != 1) throw new Exception("Out-of-combat damage is not the base value.");

            // Ritsu 的 Inherit 是框架扫描约定；AttributeUsage 自身不采用 .NET 属性继承。
            var poolRegistration = typeof(SelectSpecialCardMod.Scripts.Base.SelectSpecialBaseCard).GetCustomAttributesData()
                .Single(a => a.AttributeType == typeof(STS2RitsuLib.Interop.AutoRegistration.RegisterCardAttribute));
            if (typeof(XingBaoQiLiuZhanCard).BaseType != typeof(SelectSpecialCardMod.Scripts.Base.SelectSpecialBaseCard)
                || poolRegistration.ConstructorArguments[0].Value as Type != typeof(SelectSpecialCardMod.Scripts.Pool.SelectSpecialCardPool)
                || !Equals(poolRegistration.NamedArguments.Single(a => a.MemberName == "Inherit").TypedValue.Value, true))
                throw new Exception("Starburst is missing inherited card-pool registration.");
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
            if (!System.IO.File.Exists(System.IO.Path.Combine(root, "SelectSpecialCardMod", "images", "cards", "XingBaoQiLiuZhanCard.png")))
                throw new Exception("Prepared Starburst portrait is missing.");
            var text = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "SelectSpecialCardMod", "localization", "zhs", "cards.json")))!;
            string localizationKey = "SELECT_SPECIAL_CARD_MOD_CARD_" + card.Id.Entry;
            if (text[$"{localizationKey}.title"] != "星爆气流斩" || !text[$"{localizationKey}.description"].Contains("{CalculatedDamage:diff()}点伤害{Hits:diff()}次"))
                throw new Exception("Starburst localization is not bound to calculated damage/hits.");
        }
        finally { h.UnpatchAll(h.Id); history.Clear(); Commands.Clear(); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: Starburst metadata, exhaust, localization/art and dynamic preview verified; native history counts completed own attacks including autoplay/late acquisition, excludes current self/skills/other players, resets each turn; real OnPlay builds one 16-hit attack using the same calculated damage.");
    }

    private static void AssertDamage(XingBaoQiLiuZhanCard card, decimal expected)
    {
        if (card.DynamicVars.CalculatedDamage.Calculate(null) != expected) throw new Exception("Unexpected Starburst calculated damage.");
        EquipmentHookProbe.Models = [card];
        card.DynamicVars.ClearPreview();
        card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, card.DynamicVars);
        if (card.DynamicVars.CalculatedDamage.PreviewValue != expected) throw new Exception("Starburst displayed damage differs from its calculated damage.");
    }

    private static async Task InvokePlay(XingBaoQiLiuZhanCard card, CardPlay play, decimal expected)
    {
        Commands.Clear();
        await (Task)AccessTools.Method(typeof(XingBaoQiLiuZhanCard), "OnPlay").Invoke(card, [new BlockingPlayerChoiceContext(), play])!;
        if (Commands.Count != 1) throw new Exception("Starburst split hits into separate attacks.");
        var command = Commands.Single();
        if ((int)AccessTools.Field(typeof(AttackCommand), "_hitCount").GetValue(command)! != 16
            || command.ModelSource != card || command.CardPlay != play
            || AccessTools.Field(typeof(AttackCommand), "_singleTarget").GetValue(command) != play.Target
            || !ReferenceEquals(AccessTools.Field(typeof(AttackCommand), "_calculatedDamageVar").GetValue(command), card.DynamicVars.CalculatedDamage)
            || card.DynamicVars.CalculatedDamage.Calculate(play.Target) != expected)
            throw new Exception("Starburst attack lost its hit count, target, source or calculated damage binding.");
    }

    private static CardPlay MakePlay(CardModel card, Player player, Creature? target, bool automatic = false) => new()
    {
        Card = card, Player = player, Target = target, Resources = default, ResultPile = PileType.Discard,
        IsAutoPlay = automatic, PlayIndex = 0, PlayCount = 1,
    };

    public static bool Capture(AttackCommand __instance, ref Task<AttackCommand> __result)
    { Commands.Add(__instance); __result = Task.FromResult(__instance); return false; }
}
