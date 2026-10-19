using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class DaiZuiADaiCard : SelectSpecialBaseCard
{
    public DaiZuiADaiCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Target?.Monster == null || !cardPlay.Target.IsAlive || CombatState == null) return;

        var original = cardPlay.Target;

        // 落点：优先占空闲槽位；入场后强制检查"不许挡住任何敌人"（见 CopySpawnSlot 注释）
        string? slot = CopySpawnSlot.PickSlot(CombatState);

        var copy = await EnemyCopySpawn.Add(original, CombatState, slot);

        CopySpawnSlot.EnsureClearOf(copy, original);

        await CreatureCmd.SetMaxHp(copy, original.MaxHp);
        await CreatureCmd.SetCurrentHp(copy, original.CurrentHp);
        await CreatureCmd.Stun(copy);

        // 链接能力挂在**复制品自己**身上（Owner = 复制品），只需要告诉它"本体是谁"
        var power = ModelDb.Power<DaiZuiADaiPower>().ToMutable();
        var daiZuiPower = (DaiZuiADaiPower)power;
        daiZuiPower.Original = original;
        await PowerCmd.Apply(choiceContext, power, copy, 1, Owner.Creature, this);
        // 本体可能在异步入场／施加能力期间死亡，补上已经错过的死亡事件。
        if (original.IsDead && copy.IsAlive) await CreatureCmd.Kill(copy);
    }
}
