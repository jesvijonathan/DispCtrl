using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DisplCtrl.App.Services;

/// <summary>
/// Owns DisplCtrl's per-user shell shortcuts. Keeping these in the user's
/// profile avoids elevation and makes every setting immediately reversible.
/// </summary>
public static class StartupIntegration
{
    private const string AppShortcutName = "DisplCtrl.lnk";
    private const string EngineStartupShortcutName = "DisplCtrl Engine.lnk";

    public static string StartMenuShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        "Programs",
        AppShortcutName);

    public static string DesktopShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        AppShortcutName);

    public static string EngineStartupShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        EngineStartupShortcutName);

    public static string StartupFolderPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static bool HasStartMenuShortcut => File.Exists(StartMenuShortcutPath);
    public static bool HasDesktopShortcut => File.Exists(DesktopShortcutPath);
    public static bool StartsEngineAtSignIn => File.Exists(EngineStartupShortcutPath);

    public static void SetStartMenuShortcut(bool enabled) =>
        SetShortcut(StartMenuShortcutPath, enabled, AppPath(), "", "Open DisplCtrl");

    public static void SetDesktopShortcut(bool enabled) =>
        SetShortcut(DesktopShortcutPath, enabled, AppPath(), "", "Open DisplCtrl");

    public static void SetEngineStartup(bool enabled, string? enginePath)
    {
        if (enabled && (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath)))
            throw new FileNotFoundException("The DisplCtrl engine could not be found.", enginePath);

        SetShortcut(
            EngineStartupShortcutPath,
            enabled,
            enginePath ?? "",
            "run",
            "Start the DisplCtrl background engine at sign-in");
    }

    private static string AppPath() =>
        Environment.ProcessPath is { Length: > 0 } path && File.Exists(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, "DisplCtrl.App.exe");

    private static void SetShortcut(
        string shortcutPath,
        bool enabled,
        string targetPath,
        string arguments,
        string description)
    {
        if (!enabled)
        {
            File.Delete(shortcutPath);
            return;
        }

        if (!File.Exists(targetPath))
            throw new FileNotFoundException("The shortcut target could not be found.", targetPath);

        string? directory = Path.GetDirectoryName(shortcutPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The shortcut location is unavailable.");

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.lnk");

        object shellLink = new ShellLink();
        try
        {
            var link = (IShellLinkW)shellLink;
            link.SetPath(targetPath);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
            link.SetIconLocation(targetPath, 0);
            link.SetShowCmd(1);

            ((IPersistFile)shellLink).Save(temporaryPath, true);
            File.Move(temporaryPath, shortcutPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (Marshal.IsComObject(shellLink)) Marshal.FinalReleaseComObject(shellLink);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int count, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
