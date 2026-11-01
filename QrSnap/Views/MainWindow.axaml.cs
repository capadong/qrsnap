using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace QrSnap.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 窗口级拖拽：按下任意非交互区域即可移动窗口
        PointerPressed += OnWindowPointerPressed;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 按钮、可选中文本等交互控件不触发拖拽
        if (e.Source is Button or SelectableTextBlock)
            return;

        BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
