using Windows.Media;
using Windows.Media.Control;

namespace DispCtrl.Display.Shell;

/// <summary>
/// Which apps are playing a video now, from Windows' own media sessions - the
/// list the volume flyout shows.
/// </summary>
/// <remarks>
/// A film watched without touching anything is exactly what OLED care's idle
/// rest should leave alone, and the window showing it need not be in front or
/// fullscreen. Every app that plays media through Windows' transport controls
/// - browsers, the Films and TV app, most players - says so here, with whether
/// it is music or video, so a playlist in the background still lets the
/// displays rest.
/// <para>
/// Event-driven: the sessions say when they start, stop or change, and the set
/// is kept from that; asking costs a lock. Started on first use, so a desk that
/// never switches the option on never loads the WinRT session manager.
/// </para>
/// <para>
/// A session names its app by AppUserModelID, not process: <c>chrome.exe</c>
/// or <c>Chrome</c>, <c>MSEdge</c>, a package family for a Store app, and for
/// Firefox a hash of its install folder. <see cref="ProcessFor"/> turns the
/// common ones into the process name windows are found by. Browsers label
/// every session music, a film included, so a browser counts as video
/// whatever it says.
/// </para>
/// </remarks>
public static class MediaPlayback
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string> Playing = new(StringComparer.Ordinal);
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static int _started;

    /// <summary>Process names (no .exe) of apps playing video now; empty until <see cref="Start"/> has run.</summary>
    public static HashSet<string> VideoApps()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) _ = Task.Run(Start);
        lock (Gate) return new HashSet<string>(Playing.Values, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] Browsers = ["msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "arc"];

    private static void Start()
    {
        try
        {
            _manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            _manager.SessionsChanged += (_, _) => Refresh();
            Refresh();
        }
        catch (Exception) { /* No session manager (an old build, a locked-down machine): nothing ever counts as playing. */ }
    }

    private static readonly HashSet<GlobalSystemMediaTransportControlsSession> Watched = [];

    private static void Refresh()
    {
        if (_manager is null) return;
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (GlobalSystemMediaTransportControlsSession session in _manager.GetSessions())
        {
            lock (Gate)
                if (Watched.Add(session))
                {
                    session.PlaybackInfoChanged += (_, _) => Refresh();
                    session.MediaPropertiesChanged += (_, _) => Refresh();
                }
            try
            {
                GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
                if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                string process = ProcessFor(session.SourceAppUserModelId);
                bool music = info.PlaybackType == MediaPlaybackType.Music;
                if (music && !Browsers.Contains(process, StringComparer.OrdinalIgnoreCase)) continue;
                now[session.SourceAppUserModelId] = process;
            }
            catch (Exception) { /* A session ending while it is read. */ }
        }
        lock (Gate)
        {
            Playing.Clear();
            foreach (var (id, process) in now) Playing[id] = process;
        }
    }

    /// <summary>The process name an AppUserModelID most likely belongs to.</summary>
    public static string ProcessFor(string aumid)
    {
        string id = aumid.Trim();
        // A Store app: Family_publisher!App. The family's last dotted word is
        // usually the process (Microsoft.ZuneVideo -> ZuneVideo); not always,
        // and then only its fullscreen window pauses rest, as before.
        int bang = id.IndexOf('!');
        if (bang > 0)
        {
            string family = id[..bang];
            int under = family.IndexOf('_');
            if (under > 0) family = family[..under];
            return family[(family.LastIndexOf('.') + 1)..];
        }
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = Path.GetFileNameWithoutExtension(id);
        else if (id.Contains('\\')) id = Path.GetFileNameWithoutExtension(id);
        // Firefox registers a 16-hex-digit hash of its install folder.
        if (id.Length == 16 && id.All(Uri.IsHexDigit)) return "firefox";
        return id.Equals("MSEdge", StringComparison.OrdinalIgnoreCase) ? "msedge" : id;
    }
}
