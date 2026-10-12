using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Intent;

// 静态数值表（自动生成，请勿手改）：来源是本地反编译的怪物源码
// MegaCrit.Sts2.Core.Models.Monsters\*.cs，脚本抓取每个招式方法里的
// PowerCmd.Apply<X> / CreatureCmd.GainBlock / CreatureCmd.Heal 参数。
// 能力效果记录【种类 + 层数】（强化不一定是力量、削弱不一定是虚弱）。
//
// 表里存的是「表达式」而不是死数值：能静态算出来的写成数字，
// 算不出来的（属性名、属性*属性、*人数）原样存下来，运行期用 MonsterExpr
// 在**这只怪实例**上求值 —— 于是升阶分支 / 动态属性 / 随人数倍率都能对。
//
// 取用顺序：观测记录（最准）→ 本表（从没观测到时的兜底）→ 占位值。
internal static class MonsterEffectTable
{
    private sealed class Entry
    {
        internal string? Block;
        internal string? Heal;
        internal readonly List<(string Power, string Amount)> SelfPowers = new();
        internal readonly List<(string Power, string Amount)> PlayerPowers = new();
        internal readonly List<(string Kind, int MaxHp)> Summoned = new();
    }

    private static Entry E(string? block = null, string? heal = null,
        (string, string)[]? selfPowers = null, (string, string)[]? playerPowers = null,
        (string, int)[]? summoned = null)
    {
        var entry = new Entry { Block = block, Heal = heal };
        if (selfPowers != null) entry.SelfPowers.AddRange(selfPowers);
        if (playerPowers != null) entry.PlayerPowers.AddRange(playerPowers);
        if (summoned != null) entry.Summoned.AddRange(summoned);
        return entry;
    }

    private static readonly Dictionary<string, Entry> Table = new()
    {
        ["AEONGLASS|EBB_MOVE"] = E(block: "33"),
        ["AEONGLASS|INCREASING_INTENSITY_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "IncreasingIntensityTotalStrength") }),
        ["AXEBOT|BOOT_UP_MOVE"] = E(block: "BootUpBlock", selfPowers: new[] { ("StrengthPower", "BootUpStrGain * RespawnCount") }),
        ["AXEBOT|HAMMER_UPPERCUT_MOVE"] = E(playerPowers: new[] { ("WeakPower", "2"), ("FrailPower", "2") }),
        ["AXE_RUBY_RAIDER|SWING_1"] = E(block: "SwingBlock"),
        ["AXE_RUBY_RAIDER|SWING_2"] = E(block: "SwingBlock"),
        ["BOWLBUG_EGG|BITE_MOVE"] = E(block: "ProtectBlock"),
        ["BOWLBUG_NECTAR|BUFF_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "BuffStrengthGain") }),
        ["BOWLBUG_SILK|TOXIC_SPIT_MOVE"] = E(playerPowers: new[] { ("WeakPower", "1") }),
        ["BRUTE_RUBY_RAIDER|ROAR_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["BYGONE_EFFIGY|WAKE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "10") }),
        ["CALCIFIED_CULTIST|INCANTATION_MOVE"] = E(selfPowers: new[] { ("RitualPower", "2") }),
        ["CEREMONIAL_BEAST|BEAST_CRY_MOVE"] = E(playerPowers: new[] { ("RingingPower", "1") }),
        ["CEREMONIAL_BEAST|CRUSH_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "CrushStrength") }),
        ["CEREMONIAL_BEAST|PLOW_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["CEREMONIAL_BEAST|STAMP_MOVE"] = E(selfPowers: new[] { ("PlowPower", "PlowAmount") }),
        ["CORPSE_SLUG|GOOP_MOVE"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["CROSSBOW_RUBY_RAIDER|RELOAD_MOVE"] = E(block: "3"),
        ["CRUSHER|ADAPT_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "AdaptStrengthGain") }),
        ["CRUSHER|BUG_STING_MOVE"] = E(playerPowers: new[] { ("WeakPower", "2"), ("FrailPower", "2") }),
        ["CRUSHER|GUARDED_STRIKE_MOVE"] = E(block: "18"),
        ["CUBEX_CONSTRUCT|CHARGE_UP_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["CUBEX_CONSTRUCT|REPEATER_BLAST_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["CUBEX_CONSTRUCT|REPEATER_BLAST_MOVE_2"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["DAMP_CULTIST|INCANTATION_MOVE"] = E(selfPowers: new[] { ("RitualPower", "IncantationAmount") }),
        ["DECIMILLIPEDE_SEGMENT|BULK_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["DECIMILLIPEDE_SEGMENT|CONSTRICT_MOVE"] = E(playerPowers: new[] { ("WeakPower", "1") }),
        ["DEVOTED_SCULPTOR|FORBIDDEN_INCANTATION_MOVE"] = E(selfPowers: new[] { ("RitualPower", "9") }),
        ["ENTOMANCER|PHEROMONE_SPIT_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2"), ("PersonalHivePower", "1"), ("StrengthPower", "1") }),
        ["EXOSKELETON|ENRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["FAKE_MERCHANT_MONSTER|ENRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["FAKE_MERCHANT_MONSTER|THROW_RELIC_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["FLAIL_KNIGHT|WAR_CHANT"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["FLYCONID|FRAIL_SPORES_MOVE"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["FLYCONID|VULNERABLE_SPORES_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "2") }),
        ["FOGMOG|ILLUSION_MOVE"] = E(summoned: new[] { ("EYE_WITH_TEETH", 0) }),
        ["FOGMOG|SWIPE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "1") }),
        ["FOGMOG|SWIPE_RANDOM_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "1") }),
        ["FOSSIL_STALKER|TACKLE_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["FROG_KNIGHT|FOR_THE_QUEEN"] = E(selfPowers: new[] { ("StrengthPower", "5") }),
        ["FROG_KNIGHT|TONGUE_LASH"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["FUZZY_WURM_CRAWLER|INHALE"] = E(selfPowers: new[] { ("StrengthPower", "7") }),
        ["GLOBE_HEAD|GALVANIC_BURST"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["GLOBE_HEAD|SHOCKING_SLAP"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["GREMLIN_MERC|DOUBLE_SMASH_MOVE"] = E(playerPowers: new[] { ("WeakPower", "2") }),
        ["GREMLIN_MERC|HEHE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["HAUNTED_SHIP|HAUNT_MOVE"] = E(playerPowers: new[] { ("WeakPower", "3") }),
        ["HUNTER_KILLER|TENDERIZING_GOOP_MOVE"] = E(playerPowers: new[] { ("TenderPower", "1") }),
        ["INFESTED_PRISM|PULSATE_MOVE"] = E(block: "PulsateBlock", selfPowers: new[] { ("VitalSparkPower", "VitalSparkAmount") }),
        ["INFESTED_PRISM|RADIATE_MOVE"] = E(block: "RadiateBlock"),
        ["KIN_FOLLOWER|POWER_DANCE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "DanceStrength") }),
        ["KIN_PRIEST|ORB_OF_FRAILTY_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["KIN_PRIEST|ORB_OF_WEAKNESS_MOVE"] = E(playerPowers: new[] { ("WeakPower", "1") }),
        ["KIN_PRIEST|RITUAL_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "RitualStrength") }),
        ["KNOWLEDGE_DEMON|PONDER_MOVE"] = E(heal: "30 * base.CombatState.Players.Count", selfPowers: new[] { ("StrengthPower", "PonderStrength") }),
        ["LAGAVULIN_MATRIARCH|SLASH2_MOVE"] = E(block: "Slash2Block"),
        ["LAGAVULIN_MATRIARCH|SOUL_SIPHON_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }, playerPowers: new[] { ("StrengthPower", "-2"), ("DexterityPower", "-2") }),
        ["LIVING_FOG|ADVANCED_GAS_MOVE"] = E(playerPowers: new[] { ("SmoggyPower", "1") }),
        ["LIVING_FOG|BLOAT_MOVE"] = E(summoned: new[] { ("GAS_BOMB", 0) }),
        ["LIVING_SHIELD|SMASH_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["LOUSE_PROGENITOR|CURL_AND_GROW_MOVE"] = E(block: "CurlBlock", selfPowers: new[] { ("StrengthPower", "GrowStrength") }),
        ["LOUSE_PROGENITOR|WEB_CANNON_MOVE"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["MAGI_KNIGHT|DAMPEN_MOVE"] = E(playerPowers: new[] { ("DampenPower", "1") }),
        ["MAGI_KNIGHT|POWER_SHIELD_MOVE"] = E(block: "PowerShieldBlock"),
        ["MAGI_KNIGHT|PREP_MOVE"] = E(block: "PowerShieldBlock"),
        ["MAWLER|ROAR_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "3") }),
        ["MECHA_KNIGHT|WINDUP_MOVE"] = E(block: "15", selfPowers: new[] { ("StrengthPower", "5") }),
        ["MYTE|SUCK_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "SuckStrength") }),
        ["NIBBIT|HISS_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "HissStrengthGain") }),
        ["NIBBIT|SLICE_MOVE"] = E(block: "SliceBlock"),
        ["OVICOPTER|LAY_EGGS_MOVE"] = E(summoned: new[] { ("TOUGH_EGG", 0) }),
        ["OVICOPTER|NUTRITIONAL_PASTE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "NutritionalPasteStrengthAmount") }),
        ["OVICOPTER|TENDERIZER_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "2") }),
        ["OWL_MAGISTRATE|JUDICIAL_FLIGHT"] = E(selfPowers: new[] { ("SoarPower", "1") }),
        ["OWL_MAGISTRATE|VERDICT"] = E(playerPowers: new[] { ("VulnerablePower", "4") }),
        ["PHANTASMAL_GARDENER|ENLARGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "EnlargeStr") }),
        ["PUNCH_CONSTRUCT|FAST_PUNCH_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["PUNCH_CONSTRUCT|READY_MOVE"] = E(block: "10"),
        ["QUEEN|BURN_BRIGHT_FOR_ME_MOVE"] = E(block: "20"),
        ["QUEEN|ENRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["QUEEN|PUPPET_STRINGS_MOVE"] = E(playerPowers: new[] { ("ChainsOfBindingPower", "3") }),
        ["QUEEN|YOU_ARE_MINE_MOVE"] = E(playerPowers: new[] { ("FrailPower", "99"), ("WeakPower", "99"), ("VulnerablePower", "99") }),
        ["ROCKET|CHARGE_UP_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "ChargeUpStrengthGain") }),
        ["SCROLL_OF_BITING|MORE_TEETH"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["SEAPUNK|BUBBLE_BURP_MOVE"] = E(block: "BubbleBlock", selfPowers: new[] { ("StrengthPower", "BubbleStr") }),
        ["SEWER_CLAM|PRESSURIZE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "4") }),
        ["SHRINKER_BEETLE|SHRINKER_MOVE"] = E(playerPowers: new[] { ("ShrinkPower", "-1") }),
        ["SKULKING_COLONY|INERTIA_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "InertiaStrengthGain") }),
        ["SLIMED_BERSERKER|LEECHING_HUG_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }, playerPowers: new[] { ("WeakPower", "3") }),
        ["SLITHERING_STRANGLER|CONSTRICT"] = E(playerPowers: new[] { ("ConstrictPower", "3") }),
        ["SLITHERING_STRANGLER|THWACK"] = E(block: "5"),
        ["SLUDGE_SPINNER|OIL_SPRAY_MOVE"] = E(playerPowers: new[] { ("WeakPower", "1") }),
        ["SLUDGE_SPINNER|RAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["SLUMBERING_BEETLE|ROLL_OUT_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["SNAPPING_JAXFRUIT|ENERGY_ORB_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["SOUL_FYSH|FADE_MOVE"] = E(selfPowers: new[] { ("IntangiblePower", "2") }),
        ["SOUL_FYSH|SCREAM_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "3") }),
        ["SOUL_NEXUS|DRAIN_LIFE_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "2"), ("WeakPower", "2") }),
        ["SPECTRAL_KNIGHT|HEX"] = E(playerPowers: new[] { ("HexPower", "2") }),
        ["SPINY_TOAD|PROTRUDING_SPIKES_MOVE"] = E(selfPowers: new[] { ("ThornsPower", "5") }),
        ["SPINY_TOAD|SPIKE_EXPLOSION_MOVE"] = E(selfPowers: new[] { ("ThornsPower", "-5") }),
        ["STABBOT|STAB_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["TERROR_EEL|TERROR_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "99") }),
        ["TERROR_EEL|THRASH_MOVE"] = E(selfPowers: new[] { ("VigorPower", "6") }),
        ["TEST_SUBJECT|BURNING_GROWL_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "BurningGrowlStrengthGain") }),
        ["TEST_SUBJECT|RESPAWN_MOVE"] = E(selfPowers: new[] { ("PainfulStabsPower", "1"), ("NemesisPower", "1") }),
        ["TEST_SUBJECT|SKULL_BASH_MOVE"] = E(playerPowers: new[] { ("VulnerablePower", "1") }),
        ["THE_ADVERSARY_MK_ONE|BARRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["THE_ADVERSARY_MK_THREE|BARRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "4") }),
        ["THE_ADVERSARY_MK_TWO|BARRAGE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["THE_FORGOTTEN|MIASMA"] = E(block: "8", selfPowers: new[] { ("DexterityPower", "DebilitatingSmogDexStealAmount") }, playerPowers: new[] { ("DexterityPower", "-DebilitatingSmogDexStealAmount") }),
        ["THE_INSATIABLE|LIQUIFY_GROUND_MOVE"] = E(selfPowers: new[] { ("SandpitPower", "4") }),
        ["THE_INSATIABLE|SALIVATE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "SalivateStrength") }),
        ["THE_LOST|DEBILITATING_SMOG"] = E(selfPowers: new[] { ("StrengthPower", "DebilitatingSmogStrengthStealAmount") }, playerPowers: new[] { ("StrengthPower", "-DebilitatingSmogStrengthStealAmount") }),
        ["THE_OBSCURA|HARDENING_STRIKE_MOVE"] = E(block: "HardeningStrikeBlock"),
        ["THE_OBSCURA|ILLUSION_MOVE"] = E(summoned: new[] { ("PARAFRIGHT", 0) }),
        ["THE_OBSCURA|SAIL_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "3") }),
        ["THIEVING_HOPPER|FLUTTER_MOVE"] = E(selfPowers: new[] { ("FlutterPower", "5") }),
        ["THIEVING_HOPPER|THIEVERY_MOVE"] = E(selfPowers: new[] { ("SwipePower", "1") }),
        ["TOADPOLE|SPIKEN_MOVE"] = E(selfPowers: new[] { ("ThornsPower", "2") }),
        ["TOADPOLE|SPIKE_SPIT_MOVE"] = E(selfPowers: new[] { ("ThornsPower", "-SpikenAmount") }),
        ["TRACKER_RUBY_RAIDER|TRACK_MOVE"] = E(playerPowers: new[] { ("FrailPower", "2") }),
        ["TUNNELER|BURROW_MOVE"] = E(block: "BlockGain", selfPowers: new[] { ("BurrowedPower", "1") }),
        ["TURRET_OPERATOR|RELOAD_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "1") }),
        ["TWO_TAILED_RAT|CALL_FOR_BACKUP_MOVE"] = E(summoned: new[] { ("TWO_TAILED_RAT", 0) }),
        ["TWO_TAILED_RAT|SCREECH_MOVE"] = E(playerPowers: new[] { ("FrailPower", "1") }),
        ["VANTOM|PREPARE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
        ["VINE_SHAMBLER|GRASPING_VINES_MOVE"] = E(playerPowers: new[] { ("TangledPower", "1") }),
        ["WATERFALL_GIANT|PRESSURE_GUN_MOVE"] = E(selfPowers: new[] { ("SteamEruptionPower", "3") }),
        ["WATERFALL_GIANT|PRESSURE_UP_MOVE"] = E(selfPowers: new[] { ("SteamEruptionPower", "3") }),
        ["WATERFALL_GIANT|PRESSURIZE_MOVE"] = E(selfPowers: new[] { ("SteamEruptionPower", "PressurizeAmount") }),
        ["WATERFALL_GIANT|RAM_MOVE"] = E(selfPowers: new[] { ("SteamEruptionPower", "3") }),
        ["WATERFALL_GIANT|SIPHON_MOVE"] = E(heal: "SiphonHeal * base.CombatState.Players.Count", selfPowers: new[] { ("SteamEruptionPower", "3") }),
        ["WATERFALL_GIANT|STOMP_MOVE"] = E(selfPowers: new[] { ("SteamEruptionPower", "3") }, playerPowers: new[] { ("WeakPower", "1") }),
        ["WRIGGLER|WRIGGLE_MOVE"] = E(selfPowers: new[] { ("StrengthPower", "2") }),
    };

    // 没收录 / 求值失败返回 null 或全 0（调用方退回占位值）
    internal static MonsterMoveEffects? Lookup(string? monsterKind, string? moveId, Creature? source)
    {
        if (string.IsNullOrEmpty(monsterKind) || string.IsNullOrEmpty(moveId)) return null;
        if (!Table.TryGetValue(monsterKind + "|" + moveId, out var entry)) return null;

        var fx = new MonsterMoveEffects
        {
            Block = MonsterExpr.Eval(entry.Block, source),
            Heal = MonsterExpr.Eval(entry.Heal, source),
        };
        AddPowers(fx.SelfPowers, entry.SelfPowers, source);
        AddPowers(fx.PlayerPowers, entry.PlayerPowers, source);
        fx.SummonedMonsters.AddRange(entry.Summoned);
        return fx;
    }

    private static void AddPowers(List<PowerEffect> into,
        List<(string Power, string Amount)> from, Creature? source)
    {
        foreach (var (power, amountExpr) in from)
        {
            int amount = MonsterExpr.Eval(amountExpr, source);
            if (amount > 0) into.Add(new PowerEffect(power, amount));
        }
    }
}
