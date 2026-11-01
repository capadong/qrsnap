using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QrSnap.Services;

namespace QrSnap.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly CaptureCoordinator _coordinator;

    /// <summary>热键显示文案，由 App 在注册成功后回填。</summary>
    [ObservableProperty]
    private string _hotkeyText = "Alt + Q";

    /// <summary>底部状态提示。</summary>
    [ObservableProperty]
    private string _statusText = "按快捷键即可开始识别";

    public string VersionText { get; } =
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>设计期/无参构造，供 XAML 预览使用。</summary>
    public MainWindowViewModel()
        : this(new CaptureCoordinator(new QrSnap.Services.Platform.UnsupportedScreenCapture()))
    {
    }

    public MainWindowViewModel(CaptureCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [RelayCommand]
    private async Task StartCapture()
    {
        await _coordinator.StartCaptureAsync();
    }

    public void UpdateStatus(string text) => StatusText = text;
}
