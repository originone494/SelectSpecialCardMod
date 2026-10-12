using System.Collections.Generic;
using System.Threading.Tasks;
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

public class ZhuLeiMeiLuoCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiMeiLuoCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 8 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.SongMu,
        SelectTags.ZhuLeiRongHe,
        SelectTags.ZhuLeiXiaJiGuaiShou,
    ];

    // 卡面不再注入「珠泪融合」关键字（送墓文案直接写在描述里）；机制判据走标签，悬停里保留关键字说明
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiRongHe),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成8点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);

        // 从抽牌堆顶将3张卡送入弃牌堆
        await SelectToolCmd.DuiMu(choiceContext, Owner, 3);
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