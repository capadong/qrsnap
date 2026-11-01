using SkiaSharp;

namespace QrSnap.Services.Platform;

/// <summary>屏幕截图能力。各平台实现不同，由 <see cref="PlatformServices"/> 按运行环境选择。</summary>
public interface IScreenCapture
{
    /// <summary>是否支持当前平台。</summary>
    bool IsSupported { get; }

    /// <summary>
    /// 抓取虚拟桌面全图（覆盖所有显示器），返回物理像素坐标下的位图。
    /// 坐标原点为虚拟桌面左上角，调用方需叠加 <see cref="VirtualBounds"/> 才能换算为屏幕坐标。
    /// </summary>
    SKBitmap? CaptureVirtualDesktop();

    /// <summary>虚拟桌面在物理像素下的边界（X/Y 可能为负，多显示器时左侧屏幕原点为负）。</summary>
    CaptureBounds VirtualBounds { get; }
}

/// <summary>虚拟桌面边界，物理像素。</summary>
public readonly record struct CaptureBounds(int X, int Y, int Width, int Height);
