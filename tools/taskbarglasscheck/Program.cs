using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Hardware rendering check. Stop DispCtrl.Engine first so its hide/focus
// settings do not change the test scene. The helper restores on process exit.
internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Attach(uint pid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Update(uint pid, uint owner, uint config);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    private static readonly nint HwndTopmost = new(-1), HwndBottom = new(1);
    private const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        string dir = Path.GetFullPath(args[0]);
        string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "DispCtrl-GlassCheck");
        Directory.CreateDirectory(output);
        nint lib = NativeLibrary.Load(Path.Combine(dir, File.ReadAllText(Path.Combine(dir, "TaskbarGlass.version")).Trim()));
        var attach = Marshal.GetDelegateForFunctionPointer<Attach>(NativeLibrary.GetExport(lib, "GlassAttach"));
        var update = Marshal.GetDelegateForFunctionPointer<Update>(NativeLibrary.GetExport(lib, "GlassUpdate"));
        nint taskbar = FindWindow("Shell_TrayWnd", null);
        GetWindowThreadProcessId(taskbar, out uint explorer);
        uint owner = (uint)Environment.ProcessId;
        if (update(explorer, owner, 0) == 0) Marshal.ThrowExceptionForHR(attach(explorer));
        var screen = Screen.PrimaryScreen!.Bounds;
        GetCursorPos(out Point cursor);
        using var form = new Pattern { Bounds = new(screen.Left, screen.Bottom - 180, screen.Width, 180) };
        form.Shown += async (_, _) =>
        {
            try
            {
                SetCursorPos(screen.Left + 30, screen.Bottom - 1);
                // The pattern is deliberately below Explorer. Without this,
                // the test window becomes the foreground surface and every
                // capture measures the pattern itself instead of taskbar glass.
                SetWindowPos(form.Handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                SetWindowPos(taskbar, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                await Task.Delay(1300);
                foreach (var (name, radius, tint) in new[] { ("original", -1, 0), ("clear", 0, 0), ("blur12", 12, 0), ("blur60", 60, 0), ("tint60", 60, 60), ("restored", -1, 0) })
                {
                    uint config = radius < 0 ? 0 : 0x1000000u | (uint)radius | ((uint)tint << 8);
                    int result = update(explorer, owner, config);
                    Marshal.ThrowExceptionForHR(result);
                    await Task.Delay(800);
                    SetWindowPos(taskbar, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                    using var capture = new Bitmap(screen.Width, 180);
                    using (var graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(screen.Left, screen.Bottom - 180, 0, 0, capture.Size);
                    capture.Save(Path.Combine(output, name + ".png"));
                    // This region avoids the left widget and centered app icons.
                    double[] samples = Enumerable.Range(250, 180).Select(x => (double)capture.GetPixel(x, 162).R).ToArray();
                    double average = samples.Average();
                    double contrast = Math.Sqrt(samples.Select(v => (v - average) * (v - average)).Average());
                    Console.WriteLine($"{name}: backgrounds={result}, mean={average:F2}, contrast={contrast:F2}");
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { update(explorer, owner, 0); SetCursorPos(cursor.X, cursor.Y); form.Close(); }
        };
        Application.Run(form);
        Console.WriteLine(output);
    }

    private sealed class Pattern : Form
    {
        public Pattern() { FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; DoubleBuffered = true; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            for (int x = 0; x < Width; x += 16) e.Graphics.FillRectangle((x / 16) % 2 == 0 ? Brushes.Black : Brushes.White, x, 0, 16, Height);
        }
    }
}
