using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·天魔之声选器：每个实例每战只能打出一次，回手后保留并适用驻场。
public class YiXiangMingTianMoZhiShengXuanQiCard : SelectSpecialBaseCard
{
    private bool _zhuChangPending;

    public YiXiangMingTianMoZhiShengXuanQiCard()
        : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self) { }

    [SavedProperty]
    public bool PlayedThisCombat { get; set; }

    // 只累计本卡在手牌时实际新增的指示物；减少层数不倒扣，也不消耗能力层数。
    // 未满三的进度在本场战斗内保留，离手期间不计数。
    [SavedProperty]
    public int CounterProgress { get; set; }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.YiXiangMing,
        SelectTags.YiXiangMingMoFa,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuChang),
        HoverTipFactory.FromPower<YiXiangMingZhiShiWuPower>(),
    ];

    protected override bool IsPlayable => !PlayedThisCombat;

    // 自动打出也必须遵守每战一次；OnPlay 另有重复结算兜底。
    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
        => card != this || !PlayedThisCombat;

    // 让原版重复打出、装备清理等流程也读到正确的回手去向。
    public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay,
        ResourceInfo resources, CardLocation cardLocation)
        => card == this
            ? new CardLocation(Owner, PileType.Hand, CardPilePosition.Bottom)
            : cardLocation;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (PlayedThisCombat || CombatState == null || Owner == null) return;
        PlayedThisCombat = true;
        // 先回手再检索，避免检索占满最后一个手牌位置后把本卡挤入弃牌堆。
        // 入堆及满手处理仍由原版命令负责。
        await CardPileCmd.Add(this, PileType.Hand);
        if (CombatState == null || CombatManager.Instance.IsOverOrEnding) return;
        await CardRetrieval.Retrieve(choiceContext, Owner,
            c => c.Tags.Contains(SelectTags.YiXiangMingGuaiShou),
            new RetrievalOptions(), p => p is SelectSpecialCardPool, source: this);
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        PlayedThisCombat = false;
        CounterProgress = 0;
        _zhuChangPending = false;
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        PlayedThisCombat = false;
        CounterProgress = 0;
        _zhuChangPending = false;
    }

    private bool ZhuChangApplies => CombatState != null && Pile?.Type == PileType.Hand
        && Owner?.Creature.IsAlive == true && !CombatManager.Instance.IsOverOrEnding;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext,
        PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (!ZhuChangApplies || power is not YiXiangMingZhiShiWuPower
            || power.Owner != Owner.Creature || (int)amount <= 0) return;

        CounterProgress += (int)amount;
        if (_zhuChangPending || CounterProgress < 3) return;
        _zhuChangPending = true;
        var combat = CombatState;
        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            try
            {
                // 计数即时更新，伤害作为衍生效果排队；后续指示物只追加进度，不重复入队。
                while (CounterProgress >= 3 && ZhuChangApplies && CombatState == combat)
                {
                    if (!await ZhuChangEffectVfx.Play(this, power)) break;
                    if (!ZhuChangApplies || CombatState != combat) break;
                    CounterProgress -= 3;
                    var enemies = combat!.HittableEnemies.ToList();
                    if (enemies.Count > 0)
                        await CreatureCmd.Damage(choiceContext, enemies, 20m, ValueProp.Unpowered,
                            Owner.Creature, this, null);
                }
            }
            finally { _zhuChangPending = false; }
        }, () => _zhuChangPending = false);
    }
}
