using System.Runtime.InteropServices;
using System.Text.Json;

// Live regression check: start the engine with Focus mode, secondary taskbar
// hiding and work-area reclaim enabled, then run this tool. It creates a test
// app on that monitor, verifies hide/reveal over a maximized window, and restores
// the cursor/focus on exit. All geometry uses physical pixels (PerMonitorV2).
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        nint bar = Native.FindWindowEx(0, 0, "Shell_SecondaryTrayWnd", null);
        if (bar == 0) { Console.Error.WriteLine("A secondary taskbar is required."); return 1; }
        Native.GetWindowRect(bar, out var barRect);
        Screen? screen = Screen.AllScreens.FirstOrDefault(s => !s.Primary &&
            s.Bounds.Left == barRect.Left && s.Bounds.Right == barRect.Right);
        if (screen is null) { Console.Error.WriteLine("Cannot resolve the secondary monitor."); return 1; }

        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DispCtrl", "settings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var global = settings.RootElement.GetProperty("global");
        int settle = global.GetProperty("hideDelayMs").GetInt32() + global.GetProperty("animMs").GetInt32() + 1200;
        string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "DispCtrlTaskbarCheck");
        Directory.CreateDirectory(output);

        nint originalFocus = Native.GetForegroundWindow();
        Point originalCursor = Cursor.Position;
        int result = 1;
        using var form = new Form
        {
            Text = "DispCtrl taskbar reveal test", StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(screen.Bounds.Left + 120, screen.Bounds.Top + 120, 800, 500),
            BackColor = Color.FromArgb(25, 75, 115)
        };
        using var label = new Label
        {
            Text = "Checking taskbar reveal over a maximized app…", AutoSize = true,
            ForeColor = Color.White, Font = new Font("Segoe UI", 20), Location = new Point(50, 80)
        };
        form.Controls.Add(label);
        form.Paint += (_, e) =>
        {
            for (int x = 0; x < form.ClientSize.Width; x += 80)
                e.Graphics.FillRectangle(x % 160 == 0 ? Brushes.SteelBlue : Brushes.DarkSlateBlue,
                    x, form.ClientSize.Height - 160, 80, 160);
        };
        form.Shown += async (_, _) =>
        {
            try
            {
                int overlays = 0;
                nint mask = 0;
                while ((mask = Native.FindWindowEx(0, mask, "DispCtrl.ProtectionOverlay", "DispCtrl dim overlay")) != 0)
                {
                    Assert(Native.GetProp(mask, "NonRudeHWND") == 1, "Dim overlay must opt out of shell fullscreen detection");
                    overlays++;
                }
                Assert(overlays >= 2, "Run the engine with Focus mode enabled");
                Cursor.Position = new Point(screen.Bounds.Left + 300, screen.Bounds.Top + 300);
                await Task.Delay(settle);
                Assert(WorkArea(screen) == screen.Bounds, "Reclaim must expose the entire monitor");
                form.WindowState = FormWindowState.Maximized;
                await Task.Delay(800);
                Native.DwmGetWindowAttribute(form.Handle, 9, out var maximizedFrame, Marshal.SizeOf<Native.Rect>());
                Assert(maximizedFrame.ToRectangle() == screen.Bounds, "Maximized app must fill the entire reclaimed monitor");

                for (int cycle = 1; cycle <= 3; cycle++)
                {
                    Cursor.Position = new Point(screen.Bounds.Left + 300, screen.Bounds.Top + 300);
                    await Task.Delay(settle);
                    Native.GetWindowRect(bar, out var hidden);
                    Assert(hidden.Top >= screen.Bounds.Bottom, "Hidden taskbar must be outside the monitor");
                    Assert(WorkArea(screen) == screen.Bounds, "Hidden work area must remain fully reclaimed");
                    Assert(EdgeRoot(screen) == form.Handle, "App must occupy and receive input in the reclaimed taskbar strip");
                    if (cycle == 1) Capture(screen, Path.Combine(output, "hidden.png"));

                    Cursor.Position = new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Bottom - 1);
                    await Task.Delay(global.GetProperty("animMs").GetInt32() + 1200);
                    Native.GetWindowRect(bar, out var shown);
                    Assert(shown.Bottom == screen.Bounds.Bottom && shown.Top < shown.Bottom, "Taskbar must reveal at the monitor edge");
                    Assert(WorkArea(screen) == screen.Bounds, "Reveal must not shrink the reclaimed work area");
                    Native.DwmGetWindowAttribute(form.Handle, 9, out var currentFrame, Marshal.SizeOf<Native.Rect>());
                    Assert(currentFrame.ToRectangle() == maximizedFrame.ToRectangle(), "Reveal must not resize the maximized app");
                    int barZ = ZIndex(bar), appZ = ZIndex(form.Handle);
                    Assert(barZ >= 0 && appZ > barZ, "Revealed taskbar must be above the maximized app");
                    Assert(EdgeRoot(screen) == bar, "Revealed taskbar must receive input above the app");
                    if (cycle == 1) Capture(screen, Path.Combine(output, "revealed.png"));
                    Console.WriteLine($"PASS cycle {cycle}: work={WorkArea(screen)}, app={currentFrame.ToRectangle()}, taskbar z={barZ}, app z={appZ}");
                }
                Console.WriteLine($"PASS: {overlays} fullscreen-excluded overlays; 3 hide/reveal cycles; captures: {output}");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine($"FAIL: {ex.Message}"); }
            finally { form.Close(); }
        };
        try { Application.Run(form); }
        finally { Cursor.Position = originalCursor; Native.SetForegroundWindow(originalFocus); }
        return result;
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Rectangle WorkArea(Screen screen)
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.GetMonitorInfo(Native.MonitorFromPoint(new Point(screen.Bounds.Left + 100, screen.Bounds.Top + 100), 2), ref info);
        return info.Work.ToRectangle();
    }
    private static nint EdgeRoot(Screen screen) => Native.GetAncestor(Native.WindowFromPoint(
        new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Bottom - 10)), 2);
    private static int ZIndex(nint target)
    {
        int i = 0;
        for (nint window = Native.GetTopWindow(0); window != 0 && i < 4096; window = Native.GetWindow(window, 2), i++)
            if (window == target) return i;
        return -1;
    }
    private static void Capture(Screen screen, string path)
    {
        using var bitmap = new Bitmap(screen.Bounds.Width, 200);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screen.Bounds.Left, screen.Bounds.Bottom - 200, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint GetProp(nint hwnd, string name);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetTopWindow(nint parent);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect rect, int size);
}
