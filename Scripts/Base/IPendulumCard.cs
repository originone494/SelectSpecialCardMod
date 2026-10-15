namespace SelectSpecialCardMod.Scripts.Base;

// 灵摆刻度独立于怪兽星级和卡牌标签，规则及 UI 都读取同一个数值。
public interface IPendulumCard
{
    int PendulumScale { get; }
}
