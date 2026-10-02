using Microsoft.UI.Xaml;

namespace DispCtrl.App.ViewModels;

public sealed partial class MainViewModel
{
    // One fold per feature with more than a handful of settings; see Fold.
    private Fold? _nightLightFold, _focusFold, _oledFold, _awakeFold, _pinFold, _placementFold;

    public Fold NightLightFold => _nightLightFold ??= new(() => Raise(nameof(NightLightThemeVisibility)));
    public Fold FocusFold => _focusFold ??= new();
    public Fold OledFold => _oledFold ??= new(() => Raise(nameof(OledSecondStageVisibility)));
    public Fold AwakeFold => _awakeFold ??= new();
    public Fold PinFold => _pinFold ??= new();
    public Fold PlacementFold => _placementFold ??= new();

    /// <summary>"Dark mode on the schedule": shown with night light's advanced options, and only while night light is on.</summary>
    public Visibility NightLightThemeVisibility => NightLightFold.Open ? NightLightVisibility : Visibility.Collapsed;
}
