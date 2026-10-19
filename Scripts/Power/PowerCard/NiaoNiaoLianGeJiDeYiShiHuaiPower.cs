using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

[RegisterPower]
public class NiaoNiaoLianGeJiDeYiShiHuaiPower : SelectSpecialBasePower, ISongMuObserver
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 送墓：任意一张卡触发送墓时（不再限定「珠泪怪兽」），从抽牌堆选 1 张费用不超过 1 的卡送入弃牌堆
    public async Task OnSongMu(PlayerChoiceContext ctx, Player player, CardModel trigger)
    {
        if (player != Owner.Player) return;
        await SendOneFromDrawToDiscard(ctx, player);
    }

    // 奇巧：原版机制（CardKeyword.Sly）。带奇巧的卡本回合从手牌被效果丢弃时会被免费打出，这一刻即视为触发。
    // 送墓的触发在 OnSongMu 里处理，这里只处理奇巧，避免同一张卡（同时带送墓与奇巧）触发两次。
    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        await base.AfterCardDiscarded(choiceContext, card);
        if (Owner?.Player is null) return;
        if (card.Owner != Owner.Player) return;
        if (SongMuHook.HasSongMu(card)) return;
        if (!card.IsSlyThisTurn) return;
        var owner = Owner;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            if (Owner != owner || CombatState != combat || !owner.IsAlive || !owner.Powers.Contains(this)
                || MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding) return;
            choiceContext.PushModel(this);
            try { await SendOneFromDrawToDiscard(choiceContext, owner.Player!); }
            finally { choiceContext.PopModel(this); }
        });
    }

    private async Task SendOneFromDrawToDiscard(PlayerChoiceContext ctx, Player player)
    {
        var drawPile = PileType.Draw.GetPile(player);
        var candidates = drawPile.Cards
            .Where(card => card.EnergyCost.GetWithModifiers(CostModifiers.None) <= 1)
            .ToList();

        if (candidates.Count == 0) return;

        var selected = (await CardSelectCmd.FromSimpleGrid(
            ctx,
            candidates,
            player,
            new CardSelectorPrefs(SelectionScreenPrompt, 1, 1) { RequireManualConfirmation = true })).FirstOrDefault();

        if (selected != null)
        {
            await CardCmd.Discard(ctx, selected);
        }
    }
}
