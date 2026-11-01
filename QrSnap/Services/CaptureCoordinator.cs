using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using QrSnap.Models;
using QrSnap.Services.Platform;
using QrSnap.Views;
using SkiaSharp;

namespace QrSnap.Services;

/// <summary>
/// 截图识别主流程编排：热键 → 抓屏 → 覆盖层框选 → 裁切 → 识别 → 结果窗。
/// </summary>
public sealed class CaptureCoordinator
{
    private readonly IScreenCapture _screenCapture;
    private volatile bool _busy;

    public CaptureCoordinator(IScreenCapture screenCapture)
    {
        _screenCapture = screenCapture;
    }

    /// <summary>识别成功后回调，用于展示结果窗。</summary>
    public event Action<QrResult>? Recognized;

    /// <summary>流程中出现提示信息时回调（识别失败、截图失败等）。</summary>
    public event Action<string>? Notify;

    /// <summary>覆盖层是否正在显示，避免重复触发。</summary>
    public bool IsBusy => _busy;

    /// <summary>启动一次截图识别。必须在 UI 线程调用。</summary>
    public async Task StartCaptureAsync()
    {
        if (_busy) return;

        if (!_screenCapture.IsSupported)
        {
            Notify?.Invoke("当前平台暂不支持屏幕截图功能，敬请期待后续版本。");
            return;
        }

        _busy = true;
        SKBitmap? desktop = null;
        CaptureOverlay? overlay = null;
        Trace("开始截图流程");
        try
        {
            // 先隐藏结果窗和主窗，避免它们被截进图里
            await Task.Delay(80);

            desktop = _screenCapture.CaptureVirtualDesktop();
            Trace($"抓屏结果: {(desktop is null ? "失败" : $"{desktop.Width}x{desktop.Height}")}, bounds={_screenCapture.VirtualBounds}");
            if (desktop is null)
            {
                Notify?.Invoke("屏幕抓取失败，请检查系统权限或重试。");
                return;
            }

            var bounds = _screenCapture.VirtualBounds;
            var virtRect = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var captured = desktop;

            var tcs = new TaskCompletionSource<PixelRect?>();
            overlay = new CaptureOverlay(captured, virtRect);
            overlay.RegionSelected += rect => tcs.TrySetResult(rect);
            overlay.Cancelled += () => tcs.TrySetResult(null);

            overlay.Show();
            Trace("覆盖层已显示，等待框选");
            var selected = await tcs.Task;
            Trace($"框选结果: {selected?.ToString() ?? "已取消"}");
            overlay.Close();
            overlay = null;

            if (selected is not { } region)
                return;

            await RecognizeAsync(captured, region);
        }
        catch (Exception ex)
        {
            Trace($"异常: {ex}");
            Debug.WriteLine($"[Capture] {ex}");
            Notify?.Invoke($"截图流程出错：{ex.Message}");
        }
        finally
        {
            overlay?.Close();
            desktop?.Dispose();
            _busy = false;
        }
    }

    private static void Trace(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\QrSnap\trace.log",
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
        Debug.WriteLine($"[Capture] {msg}");
    }

    private async Task RecognizeAsync(SKBitmap desktop, PixelRect region)
    {
        var cropRect = new SKRectI(
            Math.Clamp(region.X, 0, desktop.Width),
            Math.Clamp(region.Y, 0, desktop.Height),
            Math.Clamp(region.X + region.Width, 0, desktop.Width),
            Math.Clamp(region.Y + region.Height, 0, desktop.Height));

        if (cropRect.Width < 8 || cropRect.Height < 8)
        {
            Notify?.Invoke("选区太小了，请重新框选二维码。");
            return;
        }

        using var cropped = new SKBitmap(cropRect.Width, cropRect.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (!desktop.ExtractSubset(cropped, cropRect))
        {
            Notify?.Invoke("选区裁切失败，请重试。");
            return;
        }

        // 解码是 CPU 密集操作，放后台线程避免卡 UI
        Trace($"开始解码 {cropRect.Width}x{cropRect.Height}");
        var snapshot = await Task.Run(() => QrDecoder.Decode(cropped));
        Trace($"解码结果: {(snapshot is null ? "未识别" : snapshot.Text[..Math.Min(60, snapshot.Text.Length)])}");
        if (snapshot is null)
        {
            Notify?.Invoke("没有识别到二维码，请调整选区后重试。");
            return;
        }

        var result = ContentParser.Parse(snapshot);
        await Dispatcher.UIThread.InvokeAsync(() => Recognized?.Invoke(result));
    }
}
