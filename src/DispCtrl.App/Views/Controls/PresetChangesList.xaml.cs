using DispCtrl.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views.Controls;

/// <summary>The list of what differs from the preset in use, wherever it is opened from.</summary>
public sealed partial class PresetChangesList : UserControl
{
    public PresetsViewModel ViewModel => App.ViewModel.Presets;

    public PresetChangesList() => InitializeComponent();
}
