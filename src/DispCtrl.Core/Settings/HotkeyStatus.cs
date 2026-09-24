using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispCtrl.Core.Settings;

/// <summary>
/// Which shortcuts the engine managed to register, for the Hotkeys page and
/// the command line to show.
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> refuses a combination another program already holds,
/// and the refusal used to reach only the engine's log: the shortcut looked set
/// on the page and silently did nothing, the hardest kind of setting to
/// diagnose. The engine writes this file every time it registers; it is keyed
/// on the combination as <see cref="Hotkey.Describe"/> writes it, which is what
/// a person sees on the page.
/// </remarks>
public sealed class HotkeyStatus
{
    public DateTimeOffset Written { get; set; }

    /// <summary>Combinations registered and working.</summary>
    public List<string> Registered { get; set; } = [];

    /// <summary>Combinations Windows refused, almost always because another program holds them.</summary>
    public List<string> Refused { get; set; } = [];

    public static string PathOnDisk => Path.Combine(SettingsStore.Directory, "hotkeys-status.json");

    public static void Write(IEnumerable<string> registered, IEnumerable<string> refused)
    {
        try
        {
            var status = new HotkeyStatus { Written = DateTimeOffset.UtcNow, Registered = [.. registered], Refused = [.. refused] };
            Directory.CreateDirectory(SettingsStore.Directory);
            string temp = PathOnDisk + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(status, HotkeyStatusJson.Default.HotkeyStatus));
            File.Move(temp, PathOnDisk, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The last status the engine wrote, or null when it has written none.</summary>
    public static HotkeyStatus? Read()
    {
        try
        {
            return File.Exists(PathOnDisk)
                ? JsonSerializer.Deserialize(File.ReadAllText(PathOnDisk), HotkeyStatusJson.Default.HotkeyStatus)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotkeyStatus))]
internal sealed partial class HotkeyStatusJson : JsonSerializerContext;
