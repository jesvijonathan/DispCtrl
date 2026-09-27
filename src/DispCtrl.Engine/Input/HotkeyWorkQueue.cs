namespace DispCtrl.Engine.Input;

/// <summary>Serializes slow shortcut work, with no thread or timer while idle.</summary>
internal sealed class HotkeyWorkQueue(Action<Exception> onError) : IDisposable
{
    internal const int Capacity = 16;
    private readonly Lock _gate = new();
    private readonly Queue<Action> _pending = new();
    private bool _running, _disposed;

    public bool TryEnqueue(Action work)
    {
        lock (_gate)
        {
            if (_disposed || _pending.Count >= Capacity) return false;
            _pending.Enqueue(work);
            if (!_running)
            {
                _running = true;
                _ = Task.Run(Drain);
            }
            return true;
        }
    }

    public void Clear() { lock (_gate) _pending.Clear(); }

    private void Drain()
    {
        while (true)
        {
            Action work;
            lock (_gate)
            {
                if (!_pending.TryDequeue(out work!)) { _running = false; return; }
            }
            try { work(); }
            catch (Exception ex)
            {
                try { onError(ex); }
                catch { /* A failed log must not strand the queue. */ }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _pending.Clear(); }
    }
}
