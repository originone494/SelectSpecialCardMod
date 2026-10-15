using Godot;
namespace SelectSpecialCardMod.Scripts.Hook;
public partial class RunSeenSyncTicker : Node
{
    public override void _Process(double delta) => RunSeenCards.FlushPending();
}
