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

// 异解·多罪海：造成伤害并大量灌注除外堆，除外堆数量达到阈值时逐档解锁额外效果
public class YiJieDuoZuiHaiCard : SelectSpecialBaseCard
{
    public override bool GainsBlock => true;

    private const int _randomCards = 8;
    private const int _freeCostThreshold = 10;   // 10张：免费打出
    private const int _randomDamageThreshold = 20;   // 20张：随机敌人受等同牌数的伤害
    private const int _retrieveThreshold = 30;   // 30张：从除外堆取1张加入手牌
    private const int _blockThreshold = 40;  // 40张：获得等同牌数的格挡

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(24m, ValueProp.Move),
    ];

    public YiJieDuoZuiHaiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieGuaiShou),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 1. 造成伤害
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay).Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        // 2. 向除外堆加入8张随机卡（全卡池完全随机，各抽各的）
        await YiJieExileBag.DrawToChuWai(Owner, _randomCards);

        // 3. 按除外堆数量逐档解锁（在8张随机卡入堆之后统计）
        int count = Entry.ChuWaiPile.GetPile(Owner).Cards.Count;

        if (count >= _randomDamageThreshold)
        {
            // 对随机敌人造成等同于除外堆牌数的伤害
            var randomEnemy = Owner.RunState.Rng.CombatTargets.NextItem(Owner.Creature.CombatState!.HittableEnemies);
            if (randomEnemy != null)
            {
                await DamageCmd.Attack(count).FromCard(this, cardPlay).Targeting(randomEnemy)
                    .WithHitFx("vfx/vfx_attack_slash")
                    .Execute(choiceContext);
            }
        }

        if (count >= _retrieveThreshold)
        {
            // 从除外堆将1张卡加入手牌
            var prefs = CardPrefs(this, SelectionScreenPrompt, 0, 1);
            var picked = (await CardSelectCmd.FromCombatPile(
                choiceContext,
                Entry.ChuWaiPile.GetPile(Owner),
                Owner,
                prefs)).FirstOrDefault();
            if (picked != null)
            {
                await CardPileCmd.Add(picked, PileType.Hand);
            }
        }

        if (count >= _blockThreshold)
        {
            // 获得等同于除外堆牌数的格挡
            await CreatureCmd.GainBlock(Owner.Creature, count, ValueProp.Move, cardPlay);
        }
    }

    // 除外堆数量达到10张时，这张卡可以免费打出（打出前的数量口径）
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (Entry.ChuWaiPile.GetPile(card.Owner).Cards.Count < _freeCostThreshold) return false;

        modifiedCost = 0m;
        return true;
    }
}
