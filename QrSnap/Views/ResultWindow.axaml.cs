using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using QrSnap.Models;

namespace QrSnap.Views;

/// <summary>识别结果弹窗：圆角卡片、淡入动画，支持复制与打开。</summary>
public partial class ResultWindow : Window
{
    private readonly QrResult? _result;
    private bool _copied;

    public ResultWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    public ResultWindow(QrResult result) : this()
    {
        _result = result;
        Apply(result);
    }

    private void Apply(QrResult r)
    {
        KindLabel.Text = r.KindLabel;
        TitleText.Text = string.IsNullOrWhiteSpace(r.Title) ? r.KindLabel : r.Title!;
        ContentText.Text = r.DisplayText;

        var (accent, bg, icon) = r.Kind switch
        {
            QrContentKind.Url => ("#2F7AFF", "#EAF1FF", "M3,9 H12 M9,4 L14,9 L9,14"),
            QrContentKind.Wifi => ("#D4830A", "#FFF4E8", "M8,13 A1,1 0 1,1 7.9,13 M4,8 A6,6 0 0,1 12,8 M1.5,5 A10,10 0 0,1 14.5,5"),
            QrContentKind.VCard => ("#7A4DE0", "#F3ECFF", "M5,4 A3,3 0 1,1 5,10 A3,3 0 1,1 5,4 M1,16 C1,12 9,12 9,16"),
            QrContentKind.Email => ("#D63C64", "#FDECF0", "M2,4 H14 V12 H2 Z M2,4 L8,9 L14,4"),
            QrContentKind.Phone => ("#1E9E5A", "#EAF7EF", "M4,2 C5,6 8,10 12,12 L14,9 L11,7 L9.5,8.5 C8,7.5 7,6 6,4.5 L7.5,3 L5.5,1 Z"),
            _ => ("#5A6472", "#EEF2F8", "M3,3 H13 V13 H3 Z M6,7 H10 M6,10 H9")
        };

        AccentBar.Background = SolidColorBrush.Parse(accent);
        IconBox.Background = SolidColorBrush.Parse(bg);
        IconPath.Data = Geometry.Parse(icon);
        IconPath.Foreground = SolidColorBrush.Parse(accent);

        ActionButton.Content = r.ActionText;
        ActionButton.Background = SolidColorBrush.Parse(accent);
        MetaText.Text = $"{r.KindLabel} · 识别耗时 {r.ElapsedMs} ms · {r.Format}";

        if (r.Kind == QrContentKind.Wifi && r.Subtitle is { Length: > 0 } pwd)
        {
            WifiPanel.IsVisible = true;
            WifiPassword.Text = pwd;
        }
        else if (r.Subtitle is { Length: > 0 } sub && r.Kind != QrContentKind.Url)
        {
            SubtitleText.IsVisible = true;
            SubtitleText.Text = sub;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // 淡入 + 上浮
        Opacity = 0;
        var start = Position;
        Position = new PixelPoint(start.X, start.Y + 14);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double t = 0;
        timer.Tick += (_, _) =>
        {
            t += 0.09;
            if (t >= 1)
            {
                Opacity = 1;
                Position = start;
                timer.Stop();
                return;
            }
            var eased = 1 - Math.Pow(1 - t, 3);
            Opacity = eased;
            Position = new PixelPoint(start.X, (int)(start.Y + 14 * (1 - eased)));
        };
        timer.Start();

        Activate();
        Focus();
    }

    /// <summary>把窗口定位到屏幕右下角附近，避免遮挡刚框选的区域。</summary>
    public void PositionNearCursor()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        int x = area.X + (area.Width - (int)Width) / 2;
        int y = area.Y + (area.Height - (int)Height) / 2;
        Position = new PixelPoint(Math.Max(area.X, x), Math.Max(area.Y, y));
    }

    private async void OnAction(object? sender, RoutedEventArgs e)
    {
        if (_result is null) return;

        switch (_result.Kind)
        {
            case QrContentKind.Url:
                OpenExternal(_result.RawText.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? _result.RawText
                    : "https://" + _result.RawText, alsoCopy: true);
                break;
            case QrContentKind.Email:
                OpenExternal($"mailto:{_result.RawText.Replace("mailto:", "", StringComparison.OrdinalIgnoreCase)}", false);
                break;
            case QrContentKind.Phone:
                OpenExternal($"tel:{_result.RawText.Replace("tel:", "", StringComparison.OrdinalIgnoreCase)}", false);
                break;
            default:
                await CopyAsync(_result.RawText);
                break;
        }
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (_result is null) return;
        await CopyAsync(_result.RawText);
    }

    private async Task CopyAsync(string text)
    {
        var clipboard = Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(text);
        _copied = true;
        CopyButton.Content = "已复制";
        CopyHint.Text = "内容已复制到剪贴板";

        await Task.Delay(1600);
        if (_copied)
        {
            CopyButton.Content = "复制";
            CopyHint.Text = "";
        }
    }

    private void OpenExternal(string target, bool alsoCopy)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            if (alsoCopy) _ = CopyAsync(_result?.RawText ?? target);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Open] {ex}");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
