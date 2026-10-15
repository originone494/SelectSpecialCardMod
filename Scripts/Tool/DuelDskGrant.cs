using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Relics;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class DuelDskGrant
{
    public static void Register() { } // RunSetup awaits this on every peer.
    public static async Task Grant(Player player)
    {
        var disk = player.GetRelic<DuelDskRelic>();
        foreach (var legacy in player.Relics.OfType<DeadEndRedRelic>().ToList())
        {
            if (disk == null)
            {
                // 原版 Replace 保留遗物栏位置；迁移后也保留原来的获得楼层。
                int floor = legacy.FloorAddedToDeck;
                disk = (DuelDskRelic)await RelicCmd.Replace(legacy, ModelDb.Relic<DuelDskRelic>().ToMutable());
                disk.FloorAddedToDeck = floor;
            }
            else
            {
                // 已有决斗盘时只清理兼容遗物，不重复发放。
                await RelicCmd.Remove(legacy);
            }
        }
        if (disk == null) await RelicCmd.Obtain<DuelDskRelic>(player);
    }
}
