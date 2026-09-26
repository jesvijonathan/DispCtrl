using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DispCtrl.Display;

/// <summary>
/// Owns DispCtrl's per-user startup task and shell shortcuts. Keeping these to
/// the user's own account avoids elevation and makes every setting immediately
/// reversible.
/// </summary>
public static class StartupIntegration
{
    /// <summary>An install for all users, in Program Files: nobody but the installer's own user was asked about starting at sign-in.</summary>
    public static bool InstalledForAllUsers
    {
        get
        {
            try
            {
                string app = AppContext.BaseDirectory;
                return !IsPackaged && new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                    .Select(Environment.GetFolderPath)
                    .Any(root => root.Length > 0 && app.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }
    }

    public static bool IsPackaged
    {
        get { try { _ = Windows.ApplicationModel.Package.Current.Id; return true; } catch (InvalidOperationException) { return false; } }
    }

    public static async Task<bool> ReadEngineStartupAsync()
    {
        if (!IsPackaged) return StartsEngineAtSignIn;
        var task = await Windows.ApplicationModel.StartupTask.GetAsync("DispCtrlEngine");
        return task.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
    }

    public static async Task SetEngineStartupAsync(bool enabled, string? enginePath)
    {
        if (!IsPackaged) { SetEngineStartup(enabled, enginePath); return; }
        var task = await Windows.ApplicationModel.StartupTask.GetAsync("DispCtrlEngine");
        if (!enabled) { task.Disable(); return; }
        var state = await task.RequestEnableAsync();
        if (state is not (Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy))
            throw new InvalidOperationException("Windows has disabled this startup task. Enable DispCtrl in Windows Startup Apps.");
    }
    private const string AppShortcutName = "DispCtrl.lnk";
    private const string EngineStartupShortcutName = "DispCtrl Engine.lnk";
    private static readonly string PreviousAppShortcutName = "Displ" + "Ctrl.lnk";
    private static readonly string PreviousEngineShortcutName = "Displ" + "Ctrl Engine.lnk";
    private static string EngineStartMenuShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", EngineStartupShortcutName);

    public static void MigrateLegacyShortcuts()
    {
        MigrateShortcut(StartMenuShortcutPath, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", PreviousAppShortcutName));
        MigrateShortcut(EngineStartMenuShortcutPath, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", PreviousEngineShortcutName));
        MigrateShortcut(DesktopShortcutPath, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), PreviousAppShortcutName));
        MigrateShortcut(EngineStartupShortcutPath, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), PreviousEngineShortcutName));
    }

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
    public static bool StartsEngineAtSignIn => EngineTaskRegistered || File.Exists(EngineStartupShortcutPath);

    /// <summary>The scheduled task that starts the engine at sign-in.</summary>
    public const string EngineTaskName = "DispCtrl.Engine";

    /// <summary>Tasks earlier builds registered, removed whenever the current one is.</summary>
    private static readonly string[] LegacyTaskNames = ["Umbra.Engine", "Displ" + "Ctrl.Engine"];

    /// <summary>The argument the task passes, so the engine knows it is a sign-in.</summary>
    public const string SignInArgument = "--sign-in";

    public static bool EngineTaskRegistered => Schtasks("/Query", "/TN", EngineTaskName) == 0;

    /// <summary>
    /// Registers the engine as a per-user scheduled task started at sign-in.
    /// </summary>
    /// <remarks>
    /// A task rather than a Startup-folder shortcut, because Windows holds
    /// Startup-folder apps back after sign-in, and the engine is what hides a
    /// taskbar and puts the tray icon up: every second of that shows. A logon
    /// trigger has no such delay.
    /// <para>
    /// Also: priority 4 rather than the scheduler's default 7, which is below
    /// normal and put the engine's first rescan behind whatever else sign-in
    /// was starting; restarted up to three times a minute apart if it fails; no
    /// time limit; and <c>AllowHardTerminate</c> off, because a killed engine
    /// leaves a taskbar parked off-screen. Limited rights, deliberately: an
    /// elevated engine would have its tray window cut off from Explorer by
    /// UIPI, and its command pipe from every unelevated client. Per user, so
    /// registering it needs no elevation.
    /// </para>
    /// </remarks>
    public static void RegisterEngineTask(string enginePath)
    {
        if (!File.Exists(enginePath)) throw new FileNotFoundException("The DispCtrl engine could not be found.", enginePath);
        string user = System.Security.SecurityElement.Escape(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        string command = System.Security.SecurityElement.Escape(enginePath);
        string folder = System.Security.SecurityElement.Escape(Path.GetDirectoryName(enginePath) ?? "");
        string xml = string.Join(Environment.NewLine,
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>",
            "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">",
            "  <RegistrationInfo>",
            "    <Author>DispCtrl</Author>",
            "    <Description>Starts the DispCtrl engine when you sign in: taskbar hiding, night light, hotkeys and the quick panel icon.</Description>",
            $"    <URI>\\{EngineTaskName}</URI>",
            "  </RegistrationInfo>",
            "  <Triggers>",
            $"    <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger>",
            "  </Triggers>",
            "  <Principals>",
            "    <Principal id=\"Author\">",
            $"      <UserId>{user}</UserId>",
            "      <LogonType>InteractiveToken</LogonType>",
            "      <RunLevel>LeastPrivilege</RunLevel>",
            "    </Principal>",
            "  </Principals>",
            "  <Settings>",
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>",
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>",
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>",
            "    <AllowHardTerminate>false</AllowHardTerminate>",
            "    <StartWhenAvailable>true</StartWhenAvailable>",
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>",
            "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>",
            "    <AllowStartOnDemand>true</AllowStartOnDemand>",
            "    <Enabled>true</Enabled>",
            "    <Hidden>false</Hidden>",
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>",
            "    <WakeToRun>false</WakeToRun>",
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>",
            "    <Priority>4</Priority>",
            "    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>",
            "  </Settings>",
            "  <Actions Context=\"Author\">",
            "    <Exec>",
            $"      <Command>{command}</Command>",
            $"      <Arguments>run {SignInArgument}</Arguments>",
            $"      <WorkingDirectory>{folder}</WorkingDirectory>",
            "    </Exec>",
            "  </Actions>",
            "</Task>");

        string file = Path.Combine(Path.GetTempPath(), $"DispCtrl-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            int code = Schtasks("/Create", "/TN", EngineTaskName, "/XML", file, "/F");
            if (code != 0) throw new InvalidOperationException($"Windows refused to register the startup task (schtasks exit {code}).");
        }
        finally { try { File.Delete(file); } catch (IOException) { } }

        // One way to start, not two: the shortcut, or a task an earlier build
        // registered, would start a second engine or point at a stale path.
        DeleteQuietly(EngineStartupShortcutPath);
        RemoveLegacyTasks();
    }

    public static void UnregisterEngineTask()
    {
        if (EngineTaskRegistered) Schtasks("/Delete", "/TN", EngineTaskName, "/F");
        DeleteQuietly(EngineStartupShortcutPath);
        RemoveLegacyTasks();
    }

    /// <summary>Moves an existing Startup-folder registration to the task, once.</summary>
    /// <returns>True when something was migrated.</returns>
    public static bool MigrateToTask(string enginePath)
    {
        if (IsPackaged || !File.Exists(EngineStartupShortcutPath) || EngineTaskRegistered) return false;
        RegisterEngineTask(enginePath);
        return true;
    }

    private static void RemoveLegacyTasks()
    {
        foreach (string name in LegacyTaskNames)
            if (Schtasks("/Query", "/TN", name) == 0) Schtasks("/Delete", "/TN", name, "/F");
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The engine the sign-in task starts, or null when there is no task.</summary>
    /// <remarks>
    /// Which copy owns sign-in matters once there can be several: an installed
    /// one, a portable one and a development build. Repair re-points a task only
    /// when its engine no longer exists, never one that belongs to another copy.
    /// </remarks>
    public static string? EngineTaskTarget()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string a in new[] { "/Query", "/TN", EngineTaskName, "/XML" }) info.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            if (process.ExitCode != 0) return null;
            var match = System.Text.RegularExpressions.Regex.Match(output.Result, "<Command>(?<c>[^<]+)</Command>");
            return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups["c"].Value).Trim().Trim('"') : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return null; }
    }

    private static int Schtasks(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in arguments) info.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { try { process.Kill(); } catch (InvalidOperationException) { } return -1; }
        Task.WaitAll(output, error);
        return process.ExitCode;
    }

    public static void SetStartMenuShortcut(bool enabled) =>
        SetShortcut(StartMenuShortcutPath, enabled, AppPath(), "", "Open DispCtrl");

    public static void SetDesktopShortcut(bool enabled) =>
        SetShortcut(DesktopShortcutPath, enabled, AppPath(), "", "Open DispCtrl");

    public static void SetEngineStartup(bool enabled, string? enginePath)
    {
        if (IsPackaged) throw new InvalidOperationException("Packaged startup must use SetEngineStartupAsync.");
        if (!enabled) { UnregisterEngineTask(); return; }
        if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
            throw new FileNotFoundException("The DispCtrl engine could not be found.", enginePath);
        RegisterEngineTask(enginePath);
    }

    private static string AppPath() =>
        Environment.ProcessPath is { Length: > 0 } path && Path.GetFileName(path).Equals("DispCtrl.App.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, "DispCtrl.App.exe");

    private static void MigrateShortcut(string currentPath, string previousPath)
    {
        try
        {
            if (!File.Exists(previousPath)) return;
            if (File.Exists(currentPath)) File.Delete(previousPath);
            else File.Move(previousPath, currentPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

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
