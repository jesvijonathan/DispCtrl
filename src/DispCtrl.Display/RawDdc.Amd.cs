using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DispCtrl.Core.Displays;

namespace DispCtrl.Display;

internal static unsafe partial class RawDdc
{
    // ABI: GPUOpen-LibrariesAndSDKs/display-library, Windows AdapterInfo / ADLDisplayInfo.
    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlAdapter
    {
        public int Size, Index;
        public fixed byte Udid[256];
        public int Bus, Device, Function, Vendor;
        public fixed byte Name[256];
        public fixed byte DisplayName[256];
        public int Present, Exists;
        public fixed byte DriverPath[256];
        public fixed byte DriverPathExt[256];
        public fixed byte Pnp[256];
        public int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdlDisplay
    {
        public int LogicalIndex, PhysicalIndex, LogicalAdapter, PhysicalAdapter, Controller;
        public fixed byte Name[256];
        public fixed byte Manufacturer[256];
        public int Type, OutputType, Connector, InfoMask, InfoValue;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint AdlAllocate(int size) => size > 0 ? (nint)NativeMemory.Alloc((nuint)size) : 0;

    private static LgInput.Result? Amd(DisplayInfo display, byte[] packet)
    {
        nint library = LoadSystem("atiadlxx.dll");
        if (library == 0) return null;
        try
        {
            var create = (delegate* unmanaged[Cdecl]<delegate* unmanaged[Cdecl]<int, nint>, int, int>)Export(library, "ADL_Main_Control_Create");
            var destroy = (delegate* unmanaged[Cdecl]<int>)Export(library, "ADL_Main_Control_Destroy");
            var countAdapters = (delegate* unmanaged[Cdecl]<int*, int>)Export(library, "ADL_Adapter_NumberOfAdapters_Get");
            var getAdapters = (delegate* unmanaged[Cdecl]<AdlAdapter*, int, int>)Export(library, "ADL_Adapter_AdapterInfo_Get");
            var getDisplays = (delegate* unmanaged[Cdecl]<int, int*, AdlDisplay**, int, int>)Export(library, "ADL_Display_DisplayInfo_Get");
            var write = (delegate* unmanaged[Cdecl]<int, int, int, int, int, byte*, int*, byte*, int>)Export(library, "ADL_Display_DDCBlockAccess_Get");
            if (create(&AdlAllocate, 1) != 0) return null;
            try
            {
                int count = 0;
                if (countAdapters(&count) != 0 || count <= 0 || count > 128) return null;
                var adapters = new AdlAdapter[count];
                fixed (AdlAdapter* all = adapters)
                {
                    for (int i = 0; i < count; i++) all[i].Size = sizeof(AdlAdapter);
                    if (getAdapters(all, sizeof(AdlAdapter) * count) != 0) return null;
                    var matches = new List<(int Adapter, int Output)>();
                    bool foundAdapter = false;
                    for (int i = 0; i < count; i++)
                    {
                        AdlAdapter* adapter = all + i;
                        if (adapter->Present == 0 || !Ansi(adapter->DisplayName, 256).Equals(display.GdiName, StringComparison.OrdinalIgnoreCase)) continue;
                        foundAdapter = true;
                        int displaysCount = 0;
                        AdlDisplay* displays = null;
                        try
                        {
                            if (getDisplays(adapter->Index, &displaysCount, &displays, 0) != 0 || displays == null
                                || displaysCount <= 0 || displaysCount > 128) continue;
                            for (int j = 0; j < displaysCount; j++)
                            {
                                AdlDisplay* d = displays + j;
                                if ((d->InfoValue & d->InfoMask & 3) == 3 && d->LogicalAdapter == adapter->Index)
                                    matches.Add((adapter->Index, d->LogicalIndex));
                            }
                        }
                        finally { if (displays != null) NativeMemory.Free(displays); }
                    }
                    if (!foundAdapter) return null;
                    var unique = matches.Distinct().ToArray();
                    if (unique.Length != 1) return LgInput.Result.Failed("AMD ADL could not identify a unique mapped output for this monitor.");
                    byte[] block = [0x6E, .. packet];
                    fixed (byte* data = block)
                    {
                        int received = 0;
                        return Status("AMD ADL", write(unique[0].Adapter, unique[0].Output, 0, 0, block.Length, data, &received, null));
                    }
                }
            }
            finally { destroy(); }
        }
        finally { NativeLibrary.Free(library); }
    }
}
