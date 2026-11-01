using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using QrSnap.Services;
using QrSnap.Services.Platform;
using QrSnap.ViewModels;
using QrSnap.Views;

namespace QrSnap;

public partial class App : Application
{
    private IGlobalHotkeyService? _hotkey;
    private CaptureCoordinator? _coordinator;
    private ResultWindow? _resultWindow;

    /// <summary>默认热键：Alt + Q。</summary>
    private const HotkeyModifiers DefaultModifiers = HotkeyModifiers.Alt;
    private const uint VirtualKeyQ = 0x51;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var screenCapture = PlatformServices.CreateScreenCapture();
            _coordinator = new CaptureCoordinator(screenCapture);
            _coordinator.Recognized += ShowResult;
            _coordinator.Notify += ShowNotice;

            var vm = new MainWindowViewModel(_coordinator);
            var main = new MainWindow { DataContext = vm };

            desktop.MainWindow = main;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, e) => Cleanup();

            RegisterHotkey(vm);

            // 调试用：--autostart 自动进入截图流程（供端到端冒烟测试）
            if (Environment.GetCommandLineArgs().Contains("--autostart"))
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(1200);
                    if (_coordinator is not null)
                        await _coordinator.StartCaptureAsync();
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterHotkey(MainWindowViewModel vm)
    {
        if (!OperatingSystem.IsWindows())
        {
            vm.HotkeyText = "仅 Windows 支持";
            return;
        }

        _hotkey = new WindowsGlobalHotkey();
        _hotkey.Pressed += () => Dispatcher.UIThread.Post(async () =>
        {
            if (_coordinator is null) return;
            await _coordinator.StartCaptureAsync();
        });

        bool ok = _hotkey.Register(DefaultModifiers, VirtualKeyQ);
        if (ok)
        {
            vm.HotkeyText = "Alt + Q";
            vm.UpdateStatus("快捷键已就绪，随时可以识别");
        }
        else
        {
            vm.HotkeyText = "注册失败";
            vm.UpdateStatus("快捷键被其他程序占用，请从下方按钮启动识别");
            Debug.WriteLine("[Hotkey] RegisterHotKey 失败，可能被占用");
        }
    }

    private void ShowResult(Models.QrResult result)
    {
        try
        {
            System.IO.File.AppendAllText(
                @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\QrSnap\trace.log",
                $"{DateTime.Now:HH:mm:ss.fff} ShowResult: kind={result.Kind} text={result.RawText[..Math.Min(50, result.RawText.Length)]}\n");
            _resultWindow?.Close();
            var win = new ResultWindow(result);
            _resultWindow = win;
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_resultWindow, win)) _resultWindow = null;
            };
            win.Show();
            win.PositionNearCursor();
            System.IO.File.AppendAllText(
                @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\QrSnap\trace.log",
                $"{DateTime.Now:HH:mm:ss.fff} 结果窗已显示\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                @"C:\Users\capad\WorkBuddy\2026-11-01-16-13-12\QrSnap\trace.log",
                $"{DateTime.Now:HH:mm:ss.fff} ShowResult 异常: {ex}\n");
        }
    }

    private void ShowNotice(string message)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm })
            vm.UpdateStatus(message);
    }

    private void Cleanup() => _hotkey?.Dispose();

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var plugins = BindingPlugins.DataValidators;
        foreach (var plugin in plugins.OfType<DataAnnotationsValidationPlugin>().ToArray())
            plugins.Remove(plugin);
    }
}
