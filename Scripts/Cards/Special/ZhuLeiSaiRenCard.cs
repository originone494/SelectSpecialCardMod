using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 打出：造成14点伤害
// 特殊效果（右键手牌发动）：丢弃除自身以外的1张手牌 → 这张卡在这个回合可以免费打出
// 送墓：进行一次珠泪融合；触发过珠泪融合的情况，将这张卡打出
public class ZhuLeiSaiRenCard : SelectSpecialBaseCard, ISongMuCard, ISpecialEffectCard
{
    public ZhuLeiSaiRenCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 14 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(14m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiRongHe,
        SelectTags.SpecialEffect,
    ];

    // 卡面不再注入「珠泪融合」关键字（送墓文案直接写在描述里）；机制判据走标签，悬停里保留关键字说明
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiRongHe),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成14点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    // 特殊效果：丢弃除自身以外的1张手牌，然后这张卡在这个回合可以免费打出
    // 发动条件：手牌里有除自身以外的卡可丢（没有就不能发动、不亮金边）
    public bool CanActivateSpecialEffect(Player player)
        => PileType.Hand.GetPile(player).Cards.Any(c => c != this);

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        if (hand.Cards.Count == 0) return false;

        // 由玩家自己选（范围 1~1，发动后必须选一张）：无有效选项时防御性退出
        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1) with { RequireManualConfirmation = true },
            context: choiceContext,
            player: player,
            filter: c => c != this,
            source: this)).ToList();
        if (selected.Count != 1 || selected[0].Owner != player || selected[0].Pile?.Type != PileType.Hand) return false;
        SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();

        await CardCmd.Discard(choiceContext, selected[0]);
        EnergyCost.SetThisTurn(0);
        return true;
    }

    // 送墓：进行一次珠泪融合；触发过珠泪融合的情况，将这张卡打出
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        // 触发过珠泪融合（同名卡一场战斗一次已用掉）→ 将这张卡打出（随机敌人）
        if (ZhuLeiFusion.HasFusedThisCombat(this))
        {
            Creature? enemy = player.RunState.Rng.CombatTargets.NextItem(player.Creature.CombatState!.HittableEnemies);
            if (enemy != null)
            {
                await SongMuHook.AutoPlayFromDiscard(ctx, this, enemy);
            }
            return;
        }

        // 进行一次珠泪融合
        await ZhuLeiFusion.TryFusion(ctx, player, this);
    }
}
