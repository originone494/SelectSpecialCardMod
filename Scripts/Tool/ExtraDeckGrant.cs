using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;

namespace SelectSpecialCardMod.Scripts.Tool;

// 开局把「额外卡组」里选好的卡放进额外堆（ExtraPile）。
// 额外堆里的卡不会被抽到，只能靠融合/同调这类效果取出——与游戏王的额外卡组一致。
public static class ExtraDeckGrant
{
    public static void Register()
    {
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(evt =>
        {
            _ = Grant(evt);
        });
    }

    private static async Task Grant(RunStartedEvent evt)
    {
        var keys = ExtraDeckStore.Selected;
        if (keys.Count == 0) return;

        // 只处理本地玩家（单人局即唯一玩家），匹配方式与 DeadEndRedGrant 一致。
        var playerList = evt.RunState.Players.ToList();
        ulong localNetId = RunManager.Instance.NetService?.NetId ?? 0;
        Player? me = playerList.FirstOrDefault(p => p.NetId == localNetId) ?? playerList.FirstOrDefault();
        if (me == null)
        {
            Entry.Logger.Error("[ExtraDeckGrant] 未找到本地玩家，放弃发放额外卡组");
            return;
        }

        // 开局也走"补齐到应有内容"这一套：局外选卡 + 系列补给一起算，避免两处逻辑各加一遍
        await ExtraDeckSeries.EnsureFull(me);
        Entry.Logger.Info($"[ExtraDeckGrant] 开局额外卡组已按应有内容放入（局外选择 {keys.Count} 张）");
    }
}
