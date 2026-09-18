using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DisplCtrl.App.ViewModels;

namespace DisplCtrl.App.Views;

/// <summary>
/// The drift sign, and the list behind it.
/// </summary>
/// <remarks>
/// A control rather than two copies of the same forty lines of XAML, because
/// the docked bar and the Presets page both want it and two copies would drift
/// apart the first time either changed.
/// <para>
/// It reads the shared view model directly rather than taking one as a
/// dependency property. There is exactly one preset view model in the
/// application, both hosts already bind to it, and a settable one would only
/// add a way for a host to point this at the wrong thing.
/// </para>
/// </remarks>
public sealed partial class PresetChanges : UserControl
{
    public PresetsViewModel ViewModel => App.ViewModel.Presets;

    public PresetChanges() => InitializeComponent();

    /// <remarks>
    /// The declared flyout opens on click by itself; going through here as well
    /// means the keyboard and the mouse take the same path.
    /// </remarks>
    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.Flyout?.ShowAt(button);
    }
}
