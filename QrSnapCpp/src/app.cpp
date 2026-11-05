#include "common.h"
#include <cstdio>

// ================= QrSnap C++ 版 =================
// 主窗口（介绍卡片，可拖拽，圆形关闭按钮）
// 托盘图标 + 右键菜单，全局热键 Alt+Q 触发截屏识别

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "dwmapi.lib")

static constexpr int HOTKEY_ID = 1;
static constexpr UINT WM_APP_TRAY = WM_APP + 1;

struct AppState {
    HWND mainWnd = nullptr;
    HINSTANCE hInst = nullptr;
    HICON appIcon = nullptr;
    NOTIFYICONDATAW nid = {};
    bool hotkeyOk = false;
    bool capturing = false;
    bool closeHover = false;
    RECT closeRect{};
    int  wndW = 0, wndH = 0;
    ULONG_PTR gdiplusToken = 0;
} g_app;

static CapturedScreen g_cap;

// ---------- 截屏 ----------
bool CaptureScreen(CapturedScreen& cap) {
    int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
    if (vw <= 0 || vh <= 0) return false;

    HDC screen = GetDC(nullptr);
    HDC mem = CreateCompatibleDC(screen);
    HBITMAP bmp = CreateCompatibleBitmap(screen, vw, vh);
    HGDIOBJ old = SelectObject(mem, bmp);
    BitBlt(mem, 0, 0, vw, vh, screen, vx, vy, SRCCOPY | CAPTUREBLT);

    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = vw;
    bi.bmiHeader.biHeight = -vh;   // 自顶向下
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    cap.pixels.resize((size_t)vw * vh * 4);
    GetDIBits(mem, bmp, 0, vh, cap.pixels.data(), &bi, DIB_RGB_COLORS);

    SelectObject(mem, old);
    DeleteDC(mem);
    ReleaseDC(nullptr, screen);

    cap.x = vx; cap.y = vy; cap.w = vw; cap.h = vh;
    cap.bitmap = bmp; cap.valid = true;
    return true;
}

void FreeCapture(CapturedScreen& cap) {
    if (cap.bitmap) { DeleteObject(cap.bitmap); cap.bitmap = nullptr; }
    cap.pixels.clear(); cap.pixels.shrink_to_fit();
    cap.valid = false;
}

// ---------- 识别流程 ----------
void OnCaptureConfirm(const RECT& physicalRect) {
    DecodeResult dr = DecodeRegion(g_cap, physicalRect);
    POINT pt; GetCursorPos(&pt);
    if (dr.ok) {
        ParsedContent p = ParseContent(dr.text);
        ShowResultWindow(p, pt);
    } else {
        ShowResultError(L"未识别到二维码，请重试", pt);
    }
    FreeCapture(g_cap);
    g_app.capturing = false;
}

void OnCaptureCancel() {
    FreeCapture(g_cap);
    g_app.capturing = false;
}

void RunCapture() {
    if (g_app.capturing) return;
    g_app.capturing = true;
    if (!CaptureScreen(g_cap)) { g_app.capturing = false; return; }
    ShowCaptureOverlay(g_cap, OnCaptureConfirm, OnCaptureCancel);
}

// ---------- 主窗口 ----------
void LayoutMainWindow(HWND hwnd) {
    UINT dpi = GetDpiForWindow(hwnd);
    g_app.wndW = MulDiv(400, dpi, 96);
    g_app.wndH = MulDiv(210, dpi, 96);
    int cr = MulDiv(11, dpi, 96);
    g_app.closeRect = {g_app.wndW - cr - MulDiv(18, dpi, 96), MulDiv(18, dpi, 96),
                       g_app.wndW - MulDiv(18, dpi, 96), cr + MulDiv(18, dpi, 96)};
}

void PaintMainWindow(HWND hwnd, HDC hdc) {
    RECT rc; GetClientRect(hwnd, &rc);
    UINT dpi = GetDpiForWindow(hwnd);
    auto s = [&](int v) { return MulDiv(v, (int)dpi, 96); };

    Gdiplus::Graphics gfx(hdc);
    gfx.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    gfx.SetTextRenderingHint(Gdiplus::TextRenderingHintClearTypeGridFit);

    Gdiplus::SolidBrush bg(Gdiplus::Color(255, 255, 255, 255));
    gfx.FillRectangle(&bg, 0, 0, rc.right, rc.bottom);

    // 图标
    if (g_app.appIcon) {
        int isz = s(44);
        DrawIconEx(hdc, s(22), s(20), g_app.appIcon, isz, isz, 0, nullptr, DI_NORMAL);
    }
    // 标题
    float titleSize = (float)s(17) * 0.75f; // pt 换算
    Gdiplus::Font titleF(L"Segoe UI", titleSize);
    Gdiplus::SolidBrush dark(Gdiplus::Color(255, 26, 26, 26));
    Gdiplus::SolidBrush gray(Gdiplus::Color(255, 107, 114, 128));
    Gdiplus::SolidBrush body(Gdiplus::Color(255, 55, 65, 81));
    gfx.DrawString(L"QrSnap", -1, &titleF, Gdiplus::PointF((float)s(78), (float)s(20)), &dark);
    Gdiplus::Font subF(L"Segoe UI", (float)s(11) * 0.75f);
    gfx.DrawString(L"二维码截屏识别工具", -1, &subF, Gdiplus::PointF((float)s(78), (float)s(44)), &gray);

    // 分割线
    Gdiplus::Pen lineP(Gdiplus::Color(255, 234, 236, 239), 1.0f);
    gfx.DrawLine(&lineP, s(22), s(76), rc.right - s(22), s(76));

    // 正文
    Gdiplus::Font bodyF(L"Segoe UI", (float)s(12) * 0.75f);
    gfx.DrawString(L"按 Alt+Q 启动截屏，拖拽框选二维码区域", -1, &bodyF,
                   Gdiplus::PointF((float)s(22), (float)s(90)), &body);
    gfx.DrawString(L"双击、回车或点击\u201C识别\u201D按钮完成识别", -1, &bodyF,
                   Gdiplus::PointF((float)s(22), (float)s(112)), &body);
    gfx.DrawString(L"关闭窗口后程序驻留托盘，右键托盘图标可退出", -1, &subF,
                   Gdiplus::PointF((float)s(22), (float)s(142)), &gray);

    if (!g_app.hotkeyOk) {
        Gdiplus::SolidBrush red(Gdiplus::Color(255, 200, 60, 60));
        gfx.DrawString(L"热键注册失败：Alt+Q 可能被占用", -1, &subF,
                       Gdiplus::PointF((float)s(22), (float)s(164)), &red);
    }

    // 圆形关闭按钮
    bool hov = g_app.closeHover;
    Gdiplus::Color c = hov ? Gdiplus::Color(255, 229, 72, 77)
                           : Gdiplus::Color(255, 240, 242, 245);
    Gdiplus::SolidBrush cb(c);
    gfx.FillEllipse(&cb, g_app.closeRect.left, g_app.closeRect.top,
                    g_app.closeRect.right - g_app.closeRect.left,
                    g_app.closeRect.bottom - g_app.closeRect.top);
    Gdiplus::Pen xPen(hov ? Gdiplus::Color(255, 255, 255, 255)
                          : Gdiplus::Color(255, 120, 128, 140), 1.8f);
    int cx = (g_app.closeRect.left + g_app.closeRect.right) / 2;
    int cy = (g_app.closeRect.top + g_app.closeRect.bottom) / 2;
    int r = s(4);
    gfx.DrawLine(&xPen, cx - r, cy - r, cx + r, cy + r);
    gfx.DrawLine(&xPen, cx - r, cy + r, cx + r, cy - r);
}

LRESULT CALLBACK MainProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        HDC mem = CreateCompatibleDC(hdc);
        HBITMAP bmp = CreateCompatibleBitmap(hdc, g_app.wndW, g_app.wndH);
        HGDIOBJ old = SelectObject(mem, bmp);
        PaintMainWindow(hwnd, mem);
        BitBlt(hdc, 0, 0, g_app.wndW, g_app.wndH, mem, 0, 0, SRCCOPY);
        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        EndPaint(hwnd, &ps);
        return 0;
    }
    case WM_ERASEBKGND: return 1;
    case WM_NCHITTEST: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        ScreenToClient(hwnd, &pt);
        if (PtInRect(&g_app.closeRect, pt)) return HTCLIENT;
        return HTCAPTION; // 整窗拖拽
    }
    case WM_MOUSEMOVE: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        bool hov = PtInRect(&g_app.closeRect, pt) != 0;
        if (hov != g_app.closeHover) {
            g_app.closeHover = hov;
            InvalidateRect(hwnd, nullptr, FALSE);
            TRACKMOUSEEVENT tme = {sizeof(tme), TME_LEAVE, hwnd, 0};
            TrackMouseEvent(&tme);
        }
        SetCursor(hov ? LoadCursor(nullptr, IDC_HAND) : LoadCursor(nullptr, IDC_ARROW));
        return 0;
    }
    case WM_MOUSELEAVE:
        if (g_app.closeHover) { g_app.closeHover = false; InvalidateRect(hwnd, nullptr, FALSE); }
        return 0;
    case WM_LBUTTONDOWN: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        if (PtInRect(&g_app.closeRect, pt)) ShowWindow(hwnd, SW_HIDE);
        return 0;
    }
    case WM_HOTKEY:
        if (wParam == HOTKEY_ID) RunCapture();
        return 0;
    case WM_APP_TRAY: {
        if (lParam == WM_LBUTTONDBLCLK || lParam == WM_LBUTTONUP) {
            ShowWindow(hwnd, SW_SHOW);
            SetForegroundWindow(hwnd);
        } else if (lParam == WM_RBUTTONUP) {
            SetForegroundWindow(hwnd);
            HMENU m = CreatePopupMenu();
            AppendMenuW(m, MF_STRING, 1, L"打开主窗口");
            AppendMenuW(m, MF_SEPARATOR, 0, nullptr);
            AppendMenuW(m, MF_STRING, 2, L"退出");
            POINT pt; GetCursorPos(&pt);
            int cmd = TrackPopupMenu(m, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
                                     pt.x, pt.y, 0, hwnd, nullptr);
            DestroyMenu(m);
            if (cmd == 1) { ShowWindow(hwnd, SW_SHOW); SetForegroundWindow(hwnd); }
            else if (cmd == 2) {
                Shell_NotifyIconW(NIM_DELETE, &g_app.nid);
                UnregisterHotKey(hwnd, HOTKEY_ID);
                PostQuitMessage(0);
            }
        }
        return 0;
    }
    case WM_DESTROY:
        Shell_NotifyIconW(NIM_DELETE, &g_app.nid);
        if (g_app.hotkeyOk) UnregisterHotKey(hwnd, HOTKEY_ID);
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

// ---------- 入口 ----------
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR, int nCmdShow) {
    g_app.hInst = hInst;

    // 高 DPI 感知（物理像素直出，无缩放坑）
    if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        SetProcessDPIAware();

    Gdiplus::GdiplusStartupInput gsi;
    GdiplusStartup(&g_app.gdiplusToken, &gsi, nullptr);

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = MainProc;
    wc.hInstance = hInst;
    wc.lpszClassName = L"QrSnapMain";
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hIcon = LoadIconW(hInst, MAKEINTRESOURCEW(1));
    wc.hIconSm = wc.hIcon;
    RegisterClassExW(&wc);

    HWND hwnd = CreateWindowExW(
        WS_EX_APPWINDOW, L"QrSnapMain", L"QrSnap",
        WS_POPUP | WS_SYSMENU, CW_USEDEFAULT, CW_USEDEFAULT, 400, 210,
        nullptr, nullptr, hInst, nullptr);
    if (!hwnd) return 1;
    g_app.mainWnd = hwnd;

    LayoutMainWindow(hwnd);
    SetWindowPos(hwnd, nullptr, 0, 0, g_app.wndW, g_app.wndH,
                 SWP_NOMOVE | SWP_NOZORDER);

    // Win11 圆角
    DWM_WINDOW_CORNER_PREFERENCE pref = DWMWCP_ROUND;
    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &pref, sizeof(pref));

    // 托盘
    g_app.appIcon = (HICON)LoadImageW(hInst, MAKEINTRESOURCEW(1), IMAGE_ICON,
                               GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON),
                               LR_DEFAULTCOLOR);
    if (!g_app.appIcon) g_app.appIcon = LoadIconW(nullptr, IDI_APPLICATION);
    g_app.nid.cbSize = sizeof(g_app.nid);
    g_app.nid.hWnd = hwnd;
    g_app.nid.uID = 1;
    g_app.nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_app.nid.uCallbackMessage = WM_APP_TRAY;
    g_app.nid.hIcon = g_app.appIcon;
    lstrcpyW(g_app.nid.szTip, L"QrSnap - Alt+Q 截屏识别二维码");
    Shell_NotifyIconW(NIM_ADD, &g_app.nid);

    // 全局热键 Alt+Q
    g_app.hotkeyOk = RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, 'Q') != 0;

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    Gdiplus::GdiplusShutdown(g_app.gdiplusToken);
    return (int)msg.wParam;
}
