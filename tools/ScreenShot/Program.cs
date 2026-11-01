// 验证用：置顶 QrSnap 主窗并整屏截图
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

SetProcessDPIAware();
var proc = System.Diagnostics.Process.GetProcessesByName("QrSnap").FirstOrDefault();
if (proc is null) { Console.WriteLine("not running"); return 1; }
SetForegroundWindow(proc.MainWindowHandle);
Thread.Sleep(600);

var dir = @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\screenshots";
System.IO.Directory.CreateDirectory(dir);
var b = Screen.PrimaryScreen!.Bounds;
using (var bmp = new Bitmap(b.Width, b.Height))
{
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(b.Location, Point.Empty, b.Size);
    bmp.Save(System.IO.Path.Combine(dir, "ui-check.png"), ImageFormat.Png);
}
Console.WriteLine("saved");
return 0;
