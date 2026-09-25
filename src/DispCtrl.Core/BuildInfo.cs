using System.Reflection;

namespace DispCtrl.Core;

/// <summary>Which build this is: its version and release channel.</summary>
public static class BuildInfo
{
    /// <summary>
    /// Routine diagnostics - a log line per command, per reload, per brightness
    /// key, the start-up timings - are for beta, test and builds from source.
    /// </summary>
    /// <remarks>
    /// A property over a constant, not the constant: the JIT folds it away all
    /// the same, and a bare <c>const false</c> in a condition is CS0162 in a
    /// stable build, which treats warnings as errors. Errors are logged in every
    /// build; a problem report needs them.
    /// </remarks>
    public static bool Diagnostics => DiagnosticsBuild;

#if DISPCTRL_STABLE
    private const bool DiagnosticsBuild = false;
#else
    private const bool DiagnosticsBuild = true;
#endif

    /// <summary>stable, beta, test, or dev for a build from source.</summary>
    public static string Channel { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DispCtrlChannel")?.Value ?? "dev";

    /// <summary>The version without its commit suffix: 0.1.3.</summary>
    public static string Version { get; } =
        (typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
            .Split('+')[0];

    /// <summary>What the window title says: DispCtrl in stable, with the version and channel otherwise.</summary>
    /// <remarks>A beta or test build names itself, so a screenshot or a report says which one it was.</remarks>
    public static string AppTitle => Channel == "stable" ? "DispCtrl" : "DispCtrl " + Label;

    /// <summary>v0.1.3 for stable, v0.1.3 beta for the others.</summary>
    public static string Label => Channel == "stable" ? "v" + Version : $"v{Version} {Channel}";
}
