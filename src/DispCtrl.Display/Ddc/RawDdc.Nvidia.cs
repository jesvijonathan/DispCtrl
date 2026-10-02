using System.Runtime.InteropServices;
using System.Text;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display.Ddc;

internal static unsafe partial class RawDdc
{
    // ABI: NVIDIA/nvapi nvapi.h, NV_I2C_INFO_V3 (pack 8).
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvI2c
    {
        public uint Version, DisplayMask;
        public byte IsDdcPort, DeviceAddress;
        public byte* RegisterAddress;
        public uint RegisterSize;
        public byte* Data;
        public uint Size, DeprecatedSpeed, SpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    private static LgInput.Result? Nvidia(DisplayInfo display, byte[] packet)
    {
        nint library = LoadSystem("nvapi64.dll");
        if (library == 0) return null;
        try
        {
            var query = (delegate* unmanaged[Cdecl]<uint, nint>)Export(library, "nvapi_QueryInterface");
            var init = (delegate* unmanaged[Cdecl]<int>)query(0x0150E828);
            var close = (delegate* unmanaged[Cdecl]<int>)query(0xD22BDD7E);
            var byName = (delegate* unmanaged[Cdecl]<byte*, uint*, int>)query(0xAE457190);
            var gpuForId = (delegate* unmanaged[Cdecl]<uint, nint*, uint*, int>)query(0x112BA1A5);
            var write = (delegate* unmanaged[Cdecl]<nint, NvI2c*, int>)query(0xE812EB07);
            if (init == null || close == null || byName == null || gpuForId == null || write == null || init() != 0) return null;
            try
            {
                uint id = 0, output = 0;
                nint gpu = 0;
                fixed (byte* name = Encoding.ASCII.GetBytes(display.GdiName + '\0'))
                    if (byName(name, &id) != 0) return null;
                if (gpuForId(id, &gpu, &output) != 0 || gpu == 0 || output == 0 || (output & (output - 1)) != 0)
                    return LgInput.Result.Failed("NVAPI could not identify a unique physical output for this monitor.");
                fixed (byte* data = packet)
                {
                    var info = new NvI2c
                    {
                        Version = (uint)sizeof(NvI2c) | (3u << 16), DisplayMask = output,
                        IsDdcPort = 1, DeviceAddress = 0x6E,
                        // A complete raw DDC packet; NVAPI must not add a register byte.
                        Data = data, Size = (uint)packet.Length, DeprecatedSpeed = 0xFFFF, SpeedKhz = 0,
                    };
                    return Status("NVIDIA NVAPI", write(gpu, &info));
                }
            }
            finally { close(); }
        }
        finally { NativeLibrary.Free(library); }
    }
}
