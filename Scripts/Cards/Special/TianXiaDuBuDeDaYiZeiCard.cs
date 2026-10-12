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

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 天下独步的大义贼：
//   打出：造成20点伤害。
//   特殊效果（右键手牌发动）：场上有「爪牙」，或手牌有除此卡以外的2张攻击卡时可以发动；
//         杀死所有「爪牙」或消耗此卡以外的2张攻击卡，然后这张卡在这个回合可以免费打出。
//   应对：将弃牌堆的这张卡返回抽牌堆，获得16点格挡。
public class TianXiaDuBuDeDaYiZeiCard : SelectSpecialBaseCard, ISpecialEffectCard
{
    public TianXiaDuBuDeDaYiZeiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 明确声明「不可被格挡类附魔」：本卡的格挡在「受到攻击前」的反应里获得，
    // CreatureCmd.GainBlock 只能传 null cardPlay，而引擎取 cardSource = cardPlay?.Card，
    // 所以灵活(Nimble)的 +格挡 加不上。若沿用基类的自动判定（有 BlockVar → true），
    // 菲涅尔透镜会把它附上、卡面还会预览出附魔后的数值，但实际给的格挡不变——显示与结算不符。
    public override bool GainsBlock => false;

    // 伤害使用动态变量：造成 20 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(20m, ValueProp.Move),
        // 卡面直接写了「获得16点格挡」，格挡数值是看得见的，因此声明 Block 动态变量。
        new BlockVar(16m, ValueProp.Move)
    };

    // 「应对」「特殊效果」：仅作为代码标记（标签），没有卡面注入；说明走悬停提示
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YingDui, SelectTags.SpecialEffect];


    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YingDui),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成20点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    // 场上有爪牙，或手牌有"除此卡以外的2张攻击卡"时才能发动特殊效果
    private static List<Creature> Minions(Player player)
        => player.Creature.CombatState?.HittableEnemies.Where(e => e.HasPower<MinionPower>()).ToList() ?? [];

    private List<CardModel> OtherAttackCards(Player player)
        => PileType.Hand.GetPile(player).Cards
            .Where(c => c != this && c.Type == CardType.Attack)
            .ToList();

    public bool CanActivateSpecialEffect(Player player)
        => Minions(player).Count > 0 || OtherAttackCards(player).Count >= 2;

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        var minions = Minions(player);
        if (minions.Count > 0)
        {
            // 杀死所有「爪牙」
            foreach (var minion in minions)
            {
                await CreatureCmd.Kill(minion);
            }
        }
        else
        {
            // 消耗此卡以外的2张攻击卡（进消耗堆）
            var selected = (await CardSelectCmd.FromHand(
                prefs: CardPrefs(this, SelectionScreenPrompt, 0, 2) with { RequireManualConfirmation = true },
                context: choiceContext,
                player: player,
                filter: c => c != this && c.Type == CardType.Attack,
                source: this)).ToList();
            if (selected.Count < 2) return;

            foreach (var card in selected)
            {
                await CardCmd.Exhaust(choiceContext, card);
            }
        }

        // 这张卡在这个回合可以免费打出
        EnergyCost.SetThisTurn(0);
    }

    // 在受到攻击前：将弃牌堆的这张卡返回抽牌堆，获得16点格挡
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (CombatState == null) return;
        if (!PileType.Discard.GetPile(Owner).Cards.Contains(this)) return;
        if (cardSource != null) return;
        if (dealer != null && dealer.IsMonster)
        {
            await CardPileCmd.Add(this, PileType.Draw);
            await CreatureCmd.GainBlock(Owner.Creature, 16m, ValueProp.Move, null);
        }


    }
}
