using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
namespace SelectSpecialCardMod.Scripts.Tool;
public enum RetrievalDestination { Hand, Discard, Exhaust }
public enum RetrievalChoice { Player, Random }
public sealed record RetrievalOptions(int Maximum = 1, int Minimum = 0,
    RetrievalDestination Destination = RetrievalDestination.Hand, RetrievalChoice Choice = RetrievalChoice.Player,
    bool Confirm = false);
public static class CardRetrieval
{
    public static Task<List<CardModel>> Retrieve(PlayerChoiceContext context, Player player,
        Func<CardModel, bool> filter, RetrievalOptions options, Func<CardPoolModel, bool>? poolFilter = null,
        LocString? prompt = null, CardModel? source = null)
    {
        if (options.Minimum < 0 || options.Maximum < options.Minimum) throw new ArgumentException("Invalid selection limits.");
        return SelectToolCmd.RetrieveCore(context, player, filter, poolFilter, options.Maximum,
            options.Destination == RetrievalDestination.Discard, options.Choice == RetrievalChoice.Random,
            prompt, source, options.Destination == RetrievalDestination.Exhaust, options.Minimum, options.Confirm);
    }
}
