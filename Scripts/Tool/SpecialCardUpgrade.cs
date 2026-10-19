using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

public static class SpecialCardUpgrade
{
    // 玩家可获取的特殊卡和可打出的神话合体卡；怪物意图、选择项及其他机制衍生卡保持原样。
    public static bool AppliesTo(CardModel card)
        => card is ShenHuaCompositeCard || (card is SelectSpecialBaseCard && card is not IMonsterDisplayCard
           && card.Rarity != CardRarity.Token && card.ShouldShowInCardLibrary);

    public static void Ensure(CardModel card)
    {
        if (!card.IsMutable || !AppliesTo(card) || card.IsUpgraded || !card.IsUpgradable) return;
        // 原版内部升级仅设置升级状态和更新数值缓存，不触发命令演出或战斗升级钩子。
        // SelectSpecialBaseCard 的 OnUpgrade 固定为空，所有现有效果均为最终值。
        card.UpgradeInternal();
        card.FinalizeUpgradeInternal();
    }
}

// 必须等原版完成存档中的升级次数还原后再补齐，避免升级存档重复升到+2。
[HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
public static class SpecialCardLoadedUpgradePatch
{
    public static void Postfix(CardModel __result) => SpecialCardUpgrade.Ensure(__result);
}
