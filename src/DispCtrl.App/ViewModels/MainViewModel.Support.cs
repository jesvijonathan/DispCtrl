using DispCtrl.Core.Settings;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    private SupportPrompt Support => _settings.Global.Support;

    /// <summary>Counts one opening of the window and says whether to ask for support this time.</summary>
    public bool CountWindowOpen()
    {
        Support.CountOpen(DateTimeOffset.UtcNow);
        Persist();
        return Support.Due(DateTimeOffset.UtcNow);
    }

    /// <summary>A star or a donation: never asked again.</summary>
    public void SupportGiven()
    {
        Support.Acted();
        Persist();
    }

    /// <summary>Closed without either.</summary>
    public void SupportDeclined()
    {
        Support.Decline(DateTimeOffset.UtcNow);
        Persist();
    }
}
