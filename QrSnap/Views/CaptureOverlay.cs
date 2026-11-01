using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace QrSnap.Views;

/// <summary>
/// 全屏截图覆盖窗口：显示桌面截图作为背景，除选区外的区域压暗，
/// 支持拖拽框选、拖动边缘调整、拖拽选框移动，回车/双击确认，Esc 取消。
/// </summary>
public sealed class CaptureOverlay : Window
{
    private readonly SKBitmap _desktop;
    private readonly CaptureSurface _surface;

    /// <summary>用户确认选区时触发，参数为相对虚拟桌面左上角的物理像素矩形。</summary>
    public event Action<PixelRect>? RegionSelected;

    /// <summary>用户取消时触发。</summary>
    public event Action? Cancelled;

    public CaptureOverlay(SKBitmap desktop, PixelRect virtualBounds)
    {
        _desktop = desktop;

        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(virtualBounds.X, virtualBounds.Y);
        // 尺寸在 Opened 后按实际 Scaling 换算（构造时 Scaling 未必就绪）
        _virtualBounds = virtualBounds;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);

        _surface = new CaptureSurface(desktop);
        _surface.SelectionConfirmed += rect => RegionSelected?.Invoke(rect);
        _surface.SelectionCancelled += () => Cancelled?.Invoke();

        Content = _surface;

        Opened += (_, _) =>
        {
            // 虚拟桌面边界是物理像素，窗口尺寸是逻辑像素
            double s = RenderScaling;
            Width = virtualBounds.Width / s;
            Height = virtualBounds.Height / s;
            _surface.Width = Width;
            _surface.Height = Height;

            Activate();
            Focus();
        };

        KeyDown += OnKeyDown;
    }

    private readonly PixelRect _virtualBounds;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter:
                _surface.Confirm();
                e.Handled = true;
                break;
        }
    }
}
