using DispCtrl.Control;
using DispCtrl.Core;
using DispCtrl.Core.Settings;
using DispCtrl.Display;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    private UpdateSettings Updates => _settings.Global.Updates;
    private DispatcherQueueTimer? _updateTimer;
    private bool _checkingUpdates, _updateAnnounced;
    private string _updateStatus = "";

    /// <summary>A Microsoft Store install: the Store updates it, and DispCtrl never checks.</summary>
    public bool UpdatesFromStore => StartupIntegration.IsPackaged;

    public Visibility UpdateSwitchVisibility => UpdatesFromStore ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The opt-in daily check. Off until somebody switches it on.</summary>
    public bool CheckUpdatesAutomatically
    {
        get => Updates.CheckAutomatically;
        set
        {
            if (Updates.CheckAutomatically == value) return;
            Updates.CheckAutomatically = value;
            Persist();
            Raise();
            if (value) _ = CheckForUpdatesAsync(automatic: true);
        }
    }

    public string UpdateDescription => UpdatesFromStore
        ? $"This is {BuildInfo.Version}, from the Microsoft Store, which keeps it up to date."
        : _updateStatus.Length > 0 ? _updateStatus
        : Updates.CheckedUtc is { } at
            ? $"This is {BuildInfo.Version}. Last checked {at.ToLocalTime():d MMM, HH:mm}{(Updates.LatestVersion.Length > 0 ? $": the newest release is {Updates.LatestVersion}" : "")}."
            : $"This is {BuildInfo.Version}. An update installs over this one and keeps your settings.";

    /// <summary>The release waiting to be installed, if a check found one and it was not set aside.</summary>
    private UpdateResult? Available => UpdateCheck.Known(Updates);

    public Visibility UpdateAvailableVisibility => Available is null ? Visibility.Collapsed : Visibility.Visible;

    public string UpdateAvailableText => Available is { } a
        ? $"DispCtrl {a.Latest} is available. You have {a.Current}; the new version installs over it and keeps your settings."
        : "";

    public string UpdateUrl => Available?.Url ?? UpdateCheck.ReleasesPage;

    public bool UpdateCheckReady => !_checkingUpdates;

    /// <summary>
    /// Starts the daily check: a look shortly after start, then every six
    /// hours, each of which asks GitHub only when checking is on and a day has
    /// passed. Nothing at all happens for a Store install.
    /// </summary>
    private void StartUpdateChecks()
    {
        if (UpdatesFromStore) return;
        DispatcherQueue? ui = DispatcherQueue.GetForCurrentThread();
        if (ui is null) return;
        _updateTimer = ui.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromHours(6);
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(automatic: true);
        _updateTimer.Start();
        // Not on the start-up path: the panel and the pages come first.
        ui.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CheckForUpdatesAsync(automatic: true);
        });
    }

    /// <summary>Checks for a new release: now when asked, otherwise only if due.</summary>
    /// <returns>True when the check got an answer.</returns>
    public async Task<bool> CheckForUpdatesAsync(bool automatic)
    {
        if (UpdatesFromStore || _checkingUpdates) return false;
        if (automatic && !Updates.Due(DateTimeOffset.UtcNow))
        {
            AnnounceUpdate();
            return false;
        }
        _checkingUpdates = true;
        if (!automatic) { _updateStatus = "Checking…"; RaiseUpdates(); }
        try
        {
            UpdateResult result = await Task.Run(() => UpdateCheck.CheckAsync()).ConfigureAwait(true);
            // Recorded on disk by the check; mirrored here so the page does not wait for the file watcher.
            Updates.CheckedUtc = DateTimeOffset.UtcNow;
            Updates.LatestVersion = result.Latest;
            Updates.LatestUrl = result.Url;
            _updateStatus = result.Newer ? "" : $"This is {result.Current}, the latest version. Checked just now.";
            // Asked for by hand, a release set aside with "Not now" is offered again.
            if (!automatic && result.Newer && Updates.SkippedVersion == result.Latest) { Updates.SkippedVersion = ""; Persist(); }
            AnnounceUpdate();
            return true;
        }
        catch (Exception ex)
        {
            if (!automatic) _updateStatus = $"Could not reach GitHub ({ex.Message}).";
            return false;
        }
        finally
        {
            _checkingUpdates = false;
            RaiseUpdates();
        }
    }

    /// <summary>"Not now": this release is not announced again. A later one is.</summary>
    public void SkipUpdate()
    {
        if (Updates.LatestVersion.Length == 0) return;
        Updates.SkippedVersion = Updates.LatestVersion;
        Persist();
        RaiseUpdates();
    }

    /// <summary>Says so once per run of the app, in the footer, when a new release is waiting.</summary>
    private void AnnounceUpdate()
    {
        if (_updateAnnounced || Available is not { } a) return;
        _updateAnnounced = true;
        ShowFooterStatus($"DispCtrl {a.Latest} is available: Settings > Updates.");
    }

    private void RaiseUpdates()
    {
        foreach (string name in new[] { nameof(CheckUpdatesAutomatically), nameof(UpdateDescription), nameof(UpdateAvailableVisibility),
            nameof(UpdateAvailableText), nameof(UpdateUrl), nameof(UpdateCheckReady) })
            Raise(name);
    }
}
