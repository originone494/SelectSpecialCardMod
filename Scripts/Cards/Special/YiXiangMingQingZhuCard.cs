using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·倾注：技能，先古，0费。卡图按准确类名继承基类 PortraitPath。
public class YiXiangMingQingZhuCard : YiXiangMingChoiceCard
{
    protected override async Task ResolveChoice(PlayerChoiceContext context, bool first)
    {
        await CardRetrieval.Retrieve(context, Owner,
            c => c.Tags.Contains(SelectTags.YiXiangMing) && (first || c.Id != Id),
            first
                ? new RetrievalOptions(Minimum: 1, Choice: RetrievalChoice.Random)
                : new RetrievalOptions(Minimum: 1, Destination: RetrievalDestination.Exhaust, Confirm: true),
            p => p is SelectSpecialCardPool, source: this);
    }
}
