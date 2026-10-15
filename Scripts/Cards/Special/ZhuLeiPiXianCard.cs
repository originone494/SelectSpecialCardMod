using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·劈弦（魔法）
//   不可打出 + 保留
//   驻场：一回合一次（按这张卡自己，不是按卡名：手上有多张就各触发一次）
//         → 打出「珠泪怪兽」卡时（本卡在手上）：从抽牌堆顶将3张卡送入弃牌堆，所有敌人在本回合失去5点力量
//   送墓：「检索」1张「珠泪陷阱」卡
public class ZhuLeiPiXianCard : SelectSpecialBaseCard, ISongMuCard
{
    // 「一回合一次」按实例记：记"哪一回合用过"，换回合/换战斗自动失效
    private (CombatId? Combat, int Turn)? _zhuChangUsedKey;
    private bool _zhuChangPending;
    // 按每次打出记录开始时的驻场资格，嵌套打出互不覆盖；中断的打出可自动释放。
    private ConditionalWeakTable<CardPlay, object> _zhuChangEligiblePlays = new();

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _zhuChangUsedKey = null;
        _zhuChangPending = false;
        _zhuChangEligiblePlays = new();
    }

    public ZhuLeiPiXianCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.SongMu,
        SelectTags.ZhuLei,
        SelectTags.ZhuLeiMoFa,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords =>
        [CardKeyword.Unplayable, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    // 不可打出：这只是一个兜底实现（引擎不会让这张卡被打出）
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        // 必须在这次打出开始时已驻场；本次效果中检索到的劈弦不能追溯触发。
        if (CombatState != null && Pile?.Type == PileType.Hand
            && cardPlay.Card.Owner == Owner && ZhuLeiFilter.IsMonster(cardPlay.Card))
            _zhuChangEligiblePlays.GetValue(cardPlay, _ => new object());
        return Task.CompletedTask;
    }

    // 驻场：一回合一次。保留原来的打出后结算时点，但只处理开始时已驻场的打出。
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!_zhuChangEligiblePlays.Remove(cardPlay)) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;   // 驻场：本卡在手上才算
        if (cardPlay.Card.Owner != Owner) return;
        if (!ZhuLeiFilter.IsMonster(cardPlay.Card)) return;               // 只认「珠泪怪兽」（含融合怪兽）

        // 「一回合一次」按这张卡自己算（手上有几张同名卡就能各触发一次）
        var now = PerTurnOnce.CurrentKey(Owner);
        if (_zhuChangUsedKey == now || _zhuChangPending) return;
        if (PileType.Draw.GetPile(Owner).Cards.Count == 0 && !CombatState.HittableEnemies.Any()) return;
        _zhuChangPending = true;
        try
        {
            if (!await ZhuChangEffectVfx.Play(this, cardPlay)) return;
            if (CombatState == null || Pile?.Type != PileType.Hand || Owner != cardPlay.Card.Owner
                || _zhuChangUsedKey == now) return;
            _zhuChangUsedKey = now;

            await SelectToolCmd.DuiMu(choiceContext, Owner, 3);

        // 所有敌人在本回合失去5点力量（它们的回合结束时自动恢复）
            if (CombatState != null)
                await PowerCmd.Apply<ZhuLeiPiXianTempDownPower>(
                    choiceContext, CombatState.HittableEnemies, 5m, Owner.Creature, this);
        }
        finally { _zhuChangPending = false; }
    }

    // 送墓：「检索」1张「珠泪陷阱」卡
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await SelectToolCmd.RetrieverCard(
            ctx,
            player,
            c => ZhuLeiFilter.IsTrap(c),
            p => p is SelectSpecialCardPool,
            1, source: this);
    }
}
