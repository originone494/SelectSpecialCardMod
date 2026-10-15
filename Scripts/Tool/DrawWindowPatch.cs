using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace SelectSpecialCardMod.Scripts.Tool;

// 只包住 Draw 状态机实际调用的单卡 Add，不把抽牌回调中的其他加牌当作抽牌。
[HarmonyPatch]
public static class DrawWindowPatch
{
    private static readonly HashSet<CardModel> DrawingCards = [];

    public static bool IsDrawing(CardModel card) => DrawingCards.Contains(card);

    static MethodBase TargetMethod()
    {
        var parameters = new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) };
        var draw = AccessTools.Method(typeof(CardPileCmd), "DrawInternal", parameters)
            ?? AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw), parameters);
        // 游戏发行程序集可能移除了 AsyncStateMachineAttribute，按该重载的参数字段匹配。
        var machine = draw.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? typeof(CardPileCmd).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith($"<{draw.Name}>d__", StringComparison.Ordinal)
                    && t.GetField("count")?.FieldType == typeof(decimal)
                    && t.GetField("player")?.FieldType == typeof(Player)
                    && t.GetField("fromHandDraw")?.FieldType == typeof(bool));
        return AccessTools.Method(machine, nameof(IAsyncStateMachine.MoveNext));
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var add = AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
            [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]);
        var wrappedAdd = AccessTools.Method(typeof(DrawWindowPatch), nameof(AddDrawnCard));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(add))
            {
                instruction.operand = wrappedAdd;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException($"Draw 单卡入堆调用数量异常：{replaced}");
    }

    public static async Task<CardPileAddResult> AddDrawnCard(CardModel card, CardPile pile,
        CardPilePosition position, AbstractModel? clonedBy, bool skipVisuals)
    {
        bool marked = pile.Type == PileType.Hand && DrawingCards.Add(card);
        try
        {
            return await CardPileCmd.Add(card, pile, position, clonedBy, skipVisuals);
        }
        finally
        {
            if (marked) DrawingCards.Remove(card);
        }
    }
}
