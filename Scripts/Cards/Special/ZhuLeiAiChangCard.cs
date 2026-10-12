using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·哀唱（陷阱）
//   不可打出 + 保留：它靠"驻场"与"送墓"生效，一直待在手上
//   驻场：一回合一次（按这张卡自己，手上有几张就各触发一次）。
//         回合结束时（**清空手牌之前**，所以用 BeforeSideTurnEnd 而不是 AfterSideTurnEnd）
//         → 按顺序找第一个"意图匹配"的敌人 → 丢弃1张**自身以外**的卡 → 把它击晕
//         （自身以外没有可丢的卡则不触发）
//   送墓：「检索」1张「珠泪下级怪兽」卡
public class ZhuLeiAiChangCard : SelectSpecialBaseCard, ISongMuCard
{
    // 会被击晕的意图（按用户口径）：
    //   强化=Buff、弱化=Debuff+DebuffStrong、获得格挡=Defend、回血=Heal、
    //   逃跑=Escape、召唤=Summon、塞状态=StatusCard、诅咒=CardDebuff
    private static readonly IntentType[] DisruptiveIntents =
    [
        IntentType.Buff,
        IntentType.Debuff,
        IntentType.DebuffStrong,
        IntentType.Defend,
        IntentType.Heal,
        IntentType.Escape,
        IntentType.Summon,
        IntentType.StatusCard,
        IntentType.CardDebuff,
    ];

    // 「一回合一次」按实例记：记"哪一回合用过"，换回合/换战斗自动失效
    private (CombatId? Combat, int Turn)? _zhuChangUsedKey;

    public ZhuLeiAiChangCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.SongMu,
        SelectTags.ZhuLeiXianJing,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Unplayable, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
    ];

    // 驻场丢弃手牌的选择提示
    private static LocString ZhuChangDiscardPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_AI_CHANG_ZHU_CHANG");

    // 不可打出：这只是一个兜底实现（引擎不会让这张卡被打出）
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    // 驻场：回合结束时（清空手牌之前）
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;   // 驻场：本卡在手上才算

        // 「一回合一次」按这张卡自己算（手上有两张就能各击晕一个敌人）
        var now = PerTurnOnce.CurrentKey(Owner);
        if (_zhuChangUsedKey == now) return;

        // 没卡可丢（除这张卡自己以外）→ 整段不结算（不击晕）
        if (!PileType.Hand.GetPile(Owner).Cards.Any(card => card != this)) return;

        // 按顺序找第一个意图匹配的敌人（一次只处理一个）
        // 意图可能一次带多个（例如"攻击+弱化"），只要其中有任一个匹配就算匹配
        Creature? target = null;
        foreach (Creature enemy in CombatState.HittableEnemies)
        {
            var intents = enemy.Monster?.NextMove.Intents;
            if (intents == null || intents.Count == 0) continue;
            if (!intents.Any(intent => DisruptiveIntents.Contains(intent.IntentType))) continue;

            target = enemy;
            break;
        }
        if (target == null) return;

        // 丢弃1张自身以外的卡（必选 1 张；若没能丢成 → 不结算）
        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, ZhuChangDiscardPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: card => card != this,
            source: this)).ToList();
        if (selected.Count == 0) return;

        await CardCmd.Discard(choiceContext, selected[0]);

        // 记账：这张卡本回合已发动
        _zhuChangUsedKey = now;

        // 击晕它（原版 CreatureCmd.Stun，和爪音用的是同一套）
        await CreatureCmd.Stun(target);
    }

    // 送墓：「检索」1张「珠泪下级怪兽」卡
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await SelectToolCmd.RetrieverCard(
            ctx,
            player,
            c => ZhuLeiFilter.IsLowerMonster(c),
            p => p is SelectSpecialCardPool,
            1, source: this);
    }
}