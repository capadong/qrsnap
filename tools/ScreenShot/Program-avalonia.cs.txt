// 点击"识别"按钮验证
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SkiaSharp;

const string AppPath = @"C:/Users/capad/WorkBuddy/2026-11-01-16-13-12/QrSnap/bin/Debug/net10.0/QrSnap.exe";
const uint LEFT_DOWN = 0x0002;
const uint LEFT_UP = 0x0004;

[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

SetProcessDPIAware();

using var qr = new ZXing.SkiaSharp.BarcodeWriter
{
    Format = ZXing.BarcodeFormat.QR_CODE,
    Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 320, Height = 320, Margin = 2 }
}.Write("https://qrsnap.test/btn-click");

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

var app = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPath, "--autostart") { UseShellExecute = true });
Thread.Sleep(3300);

int cx1 = screen.Width / 2 - 190, cy1 = screen.Height / 2 - 190;
int cx2 = screen.Width / 2 + 190, cy2 = screen.Height / 2 + 190;

SetCursorPos(cx1, cy1); Thread.Sleep(120);
mouse_event(LEFT_DOWN,0,0,0,0); Thread.Sleep(150);
for (int i = 1; i <= 10; i++) { SetCursorPos(cx1+(cx2-cx1)*i/10, cy1+(cy2-cy1)*i/10); Thread.Sleep(60); }
mouse_event(LEFT_UP,0,0,0,0);
Thread.Sleep(600);

// 点击"识别"按钮（悬停位置即按钮）
SetCursorPos((cx1+cx2)/2 - 60, cy2 + 28); Thread.Sleep(500);
mouse_event(LEFT_DOWN,0,0,0,0); Thread.Sleep(80); mouse_event(LEFT_UP,0,0,0,0);
Thread.Sleep(2500);

try { app.Kill(); } catch { }
try { qrForm?.Close(); } catch { }
Environment.Exit(0);
