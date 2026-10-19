using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

public static class PetAllyProbe
{
    public static void Check()
    {
        var placement = typeof(PetAlly).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.PetAllyPlacement", true)!;
        var screen = new Rect2(20, 100, 1400, 600);
        var players = new[] { new Rect2(180, 300, 220, 300), new Rect2(550, 220, 220, 300) };
        var body = new Rect2(250, 420, 160, 180);
        var first = Find(body, []);
        var others = new[] { first.Grow(16) };
        var second = Find(body, others);
        if (first.Intersects(second)) throw new Exception("Multiple summoned pets overlap despite available room.");
        var stable = Find(first, []);
        if (stable != first) throw new Exception("A safe summon position drifts on refresh.");
        Find(body, [screen]); // 空间不足时可以与其他生物让步，但不能与任何玩家重叠。
        var impossibleArgs = new object?[] { new Rect2(0, 0, 1600, 800), players[0], screen, players, Array.Empty<Rect2>(), Vector2.Zero };
        if ((bool)AccessTools.Method(placement, "TryFind").Invoke(null, impossibleArgs)!)
            throw new Exception("An oversized pet was reported as safely placed without resizing.");

        AccessTools.Method(typeof(PetAlly), "Clear").Invoke(null, null);
        try
        {
            var player = ArrivalEquipmentProbe.NewPlayer();
            var combat = player.Creature.CombatState!;
            var pet = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Player, "summon-intent") { CombatState = combat };
            player.PlayerCombatState!.AddPetInternal(pet);
            AccessTools.Field(typeof(Creature), "_currentHp").SetValue(pet, 20);
            PetAlly.Register(pet, 17);
            var intent = PetAlly.IntentOf(pet);
            if (intent is not MonsterAttackCard { IntentDamage: 17, Repeats: 1 } || intent.Owner != player || !PetAlly.IsRegistered(pet))
                throw new Exception("Summon intent did not use its owner's actual registered attack snapshot.");
            for (int i = 0; i < 20; i++)
                if (!ReferenceEquals(intent, PetAlly.IntentOf(pet))) throw new Exception("Viewing summon intent creates new cards.");
            PetAlly.Register(pet, 8);
            if (!ReferenceEquals(intent, PetAlly.IntentOf(pet)) || ((MonsterAttackCard)intent).IntentDamage != 8)
                throw new Exception("Changed summon damage was not reflected by its cached intent.");
            PetAlly.Register(pet, 0);
            if (PetAlly.IntentOf(pet) is not MonsterMoveCard { LiveText: not null })
                throw new Exception("A non-attacking summon did not show an idle intent.");
            AccessTools.Field(typeof(Creature), "_currentHp").SetValue(pet, 0);
            if (PetAlly.IntentOf(pet) != null) throw new Exception("Dead summon retained its intent.");
        }
        finally { AccessTools.Method(typeof(PetAlly), "Clear").Invoke(null, null); }
        GD.Print("PASS: summons avoid all player bounds and screen edges, prefer separate positions, keep safe positions stable, and cache owner-specific attack/idle intent cards without advancing AI.");

        Rect2 Find(Rect2 current, Rect2[] blockers)
        {
            var args = new object?[] { current, players[0], screen, players, blockers, Vector2.Zero };
            if (!(bool)AccessTools.Method(placement, "TryFind").Invoke(null, args)!) throw new Exception("No summon position found despite free space.");
            var placed = new Rect2(current.Position + (Vector2)args[5]!, current.Size);
            if (!screen.Encloses(placed) || players.Any(p => p.Intersects(placed))) throw new Exception("Summon overlaps a player or leaves the usable screen.");
            return placed;
        }
    }
}
