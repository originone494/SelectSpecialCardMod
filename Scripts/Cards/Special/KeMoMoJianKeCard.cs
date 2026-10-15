using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·魔镌客（主卡组的「刻魔怪兽」，不是额外怪兽）
//   打出：造成18点伤害
//   特殊效果（右键手牌发动）：消耗这张卡，「检索」1张「刻魔魔法」或「刻魔陷阱」
//   时机（从消耗堆发动，同名卡一回合一次）：从消耗堆将此卡以外的1张「光恶魔」卡返回抽牌堆·额外堆
//                          （额外怪兽 → 额外堆，其他的卡 → 抽牌堆），将消耗堆的这张卡加入手牌
public class KeMoMoJianKeCard : SelectSpecialBaseCard, ISpecialEffectCard, IShiJiCard
{
    public KeMoMoJianKeCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(18m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.GuangEMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.KeMo,
        SelectTags.SpecialEffect,
        SelectTags.ShiJi,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成18点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 特殊效果：消耗这张卡，「检索」1张「刻魔魔法」或「刻魔陷阱」
    public bool CanActivateSpecialEffect(Player player) => true;

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        // 先「检索」：让玩家选完卡，再消耗自己
        var retrieved = await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            IsKeMoMagicOrTrap,
            p => p is SelectSpecialCardPool,
            1, source: this,
            minAmount: 1, requireManualConfirmation: true);   // 范围 (1,1)：必须选 1 张，只有 1 张候选也要自己点

        if (retrieved.Count == 0) return false;
        SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();
        // 之后消耗这张卡（走原版 CardCmd.Exhaust：从手牌消耗会播原版快速消耗特效 + 音效）
        await CardCmd.Exhaust(choiceContext, this);
        return true;
    }

    // 时机：只能从消耗堆发动
    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    // 「同名卡一回合一次」：按卡名（注册 Id）记，多份拷贝共享一次
    private string ShiJiOnceKey => PerTurnOnce.Key("ShiJi", Id.ToString());

    public bool CanActivateShiJi(Player player)
        => !PerTurnOnce.IsUsed(player, ShiJiOnceKey)
           && PileType.Exhaust.GetPile(player).Cards.Any(c => c != this && IsGuangEMo(c));

    private static LocString ShiJiPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_MO_JIAN_KE_SHI_JI");

    // 时机发动（同名卡一回合一次）：从消耗堆将此卡以外的1张「光恶魔」卡返回（额外怪兽→额外堆，其他→抽牌堆），
    // 之后，将消耗堆的这张卡加入手牌。
    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(c => c != this && IsGuangEMo(c))) return;

        // CardPrefsConfirm：只有 1 张候选也不能替玩家自动选
        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefsConfirm(this, ShiJiPrompt, 1, 1),
            filter: c => c != this && IsGuangEMo(c))).FirstOrDefault();
        if (picked == null) return;

        // 同名卡一回合一次（真正选到卡、效果成立才记账；中途放弃不算发动）
        PerTurnOnce.Mark(player, ShiJiOnceKey);

        // 额外怪兽 → 额外堆；其他的卡 → 抽牌堆
        if (picked.Tags.Contains(SelectTags.ExtraMonster))
            await ExtraDeckSeries.ReturnExtraMonster(player, picked);   // 额外怪兽：持久卡还给额外堆，副本退场
        else
            await SelectToolCmd.ReturnCardToPile(picked, PileType.Draw);

        // 将消耗堆的这张卡加入手牌
        await CardPileCmd.Add(this, PileType.Hand);
    }

    private static bool IsGuangEMo(CardModel c) => c.Tags.Contains(SelectTags.GuangEMo);

    private static bool IsKeMoMagicOrTrap(CardModel c)
        => c.Tags.Contains(SelectTags.KeMoMoFa) || c.Tags.Contains(SelectTags.KeMoXianJing);
}
