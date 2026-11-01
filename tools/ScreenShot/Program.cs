// 端到端冒烟测试驱动
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

const string Payload = "https://qrsnap.test/e2e-smoke";
const string AppPath = @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\QrSnap\bin\Debug\net10.0\QrSnap.exe";
const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
const uint MOUSEEVENTF_LEFTUP = 0x0004;

[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
[DllImport("user32.dll")] static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);

SetProcessDPIAware();
Console.WriteLine($"screen: {Screen.PrimaryScreen.Bounds.Width}x{Screen.PrimaryScreen.Bounds.Height}, virtualSM: {GetSystemMetrics(78)}x{GetSystemMetrics(79)}");

var qrBmp = new ZXing.SkiaSharp.BarcodeWriter
{
    Format = ZXing.BarcodeFormat.QR_CODE,
    Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 320, Height = 320, Margin = 2 }
}.Write(Payload);

var screen = Screen.PrimaryScreen!.Bounds;

// SKBitmap -> GDI Bitmap（经 PNG 字节中转，简单可靠）
var pngBytes = qrBmp.Encode(SKEncodedImageFormat.Png, 100).ToArray();
var qrImage = System.Drawing.Image.FromStream(new MemoryStream(pngBytes));

Form? qrForm = null;
var ui = new Thread(() =>
{
    qrForm = new Form
    {
        FormBorderStyle = FormBorderStyle.None,
        WindowState = FormWindowState.Maximized,
        TopMost = true,
        StartPosition = FormStartPosition.Manual,
        Bounds = screen,
        BackColor = Color.White
    };
    qrForm.Controls.Add(new PictureBox
    {
        Image = qrImage,
        Size = new Size(320, 320),
        Location = new Point((screen.Width - 320) / 2, (screen.Height - 320) / 2)
    });
    Application.Run(qrForm);
});
ui.SetApartmentState(ApartmentState.STA);
ui.IsBackground = true;
ui.Start();
Thread.Sleep(900);

var app = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPath, "--autostart")
{
    UseShellExecute = true
});

Thread.Sleep(3300); // 等 autostart + 截屏 + 覆盖层

// 中途截图：确认覆盖层状态
var dbgDir = @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\screenshots";
System.IO.Directory.CreateDirectory(dbgDir);
using (var dbmp = new Bitmap(screen.Width, screen.Height))
{
    using (var g = Graphics.FromImage(dbmp))
        g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
    dbmp.Save(System.IO.Path.Combine(dbgDir, "overlay-during.png"), ImageFormat.Png);
}

int cx1 = screen.Width / 2 - 190, cy1 = screen.Height / 2 - 190;
int cx2 = screen.Width / 2 + 190, cy2 = screen.Height / 2 + 190;

void Drag(int x, int y) { SetCursorPos(x, y); Thread.Sleep(100); }

Drag(cx1, cy1);
mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
Thread.Sleep(150);
for (int i = 1; i <= 10; i++)
{
    SetCursorPos(cx1 + (cx2 - cx1) * i / 10, cy1 + (cy2 - cy1) * i / 10);
    Thread.Sleep(60);
}
mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
Thread.Sleep(500);

Drag((cx1 + cx2) / 2, (cy1 + cy2) / 2);   // 双击确认
mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
Thread.Sleep(90);
mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
Thread.Sleep(400); // 等结果窗出现
try { qrForm?.BeginInvoke(new Action(() => { qrForm.Visible = false; qrForm.TopMost = false; })); } catch { }
Thread.Sleep(2200); // 等识别 + 结果窗

var dir = @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\screenshots";
System.IO.Directory.CreateDirectory(dir);
using (var bmp = new Bitmap(screen.Width, screen.Height))
{
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
    bmp.Save(System.IO.Path.Combine(dir, "e2e.png"), ImageFormat.Png);
}
Console.WriteLine("e2e screenshot saved");

try { app.Kill(); } catch { }
try { qrForm?.Close(); } catch { }
Environment.Exit(0);
