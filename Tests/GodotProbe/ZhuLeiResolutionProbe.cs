using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;

// 检查真实劈弦前后钩子和返回牌堆入口；不加载完整战斗场景。
public static class ZhuLeiResolutionProbe
{
    private static int _presentations;
    private static bool? _skipVisuals;

    public static async Task Check()
    {
        var h = new Harmony("scm.tearlaments-resolution.godot-framework");
        try
        {
            var vfx = typeof(ZhuLeiPiXianCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.ZhuChangEffectVfx", true)!;
            h.Patch(AccessTools.Method(vfx, "Play"), prefix: new HarmonyMethod(typeof(ZhuLeiResolutionProbe), nameof(Present)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            var context = new BlockingPlayerChoiceContext();
            var reno = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiLeiNuoHaTeCard), player, PileType.Play);
            ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Draw);
            var outer = MakePlay(reno);
            var late = (ZhuLeiPiXianCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiPiXianCard), player, PileType.Hand);
            _presentations = 0;
            await late.AfterCardPlayed(context, outer); // 结算中才获得的劈弦没有开始时资格。
            AssertPresentations(0);
            var next = MakePlay(reno);
            await late.BeforeCardPlayed(next);
            await late.AfterCardPlayed(context, next);
            AssertPresentations(1); // 下一次打出可正常响应。
            await late.AfterCardPlayed(context, next);
            AssertPresentations(1); // 同一个打出通知不重复结算。

            outer = MakePlay(reno);
            var inner = MakePlay(reno);
            await late.BeforeCardPlayed(outer);
            await late.BeforeCardPlayed(inner);
            await late.AfterCardPlayed(context, inner);
            await late.AfterCardPlayed(context, outer);
            AssertPresentations(3); // 嵌套打出不会覆盖外层资格。

            var departed = MakePlay(reno);
            await late.BeforeCardPlayed(departed);
            late.Pile!.RemoveInternal(late);
            PileType.Discard.GetPile(player).AddInternal(late);
            await late.AfterCardPlayed(context, departed);
            AssertPresentations(3); // 结算时已离手，不发动。
            late.Pile!.RemoveInternal(late);
            PileType.Hand.GetPile(player).AddInternal(late);
            await late.AfterCardPlayed(context, departed);
            AssertPresentations(3); // 回手后也不能重用已结束的资格。

            // 捕获显示路由并移动实际卡牌，验证不生成重复卡、不改牌堆目标。
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ZhuLeiResolutionProbe), nameof(Add)));
            await SelectToolCmd.ReturnCardToPile(reno, PileType.Draw);
            AssertReturn(reno, false);
            var handCard = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiLeiNuoHaTeCard), player, PileType.Hand);
            await SelectToolCmd.ReturnCardToPile(handCard, PileType.Draw);
            AssertReturn(handCard, false);
            var hidden = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiSaiRenCard), player, PileType.Discard);
            await SelectToolCmd.ReturnCardToPile(hidden, PileType.Draw);
            AssertReturn(hidden, true);
        }
        finally { h.UnpatchAll(h.Id); _presentations = 0; _skipVisuals = null; }
        GD.Print("PASS: late-acquired Pi Xian skips the current play, subsequent/nested plays retain eligibility, repeated/departed sources skip; Play/Hand returns use native visuals and hidden returns keep custom visuals without duplicating cards. Actual animation requires in-game verification.");
    }

    private static CardPlay MakePlay(CardModel card) => new()
    {
        Card = card, Player = card.Owner, Target = null, ResultPile = PileType.Discard,
        Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1,
    };

    private static void AssertPresentations(int expected)
    {
        if (_presentations != expected) throw new Exception("Pi Xian responded to the wrong card play.");
    }

    private static void AssertReturn(CardModel card, bool expectedSkip)
    {
        if (_skipVisuals != expectedSkip || card.Pile?.Type != PileType.Draw
            || PileType.Draw.GetPile(card.Owner).Cards.Count(c => ReferenceEquals(c, card)) != 1)
            throw new Exception("Return-to-draw routing or card identity regressed.");
    }

    public static bool Present(ref Task<bool> __result)
    {
        _presentations++;
        // 中止演出后的效果，避免真正堆墓和施加能力；资格判断保留真实实现。
        __result = Task.FromResult(false);
        return false;
    }

    public static bool Add(CardModel card, PileType newPileType, CardPilePosition position, bool skipVisuals, ref Task<CardPileAddResult> __result)
    {
        _skipVisuals = skipVisuals;
        var old = card.Pile;
        card.RemoveFromCurrentPile(skipVisuals);
        newPileType.GetPile(card.Owner).AddInternal(card, position == CardPilePosition.Top ? 0 : -1);
        __result = Task.FromResult(new CardPileAddResult { cardAdded = card, success = true, oldPile = old, targetPile = newPileType });
        return false;
    }
}
