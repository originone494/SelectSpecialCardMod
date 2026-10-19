using Godot;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using STS2RitsuLib.Models.Capabilities;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;
public partial class Probe : Node
{
    public override async void _Ready()
    {
        try
        {
            CardClassificationProbe.Check();
            SpecialCardUpgradeProbe.Check();
            YaoShengLuXiNaProbe.Check();
            await FortunaBuffProbe.Check();
            var h = new Harmony("scm.architecture.godot-framework");
            var attack = typeof(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand);
            var execute = AccessTools.Method(attack, "Execute", [typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext)]);
            var machine = execute.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()?.StateMachineType
                ?? attack.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Single(t => t.Name.StartsWith("<Execute>d__"));
            var redirect = typeof(STS2RitsuLib.Combat.AttackHits.AttackHitHook).Assembly.GetType("STS2RitsuLib.Combat.AttackHits.Patches.AttackCommandExecuteHitHookPatch")!;
            h.Patch(AccessTools.Method(machine, "MoveNext"), transpiler: new HarmonyMethod(AccessTools.Method(redirect, "Transpiler")));
            // 复现真实加载顺序：RitsuLib先替换OnPlay调用，再加载本模组，防止探针漏测入口冲突。
            var wrapper = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.CardModel), "OnPlayWrapper");
            var playMachine = wrapper.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()?.StateMachineType
                ?? wrapper.DeclaringType!.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(t => t.Name.StartsWith("<OnPlayWrapper>d__"));
            var playRedirect = typeof(STS2RitsuLib.Cards.CardOnPlayHook).Assembly.GetType("STS2RitsuLib.Cards.Patches.CardOnPlayHookPatch", true)!;
            h.Patch(AccessTools.Method(playMachine, "MoveNext"), transpiler: new HarmonyMethod(AccessTools.Method(playRedirect, "Transpiler")));
            SelectSpecialCardMod.Scripts.Entry.Init();
            SelectSpecialCardMod.Scripts.Entry.Init();
            if (!FeatureStatus.Arrival || !FeatureStatus.AttackProtection || !FeatureStatus.RunSetup || !FeatureStatus.MonsterDeck || !FeatureStatus.PendulumPlacement || !FeatureStatus.Xyz)
                throw new System.Exception("Feature completeness check failed.");
            GD.Print("PASS: complete production Entry.Init is idempotent and all critical feature patch groups are available.");
            await CardEffectQueueProbe.Check();
            SpecialEffectRightClickProbe.Check();
            RunSeenCards.Init(); RunSeenCards.Init();
            ExtraDeckRunState.Init(); ExtraDeckRunState.Init();
            ExtraDeckSummonHook.Register(); ExtraDeckSummonHook.Register();
            SpecialEffectHook.Init(); SpecialEffectHook.Init();
            ExtraDeckSeries.Register(); ExtraDeckSeries.Register();
            GD.Print("PASS: real Godot registers per-player persistence, sidecar, managed summon action and right-click once.");
            if (SeriesRegistry.All.Count() != 3) throw new System.Exception("Series registry missing or duplicated a series.");
            CheckZhuLeiSupply();
            GD.Print("PASS: series registration is idempotent.");
            var normal = new SelectSpecialCardMod.Scripts.Cards.Special.KeMoMoJianKeCard();
            var link = new SelectSpecialCardMod.Scripts.Cards.Special.KeMoDaShengGuanCard();
            if (!KeMoLinkSummon.CanPay(new MegaCrit.Sts2.Core.Models.CardModel[] { link, normal }, 3, 2, true)
                || !KeMoLinkSummon.CanPay(new MegaCrit.Sts2.Core.Models.CardModel[] { link, normal }, 2, 2, true)
                || KeMoLinkSummon.CanPay(new MegaCrit.Sts2.Core.Models.CardModel[] { link }, 2, 2, true))
                throw new System.Exception("Link material rules changed.");
            GD.Print("PASS: actual card tags retain link rating-or-one counting and distinct-material minimum.");
            await CheckPersistentExtraReturns();
            await CheckMandatorySelection();
            await SelectionFlowProbe.Check();
            CheckEquipmentVisualState();
            await CheckSpecialEffectPresentationOrder();
            await ArrivalEquipmentProbe.CheckArrivalPresentation();
            await ArrivalEquipmentProbe.CheckEquipmentLifecycle();
            await ChuWaiKeywordProbe.Check();
            await XingBaoQiLiuZhanProbe.Check();
            await ZhuLeiResolutionProbe.Check();
            await ExtraFusionMaterialReturnProbe.Check();
            await ZhuLeiFusionEffectsProbe.Check();
            await SeenAndRecallProbe.Check();
            await YiCunJiaProbe.Check();
            await DuelDskGrantProbe.Check();
            await YiXiangMingSpellProbe.Check();
            await YiXiangMingPendulumProbe.Check();
            await YiXiangMingXuanJiProbe.Check();
            await YiXiangMingLinkProbe.Check();
            await XyzProbe.Check();
            await YiXiangMingInvitationProbe.Check();
            await CardRuleRegressionProbe.Check();
            await MultiplayerFlowProbe.Check();
            await SynchroAndBeiManProbe.Check();
            await CardAvailabilityProbe.Check();
            await RunStartCardsProbe.Check();
            await MythCompositeProbe.Check();
            await MythMutationProbe.Check();
            MythCardRewardsProbe.Check();
            LocalizationProbe.Check();
            await IntentTheftProbe.Check();
            await ZhuaYinIntentCancelProbe.Check();
            await MonsterRevivalProbe.Check();
            await LinkedEnemyCopyProbe.Check();
            IntentForecastProbe.Check();
            PetAllyProbe.Check();
            var copyData = new SelectSpecialCardMod.Scripts.Intent.MonsterMoveCard();
            copyData.EffectSnapshot = System.Text.Json.JsonSerializer.Serialize(new SelectSpecialCardMod.Scripts.Intent.MonsterEffectSnapshot { BlockKnown = true });
            if (copyData.EffectSnapshot.Length == 0) throw new System.Exception("Snapshot property failed.");
            var property = typeof(SelectSpecialCardMod.Scripts.Intent.MonsterMoveCard).GetProperty("EffectSnapshot")!;
            if (property.GetCustomAttribute<MegaCrit.Sts2.Core.Saves.Runs.SavedPropertyAttribute>() == null)
                throw new System.Exception("Snapshot property lacks persistence metadata.");
            GD.Print("PASS: actual card snapshot accepts data and has native saved-property metadata.");
            AtomicJsonStore.Write("user://SelectSpecialCardMod/architecture_probe.json", "{\"version\":1}");
            AtomicJsonStore.Write("user://SelectSpecialCardMod/architecture_probe.json", "{\"version\":2}");
            GD.Print("PASS: atomic persistence resolves actual user:// paths.");
            var path = ProjectSettings.GlobalizePath("user://SelectSpecialCardMod/architecture_probe.json");
            System.IO.File.Delete(path); System.IO.File.Delete(path + ".bak");
            GetTree().Quit();
        }
        catch (System.Exception ex) { GD.PrintErr(ex); GetTree().Quit(1); }
    }

    private static void CheckZhuLeiSupply()
    {
        // 复现牌组仅有珍珠世界、初始额外卡组没有珠泪怪兽的场景。
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var run = (MegaCrit.Sts2.Core.Runs.RunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
        var deck = new CardPile(PileType.Deck);
        AccessTools.Field(player.GetType(), "_runState").SetValue(player, run);
        AccessTools.Field(player.GetType(), "_runPiles").SetValue(player, new CardPile[] { deck });
        var extraTypes = new[]
        {
            typeof(SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiShuiXianCard),
            typeof(SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiKaLeiDuoHaTeCard),
            typeof(SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiLuShaKaCard),
        };
        void CheckDesired(int count)
        {
            var desired = (Dictionary<Type, int>)AccessTools.Method(typeof(ExtraDeckSeries), "Desired").Invoke(null, [player])!;
            if (extraTypes.Any(t => desired.GetValueOrDefault(t) != count))
                throw new Exception("Pearl World supplied the wrong number of Tearlaments fusion monsters.");
        }
        CheckDesired(0);
        for (int i = 1; i <= 4; i++)
        {
            var world = new SelectSpecialCardMod.Scripts.Cards.Special.ZhenZhuShiJieCard();
            AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "IsMutable").SetValue(world, true);
            world.Owner = player;
            deck.AddInternal(world);
            if (ZhuLeiFilter.IsCard(world)) throw new Exception("Supply fix changed Pearl World's retrieval eligibility.");
            CheckDesired(Math.Min(i, ExtraDeckSeries.MaxCopiesPerName));
        }
        foreach (var card in deck.Cards.ToList()) deck.RemoveInternal(card);
        var fusion = new SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiShuiXianCard();
        AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "IsMutable").SetValue(fusion, true);
        fusion.Owner = player;
        deck.AddInternal(fusion);
        CheckDesired(0); // 额外怪兽本身仍不能反过来产生补给。
        var claw = new SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiZhuaYinCard().CanonicalKeywords.ToHashSet();
        var echo = new SelectSpecialCardMod.Scripts.Cards.Special.ZhuLeiCanXiangCard().CanonicalKeywords.ToHashSet();
        if (!claw.Contains(CardKeyword.Unplayable) || claw.Contains(CardKeyword.Exhaust) || !claw.Contains(CardKeyword.Retain)
            || echo.Contains(CardKeyword.Retain) || !echo.Contains(CardKeyword.Exhaust))
            throw new Exception("Claw/Echo keyword configuration regressed.");
        GD.Print("PASS: Pearl World alone supplies all three Tearlaments fusion types, supply caps at three, extra monsters do not supply themselves, retrieval and Claw/Echo keywords stay correct.");
    }

    private static async Task CheckSpecialEffectPresentationOrder()
    {
        // 只替换资源演出和卡牌详细效果，实际统一入口及默认接口判定仍参与验证。
        var assembly = typeof(SpecialEffectHook).Assembly;
        var cardTypes = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ISpecialEffectCard).IsAssignableFrom(t)).ToArray();
        if (cardTypes.Length == 0) throw new Exception("No special-effect cards discovered for presentation coverage.");
        var presentationType = assembly.GetType("SelectSpecialCardMod.Scripts.Tool.SpecialEffectVfx", true)!;
        var execute = AccessTools.Method(typeof(SpecialEffectHook), "ExecuteSpecialEffectAsync");
        var h = new Harmony("scm.special-effect-presentation.godot-framework");
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var run = (MegaCrit.Sts2.Core.Runs.RunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
        var creature = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
        var combat = new MegaCrit.Sts2.Core.Combat.CombatState(runState: run);
        creature.CombatState = combat;
        AccessTools.Field(player.GetType(), "<Creature>k__BackingField").SetValue(player, creature);
        AccessTools.Field(player.GetType(), "_runState").SetValue(player, run);
        AccessTools.Field(player.GetType(), "_runPiles").SetValue(player, Array.Empty<CardPile>());
        AccessTools.Property(player.GetType(), "PlayerCombatState").SetValue(player, new MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState(player));
        var hand = PileType.Hand.GetPile(player);
        var context = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
        var patchedMethods = new HashSet<MethodInfo>();
        try
        {
            h.Patch(AccessTools.Method(presentationType, "Play"), prefix: new HarmonyMethod(typeof(SpecialEffectPresentationProbe), "Present"));
            foreach (var type in cardTypes)
            {
                // 多张卡可继承同一个效果实现；Harmony 必须补声明该方法的类型，且只补一次。
                var canActivate = AccessTools.Method(type, "CanActivateSpecialEffect");
                var effect = AccessTools.Method(type, "TryActivateSpecialEffect");
                if (patchedMethods.Add(canActivate))
                    h.Patch(canActivate, prefix: new HarmonyMethod(typeof(SpecialEffectPresentationProbe), "CanActivate"));
                if (patchedMethods.Add(effect))
                    h.Patch(effect, prefix: new HarmonyMethod(typeof(SpecialEffectPresentationProbe), "Effect"));
                var card = (MegaCrit.Sts2.Core.Models.CardModel)Activator.CreateInstance(type)!;
                AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "IsMutable").SetValue(card, true);
                combat.AddCard(card, player);
                hand.AddInternal(card);
                SpecialEffectPresentationProbe.Completion = new();
                SpecialEffectPresentationProbe.EffectCalls = 0;
                var task = (Task<SelectSpecialCardMod.Scripts.Rules.ActivationResult>)execute.Invoke(null, [card, context, player])!;
                if (task.IsCompleted || SpecialEffectPresentationProbe.EffectCalls != 0)
                    throw new Exception("Detailed effect started before presentation completed: " + type.Name);
                SpecialEffectPresentationProbe.Completion.SetResult(true);
                if (!(await task).Activated || SpecialEffectPresentationProbe.EffectCalls != 1 || card.Pile != hand)
                    throw new Exception("Shared presentation did not preserve one activation and the hand: " + type.Name);

                int finished = 0;
                card.ExecutionFinished += source => { if (source == card) finished++; };
                SpecialEffectPresentationProbe.ThrowEffect = true;
                SpecialEffectPresentationProbe.Completion = new();
                var failing = (Task<SelectSpecialCardMod.Scripts.Rules.ActivationResult>)execute.Invoke(null, [card, context, player])!;
                SpecialEffectPresentationProbe.Completion.SetResult(true);
                try
                {
                    await failing;
                    throw new Exception("Special-effect failure was swallowed: " + type.Name);
                }
                catch (InvalidOperationException ex) when (ex.Message == "selection cleanup fixture") { }
                finally { SpecialEffectPresentationProbe.ThrowEffect = false; }
                if (finished != 1) throw new Exception("Failed special effect did not restore selection lifecycle: " + type.Name);

                SpecialEffectPresentationProbe.EffectCalls = 0;
                SpecialEffectPresentationProbe.Completion = new();
                var interrupted = (Task<SelectSpecialCardMod.Scripts.Rules.ActivationResult>)execute.Invoke(null, [card, context, player])!;
                SpecialEffectPresentationProbe.Completion.SetResult(false);
                if ((await interrupted).Activated || SpecialEffectPresentationProbe.EffectCalls != 0)
                    throw new Exception("Interrupted presentation executed an effect: " + type.Name);

                SpecialEffectPresentationProbe.Completion = new();
                var moved = (Task<SelectSpecialCardMod.Scripts.Rules.ActivationResult>)execute.Invoke(null, [card, context, player])!;
                hand.RemoveInternal(card);
                SpecialEffectPresentationProbe.Completion.SetResult(true);
                if ((await moved).Activated || SpecialEffectPresentationProbe.EffectCalls != 0)
                    throw new Exception("Moved source card executed a stale effect: " + type.Name);
            }
        }
        finally { h.UnpatchAll(h.Id); SpecialEffectPresentationProbe.Completion = null; SpecialEffectPresentationProbe.ThrowEffect = false; }
        GD.Print($"PASS: all {cardTypes.Length} special-effect cards await presentation, preserve exceptions with selection cleanup, and skip interrupted/stale effects ({cardTypes.Length * 4} checks).");
    }

    private static void CheckEquipmentVisualState()
    {
        ModelCapabilities.EnsureInitialized();
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var run = (MegaCrit.Sts2.Core.Runs.RunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
        var creature = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
        var combat = new MegaCrit.Sts2.Core.Combat.CombatState(runState: run);
        creature.CombatState = combat;
        AccessTools.Field(creature.GetType(), "_currentHp").SetValue(creature, 10);
        AccessTools.Field(player.GetType(), "<Creature>k__BackingField").SetValue(player, creature);
        AccessTools.Field(player.GetType(), "_runState").SetValue(player, run);
        AccessTools.Field(player.GetType(), "_runPiles").SetValue(player, Array.Empty<CardPile>());
        AccessTools.Property(player.GetType(), "PlayerCombatState").SetValue(player, new MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState(player));
        var target = new SelectSpecialCardMod.Scripts.Cards.Special.KeMoMoJianKeCard();
        var equip = new SelectSpecialCardMod.Scripts.Cards.Special.KeMoShenShengGuanCard();
        foreach (var card in new MegaCrit.Sts2.Core.Models.CardModel[] { target, equip })
        {
            AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "IsMutable").SetValue(card, true);
            combat.AddCard(card, player);
            PileType.Hand.GetPile(player).AddInternal(card);
        }
        var hookHarmony = new Harmony("scm.equipment-preview.godot-framework");
        var sourceMethod = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Runs.RunState), "IterateHookListeners", [typeof(MegaCrit.Sts2.Core.Combat.ICombatState)]);
        var runtime = typeof(ModelCapabilityRegistry).Assembly;
        var capabilityPatch = runtime.GetType("STS2RitsuLib.Models.Capabilities.Patches.ModelCapabilityHookListenerPatches+RunStateHookListenersPatch", true)!;
        EquipmentHookProbe.Models = [target];
        hookHarmony.Patch(sourceMethod, prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"),
            postfix: new HarmonyMethod(AccessTools.Method(capabilityPatch, "Postfix")));
        try
        {
            var baseDamage = target.DynamicVars.Damage.BaseValue;
            var first = new KeMoEquipCapability { LinkRating = 1, EquipName = "fixture one" };
            var second = new KeMoEquipCapability { LinkRating = 2, EquipName = "fixture two" };
            target.AddCapability(first);
            target.AddCapability(second);
            AccessTools.Method(typeof(KeMoEquip), "RefreshEquipmentPreview").Invoke(null, [target]);
            if (target.DynamicVars.Damage.PreviewValue != baseDamage + 18 || target.DynamicVars.Damage.BaseValue != baseDamage)
                throw new Exception("Equipment preview did not include stacked native damage modifiers.");
            AccessTools.Method(typeof(KeMoEquip), "RefreshEquipmentPreview").Invoke(null, [target]);
            if (target.DynamicVars.Damage.PreviewValue != baseDamage + 18) throw new Exception("Repeated preview double-counted equipment.");
            first.RemoveFromOwner();
            AccessTools.Method(typeof(KeMoEquip), "RefreshEquipmentPreview").Invoke(null, [target]);
            if (target.DynamicVars.Damage.PreviewValue != baseDamage + 12) throw new Exception("Unequipped damage remained in preview.");

            var hand = PileType.Hand.GetPile(player);
            var equipmentPile = SelectSpecialCardMod.Scripts.Entry.EquipPile.GetPile(player);
            int removed = 0, finished = 0, changed = 0;
            hand.CardRemoved += card => { if (card == equip) removed++; };
            hand.CardRemoveFinished += () => finished++;
            hand.ContentsChanged += () => changed++;
            hand.RemoveInternal(equip, silent: true);
            equipmentPile.AddInternal(equip);
            if (removed + finished + changed != 0) throw new Exception("Silent source move unexpectedly notified hand.");
            AccessTools.Method(typeof(KeMoEquip), "CompleteSilentEquipmentMove").Invoke(null,
                [new CardPileAddResult { success = true, cardAdded = equip, oldPile = hand, targetPile = equipmentPile.Type }]);
            if (removed != 1 || finished != 1 || changed != 1 || hand.Cards.Contains(equip) || !equipmentPile.Cards.Contains(equip))
                throw new Exception("Equipment source hand did not receive complete removal notifications.");
            second.RemoveFromOwner();
            var localizationPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "SelectSpecialCardMod", "localization", "zhs", "cards.json"));
            var descriptions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(localizationPath))!;
            foreach (var name in new[] { "MO_HONG_SHEN_LU_LI", "KE_MO_MO_JIAN_KE", "KE_MO_ZHEN_NU_RI_ZI", "KE_MO_YAN_SHENG_WU", "HONG_LEI_ZHI_MO_LUO_LEI", "KE_MO_HE_HE_JUN_WANG", "KE_MO_LUO_LEI_RI_ZI" })
                if (!descriptions[$"SELECT_SPECIAL_CARD_MOD_CARD_{name}_CARD.description"].Contains("造成{Damage:diff()}点伤害"))
                    throw new Exception("Equip target description has a fixed primary damage number: " + name);
            if (!descriptions["SELECT_SPECIAL_CARD_MOD_CARD_KE_MO_LUO_LEI_RI_ZI_CARD.description"].EndsWith("对所有敌人造成12点伤害。"))
                throw new Exception("The separate exhaust damage description changed.");
        }
        finally { hookHarmony.UnpatchAll(hookHarmony.Id); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: real capability damage previews stack, refresh without doubling and remove bonuses; silent equipment move restores hand notifications; seven primary damage templates are dynamic.");
    }

    private static async Task CheckMandatorySelection()
    {
        var owner = (MegaCrit.Sts2.Core.Entities.Players.Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var onlyCandidate = (MegaCrit.Sts2.Core.Models.CardModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SelectSpecialCardMod.Scripts.Cards.Special.TianZhiYueCard));
        var secondCandidate = (MegaCrit.Sts2.Core.Models.CardModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SelectSpecialCardMod.Scripts.Cards.Special.KeMoZhenHunGuanCard));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "<Id>k__BackingField").SetValue(onlyCandidate, new MegaCrit.Sts2.Core.Models.ModelId("CARD", "MANUAL_CHOICE_FIXTURE_ONE"));
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "<Id>k__BackingField").SetValue(secondCandidate, new MegaCrit.Sts2.Core.Models.ModelId("CARD", "MANUAL_CHOICE_FIXTURE_TWO"));
        var selector = new ManualChoiceProbe();
        var prompt = new MegaCrit.Sts2.Core.Localization.LocString("card_selection", "manual_choice_fixture");
        var factory = AccessTools.Method(typeof(SelectSpecialCardMod.Scripts.Base.SelectSpecialBaseCard), "CardPrefs");
        using (MegaCrit.Sts2.Core.Commands.CardSelectCmd.UseSelector(selector))
        {
            var automatic = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), [onlyCandidate], owner,
                new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(prompt, 1, 1));
            if (automatic.Single() != onlyCandidate || selector.Calls != 0) throw new Exception("Native automatic shortcut was not reproduced.");
            var manualPrefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)factory.Invoke(null, [null, prompt, 1, 1])!;
            var manual = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), [onlyCandidate], owner, manualPrefs);
            if (manual.Single() != onlyCandidate || selector.Calls != 1) throw new Exception("Single candidate bypassed selection.");
            var twoPrefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)factory.Invoke(null, [null, prompt, 2, 2])!;
            var two = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), [onlyCandidate, secondCandidate], owner, twoPrefs);
            if (two.Count() != 2 || selector.Calls != 2) throw new Exception("Exactly enough candidates bypassed selection.");
            selector.ReturnEmpty = true;
            var cancelled = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), [onlyCandidate], owner,
                manualPrefs with { Cancelable = true });
            if (cancelled.Any() || selector.Calls != 3) throw new Exception("Cancelable single-candidate choice was forced.");
            var optionalPrefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)factory.Invoke(null, [null, prompt, 0, 1])!;
            if (!optionalPrefs.RequireManualConfirmation || optionalPrefs.MinSelect != 0 || optionalPrefs.MaxSelect != 1)
                throw new Exception("Optional card selection lost manual confirmation or its skip range.");
            var skipped = await MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromSimpleGrid(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), [onlyCandidate], owner, optionalPrefs);
            if (skipped.Any() || selector.Calls != 4) throw new Exception("Optional single-candidate choice did not wait for a player decision.");
        }
        GD.Print("PASS: native automatic shortcut reproduced; one/exactly-enough candidates require selection, and optional/cancelable choices preserve skipping (5 checks).");
    }

    private static async Task CheckPersistentExtraReturns()
    {
        // 使用真实模型、原版 CardPileCmd 校验和 Ritsu 动态牌堆；只省略游戏资源启动。
        var pileHarmony = new Harmony("scm.extra-return.godot-framework");
        var pileRuntime = typeof(STS2RitsuLib.CardPiles.ModCardPileRegistry).Assembly;
        foreach (var (target, patchName, prefix) in new[]
        {
            (AccessTools.Method(typeof(CardPile), "Get"), "ModCardPileGetPatch", true),
            (AccessTools.Method(typeof(PileTypeExtensions), "IsCombatPile"), "ModCardPileIsCombatPatch", false),
            (AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Entities.Players.Player), "Piles"), "ModCardPilePlayerPilesPatch", false),
            (AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState), "AllPiles"), "ModCardPileAllPilesPatch", false),
        })
        {
            var patchType = pileRuntime.GetType("STS2RitsuLib.CardPiles.Patches." + patchName, true)!;
            var patch = new HarmonyMethod(AccessTools.Method(patchType, prefix ? "Prefix" : "Postfix"));
            pileHarmony.Patch(target, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var run = (MegaCrit.Sts2.Core.Runs.RunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Runs.RunState));
        var creature = (MegaCrit.Sts2.Core.Entities.Creatures.Creature)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature));
        var combat = new MegaCrit.Sts2.Core.Combat.CombatState(runState: run);
        creature.CombatState = combat;
        AccessTools.Field(creature.GetType(), "_currentHp").SetValue(creature, 10);
        AccessTools.Field(player.GetType(), "<Creature>k__BackingField").SetValue(player, creature);
        AccessTools.Field(player.GetType(), "_runState").SetValue(player, run);
        AccessTools.Field(player.GetType(), "_runPiles").SetValue(player, new MegaCrit.Sts2.Core.Entities.Cards.CardPile[] { new(MegaCrit.Sts2.Core.Entities.Cards.PileType.Deck) });
        AccessTools.Property(player.GetType(), "PlayerCombatState").SetValue(player, new MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState(player));
        var cards = new List<MegaCrit.Sts2.Core.Models.CardModel>
        {
            new SelectSpecialCardMod.Scripts.Cards.Special.TianZhiYueCard(),
            new SelectSpecialCardMod.Scripts.Cards.Special.KeMoZhenHunGuanCard(),
        };
        AccessTools.Field(run.GetType(), "_allCards").SetValue(run, cards);
        var used = SelectSpecialCardMod.Scripts.Entry.ExtraUsedPile.GetPile(player);
        var extra = SelectSpecialCardMod.Scripts.Entry.ExtraPile.GetPile(player);
        int changes = 0;
        extra.ContentsChanged += () => changes++;
        foreach (var card in cards)
        {
            AccessTools.Property(typeof(MegaCrit.Sts2.Core.Models.AbstractModel), "IsMutable").SetValue(card, true);
            card.Owner = player;
            used.AddInternal(card);
            bool originalRejected = false;
            try { await MegaCrit.Sts2.Core.Commands.CardPileCmd.Add(card, extra, skipVisuals: true); }
            catch (InvalidOperationException e) { originalRejected = e.Message.Contains("must be added to a CombatState"); }
            if (!originalRejected) throw new Exception("Fixture did not reproduce the native persistent-card guard.");
            AccessTools.Method(typeof(ExtraDeckSeries), "RestorePersistentExtraCard").Invoke(null, [player, card, extra]);
            if (!ReferenceEquals(card.Pile, extra) || !run.ContainsCard(card) || combat.ContainsCard(card)
                || card.HasBeenRemovedFromState || card.IsInCombat)
                throw new Exception("Persistent extra return corrupted card identity or scope.");
        }
        if (used.Cards.Count != 0 || extra.Cards.Count != 2 || changes != 2)
            throw new Exception("Two persistent fusion materials were not restored exactly once.");
        GD.Print("PASS: native guard reproduced; two persistent extra materials return intact without CombatState registration, with pile notifications.");
    }
}

public static class SpecialEffectPresentationProbe
{
    public static TaskCompletionSource<bool>? Completion;
    public static int EffectCalls;
    public static bool ThrowEffect;
    public static bool Present(ref Task<bool> __result) { __result = Completion!.Task; return false; }
    public static bool CanActivate(ref bool __result) { __result = true; return false; }
    public static bool Effect(ref Task<bool> __result)
    {
        EffectCalls++;
        __result = ThrowEffect ? Task.FromException<bool>(new InvalidOperationException("selection cleanup fixture")) : Task.FromResult(true);
        return false;
    }
}

public static class EquipmentHookProbe
{
    public static IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel> Models = [];
    public static bool Prefix(ref IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel> __result)
    { __result = Models; return false; }
}

public sealed class ManualChoiceProbe : MegaCrit.Sts2.Core.TestSupport.ICardSelector
{
    public int Calls;
    public bool ReturnEmpty;
    public Task<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>> GetSelectedCards(IEnumerable<MegaCrit.Sts2.Core.Models.CardModel> options, int minSelect, int maxSelect)
    {
        Calls++;
        return Task.FromResult<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>>(ReturnEmpty ? Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>() : options.Take(maxSelect).ToArray());
    }
    public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(
        IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
        => throw new NotSupportedException("This fixture only selects cards.");
}
