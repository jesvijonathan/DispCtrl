using System.Runtime.InteropServices;
using System.Security.Principal;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Ddc;

internal static unsafe partial class RawDdc
{
    // ABI: Intel drivers.gpu.control-library/include/igcl_api.h, pack 8.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlInit
    {
        public uint Size;
        public byte Version;
        public uint AppVersion, Flags, SupportedVersion;
        public Guid ApplicationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlAdapter
    {
        public uint Size;
        public byte Version;
        public void* DeviceId;
        public uint DeviceIdSize, DeviceType, SupportedFunctions;
        public ulong DriverVersion, FirmwareMajor, FirmwareMinor, FirmwareBuild;
        public uint VendorId, DeviceCode, Revision, Eus, SubSlices, Slices;
        public fixed byte Name[100];
        public uint Properties, Frequency;
        public ushort SubsystemId, SubsystemVendor;
        public byte Bus, Device, Function;
        public uint XeCores;
        public fixed byte Reserved[108];
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct CtlOsEncoder
    {
        [FieldOffset(0)] public uint WindowsId;
        [FieldOffset(0)] public nint Pointer;
        [FieldOffset(8)] public uint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlEncoder
    {
        public uint Size;
        public byte Version;
        public CtlOsEncoder OsId;
        public uint Type;
        public byte HasConverter, SpecMajor, SpecMinor;
        public uint BpcFlags, ConfigFlags, Features, AdvancedFeatures;
        public fixed uint Reserved[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtlI2c
    {
        public uint Size;
        public byte Version;
        public uint DataSize, Address, Operation, Offset, Flags;
        public ulong Rad;
        public fixed byte Data[128];
    }

    private static LgInput.Result? Intel(DisplayInfo display, byte[] packet)
    {
        // Use only the installed runtime or an explicitly deployed official
        // loader beside the app; never search the current working directory.
        nint library = LoadSystem("ControlLib.dll");
        if (library == 0)
            NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "ControlLib.dll"), out library);
        if (library == 0) return null;
        try
        {
            var init = (delegate* unmanaged[Cdecl]<CtlInit*, nint*, int>)Export(library, "ctlInit");
            var close = (delegate* unmanaged[Cdecl]<nint, int>)Export(library, "ctlClose");
            var devices = (delegate* unmanaged[Cdecl]<nint, uint*, nint*, int>)Export(library, "ctlEnumerateDevices");
            var outputs = (delegate* unmanaged[Cdecl]<nint, uint*, nint*, int>)Export(library, "ctlEnumerateDisplayOutputs");
            var adapterProperties = (delegate* unmanaged[Cdecl]<nint, CtlAdapter*, int>)Export(library, "ctlGetDeviceProperties");
            var encoderProperties = (delegate* unmanaged[Cdecl]<nint, CtlEncoder*, int>)Export(library, "ctlGetAdaperDisplayEncoderProperties");
            var write = (delegate* unmanaged[Cdecl]<nint, CtlI2c*, int>)Export(library, "ctlI2CAccess");
            var args = new CtlInit { Size = (uint)sizeof(CtlInit), AppVersion = 1u << 16 };
            nint api = 0;
            if (init(&args, &api) != 0 || api == 0) return null;
            try
            {
                var matches = new List<nint>();
                foreach (nint adapter in CtlHandles(devices, api))
                {
                    ulong luid = 0;
                    var info = new CtlAdapter { Size = (uint)sizeof(CtlAdapter), DeviceId = &luid, DeviceIdSize = 8 };
                    if (adapterProperties(adapter, &info) != 0 || info.VendorId != 0x8086
                        || !display.AdapterId.Equals($"{luid >> 32:X8}:{(uint)luid:X8}", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (nint output in CtlHandles(outputs, adapter))
                    {
                        var encoder = new CtlEncoder { Size = (uint)sizeof(CtlEncoder) };
                        if (encoderProperties(output, &encoder) == 0 && encoder.OsId.WindowsId == display.TargetId
                            && encoder.Type is 1 or 2 && (encoder.ConfigFlags & (1u | 2u | 32u | 64u | 128u | 256u | 1024u)) == 0)
                            matches.Add(output);
                    }
                }
                if (matches.Count == 0) return null;
                if (matches.Count != 1) return LgInput.Result.Failed("Intel IGCL could not identify a unique output for this monitor.");
                using var identity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    return LgInput.Result.Failed("Intel IGCL requires administrator rights for LG input switching. Run the command from an elevated terminal with --local.");
                var data = new CtlI2c
                {
                    Size = (uint)sizeof(CtlI2c), DataSize = (uint)packet.Length - 1,
                    Address = 0x6E, Offset = packet[0], Operation = 2,
                };
                for (int i = 1; i < packet.Length; i++) data.Data[i - 1] = packet[i];
                return Status("Intel IGCL", write(matches[0], &data));
            }
            finally { close(api); }
        }
        finally { NativeLibrary.Free(library); }
    }

    private static nint[] CtlHandles(delegate* unmanaged[Cdecl]<nint, uint*, nint*, int> enumerate, nint parent)
    {
        uint count = 0;
        if (enumerate(parent, &count, null) != 0 || count == 0 || count > 128) return [];
        var result = new nint[count];
        fixed (nint* handles = result)
        {
            if (enumerate(parent, &count, handles) != 0 || count > result.Length) return [];
        }
        if (count != result.Length) Array.Resize(ref result, (int)count);
        return result;
    }
}
