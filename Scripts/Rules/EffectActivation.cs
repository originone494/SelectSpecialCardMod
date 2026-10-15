using System.Threading;
namespace SelectSpecialCardMod.Scripts.Rules;
public enum ActivationOutcome { Activated, NotActivated, ConditionChanged }
public readonly record struct ActivationResult(ActivationOutcome Outcome)
{
    public bool Activated => Outcome == ActivationOutcome.Activated;
}
public static class EffectActivation
{
    private static readonly AsyncLocal<Action?> Payment = new();
    public static IDisposable Begin(Action commit)
    {
        var previous = Payment.Value;
        Payment.Value = commit;
        return new Scope(() => Payment.Value = previous);
    }
    public static void PaymentStarted() => Payment.Value?.Invoke();
    private sealed class Scope(Action restore) : IDisposable
    { private bool _disposed; public void Dispose() { if (!_disposed) { _disposed = true; restore(); } } }
}
