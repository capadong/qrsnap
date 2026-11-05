// QrSnapCpp E2E: QR -> Alt+Q -> drag select -> click button -> screenshot
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

const string AppPath = @"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/QrSnapCpp/QrSnap.exe";
const string ShotPath = @"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/screenshots/cpp-e2e.png";
const uint LEFT_DOWN = 0x0002, LEFT_UP = 0x0004;
const byte VK_Q = 0x51, VK_MENU = 0x12;

[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] static extern void keybd_event(byte k, byte s, uint f, IntPtr e);
[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

SetProcessDPIAware();

// --mainshot 模式：只启动应用、截主窗口、退出
if (args.Contains("--mainshot"))
{
    var scr = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPath) { UseShellExecute = true });
    Thread.Sleep(2800);
    using (var bmp = new Bitmap(scr.Width, scr.Height))
    using (var g = Graphics.FromImage(bmp))
    {
        g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
        bmp.Save(@"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/screenshots/cpp-main.png");
    }
    try { p.Kill(); } catch { }
    Environment.Exit(0);
}

using var qr = new ZXing.SkiaSharp.BarcodeWriter
{
    Format = ZXing.BarcodeFormat.QR_CODE,
    Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 320, Height = 320, Margin = 2 }
}.Write("https://qrsnap.test/cpp-e2e");

var pngBytes = qr.Encode(SKEncodedImageFormat.Png, 100).ToArray();
var qrImage = System.Drawing.Image.FromStream(new MemoryStream(pngBytes));
var screen = Screen.PrimaryScreen!.Bounds;

Form? qrForm = null;
var ui = new Thread(() =>
{
    qrForm = new Form
    {
        FormBorderStyle = FormBorderStyle.None, WindowState = FormWindowState.Maximized,
        TopMost = true, StartPosition = FormStartPosition.Manual,
        Bounds = screen, BackColor = Color.White
    };
    qrForm.Controls.Add(new PictureBox
    {
        Image = qrImage, Size = new Size(320, 320),
        Location = new Point((screen.Width - 320) / 2, (screen.Height - 320) / 2)
    });
    Application.Run(qrForm);
});
ui.SetApartmentState(ApartmentState.STA); ui.IsBackground = true; ui.Start();
Thread.Sleep(900);

var app = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPath) { UseShellExecute = true });
Thread.Sleep(2500);

// 主窗口截图（Alt+Q 之前）
using (var bmp = new Bitmap(screen.Width, screen.Height))
using (var g = Graphics.FromImage(bmp))
{
    g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
    bmp.Save(@"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/screenshots/cpp-main.png");
}

// Alt+Q 触发截屏
keybd_event(VK_MENU, 0, 0, IntPtr.Zero); Thread.Sleep(60);
keybd_event(VK_Q, 0, 0, IntPtr.Zero); Thread.Sleep(60);
keybd_event(VK_Q, 0, 2, IntPtr.Zero); Thread.Sleep(60);
keybd_event(VK_MENU, 0, 2, IntPtr.Zero);
Thread.Sleep(1200);

int cx1 = screen.Width / 2 - 190, cy1 = screen.Height / 2 - 190;
int cx2 = screen.Width / 2 + 190, cy2 = screen.Height / 2 + 190;

SetCursorPos(cx1, cy1); Thread.Sleep(120);
mouse_event(LEFT_DOWN, 0, 0, 0, 0); Thread.Sleep(150);
for (int i = 1; i <= 10; i++) { SetCursorPos(cx1 + (cx2 - cx1) * i / 10, cy1 + (cy2 - cy1) * i / 10); Thread.Sleep(60); }
mouse_event(LEFT_UP, 0, 0, 0, 0);
Thread.Sleep(700);

// 框选过程截图（覆盖层状态）
using (var bmp = new Bitmap(screen.Width, screen.Height))
using (var g = Graphics.FromImage(bmp))
{
    g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
    bmp.Save(@"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/screenshots/cpp-overlay.png");
}

// 点击"识别"按钮（选区中心偏右下方，即按钮位置）
SetCursorPos((cx1 + cx2) / 2 + 100, cy2 + 31); Thread.Sleep(500);
mouse_event(LEFT_DOWN, 0, 0, 0, 0); Thread.Sleep(80); mouse_event(LEFT_UP, 0, 0, 0, 0);
Thread.Sleep(2500);

// 结果截图
using (var bmp = new Bitmap(screen.Width, screen.Height))
using (var g = Graphics.FromImage(bmp))
{
    g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
    bmp.Save(ShotPath);
}

try { app.Kill(); } catch { }
try { qrForm?.Close(); } catch { }
Environment.Exit(0);
