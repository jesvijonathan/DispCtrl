namespace DisplCtrl.Core;

/// <summary>Build-time opt-ins for unfinished features. Never enabled by saved settings.</summary>
public static class FeatureFlags
{
    public static bool Presets
    {
        get
        {
#if BETA_PRESETS
            return true;
#else
            return false;
#endif
        }
    }
}
