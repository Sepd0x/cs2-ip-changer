namespace SRT.Core;

/// <summary>
/// Runs a write off the window thread, one at a time, and only for the latest value asked for: two quick changes never write at the
/// same time, and the last one asked is the one that stays. Firewall.Apply isn't reentrant for one rule name (the temporary rule of one
/// call was removed by the other, and the rule could end up old or gone: audit A-08, 13 nov.).
/// </summary>
public sealed class LatestOnly<T>
{
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly object _lock = new();
    T _want;
    long _asked, _written;

    /// <summary>Asks for <paramref name="value"/> to be written. A write already running finishes first; a value asked meanwhile replaces this one.</summary>
    public Task Run(T value, Action<T> write)
    {
        lock (_lock) { _want = value; _asked++; }
        return Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                T now;
                long seq;
                lock (_lock)
                {
                    if (_written >= _asked) return;   // a later run already wrote the latest value
                    now = _want;
                    seq = _asked;
                }
                write(now);
                lock (_lock) _written = seq;
            }
            finally { _gate.Release(); }
        });
    }
}
