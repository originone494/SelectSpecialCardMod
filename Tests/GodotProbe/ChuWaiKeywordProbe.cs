using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using STS2RitsuLib.Keywords;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;

public static class ChuWaiKeywordProbe
{
    private static int _sourceExhausts;
    private static CardModel? _source;

    public static async Task Check()
    {
        var h = new Harmony("scm.chu-wai-keyword.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            // 仅控制可见模型列表；生产的除外前缀由完整 Entry.Init 加载。
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Runs.RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(GameHook), "AfterCardExhausted"), prefix: new HarmonyMethod(typeof(ChuWaiKeywordProbe), nameof(Exhaust)));
            var nativeResult = AccessTools.Method(typeof(CardModel), "GetResultLocationForCardPlay");
            foreach (bool automatic in new[] { false, true })
            {
                var player = ArrivalEquipmentProbe.NewPlayer();
                var token = ArrivalEquipmentProbe.AddCard(typeof(KeMoYanShengWuCard), player, PileType.Play);
                _source = token; _sourceExhausts = 0;
                EquipmentHookProbe.Models = [token];
                if (!token.Keywords.Contains(SelectKeywords.ChuWai) || token.Keywords.Contains(CardKeyword.Exhaust))
                    throw new Exception("Token did not replace Exhaust with ChuWai.");
                var initial = (CardLocation)nativeResult.Invoke(token, null)!;
                var result = GameHook.ModifyCardPlayResultLocation(token.CombatState!, token, automatic, default, initial, out _);
                if (result.pileType != Entry.ChuWaiPile || result.player != player || result.position != initial.position)
                    throw new Exception("Exile keyword did not preserve play destination ownership/position.");
                // 模拟原版重复打出的两个 AfterCardPlayed，最终落堆仍由原版 Add 执行。
                for (int index = 0; index < 2; index++)
                {
                    var play = new CardPlay
                    {
                        Card = token, Player = player, Target = null, ResultPile = result.pileType,
                        Resources = default, IsAutoPlay = automatic, PlayIndex = index, PlayCount = 2,
                    };
                    await GameHook.AfterCardPlayed(token.CombatState!, new BlockingPlayerChoiceContext(), play);
                    if (token.Pile?.Type != PileType.Play) throw new Exception("Exile moved the card before replay finished.");
                }
                var moved = await CardPileCmd.Add(token, result.pileType, result.position, skipVisuals: true);
                if (!moved.success || token.Pile?.Type != Entry.ChuWaiPile || _sourceExhausts != 0
                    || PileType.Exhaust.GetPile(player).Cards.Contains(token) || token.HasBeenRemovedFromState)
                    throw new Exception("Exile failed to keep the real card in the exile pile without exhausting it.");
            }

            // 原版卡也支持运行时添加/移除该关键字，证明行为不依赖衍生物或模组基类。
            var vanilla = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), ArrivalEquipmentProbe.NewPlayer(), PileType.Play);
            EquipmentHookProbe.Models = [vanilla];
            if (Resolve(vanilla).pileType != PileType.Discard) throw new Exception("Unmarked vanilla card no longer discards.");
            vanilla.AddKeyword(CardKeyword.Exhaust);
            if (Resolve(vanilla).pileType != PileType.Exhaust) throw new Exception("Ordinary Exhaust card behavior changed.");
            vanilla.AddKeyword(SelectKeywords.ChuWai);
            if (Resolve(vanilla).pileType != Entry.ChuWaiPile) throw new Exception("Runtime exile keyword failed on a vanilla card.");
            vanilla.RemoveKeyword(SelectKeywords.ChuWai);
            if (Resolve(vanilla).pileType != PileType.Exhaust) throw new Exception("Removing exile keyword did not restore ordinary behavior.");

            var attribute = typeof(SelectKeywords).GetCustomAttributesData().Single(a => a.AttributeType.Name == "RegisterOwnedCardKeywordAttribute"
                && a.ConstructorArguments[0].Value?.ToString() == "ChuWai");
            var placement = attribute.NamedArguments.Single(a => a.MemberName == "CardDescriptionPlacement").TypedValue.Value;
            if (Convert.ToInt32(placement) != (int)ModKeywordCardDescriptionPlacement.AfterCardDescription)
                throw new Exception("Exile keyword is not appended like native Exhaust.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "SelectSpecialCardMod", "localization", "zhs", "card_keywords.json"));
            var text = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(path))!;
            if (text["SELECT_SPECIAL_CARD_MOD_KEYWORD_CHU_WAI.title"] != "除外"
                || text["SELECT_SPECIAL_CARD_MOD_KEYWORD_CHU_WAI.description"] != "在战斗结束前移除，并进入[gold]除外堆[/gold]。")
                throw new Exception("Exile localization did not match the intended keyword.");

            CardLocation Resolve(CardModel card) => GameHook.ModifyCardPlayResultLocation(card.CombatState!, card, false, default,
                (CardLocation)nativeResult.Invoke(card, null)!, out _);
        }
        finally { h.UnpatchAll(h.Id); EquipmentHookProbe.Models = []; _source = null; }
        GD.Print("PASS: token manual/auto results exile after replay; native pile movement does not exhaust/remove the card; runtime keywords work on vanilla cards; ordinary discard/exhaust and keyword removal remain correct; appended keyword/localization verified.");
    }

    public static bool Exhaust(CardModel card, ref Task __result)
    {
        if (card == _source) _sourceExhausts++;
        __result = Task.CompletedTask;
        return false;
    }
}
