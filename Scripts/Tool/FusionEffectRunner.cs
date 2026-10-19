using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Cards.Other;

namespace SelectSpecialCardMod.Scripts.Tool;

public static class FusionEffectRunner
{
    public static async Task Execute(ShenHuaCompositeCard source, PlayerChoiceContext context, CardPlay cardPlay)
    {
        var ctx = new FusionActionContext(source, context, cardPlay);
        foreach (var module in source.Modules)
        for (int i = 0; i < module.Actions.Count; i++)
        {
            if (!ctx.CanContinue) return;
            await FusionActions.Execute(module.Actions[i], FusionEffectLibrary.VarKey(module.Id, i), ctx);
        }
    }
}
