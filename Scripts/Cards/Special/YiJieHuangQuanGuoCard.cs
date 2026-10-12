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
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异解·黄泉国：获得格挡，向除外堆加入5张随机卡，之后将1张异解怪兽返回抽牌堆并按其类型结算奖励
public class YiJieHuangQuanGuoCard : SelectSpecialBaseCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(16m, ValueProp.Move),
        new DamageVar(10m, ValueProp.Move),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieGuaiShou),
    ];

    public YiJieHuangQuanGuoCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 1. 获得格挡
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 2. 向除外堆加入5张随机卡（全卡池完全随机，各抽各的）
        await YiJieExileBag.DrawToChuWai(Owner, 5);

        // 3. 从除外堆选择1张「异解」返回抽牌堆（可以不选）
        var prefs = CardPrefs(this, SelectionScreenPrompt, 0, 1);
        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            Entry.ChuWaiPile.GetPile(Owner),
            Owner,
            prefs,
            filter: c => c.Tags.Contains(SelectTags.YiJie))).FirstOrDefault();
        if (picked == null) return;

        await CardPileCmd.Add(picked, PileType.Draw);

        // 4. 按返回卡牌的类型结算：攻击牌→对所有敌人造成伤害；技能牌→获得格挡
        switch (picked.Type)
        {
            case CardType.Attack:
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay)
                    .TargetingAllOpponents(Owner.Creature.CombatState!)
                    .Execute(choiceContext);
                break;
            case CardType.Skill:
                await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
                break;
        }
    }
}