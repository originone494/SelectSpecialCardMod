namespace SelectSpecialCardMod.Scripts.Rules;

// Period is supplied by the caller; reservations prevent reentry while a choice/cost is resolving.
public sealed class EffectLedger<TPeriod> where TPeriod : notnull
{
    internal sealed class Record(TPeriod period) { public TPeriod Period = period; public bool Committed; }
    private readonly Dictionary<string, Record> _records = [];
    public bool IsUnavailable(string key, TPeriod period)
        => _records.TryGetValue(key, out var r) && EqualityComparer<TPeriod>.Default.Equals(r.Period, period);
    public bool IsCommitted(string key, TPeriod period)
        => _records.TryGetValue(key, out var r) && r.Committed
           && EqualityComparer<TPeriod>.Default.Equals(r.Period, period);
    public void Mark(string key, TPeriod period) => _records[key] = new(period) { Committed = true };
    public Lease? Reserve(string key, TPeriod period)
    {
        if (IsUnavailable(key, period)) return null;
        var record = new Record(period);
        _records[key] = record;
        return new Lease(this, key, record);
    }
    public sealed class Lease : IDisposable
    {
        private readonly EffectLedger<TPeriod> _owner;
        private readonly string _key;
        private readonly Record _record;
        private bool _disposed;
        internal Lease(EffectLedger<TPeriod> owner, string key, Record record)
            => (_owner, _key, _record) = (owner, key, record);
        public void Commit() { if (!_disposed) _record.Committed = true; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_record.Committed && _owner._records.TryGetValue(_key, out var current)
                && ReferenceEquals(current, _record)) _owner._records.Remove(_key);
        }
    }
}
