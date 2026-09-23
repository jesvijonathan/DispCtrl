$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellInspect {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p,IntPtr after,string cls,string name);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr w,out Rect r);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr w,out Rect r);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr w);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern long GetWindowLongPtr(IntPtr w,int i);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr w,int a,out uint v,int size);
}
'@
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$previousDpi = [ShellInspect]::SetThreadDpiAwarenessContext([IntPtr](-4))
$handles = [System.Collections.Generic.List[IntPtr]]::new()
foreach ($cls in @('Shell_TrayWnd','Shell_SecondaryTrayWnd')) {
 $h = [IntPtr]0
 while (($h = [ShellInspect]::FindWindowEx(0,$h,$cls,$null)) -ne 0) { $handles.Add($h) }
}
$app = Get-Process DispCtrl.App -ErrorAction SilentlyContinue | Select-Object -First 1
if ($app) {
 $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$app.Id)
 foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)) {
  $handles.Add([IntPtr]$window.Current.NativeWindowHandle)
 }
}
foreach ($handle in $handles) {
 $r=[ShellInspect+Rect]::new(); $c=$r; $colour=[uint32]0
 [void][ShellInspect]::GetWindowRect($handle,[ref]$r)
 [void][ShellInspect]::GetClientRect($handle,[ref]$c)
 $hr=[ShellInspect]::DwmGetWindowAttribute($handle,34,[ref]$colour,4)
 [pscustomobject]@{ Handle=$handle; X=$r.L; Y=$r.T; Width=$r.R-$r.L; Height=$r.B-$r.T; ClientWidth=$c.R; ClientHeight=$c.B; Dpi=[ShellInspect]::GetDpiForWindow($handle); Style=('{0:X}' -f [ShellInspect]::GetWindowLongPtr($handle,-16)); Border=('{0:X8}' -f $colour); BorderResult=$hr }
}
[void][ShellInspect]::SetThreadDpiAwarenessContext($previousDpi)
