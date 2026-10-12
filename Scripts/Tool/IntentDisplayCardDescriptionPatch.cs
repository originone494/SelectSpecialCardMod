using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 怪物卡的动态描述：CardModel.Description 是非虚属性，
// 用 Harmony 在 getter 之后替换为注入了动作文本的 LocString。
// 攻击卡注入伤害值/段数，其余怪物卡注入意图动作文本。
// 注入文本为空时不替换——保留卡面静态描述（图鉴里显示的就是它）。
// （不能用 {Damage:diff()} 变量：CreateCard 克隆的注册单例伤害为 0，
//   且意图数值本身已含力量修正，直接嵌文本更准确。）
[HarmonyPatch(typeof(CardModel), "Description", MethodType.Getter)]
public static class IntentDisplayCardDescriptionPatch
{
    [HarmonyPostfix]
    static void Postfix(CardModel __instance, ref LocString __result)
    {
        switch (__instance)
        {
            case MonsterAttackCard atk when !string.IsNullOrWhiteSpace(atk.IntentText):
            {
                // 动态伤害回退：数值读不到（IntentDamage=0），描述用意图原文
                var p = new LocString("cards",
                    "SELECT_SPECIAL_CARD_MOD_CARD_MONSTER_ATTACK_CARD.description_fallback");
                p.Add("IntentText", atk.IntentText);
                __result = p;
                break;
            }
            case MonsterAttackCard atk:
            {
                var key = atk.Repeats > 1
                    ? "SELECT_SPECIAL_CARD_MOD_CARD_MONSTER_ATTACK_CARD.description_multi"
                    : "SELECT_SPECIAL_CARD_MOD_CARD_MONSTER_ATTACK_CARD.description";
                var p = new LocString("cards", key);
                p.Add("DamageText", atk.IntentDamage.ToString());
                p.Add("RepeatText", atk.Repeats.ToString());
                __result = p;
                break;
            }
            case MonsterDisplayCard live when live.LiveText != null:
            {
                // 每次取描述都重算：伤害随力量/虚弱等实时变化（意图数值含 ModifyDamage 修正），
                // 否则玩家看到的还是建堆时的旧数值。
                var p = new LocString("cards",
                    $"SELECT_SPECIAL_CARD_MOD_CARD_{ModelKey(live)}.description_dynamic");
                p.Add("IntentText", live.LiveText());
                __result = p;
                break;
            }
            case MonsterDisplayCard txt when !string.IsNullOrWhiteSpace(txt.IntentText):
            {
                // 战斗中：注入意图动作文本（hover 描述）；图鉴里 IntentText 为空，
                // 不进此分支，保留卡面静态 description（图鉴展示用）
                var p = new LocString("cards",
                    $"SELECT_SPECIAL_CARD_MOD_CARD_{ModelKey(txt)}.description_dynamic");
                p.Add("IntentText", txt.IntentText);
                __result = p;
                break;
            }
        }
    }

    // 类名 → 本地化键段（全类名蛇形大写，与卡描述键约定一致）：
    // MonsterCardDebuffCard → MONSTER_CARD_DEBUFF_CARD
    private static string ModelKey(CardModel model)
    {
        var name = model.GetType().Name;
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }
}
