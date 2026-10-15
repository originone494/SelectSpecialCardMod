using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·抉择：技能，先古，0费。卡图按准确类名继承基类 PortraitPath。
public class YiXiangMingJueZeCard : YiXiangMingChoiceCard
{
    protected override async Task ResolveChoice(PlayerChoiceContext context, bool first)
    {
        if (first)
            await CardPileCmd.Draw(context, 2m, Owner);
        else
            await CardRetrieval.Retrieve(context, Owner,
                c => c.Tags.Contains(SelectTags.YiXiangMingMoFa) || c.Tags.Contains(SelectTags.YiXiangMingXianJing),
                new RetrievalOptions(Minimum: 1, Confirm: true), p => p is SelectSpecialCardPool, source: this);
    }
}
