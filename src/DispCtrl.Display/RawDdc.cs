using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display;

/// <summary>Optional GPU APIs. No raw writes are broadcast or retried on another output.</summary>
internal static unsafe partial class RawDdc
{
    // Driver initialization and teardown are process-wide on some vendors.
    private static readonly Lock Gate = new();

    internal static LgInput.Result Write(DisplayInfo display, byte[] packet)
    {
        if (!Environment.Is64BitProcess) return LgInput.Result.Failed("LG alternate input switching requires a 64-bit process.");
        lock (Gate)
        {
            // Resolve after both locks: waiting for another DDC operation or
            // driver teardown can outlive a hot-plug or topology change.
            var output = LgInput.ResolveOutput(display, DisplayRegistry.Enumerate());
            if (output.Display is null) return LgInput.Result.Failed(output.Error!);
            display = output.Display;
            // A stale loader from another GPU must not prevent the owning GPU
            // from being found. All exports are resolved before any write;
            // an actual write failure is a result, and stops the chain.
            return Available(Nvidia, display, packet) ?? Available(Amd, display, packet) ?? Available(Intel, display, packet)
                ?? LgInput.Result.Failed("No supported GPU I2C path matched this display. NVIDIA NVAPI, AMD ADL or Intel IGCL is required.");
        }
    }

    private static LgInput.Result? Available(Func<DisplayInfo, byte[], LgInput.Result?> backend, DisplayInfo display, byte[] packet)
    {
        try { return backend(display, packet); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return null; }
    }

    private static nint LoadSystem(string file) => NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, file), out nint library) ? library : 0;
    private static nint Export(nint library, string name) => NativeLibrary.GetExport(library, name);
    private static string Ansi(byte* value, int capacity)
    {
        int length = 0;
        while (length < capacity && value[length] != 0) length++;
        return System.Text.Encoding.ASCII.GetString(new ReadOnlySpan<byte>(value, length));
    }
    private static LgInput.Result Status(string backend, int status) => status == 0
        ? new(true, null) : LgInput.Result.Failed($"{backend} rejected the LG input command (status 0x{status:X8}).");
}
