using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace QrSnap.Services.Platform;

/// <summary>全局热键服务。</summary>
public interface IGlobalHotkeyService : IDisposable
{
    bool IsSupported { get; }

    /// <summary>热键被按下时触发。</summary>
    event Action? Pressed;

    /// <summary>注册热键，成功返回 true。modifiers 见 <see cref="HotkeyModifiers"/>。</summary>
    bool Register(HotkeyModifiers modifiers, uint virtualKey);

    void Unregister();
}

/// <summary>热键修饰键。</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

/// <summary>
/// Windows 实现：独立 STA 线程 + RegisterHotKey(NULL)。
/// hWnd 为 NULL 时 WM_HOTKEY 直接投递到线程消息队列，
/// 无需创建窗口和窗口过程，结构最简、风险最小。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalHotkey : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int HotkeyId = 0xA17;

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _registered;

    /// <summary>热键被按下时触发（在线程池上执行）。</summary>
    public event Action? Pressed;

    public bool IsSupported => true;

    public bool Register(HotkeyModifiers modifiers, uint virtualKey)
    {
        Unregister();

        using var ready = new ManualResetEventSlim(false);
        bool success = false;

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            success = RegisterHotKey(IntPtr.Zero, HotkeyId,
                (uint)(modifiers | HotkeyModifiers.NoRepeat), virtualKey);
            ready.Set();
            if (!success) return;

            // GetMessage 返回 0 表示收到 WM_QUIT，-1 表示错误
            while (true)
            {
                int r = GetMessage(out var msg, IntPtr.Zero, 0, 0);
                if (r <= 0) break;
                if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HotkeyId)
                    ThreadPool.QueueUserWorkItem(_ => Pressed?.Invoke());
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        {
            IsBackground = true,
            Name = "GlobalHotkeyThread"
        };

        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
        _registered = success;
        return success;
    }

    public void Unregister()
    {
        if (_registered && _threadId != 0)
        {
            // PostThreadMessage 让 GetMessage 循环退出
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            // 再补一次 UnregisterHotKey（在注册线程执行，此处调用同样有效）
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
        _registered = false;
        _threadId = 0;
        _thread = null;
    }

    public void Dispose() => Unregister();

    // ── Win32 ──────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
