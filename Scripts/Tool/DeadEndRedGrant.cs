using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Relics;

namespace SelectSpecialCardMod.Scripts.Tool;

// 开局给本地玩家发放「死亡讯息-红」：本 mod 的特殊卡奖励以它为前提（见 EliteSpecialReward）。
// 移植自 yuno mod 的 RunGameAddRelicReward（那边是模组设置里的一个开关，这边直接发放）。
public static class DeadEndRedGrant
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
        // 只处理本地玩家（单人局即唯一玩家）。联机时各客户端只处理自己的角色。
        // 注意时序：RunStartedEvent 触发于 RunManager.InitializeNewRun，此时 LocalContext.NetId
        // 尚未赋值（RunState.Launch 才赋值），LocalContext.GetMe 会返回 null 并静默跳过。
        // 因此优先用 NetService 的 NetId 匹配本地玩家，匹配不到时回退为列表里的第一个玩家（单人局）。
        var playerList = evt.RunState.Players.ToList();
        ulong localNetId = RunManager.Instance.NetService?.NetId ?? 0;
        Player? me = playerList.FirstOrDefault(p => p.NetId == localNetId) ?? playerList.FirstOrDefault();
        if (me == null)
        {
            Entry.Logger.Error("[DeadEndRedGrant] 未找到本地玩家，放弃发放");
            return;
        }

        if (me.GetRelic<DeadEndRedRelic>() != null)
        {
            return;
        }

        await RelicCmd.Obtain<DeadEndRedRelic>(me);
        Entry.Logger.Info("[DeadEndRedGrant] 已发放「死亡讯息-红」");
    }
}
