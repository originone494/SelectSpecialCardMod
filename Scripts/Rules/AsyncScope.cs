using System.Threading;
namespace SelectSpecialCardMod.Scripts.Rules;
public sealed class AsyncScope<T> where T : class
{
    public sealed class Frame(T value, Frame? parent)
    { public T Value { get; } = value; internal Frame? Parent { get; } = parent; public bool Active { get; set; } = true; }
    private readonly AsyncLocal<Frame?> _current = new();
    public T? Current => _current.Value is { Active: true } f ? f.Value : null;
    public Frame Enter(T value) { var frame = new Frame(value, _current.Value); _current.Value = frame; return frame; }
    public void Restore(Frame frame) { if (ReferenceEquals(_current.Value, frame)) _current.Value = frame.Parent; }
    public void Complete(Frame frame) { frame.Active = false; Restore(frame); }
}
