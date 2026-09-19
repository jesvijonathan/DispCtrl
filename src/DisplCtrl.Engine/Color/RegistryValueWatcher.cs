using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace DisplCtrl.Engine.Color;

/// <summary>
/// Calls back when a registry key's values change.
/// </summary>
/// <remarks>
/// For Windows' own night light, which has no notification of its own to
/// subscribe to. Polling it would work, but a toggle pressed in Quick Settings
/// would then take up to a tick to reach this process, and a switch that lags a
/// visible second behind the screen it controls reads as broken rather than as
/// slow.
/// <para>
/// A thread each rather than a shared one: the wait is what the thread is for,
/// and there are two keys — the on/off state and the strength — which change
/// independently.
/// </para>
/// </remarks>
internal sealed class RegistryValueWatcher : IDisposable
{
    /// <summary>How long to wait before looking again when the key is missing.</summary>
    /// <remarks>
    /// A key that is not there is not an error worth logging on a loop: a
    /// machine that has never opened the night light settings has never had it
    /// written. Retrying quietly costs nothing and picks it up when it appears.
    /// </remarks>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly string _path;
    private readonly Action _changed;
    private readonly ManualResetEvent _signal = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;

    public RegistryValueWatcher(string path, Action changed)
    {
        _path = path;
        _changed = changed;

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "DisplCtrl registry watch",
        };

        _thread.Start();
    }

    private void Loop()
    {
        while (true)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_path);
                if (key is null)
                {
                    if (_stop.WaitOne(RetryDelay)) return;
                    continue;
                }

                _signal.Reset();

                // Asynchronous, so the call returns and the wait below is what
                // blocks. The key has to stay open until the wait completes —
                // closing it cancels the registration — which is why it is
                // disposed at the end of the iteration rather than straight away.
                WIN32_ERROR registered = PInvoke.RegNotifyChangeKeyValue(
                    key.Handle,
                    bWatchSubtree: true,
                    REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET,
                    _signal.SafeWaitHandle,
                    fAsynchronous: true);

                if (registered != WIN32_ERROR.NO_ERROR)
                {
                    if (_stop.WaitOne(RetryDelay)) return;
                    continue;
                }

                if (WaitHandle.WaitAny([_signal, _stop]) == 1) return;
            }
            catch (Exception ex)
            {
                Log.Write($"registry watch on {_path} failed: {ex.Message}");
                if (_stop.WaitOne(RetryDelay)) return;
                continue;
            }

            try
            {
                _changed();
            }
            catch (Exception ex)
            {
                // A callback that throws must not end the watch, or the feature
                // stops working for the life of the process with nothing said.
                Log.Write($"registry watch callback failed: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _signal.Dispose();
        _stop.Dispose();
    }
}
