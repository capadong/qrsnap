using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkiaSharp;

namespace QrSnap.Services.Platform;

/// <summary>Windows 桌面截图实现，使用 BitBlt 逐屏抓取后拼合到虚拟桌面。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScreenCapture : IScreenCapture
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    public bool IsSupported => true;

    public CaptureBounds VirtualBounds
    {
        get
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            return new CaptureBounds(x, y, w, h);
        }
    }

    public SKBitmap? CaptureVirtualDesktop()
    {
        var bounds = VirtualBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBitmap = IntPtr.Zero, oldObj = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            memDc = CreateCompatibleDC(screenDc);
            hBitmap = CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero) return null;

            oldObj = SelectObject(memDc, hBitmap);

            // CAPTUREBLT 让分层窗口（含半透明）也被抓取
            if (!BitBlt(memDc, 0, 0, bounds.Width, bounds.Height,
                        screenDc, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT))
                return null;

            var bmp = ToSkBitmap(hBitmap, bounds.Width, bounds.Height);
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WinCapture] {ex}");
            return null;
        }
        finally
        {
            if (oldObj != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static SKBitmap ToSkBitmap(IntPtr hBitmap, int w, int h)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // 负高度 = 自上而下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        var buffer = new byte[w * h * 4];
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            int lines = GetDIBits(screenDc, hBitmap, 0, (uint)h, buffer, ref bmi, 0);
            if (lines == 0)
                throw new InvalidOperationException("GetDIBits 失败");
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length);
        return bitmap;
    }

    // ── Win32 ──────────────────────────────────────────────────
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public int[] bmiColors;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
        byte[] bits, ref BITMAPINFO bmi, uint usage);
}
