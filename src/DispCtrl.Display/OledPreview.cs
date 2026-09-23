using System.Runtime.InteropServices;

namespace DispCtrl.Display;

/// <summary>A transient preview request; never saved in settings or exported presets.</summary>
public static partial class OledPreview
{
    public static uint MessageId { get; } = RegisterWindowMessage("DispCtrl.OledPreview.v1");

    public static bool TryShow(int percent)
    {
        nint window = FindWindow("DispCtrl.ProtectionOverlay", "DispCtrl protection service");
        return window != 0 && MessageId != 0
            && PostMessage(window, MessageId, (nuint)Math.Clamp(percent, 0, 100), 0) != 0;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string name);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string title);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessage(nint window, uint message, nuint wparam, nint lparam);
}
