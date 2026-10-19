using MegaCrit.Sts2.Core.Commands;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Myth;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Tool;

// 开局自选、开局奖励及精英奖励共用同一候选规则；决斗盘要求由精英入口单独检查。
public static class SpecialCardRewards
{
    internal static List<CardModel> Candidates(Player player)
        => ModelDb.CardPool<SelectSpecialCardPool>()
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(c => !SelectToolCmd.IsNonObtainable(c))
            .Where(c => CardAvailability.IsAllowed(c, player.RunState))
            .Where(c => !CardClassification.IsExtraMonster(c))
            .GroupBy(c => c.GetType()).Select(g => g.First()).OrderBy(c => c.Id).ToList();

    internal static List<CardModel> RollOptions(Player player)
    {
        var remaining = Candidates(player);
        var offered = new List<CardModel>();
        for (int i = 0; i < 3 && remaining.Count > 0; i++)
        {
            var card = player.PlayerRng.Rewards.NextItem(remaining);
            if (card == null) break;
            remaining.Remove(card);
            offered.Add(card);
        }
        return offered;
    }

    internal static CardReward? Create(Player player, IReadOnlyList<CardModel>? canonicalOptions = null, bool forRunStart = false,
        IReadOnlyList<string>? fixedCards = null)
    {
        var options = canonicalOptions ?? RollOptions(player);
        if (options.Count == 0 && fixedCards == null) return null;
        var cards = fixedCards != null ? MythCardRewards.RestoreCards(player, fixedCards)
            : options.Select(c => player.RunState.CreateCard(c, player)).ToList();
        // Ancient 不能通过原版稀有度随机生成，必须使用指定卡牌构造器。
        // 实例由基类统一升级；重掷兜底仍沿用原有的普通精英奖励参数。
        var reroll = CardCreationOptions.ForRoom(player, RoomType.Elite);
        return forRunStart ? CreateOpeningReward(cards, player, reroll)
            : new CardReward(cards, CardCreationSource.Other, player, reroll);
    }

    internal static CardReward CreateOpeningReward(IEnumerable<CardModel> cards, Player player, CardCreationOptions reroll, bool canSkip = true)
        => new OpeningSpecialCardReward(cards, player, reroll) { CanSkip = canSkip };

    internal static CardReward CreateOpeningMythReward(IEnumerable<CardModel> cards, Player player, CardCreationOptions reroll, bool canSkip = true)
        => new OpeningMythCardReward(cards, player, reroll) { CanSkip = canSkip };

    private sealed class OpeningSpecialCardReward(IEnumerable<CardModel> cards, Player player, CardCreationOptions reroll)
        : OpeningCardReward(cards, player, reroll), INormalSpecialCardReward;

    internal static void TrackOpeningMythReward(CardReward reward, bool wasRerolled, Action<CardReward, bool> changed)
    {
        var opening = (OpeningMythCardReward)reward;
        opening.OptionsChanged = changed;
        if (wasRerolled)
        {
            opening.CanReroll = false;
            AccessTools.Field(typeof(CardReward), "_hasBeenRerolled").SetValue(opening, true);
        }
    }

    private sealed class OpeningMythCardReward(IEnumerable<CardModel> cards, Player player, CardCreationOptions reroll)
        : OpeningCardReward(cards, player, reroll), IMythCardReward
    {
        internal Action<CardReward, bool>? OptionsChanged { get; set; }
        public override void Populate()
        {
            base.Populate();
            if (OptionsChanged != null)
            {
                bool rerolled = (bool)AccessTools.Field(typeof(CardReward), "_hasBeenRerolled").GetValue(this)!;
                if (rerolled) CanReroll = false;
                OptionsChanged(this, rerolled);
            }
        }
    }

    // 无涅奥开局停在空地图，尚无房间历史；原版 CardReward 的领取/跳过会直接访问该历史。
    // 普通开局完全走原版结算；空地图时仍用原版奖励列表和三选一界面，仅省略不存在的房间记录。
    private abstract class OpeningCardReward : CardReward
    {
        public OpeningCardReward(IEnumerable<CardModel> cards, Player player, CardCreationOptions reroll)
            : base(cards, CardCreationSource.Other, player, reroll)
        {
            if (this is IMythCardReward) CardRewardReplacement.KeepMythOptions(reroll);
            else CardRewardReplacement.KeepSpecialOptions(reroll);
        }

        protected override async Task<bool> OnSelect()
        {
            if (Player.RunState.CurrentMapPointHistoryEntry != null) return await base.OnSelect();
            var picked = await CardSelectCmd.FromChooseACardScreen(new BlockingPlayerChoiceContext(), Cards.ToList(), Player, canSkip: true);
            if (picked == null) return false;
            await CardPileCmd.Add(picked, PileType.Deck);
            return true;
        }

        public override void OnSkipped()
        {
            if (Player.RunState.CurrentMapPointHistoryEntry != null) base.OnSkipped();
        }
    }
}
