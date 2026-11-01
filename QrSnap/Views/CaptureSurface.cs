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
/// 截图选区绘制层。负责背景截图渲染、暗色遮罩、选框、控制点、尺寸提示。
/// 所有坐标以控件内逻辑像素计算，导出时换算回位图像素。
/// </summary>
public sealed class CaptureSurface : Control
{
    private const double MinSelection = 6;
    private const double HandleHitSize = 10;

    private readonly WriteableBitmap _bitmap;

    /// <summary>逻辑像素 → 物理像素的换算比（取自所在窗口）。</summary>
    private double PixelRatio => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    private Point _start;
    private Point _current;
    private Rect? _selection;
    private bool _isDragging;
    private bool _isMoving;
    private Point _moveOffset;
    private HandleKind _activeHandle = HandleKind.None;

    private static readonly IBrush MaskBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x7A, 0xFF));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x7A, 0xFF)), 1.5);
    private static readonly IPen DashPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1,
        new DashStyle([4, 4], 0));
    private static readonly IBrush HandleBrush = Brushes.White;
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x7A, 0xFF)), 1.5);
    private static readonly IBrush HintBg = new SolidColorBrush(Color.FromArgb(230, 32, 36, 44));
    private static readonly IBrush DimText = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));

    private enum HandleKind { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight, Move }

    public event Action<PixelRect>? SelectionConfirmed;
    public event Action? SelectionCancelled;

    public CaptureSurface(SKBitmap desktop)
    {
        Focusable = true;
        ClipToBounds = true;

        _bitmap = ToWriteableBitmap(desktop);
        // 截图按物理像素保存，控件在实际显示尺寸下缩放渲染
    }

    private static unsafe WriteableBitmap ToWriteableBitmap(SKBitmap src)
    {
        var info = new PixelSize(src.Width, src.Height);
        var wb = new WriteableBitmap(info, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();

        var srcInfo = new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var converted = new SKBitmap(srcInfo);
        if (!src.CopyTo(converted, SKColorType.Bgra8888))
            return wb;

        var dst = (byte*)fb.Address;
        var srcPtr = (byte*)converted.GetPixels();
        int rowBytes = src.Width * 4;
        int dstStride = fb.RowBytes;

        for (int y = 0; y < src.Height; y++)
            Buffer.MemoryCopy(srcPtr + y * rowBytes, dst + y * dstStride, dstStride, rowBytes);

        return wb;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);
        context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds);

        if (_selection is not { } sel || sel.Width < 0.5 || sel.Height < 0.5)
        {
            DrawFullMaskAndHint(context, bounds);
            return;
        }

        // 四周压暗，选区保持原亮度
        context.FillRectangle(MaskBrush, new Rect(0, 0, bounds.Width, sel.Top));
        context.FillRectangle(MaskBrush, new Rect(0, sel.Bottom, bounds.Width, bounds.Height - sel.Bottom));
        context.FillRectangle(MaskBrush, new Rect(0, sel.Top, sel.Left, sel.Height));
        context.FillRectangle(MaskBrush, new Rect(sel.Right, sel.Top, bounds.Width - sel.Right, sel.Height));

        // 三分线辅助
        for (int i = 1; i < 3; i++)
        {
            double x = sel.Left + sel.Width * i / 3;
            double y = sel.Top + sel.Height * i / 3;
            context.DrawLine(DashPen, new Point(x, sel.Top), new Point(x, sel.Bottom));
            context.DrawLine(DashPen, new Point(sel.Left, y), new Point(sel.Right, y));
        }

        context.DrawRectangle(null, BorderPen, sel);
        DrawHandles(context, sel);
        DrawSizeTag(context, sel, bounds);
    }

    private void DrawFullMaskAndHint(DrawingContext context, Rect bounds)
    {
        context.FillRectangle(MaskBrush, bounds);

        var text = new FormattedText("拖动鼠标框选二维码区域  ·  Esc 取消",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 15, Brushes.White);
        var origin = new Point((bounds.Width - text.Width) / 2, (bounds.Height - text.Height) / 2);
        context.DrawText(text, origin);
    }

    private void DrawHandles(DrawingContext context, Rect sel)
    {
        foreach (var p in HandlePoints(sel))
        {
            var r = new Rect(p.X - 3.5, p.Y - 3.5, 7, 7);
            context.DrawRectangle(HandleBrush, HandlePen, r, 1.5, 1.5);
        }
    }

    private void DrawSizeTag(DrawingContext context, Rect sel, Rect bounds)
    {
        var w = (int)Math.Round(sel.Width * PixelRatio);
        var h = (int)Math.Round(sel.Height * PixelRatio);
        var text = new FormattedText($"{w} × {h}",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 13, Brushes.White);

        double padX = 8, padY = 4;
        double tagW = text.Width + padX * 2;
        double tagH = text.Height + padY * 2;

        // 优先显示在选框上方，空间不足则放到内部顶部
        double x = sel.Left;
        double y = sel.Top - tagH - 6;
        if (y < 0) y = sel.Top + 6;
        if (x + tagW > bounds.Width) x = bounds.Width - tagW - 2;

        var tagRect = new Rect(x, y, tagW, tagH);
        context.DrawRectangle(HintBg, null, tagRect, 4, 4);
        context.DrawText(text, new Point(x + padX, y + padY - 0.5));

        var tip = new FormattedText("双击或回车识别",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 11, DimText);
        if (sel.Height > tip.Height + 16 && sel.Width > tip.Width + 16)
        {
            context.DrawText(tip, new Point(sel.Left + 8, sel.Bottom - tip.Height - 8));
        }
    }

    // ── 交互 ────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            SelectionCancelled?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2 && _selection.HasValue)
        {
            Confirm();
            e.Handled = true;
            return;
        }

        if (_selection is { } sel)
        {
            var handle = HitTest(sel, pt);
            if (handle != HandleKind.None)
            {
                if (handle == HandleKind.Move)
                {
                    _isMoving = true;
                    _moveOffset = new Point(pt.X - sel.X, pt.Y - sel.Y);
                }
                else
                {
                    _activeHandle = handle;
                }
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            // 已有选框时点击框外，重新开始框选
        }

        _start = pt;
        _current = pt;
        _selection = new Rect(pt, pt);
        _isDragging = true;
        _activeHandle = HandleKind.None;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);

        if (_isDragging || _activeHandle != HandleKind.None)
        {
            _current = pt;
            UpdateSelectionFromDrag(pt);
            InvalidateVisual();
        }
        else if (_isMoving && _selection is { } sel)
        {
            var w = sel.Width;
            var h = sel.Height;
            double x = Math.Clamp(pt.X - _moveOffset.X, 0, Math.Max(0, Bounds.Width - w));
            double y = Math.Clamp(pt.Y - _moveOffset.Y, 0, Math.Max(0, Bounds.Height - h));
            _selection = new Rect(x, y, w, h);
            InvalidateVisual();
        }
        else
        {
            UpdateCursor(pt);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging || _isMoving || _activeHandle != HandleKind.None)
        {
            _isDragging = false;
            _isMoving = false;
            _activeHandle = HandleKind.None;
            e.Pointer.Capture(null);

            if (_selection is { } sel && (sel.Width < MinSelection || sel.Height < MinSelection))
                _selection = null;

            InvalidateVisual();
        }
    }

    private void UpdateSelectionFromDrag(Point pt)
    {
        if (_isDragging)
        {
            _selection = Normalize(_start, pt);
            return;
        }

        if (_selection is not { } sel) return;

        double l = sel.Left, t = sel.Top, r = sel.Right, b = sel.Bottom;
        switch (_activeHandle)
        {
            case HandleKind.Left: case HandleKind.TopLeft: case HandleKind.BottomLeft: l = pt.X; break;
            case HandleKind.Right: case HandleKind.TopRight: case HandleKind.BottomRight: r = pt.X; break;
        }
        switch (_activeHandle)
        {
            case HandleKind.Top: case HandleKind.TopLeft: case HandleKind.TopRight: t = pt.Y; break;
            case HandleKind.Bottom: case HandleKind.BottomLeft: case HandleKind.BottomRight: b = pt.Y; break;
        }

        _selection = Normalize(new Point(l, t), new Point(r, b));
    }

    private Rect Normalize(Point a, Point b)
    {
        double x = Math.Max(0, Math.Min(a.X, b.X));
        double y = Math.Max(0, Math.Min(a.Y, b.Y));
        double x2 = Math.Min(Bounds.Width, Math.Max(a.X, b.X));
        double y2 = Math.Min(Bounds.Height, Math.Max(a.Y, b.Y));
        return new Rect(x, y, Math.Max(0, x2 - x), Math.Max(0, y2 - y));
    }

    private HandleKind HitTest(Rect sel, Point p)
    {
        if (Near(p, sel.TopLeft)) return HandleKind.TopLeft;
        if (Near(p, sel.TopRight)) return HandleKind.TopRight;
        if (Near(p, sel.BottomLeft)) return HandleKind.BottomLeft;
        if (Near(p, sel.BottomRight)) return HandleKind.BottomRight;
        if (Math.Abs(p.Y - sel.Top) < HandleHitSize / 2 && p.X > sel.Left && p.X < sel.Right) return HandleKind.Top;
        if (Math.Abs(p.Y - sel.Bottom) < HandleHitSize / 2 && p.X > sel.Left && p.X < sel.Right) return HandleKind.Bottom;
        if (Math.Abs(p.X - sel.Left) < HandleHitSize / 2 && p.Y > sel.Top && p.Y < sel.Bottom) return HandleKind.Left;
        if (Math.Abs(p.X - sel.Right) < HandleHitSize / 2 && p.Y > sel.Top && p.Y < sel.Bottom) return HandleKind.Right;
        if (sel.Contains(p)) return HandleKind.Move;
        return HandleKind.None;

        bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < HandleHitSize && Math.Abs(a.Y - b.Y) < HandleHitSize;
    }

    private void UpdateCursor(Point pt)
    {
        Cursor = _selection is { } sel
            ? HitTest(sel, pt) switch
            {
                HandleKind.Move => new Cursor(StandardCursorType.SizeAll),
                HandleKind.Left or HandleKind.Right => new Cursor(StandardCursorType.SizeWestEast),
                HandleKind.Top or HandleKind.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                HandleKind.TopLeft or HandleKind.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
                HandleKind.TopRight or HandleKind.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
                _ => new Cursor(StandardCursorType.Cross)
            }
            : new Cursor(StandardCursorType.Cross);
    }

    /// <summary>确认当前选区。</summary>
    public void Confirm()
    {
        if (_selection is not { } sel || sel.Width < MinSelection || sel.Height < MinSelection)
            return;

        var rect = new PixelRect(
            (int)Math.Round(sel.X * PixelRatio),
            (int)Math.Round(sel.Y * PixelRatio),
            Math.Max(1, (int)Math.Round(sel.Width * PixelRatio)),
            Math.Max(1, (int)Math.Round(sel.Height * PixelRatio)));

        SelectionConfirmed?.Invoke(rect);
    }

    private static Point[] HandlePoints(Rect r) =>
    [
        r.TopLeft, new Point(r.Center.X, r.Top), r.TopRight,
        new Point(r.Left, r.Center.Y), new Point(r.Right, r.Center.Y),
        r.BottomLeft, new Point(r.Center.X, r.Bottom), r.BottomRight
    ];
}
