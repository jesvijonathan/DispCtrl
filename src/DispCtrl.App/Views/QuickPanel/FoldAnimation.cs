using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace DispCtrl.App.Views.QuickPanel;

/// <summary>The quick panel's sections fading and sliding as they open and fold.</summary>
/// <remarks>
/// Implicit show and hide animations on the section's visual: the compositor
/// plays them on its own thread when Visibility changes, so there is no layout
/// pass per frame and nothing for the UI thread to keep up with. Opacity and a
/// few pixels of translation only; the panel's own height still changes at
/// once, as it fits the new content.
/// </remarks>
internal static class FoldAnimation
{
    public static void Attach(UIElement body)
    {
        Compositor compositor = ElementCompositionPreview.GetElementVisual(body).Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(body, true);

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var showFade = compositor.CreateScalarKeyFrameAnimation();
        showFade.Target = "Opacity";
        showFade.InsertKeyFrame(0f, 0f);
        showFade.InsertKeyFrame(1f, 1f, easing);
        showFade.Duration = TimeSpan.FromMilliseconds(200);
        var showSlide = compositor.CreateVector3KeyFrameAnimation();
        showSlide.Target = "Translation";
        showSlide.InsertKeyFrame(0f, new Vector3(0, -8, 0));
        showSlide.InsertKeyFrame(1f, Vector3.Zero, easing);
        showSlide.Duration = TimeSpan.FromMilliseconds(220);
        var show = compositor.CreateAnimationGroup();
        show.Add(showFade);
        show.Add(showSlide);
        ElementCompositionPreview.SetImplicitShowAnimation(body, show);

        var hideFade = compositor.CreateScalarKeyFrameAnimation();
        hideFade.Target = "Opacity";
        hideFade.InsertKeyFrame(1f, 0f);
        hideFade.Duration = TimeSpan.FromMilliseconds(110);
        ElementCompositionPreview.SetImplicitHideAnimation(body, hideFade);
    }

    /// <summary>Animations switched off: the section shows and folds at once again.</summary>
    public static void Detach(UIElement body)
    {
        ElementCompositionPreview.SetImplicitShowAnimation(body, null);
        ElementCompositionPreview.SetImplicitHideAnimation(body, null);
    }
}
