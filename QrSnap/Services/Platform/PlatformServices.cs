using System;
using SkiaSharp;

namespace QrSnap.Services.Platform;

/// <summary>不支持截图时的降级实现，仅用于其他平台占位。</summary>
public sealed class UnsupportedScreenCapture : IScreenCapture
{
    public bool IsSupported => false;
    public CaptureBounds VirtualBounds => default;
    public SKBitmap? CaptureVirtualDesktop() => null;
}

/// <summary>按运行平台选择具体实现。</summary>
public static class PlatformServices
{
    public static IScreenCapture CreateScreenCapture()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsScreenCapture();

        // TODO: macOS 走 screencapture -x 命令或 CGDisplayCreateImage
        // TODO: Linux X11 走 XGetImage，Wayland 需 portal 授权
        return new UnsupportedScreenCapture();
    }
}
