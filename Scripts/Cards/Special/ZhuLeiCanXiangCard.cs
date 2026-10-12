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
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·残响（陷阱）
//   应对：受到敌人攻击前（本卡在手上）→ 丢弃1张攻击卡，对攻击者造成6点伤害、获得7点格挡、对攻击者给予2层虚弱，
//         处理完把这张卡打出（带「消耗」→ 进消耗堆）
//   送墓：从消耗堆将1张「珠泪怪兽」卡加入手牌
//   打出没有效果（按新文案）：靠「应对」与「送墓」生效，手动打出只是把它消耗掉
public class ZhuLeiCanXiangCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiCanXiangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6m, ValueProp.Move),
        new BlockVar(7m, ValueProp.Move),
        new PowerVar<WeakPower>(2),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.SongMu,
        SelectTags.ZhuLeiXianJing,
        SelectTags.YingDui,
    ];

    // 「应对」不进关键字列表（卡面靠"应对：…"纯文本），只挂在悬停提示里；
    // 「保留」是这一版新加的（应对从手牌触发，所以让它留得住）
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust, CardKeyword.Retain];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<WeakPower>(),
        HoverTipFactory.FromKeyword(SelectKeywords.YingDui),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
    ];

    // 打出没有效果（按新文案）：这张卡靠「应对」与「送墓」生效，打出只是把它消耗掉
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay) => Task.CompletedTask;

    // 应对：受到敌人攻击前（参考小美人鱼 / 天下独步的大义贼 / 露莎卡）
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (cardSource != null) return;                 // 只要"受到敌人攻击"
        if (dealer == null || !dealer.IsMonster) return;

        // ⓪ 丢弃1张攻击卡：手牌没有攻击卡（或玩家取消）时，整个应对不发动
        if (!PileType.Hand.GetPile(Owner).Cards.Any(c => c.Type == CardType.Attack)) return;

        var discarded = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: c => c.Type == CardType.Attack,
            source: this)).FirstOrDefault();
        if (discarded == null) return;
        await CardCmd.Discard(choiceContext, discarded);

        // ① 造成6点伤害（对攻击者）
        await CreatureCmd.Damage(choiceContext, dealer, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, null);

        // ② 获得7点格挡（在扣血之前，所以能挡下这次攻击）
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, null);

        // ③ 给予2层虚弱（文本没写目标 → 对攻击者）
        await PowerCmd.Apply<WeakPower>(choiceContext, dealer, DynamicVars.Weak.IntValue, Owner.Creature, this);

        // ④ 效果处理完，把这张卡打出——它带「消耗」，于是被消耗掉
        //    （AutoPlay 只在 card.Pile == null 时才搬进 Play，手牌里的卡要先自己搬）
        await CardPileCmd.Add(this, PileType.Play);
        await CardCmd.AutoPlay(choiceContext, this, null);
    }

    // 送墓：从消耗堆将1张「珠泪怪兽」卡加入手牌（按效果文本口径 = 含融合怪兽）
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(ZhuLeiFilter.IsMonster)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1),
            filter: ZhuLeiFilter.IsMonster)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(4m);
        DynamicVars.Weak.UpgradeValueBy(1);
    }
}