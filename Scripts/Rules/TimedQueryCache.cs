using System.Runtime.CompilerServices;
namespace SelectSpecialCardMod.Scripts.Rules;
// Only presentation uses cached results. Executors always query live state.
public sealed class TimedQueryCache<TKey, TValue>(ulong lifetime) where TKey : class
{
    private sealed class Entry { public bool Present; public long Revision; public ulong Time; public TValue Value = default!; }
    private readonly ConditionalWeakTable<TKey, Entry> _entries = new();
    public TValue Get(TKey key, long revision, ulong now, Func<TValue> query)
    {
        var entry = _entries.GetOrCreateValue(key);
        if (!entry.Present || entry.Revision != revision || now < entry.Time || now - entry.Time >= lifetime)
        { entry.Value = query(); entry.Time = now; entry.Revision = revision; entry.Present = true; }
        return entry.Value;
    }
}
