using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·爪音（陷阱）
//   应对：含攻击的敌人意图执行前（本卡在手上）→ 先消耗本卡，
//         击晕并取消整条意图，然后「检索」并丢弃1张「珠泪怪兽」卡
//   送墓：从弃牌堆将1张「珠泪怪兽」卡加入手牌
//   不可手动打出：靠「应对」与「送墓」生效
public class ZhuLeiZhuaYinCard : SelectSpecialBaseCard, ISongMuCard
{
    // 目标改成自身：新文案里打出没有效果、也没有需要指定的敌人，留"指定敌人"只会让玩家白点一下
    public ZhuLeiZhuaYinCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.SongMu,
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiXianJing,
        SelectTags.YingDui,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Unplayable, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.YingDui),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    // 应对效果由MonsterDeck在整条意图执行前结算，不通过OnPlay或伤害回调。
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    // 所有客户端按同一玩家顺序、手牌顺序处理；一张成功应对后即停止，不消耗其他爪音。
    internal static async Task<bool> TryCancelIntent(Creature attacker, MonsterMoveCard intent)
    {
        if (!attacker.IsAlive || attacker.Side != CombatSide.Enemy || attacker.Monster == null
            || attacker.CombatState is not { } combat || intent.IsPlaceholder
            || !intent.Intents.Any(i => i.IntentType is IntentType.Attack or IntentType.DeathBlow)) return false;

        foreach (var player in combat.Players.OrderBy(p => p.NetId))
        {
            if (!player.Creature.IsAlive) continue;
            foreach (var card in PileType.Hand.GetPile(player).Cards.OfType<ZhuLeiZhuaYinCard>().ToArray())
            {
                if (card.Owner != player || card.CombatState != combat || card.Pile?.Type != PileType.Hand) continue;
                if (await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Run(
                        () => card.Respond(new BlockingPlayerChoiceContext(), attacker))) return true;
            }
        }
        return false;
    }

    private async Task<bool> Respond(PlayerChoiceContext choiceContext, Creature attacker)
    {
        // ① 先消耗手牌中的本卡；消耗失败时不继续结算应对。
        var exhausted = await CardCmd.Exhaust(choiceContext, this);
        if (exhausted?.success != true) return false;

        // ② 本次意图由调用方整体跳过，使用原版击晕演出。
        // 不再设置STUNNED后续状态：外层原生回合会正常记录本次行动，下一手正常推进，
        // 避免多空过一回合、重新执行被取消的招式或打断一次性招式的转换条件。
        if (attacker.GetVfxContainer() is { } container && NStunnedVfx.Create(attacker) is { } vfx)
            container.AddChildSafely(vfx);

        // ③ 「检索」并丢弃1张「珠泪怪兽」卡
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            ZhuLeiFilter.IsMonster,
            p => p is SelectSpecialCardPool,
            1,
            isDiscard: true, source: this);
        return true;
    }

    // 送墓：从弃牌堆将1张「珠泪怪兽」卡加入手牌
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        var discardPile = PileType.Discard.GetPile(player);
        if (!discardPile.Cards.Any(ZhuLeiFilter.IsMonster)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            discardPile,
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1),
            filter: ZhuLeiFilter.IsMonster)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }
}
