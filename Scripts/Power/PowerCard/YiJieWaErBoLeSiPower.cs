using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

// 异解·瓦尔波勒斯：回合开始时，向除外堆加入5张随机卡，之后，可以从除外堆将1张「异解」卡加入手牌
[RegisterPower]
public class YiJieWaErBoLeSiPower : SelectSpecialBasePower
{
    private const string _handPromptKey = "SELECT_SPECIAL_CARD_MOD_POWER_YI_JIE_WA_ER_BO_LE_SI_POWER.handPrompt";
    private const int _randomCards = 5;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 回合开始：灌注除外堆，之后可以将1张「异解」卡加入手牌
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        var self = Owner.Player!;
        if (player != self) return;

        // 1. 向除外堆加入5张随机卡
        await YiJieExileBag.DrawToChuWai(self, _randomCards);

        // 2. 可以选择除外堆的1张「异解」卡加入手牌（0-1张，可取消）
        var options = Entry.ChuWaiPile.GetPile(self).Cards
            .Where(c => c.Tags.Contains(SelectTags.YiJie)).ToList();
        if (options.Count == 0) return;

        var prefs = new CardSelectorPrefs(new LocString("powers", _handPromptKey), 0, 1);
        var picked = (await CardSelectCmd.FromCombatPile(choiceContext,
            Entry.ChuWaiPile.GetPile(self), self, prefs,
            filter: c => c.Tags.Contains(SelectTags.YiJie))).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }
}