using System.Collections;

namespace ProfitFox.MarketData.Tests.Shared.Bybit;

/// <summary>
///     Requests a fake records in order, safe to read while the app keeps sending them from its own threads: every read sees a
///     snapshot taken under the lock the fake records under.
/// </summary>
public sealed class RequestLog<T> : IReadOnlyCollection<T>
{
    private readonly List<T> _items = [];
    private readonly Lock _lock = new Lock();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return Snapshot().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>The requests recorded so far, as a copy later requests do not change.</summary>
    public List<T> Snapshot()
    {
        lock (_lock)
        {
            return [.. _items];
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    public void Record(T item)
    {
        lock (_lock)
        {
            _items.Add(item);
        }
    }
}