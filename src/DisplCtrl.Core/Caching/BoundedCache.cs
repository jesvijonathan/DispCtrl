using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DisplCtrl.Core.Caching;

/// <summary>Bounded LRU cache with one concurrent factory per key and monotonic expiry.</summary>
/// <remarks>Values must be immutable or copied before exposing them to callers.</remarks>
// Lazy<T> carries a parameterless-constructor annotation because of its
// activating overloads. Only the factory overload is used here, so T is never
// constructed reflectively; annotating TValue instead would impose a
// constructor requirement on every cached type for no reason.
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Only the Func<TValue> overload of Lazy<T> is used; TValue is never activated.")]
public sealed class BoundedCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private sealed class Entry(TKey key)
    {
        public TKey Key { get; } = key;
        public required Lazy<TValue> Value { get; init; }
        public long CompletedAt;
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = new();

    public TValue Get(TKey key, TimeSpan lifetime, Func<TValue> read)
    {
        LinkedListNode<Entry> node;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var found)
                && (!found.Value.Value.IsValueCreated
                    || Stopwatch.GetElapsedTime(Volatile.Read(ref found.Value.CompletedAt)) < lifetime))
            {
                node = found;
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            else
            {
                if (found is not null) { _entries.Remove(key); _lru.Remove(found); }
                Entry? entry = null;
                entry = new Entry(key)
                {
                    Value = new Lazy<TValue>(() =>
                    {
                        TValue value = read();
                        Volatile.Write(ref entry!.CompletedAt, Stopwatch.GetTimestamp());
                        return value;
                    }, LazyThreadSafetyMode.ExecutionAndPublication),
                };
                node = _lru.AddFirst(entry);
                _entries[key] = node;
                while (_entries.Count > Math.Max(1, capacity))
                {
                    var last = _lru.Last!;
                    _entries.Remove(last.Value.Key);
                    _lru.RemoveLast();
                }
            }
        }
        try { return node.Value.Value.Value; }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, node))
                { _entries.Remove(key); _lru.Remove(node); }
            throw;
        }
    }

    public void Remove(TKey key)
    {
        lock (_gate)
            if (_entries.Remove(key, out var node)) _lru.Remove(node);
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); _lru.Clear(); }
    }
}
