param([string]$NativeDirectory = "$PSScriptRoot/../../native/DisplCtrl.TaskbarGlass/bin")
$ErrorActionPreference = 'Stop'
$nativeDir = (Resolve-Path -LiteralPath $NativeDirectory).Path
$dll = Join-Path $nativeDir (Get-Content -LiteralPath (Join-Path $nativeDir 'TaskbarGlass.version'))
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GlassProbe {
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Attach(uint pid);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int Update(uint pid,uint owner,uint config);
 public static string Run(string path,uint pid) {
  var lib=NativeLibrary.Load(path);
  var attach=Marshal.GetDelegateForFunctionPointer<Attach>(NativeLibrary.GetExport(lib,"GlassAttach"));
  var update=Marshal.GetDelegateForFunctionPointer<Update>(NativeLibrary.GetExport(lib,"GlassUpdate"));
  int existing=update(pid,(uint)Environment.ProcessId,0);
  int hr=existing>0 ? 0 : attach(pid);
  System.Threading.Thread.Sleep(1000);
  int result;
  try {
   result=update(pid,(uint)Environment.ProcessId,0x1000000|30);
   System.Threading.Thread.Sleep(1000);
  } finally { update(pid,(uint)Environment.ProcessId,0); }
  return $"attach=0x{hr:X8} applied={result} (0x{result:X8})";
 }
}
'@
[GlassProbe]::Run($dll, [uint32](Get-Process explorer | Select-Object -First 1).Id)
Get-Content "$env:LOCALAPPDATA/DisplCtrl/taskbar-glass-native.log" -Tail 12
