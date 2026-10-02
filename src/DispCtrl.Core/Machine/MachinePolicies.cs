using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DispCtrl.Core.Machine;

/// <summary>What a switch is set with.</summary>
public enum PolicyKind
{
    /// <summary>On or off.</summary>
    Switch,
    /// <summary>A number of minutes; 0 is off.</summary>
    Minutes,
    /// <summary>A picture file.</summary>
    Picture,
}

/// <summary>One registry value a switch writes: where, and what each state writes there.</summary>
/// <param name="On">Written for on (or the number, or the path, for the other kinds: null means "the value given").</param>
/// <param name="Off">Written for off; null deletes the value, which is Windows' default.</param>
public sealed record PolicyValue(bool Machine, string Key, string Name, RegistryValueKind Type, object? On, object? Off);

/// <summary>A sign-in or lock screen setting a company laptop's owner sets with regedit, as a switch.</summary>
public sealed record MachinePolicy(string Id, string Label, string Hint, PolicyKind Kind, PolicyValue[] Values)
{
    /// <summary>Whether changing it writes to HKLM, and so asks for administrator rights.</summary>
    public bool NeedsAdmin => Values.Any(v => v.Machine);
}

/// <summary>The switch's state now, read from the registry.</summary>
/// <param name="Value">On/off as "on"/"off", minutes as a number, a picture as its path; null when unset.</param>
public sealed record PolicyState(MachinePolicy Policy, string? Value, bool Set);

/// <summary>
/// The sign-in and lock screen settings people on company laptops ask for, as
/// switches that read back what Windows has.
/// </summary>
/// <remarks>
/// Each is a documented registry value, the same one regedit or Group Policy
/// writes. Machine-wide ones need administrator rights, asked for when the
/// switch is changed - never at start - through Windows' own <c>reg.exe</c>, as
/// the gamma range does, so DispCtrl itself never runs elevated and a packaged
/// install works the same. Per-user ones are written directly.
/// <para>
/// A machine joined to a domain or Entra ID, or enrolled in device management,
/// may have these set by its organisation, which applies them again over a
/// local change. <see cref="Managed"/> says so rather than pretending a switch
/// stuck; whether the owner may change them is between them and their IT.
/// </para>
/// </remarks>
public static partial class MachinePolicies
{
    private const string PoliciesSystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string Csp = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
    private const string Delivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public static IReadOnlyList<MachinePolicy> All { get; } =
    [
        new("no-ctrl-alt-del", "Sign in without Ctrl+Alt+Del",
            "Go straight to the password or PIN instead of pressing Ctrl+Alt+Del first. Company images often require it.",
            // Windows reads the policy location first and Winlogon's after it;
            // regedit guides use either, so both are read and both written.
            PolicyKind.Switch,
            [
                new(true, PoliciesSystem, "DisableCAD", RegistryValueKind.DWord, 1, 0),
                new(true, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "DisableCAD", RegistryValueKind.DWord, 1, 0),
            ]),
        new("lock-screen-picture", "Lock screen picture",
            "A picture of your own on the lock screen, where the company image or Spotlight was. Works on Pro as well as Enterprise.",
            PolicyKind.Picture,
            [
                new(true, Csp, "LockScreenImagePath", RegistryValueKind.String, null, null),
                new(true, Csp, "LockScreenImageUrl", RegistryValueKind.String, null, null),
                new(true, Csp, "LockScreenImageStatus", RegistryValueKind.DWord, 1, null),
            ]),
        new("no-lock-screen", "Skip the lock screen",
            "Waking or locking shows the sign-in box directly, without the picture and clock to dismiss first.",
            PolicyKind.Switch, [new(true, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", RegistryValueKind.DWord, 1, null)]),
        new("sharp-sign-in", "No blur behind the sign-in box",
            "Show the lock screen picture as it is behind the sign-in box, instead of blurred.",
            PolicyKind.Switch, [new(true, @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", RegistryValueKind.DWord, 1, null)]),
        new("lock-after", "Lock after inactivity",
            "Lock the computer after this many minutes without input, whatever the screen saver says. 0 leaves it to Windows.",
            PolicyKind.Minutes, [new(true, PoliciesSystem, "InactivityTimeoutSecs", RegistryValueKind.DWord, null, null)]),
        new("dynamic-lock", "Lock when your phone leaves",
            "Windows' dynamic lock: lock shortly after a paired Bluetooth phone goes out of range.",
            PolicyKind.Switch, [new(false, @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "EnableGoodbye", RegistryValueKind.DWord, 1, 0)]),
        new("quiet-lock-screen", "No tips on the lock screen",
            "Turn off the fun facts, tips and suggestions Windows lays over the lock screen picture.",
            PolicyKind.Switch,
            [
                new(false, Delivery, "RotatingLockScreenOverlayEnabled", RegistryValueKind.DWord, 0, 1),
                new(false, Delivery, "SubscribedContent-338387Enabled", RegistryValueKind.DWord, 0, 1),
            ]),
    ];

    public static MachinePolicy Find(string id) =>
        All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"No such switch: {id}. Switches: {string.Join(", ", All.Select(p => p.Id))}.");

    /// <summary>Reads one value; null when it is not there or cannot be read.</summary>
    public static object? Read(PolicyValue v)
    {
        try
        {
            using RegistryKey? key = (v.Machine ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(v.Key);
            return key?.GetValue(v.Name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    public static PolicyState State(MachinePolicy p)
    {
        // A switch is what the first of its values that is set says.
        PolicyValue first = p.Kind == PolicyKind.Switch ? p.Values.FirstOrDefault(v => Read(v) is not null) ?? p.Values[0] : p.Values[0];
        object? raw = Read(first);
        return p.Kind switch
        {
            PolicyKind.Switch => new(p, raw is null ? null : Equals(Normal(raw), Normal(first.On)) ? "on" : "off",
                raw is not null && Equals(Normal(raw), Normal(first.On))),
            PolicyKind.Minutes => new(p, raw is int s ? (s / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) : null, raw is int n && n > 0),
            _ => new(p, raw as string, raw is string path && path.Length > 0
                && Normal(Read(p.Values[2])) is int status && status == 1),
        };
    }

    private static object? Normal(object? value) => value switch { int i => i, uint u => (int)u, long l => (int)l, _ => value };

    /// <summary>
    /// What to write for a requested value, as (value, data) pairs; null data deletes.
    /// </summary>
    /// <param name="wanted">"on"/"off" for a switch, minutes for <see cref="PolicyKind.Minutes"/>, a file path or "off" for a picture.</param>
    public static List<(PolicyValue Value, object? Data)> Plan(MachinePolicy p, string wanted)
    {
        string w = wanted.Trim();
        bool off = w.Equals("off", StringComparison.OrdinalIgnoreCase) || w.Equals("false", StringComparison.OrdinalIgnoreCase);
        switch (p.Kind)
        {
            case PolicyKind.Switch:
                if (!off && !w.Equals("on", StringComparison.OrdinalIgnoreCase) && !w.Equals("true", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"{p.Label} is on or off.");
                return [.. p.Values.Select(v => (v, off ? v.Off : v.On))];
            case PolicyKind.Minutes:
                if (off) return [(p.Values[0], null)];
                if (!int.TryParse(w, out int minutes) || minutes is < 0 or > 9999)
                    throw new ArgumentException($"{p.Label} is a number of minutes, 0 to 9999.");
                return [(p.Values[0], minutes == 0 ? null : minutes * 60)];
            default:
                if (off) return [.. p.Values.Select(v => (v, (object?)null))];
                string path = Path.GetFullPath(w);
                if (!File.Exists(path)) throw new ArgumentException($"There is no picture at {path}.");
                if (Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
                    throw new ArgumentException("The lock screen takes a .jpg, .png or .bmp picture.");
                return [(p.Values[0], path), (p.Values[1], path), (p.Values[2], 1)];
        }
    }

    /// <summary>The value written before, as data <see cref="Apply"/> can write back.</summary>
    public static object? Before(PolicyValue v) => Normal(Read(v));

    /// <summary>
    /// Writes a plan: per-user values directly, machine values through one
    /// elevated <c>reg.exe</c> chain. False when the prompt was dismissed or a
    /// write failed.
    /// </summary>
    /// <remarks>
    /// Commands on the command line, not a .reg file: a file written here and
    /// imported elevated could be swapped in between by anything running as the
    /// same user, and Windows' prompt shows the command line for anyone who
    /// opens its details.
    /// </remarks>
    public static bool Apply(IReadOnlyList<(PolicyValue Value, object? Data)> plan, out string? error)
    {
        error = null;
        foreach (var (v, data) in plan.Where(p => !p.Value.Machine))
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(v.Key, writable: true);
                if (data is null) key.DeleteValue(v.Name, throwOnMissingValue: false);
                else key.SetValue(v.Name, data, v.Type);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                error = $"{v.Name} could not be written: {ex.Message}";
                return false;
            }
        }
        var machine = plan.Where(p => p.Value.Machine).ToList();
        if (machine.Count == 0) return true;

        string commands = string.Join(" && ", machine.Select(p => p.Data is null
            ? $"(reg query \"HKLM\\{p.Value.Key}\" /v {p.Value.Name} >nul 2>&1 && reg delete \"HKLM\\{p.Value.Key}\" /v {p.Value.Name} /f >nul || ver >nul)"
            : $"reg add \"HKLM\\{p.Value.Key}\" /v {p.Value.Name} /t {(p.Value.Type == RegistryValueKind.DWord ? "REG_DWORD" : "REG_SZ")} /d \"{Quote(p.Data)}\" /f >nul"));
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c " + commands,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null) { error = "Windows did not start the change."; return false; }
            process.WaitForExit(20_000);
            if (!process.HasExited || process.ExitCode != 0) { error = "Windows did not accept the change."; return false; }
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { error = "Administrator permission was not given."; return false; }
    }

    private static string Quote(object data)
    {
        string text = Convert.ToString(data, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        // cmd has no escape inside quotes, so characters it would read as
        // syntax are refused rather than passed on.
        if (text.IndexOfAny(['"', '%', '^', '&', '|', '<', '>', '\r', '\n']) >= 0)
            throw new ArgumentException("That path has characters the command line cannot carry safely. Rename the file or move it.");
        return text;
    }

    /// <summary>Whether an organisation may manage this computer's policies, and how.</summary>
    /// <returns>Null when nothing says it is managed.</returns>
    public static string? Managed()
    {
        var how = new List<string>();
        if (DomainJoined()) how.Add("joined to a domain");
        if (HasSubKeys(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo")) how.Add("joined to Entra ID");
        if (Enrolled()) how.Add("enrolled in device management");
        return how.Count == 0 ? null
            : $"This computer is {string.Join(" and ", how)}. Its organisation may set these switches itself and put them back after a change.";
    }

    private static bool HasSubKeys(string path)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
            return key?.SubKeyCount > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    private static bool Enrolled()
    {
        try
        {
            using RegistryKey? enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (enrollments is null) return false;
            foreach (string name in enrollments.GetSubKeyNames())
            {
                using RegistryKey? one = enrollments.OpenSubKey(name);
                if (one?.GetValue("ProviderID") is string provider && provider.Length > 0) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    private static bool DomainJoined()
    {
        if (NetGetJoinInformation(null, out nint name, out int status) != 0) return false;
        NetApiBufferFree(name);
        return status == 3; // NetSetupDomainName
    }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetGetJoinInformation(string? server, out nint name, out int status);

    [LibraryImport("netapi32.dll")]
    private static partial int NetApiBufferFree(nint buffer);
}
