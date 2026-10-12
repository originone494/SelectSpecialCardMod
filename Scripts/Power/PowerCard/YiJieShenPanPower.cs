using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Power;

// 异解·审判：施加在玩家身上生效的能力。
// 效果1：洗牌时，从除外堆取回1张「异解」卡加入手牌。
// 效果2：打出「异解」卡时，若抽牌堆数量为0，所有敌人在本回合失去99点力量。
[RegisterPower]
public class YiJieShenPanPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 效果1：洗牌时，从除外堆取回1张「异解」卡加入手牌
    public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player player)
    {
        if (!Owner.IsPlayer) return;
        if (player.Creature != Owner) return;

        var yiJieCards = Entry.ChuWaiPile.GetPile(player).Cards
            .Where(c => c.Tags.Contains(SelectTags.YiJie))
            .ToList();
        if (yiJieCards.Count == 0) return;

        var card = player.RunState.Rng.Niche.NextItem(yiJieCards);
        if (card == null) return;

        await CardPileCmd.Add(card, PileType.Hand);
    }

    // 效果2：打出「异解」卡时，若抽牌堆数量为0，所有敌人在本回合失去99点力量
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (!Owner.IsPlayer) return;
        if (cardPlay.Card == null) return;
        if (!cardPlay.Card.Tags.Contains(SelectTags.YiJie)) return;

        if (PileType.Draw.GetPile(Owner.Player!).Cards.Count != 0) return;

        foreach (var enemy in Owner.CombatState!.HittableEnemies)
        {
            await PowerCmd.Apply<YiJieShenPanTempDownPower>(choiceContext, enemy, 99m, Owner, null);
        }
    }
}

// 异解·审判的临时力量（负面）：敌人本回合失去力量，其回合结束自动恢复
[RegisterPower]
public class YiJieShenPanTempDownPower : ModTemporaryAppliedPowerTemplate<YiJieShenPanCard, StrengthPower>
{
    protected override bool IsPositive => false;

    // 自定义图标路径。1:1即可。原版游戏大图256x256，小图64x64。
    public override PowerAssetProfile AssetProfile => new(
        IconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png",
        BigIconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png"
    );

    // 多个临时能力共用一条文本（与 yuno 的 TEMP_STRENGTH_POWER_DOWN 一致）
    public override LocString Description => new("powers", "SELECT_SPECIAL_CARD_MOD_POWER_TEMP_STRENGTH_POWER_DOWN.description");
}