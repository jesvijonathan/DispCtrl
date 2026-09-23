<# Tests the running engine's overlay opacity without changing saved settings.
   Requires OLED idle protection enabled, one covered display, and Focus Mode off. #>
$ErrorActionPreference = 'Stop'
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'DispCtrl/settings.json') -Raw | ConvertFrom-Json
if (-not $settings.global.oledCare.enabled -or $settings.global.focus.enabled) {
    throw 'Enable OLED idle protection and disable Focus Mode before this check.'
}
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class OledProbe {
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
    public static uint IdleMs() {
        var input = new LastInput { Size = 8 };
        if (!GetLastInputInfo(ref input)) throw new InvalidOperationException("Cannot read idle clock.");
        return unchecked((uint)Environment.TickCount - input.Tick);
    }
    public delegate bool Visitor(IntPtr window, IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visitor visitor, IntPtr arg);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, UIntPtr value, IntPtr extra);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);
    public static int[] VisibleAlphas() {
        var result = new List<int>();
        EnumWindows((window, arg) => {
            var text = new StringBuilder(128);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() == "DispCtrl dim overlay" && IsWindowVisible(window)
                && GetLayeredWindowAttributes(window, out _, out byte alpha, out _)) result.Add(alpha);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
}
'@
$control = [OledProbe]::FindWindow('DispCtrl.ProtectionOverlay', 'DispCtrl protection service')
if ($control -eq [IntPtr]::Zero) { throw 'Protection service is not running.' }
$message = [OledProbe]::RegisterWindowMessage('DispCtrl.OledPreview.v1')
foreach ($percent in @(25, 50, 80, 100, 0)) {
    if (-not [OledProbe]::PostMessage($control, $message, [UIntPtr]$percent, [IntPtr]::Zero)) { throw 'Preview request failed.' }
    Start-Sleep -Milliseconds 450
    $alphas = @([OledProbe]::VisibleAlphas())
    $expected = [int][Math]::Round($percent * 2.55)
    if ($percent -eq 0) {
        if ($alphas.Count -ne 0) { throw "0% preview left visible overlays: $alphas" }
    } elseif ($alphas.Count -eq 0 -or @($alphas | Where-Object { [Math]::Abs($_ - $expected) -gt 1 }).Count -gt 0) {
        throw "Expected alpha $expected for $percent percent; got $alphas"
    }
    Write-Output "PASS Focus Mode off: $percent percent maps to alpha $expected"
}
[OledProbe]::PostMessage($control, $message, [UIntPtr]37, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 4500
$remaining = @([OledProbe]::VisibleAlphas())
$idle = [OledProbe]::IdleMs()
$idleLevel = 0
if ($idle -ge $settings.global.oledCare.idleMinutes * 60000) {
    $idleLevel = $settings.global.oledCare.dimPercent
    if ($settings.global.oledCare.secondStageEnabled -and $idleLevel -gt 0 -and $idleLevel -lt 100 -and
        $idle -ge ($settings.global.oledCare.idleMinutes + $settings.global.oledCare.secondStageMinutes) * 60000) {
        $idleLevel = [Math]::Clamp($settings.global.oledCare.secondStageDimPercent, $idleLevel, 100)
    }
}
$idleAlpha = [int][Math]::Round($idleLevel * 2.55)
if (@($remaining | Where-Object { [Math]::Abs($_ - $idleAlpha) -gt 1 }).Count -gt 0) {
    throw "Preview failed to return to idle state: expected $idleAlpha or hidden, got $remaining"
}
Write-Output "PASS Preview expires: idle age $idle ms, resulting visible alpha(s) $remaining. Settings unchanged."
