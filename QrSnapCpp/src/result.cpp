#include "common.h"
#include <cstdio>

// ================= 结果窗 =================
// 圆角卡片：图标 + 类型标题 + 内容 + 操作按钮（打开/复制/知道了）
// 激活状态下失焦自动关闭，10 秒兜底自动关闭。

namespace {

constexpr COLORREF kAccent = RGB(46, 124, 246);

struct ResultState {
    HWND hwnd = nullptr;
    ParsedContent content;
    bool error = false;
    bool copied = false;
    bool openHover = false, copyHover = false;
    RECT openRect{}, copyRect{};
    int  w = 380, h = 200;
    std::vector<std::wstring> lines;
    std::wstring openUrl;
} g;

Gdiplus::Color KindColor(ContentKind k, bool error) {
    if (error) return Gdiplus::Color(255, 226, 75, 74);
    switch (k) {
        case ContentKind::Url:   return Gdiplus::Color(255, 55, 138, 221);
        case ContentKind::Wifi:  return Gdiplus::Color(255, 99, 153, 34);
        case ContentKind::VCard: return Gdiplus::Color(255, 127, 119, 221);
        case ContentKind::Email: return Gdiplus::Color(255, 239, 159, 39);
        case ContentKind::Phone: return Gdiplus::Color(255, 29, 158, 117);
        default:                 return Gdiplus::Color(255, 136, 135, 128);
    }
}

const wchar_t* KindLetter(ContentKind k, bool error) {
    if (error) return L"!";
    switch (k) {
        case ContentKind::Url:   return L"U";
        case ContentKind::Wifi:  return L"W";
        case ContentKind::VCard: return L"V";
        case ContentKind::Email: return L"@";
        case ContentKind::Phone: return L"#";
        default:                 return L"T";
    }
}

void Layout() {
    g.w = 380;
    const int maxLines = 9;
    if (!g.error && g.content.kind == ContentKind::Url)
        g.openUrl = g.content.text;
    int n = (int)g.lines.size();
    if (n > maxLines) n = maxLines;
    int bodyH = n * 21;
    int btnH = 36;
    g.h = 18 + 40 + 10 + bodyH + 14 + btnH + 18;
    if (g.h < 150) g.h = 150;
    if (g.h > 360) g.h = 360;
}

void DrawButton(Gdiplus::Graphics& g, const RECT& rc, const wchar_t* text,
                bool hover, bool primary, bool pressed) {
    Gdiplus::Font font(L"Segoe UI", 13.0f);
    Gdiplus::Color bg = primary
        ? Gdiplus::Color(255, hover ? 90u : 46u, hover ? 152u : 124u, hover ? 250u : 246u)
        : Gdiplus::Color(255, (BYTE)(hover ? 225 : 241), (BYTE)(hover ? 228 : 243), (BYTE)(hover ? 232 : 245));
    FillRoundedRect(g, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top, 17, bg);
    Gdiplus::SolidBrush textBrush(primary ? Gdiplus::Color(255, 255, 255, 255)
                                          : Gdiplus::Color(255, 55, 65, 81));
    Gdiplus::StringFormat sf;
    sf.SetAlignment(Gdiplus::StringAlignmentCenter);
    sf.SetLineAlignment(Gdiplus::StringAlignmentCenter);
    g.DrawString(text, -1, &font, Gdiplus::RectF((float)rc.left, (float)rc.top - 1,
                 (float)(rc.right - rc.left), (float)(rc.bottom - rc.top)), &sf, &textBrush);
}

void Paint(HDC hdc) {
    Gdiplus::Graphics gfx(hdc);
    gfx.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    gfx.SetTextRenderingHint(Gdiplus::TextRenderingHintClearTypeGridFit);

    Gdiplus::SolidBrush bg(Gdiplus::Color(255, 255, 255, 255));
    gfx.FillRectangle(&bg, 0, 0, g.w, g.h);

    int pad = 20;
    // 头部：圆形图标 + 类型标题
    auto kc = KindColor(g.content.kind, g.error);
    Gdiplus::SolidBrush iconBrush(kc);
    gfx.FillEllipse(&iconBrush, pad, 18, 36, 36);
    Gdiplus::SolidBrush iconText(Gdiplus::Color(255, 255, 255, 255));
    Gdiplus::Font iconFont(L"Segoe UI", 14.0f);
    Gdiplus::StringFormat cf;
    cf.SetAlignment(Gdiplus::StringAlignmentCenter);
    cf.SetLineAlignment(Gdiplus::StringAlignmentCenter);
    const wchar_t* letter = KindLetter(g.content.kind, g.error);
    gfx.DrawString(letter, -1, &iconFont, Gdiplus::RectF((float)pad, 17.0f, 36.0f, 36.0f), &cf, &iconText);

    Gdiplus::Font titleFont(L"Segoe UI", 13.5f);
    Gdiplus::SolidBrush titleBrush(Gdiplus::Color(255, 107, 114, 128));
    const wchar_t* title = g.error ? L"识别失败" : g.content.title.c_str();
    gfx.DrawString(title, -1, &titleFont, Gdiplus::PointF((float)pad + 46, 24.0f), &titleBrush);

    // 内容
    Gdiplus::Font bodyFont(L"Segoe UI", 13.5f);
    Gdiplus::SolidBrush bodyBrush(g.error ? Gdiplus::Color(255, 163, 45, 45)
                                          : Gdiplus::Color(255, 31, 41, 55));
    float y = 68.0f;
    int maxLines = 9;
    int n = (int)g.lines.size();
    for (int i = 0; i < n && i < maxLines; i++) {
        gfx.DrawString(g.lines[i].c_str(), -1, &bodyFont, Gdiplus::PointF((float)pad, y), &bodyBrush);
        y += 21;
    }

    // 按钮行（右对齐）
    int btnH = 36, btnW = 76, gap = 10;
    int by = g.h - btnH - 18;
    int bx = g.w - pad - btnW;
    g.copyRect = {bx, by, bx + btnW, by + btnH};
    const wchar_t* copyText = g.copied ? L"已复制" : L"复制";
    DrawButton(gfx, g.copyRect, copyText, g.copyHover, false, false);
    if (!g.error && !g.openUrl.empty()) {
        bx -= btnW + gap;
        g.openRect = {bx, by, bx + btnW, by + btnH};
        DrawButton(gfx, g.openRect, L"打开", g.openHover, true, false);
    } else {
        g.openRect = {};
    }
}

void DoCopy() {
    if (CopyToClipboard(g.hwnd, g.content.text)) {
        g.copied = true;
        InvalidateRect(g.hwnd, nullptr, FALSE);
        SetTimer(g.hwnd, 2, 900, nullptr);
    }
}

void DoOpen() {
    if (!g.openUrl.empty()) {
        ShellExecuteW(g.hwnd, L"open", g.openUrl.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }
    DestroyWindow(g.hwnd);
}

LRESULT CALLBACK ResultProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_ERASEBKGND: return 1;
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        // 双缓冲防闪烁
        HDC mem = CreateCompatibleDC(hdc);
        HBITMAP bmp = CreateCompatibleBitmap(hdc, g.w, g.h);
        HGDIOBJ old = SelectObject(mem, bmp);
        Paint(mem);
        BitBlt(hdc, 0, 0, g.w, g.h, mem, 0, 0, SRCCOPY);
        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        EndPaint(hwnd, &ps);
        return 0;
    }
    case WM_MOUSEMOVE: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        bool oh = PtInRect(&g.openRect, pt) != 0;
        bool ch = PtInRect(&g.copyRect, pt) != 0;
        if (oh != g.openHover || ch != g.copyHover) {
            g.openHover = oh; g.copyHover = ch;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        SetCursor(oh || ch ? LoadCursor(nullptr, IDC_HAND) : LoadCursor(nullptr, IDC_ARROW));
        return 0;
    }
    case WM_LBUTTONDOWN: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        if (PtInRect(&g.openRect, pt)) DoOpen();
        else if (PtInRect(&g.copyRect, pt)) DoCopy();
        return 0;
    }
    case WM_ACTIVATE:
        if (LOWORD(wParam) == WA_INACTIVE) DestroyWindow(hwnd);
        return 0;
    case WM_TIMER:
        if (wParam == 1) DestroyWindow(hwnd);        // 10s 兜底
        else if (wParam == 2) {                      // "已复制"恢复
            KillTimer(hwnd, 2);
            g.copied = false;
            InvalidateRect(hwnd, nullptr, FALSE);
        }
        return 0;
    case WM_KEYDOWN:
        if (wParam == VK_ESCAPE) DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY:
        g.hwnd = nullptr;
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

HWND CreateResult(const ParsedContent* content, const wchar_t* errorMsg, POINT anchor) {
    static bool registered = false;
    if (!registered) {
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = ResultProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"QrSnapResult";
        wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
        RegisterClassExW(&wc);
        registered = true;
    }

    g = ResultState{};
    if (content) {
        g.content = *content;
        // 先建窗口才能拿 Graphics 折行 → 这里用屏幕 DC 折行即可
        HDC sdc = GetDC(nullptr);
        Gdiplus::Graphics grp(sdc);
        Gdiplus::Font bodyFont(L"Segoe UI", 13.5f);
        g.lines = WrapText(grp, g.content.text, bodyFont, (float)(380 - 40), 12);
        ReleaseDC(nullptr, sdc);
    } else {
        g.error = true;
        g.content.kind = ContentKind::Text;
        g.content.title = L"识别失败";
        g.content.text = errorMsg;
        HDC sdc = GetDC(nullptr);
        Gdiplus::Graphics grp(sdc);
        Gdiplus::Font bodyFont(L"Segoe UI", 13.5f);
        g.lines = WrapText(grp, errorMsg, bodyFont, (float)(380 - 40), 12);
        ReleaseDC(nullptr, sdc);
    }
    Layout();

    // 定位：锚点右下方，夹在锚点所在显示器工作区内
    HMONITOR mon = MonitorFromPoint(anchor, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = {};
    mi.cbSize = sizeof(mi);
    GetMonitorInfoW(mon, &mi);
    int x = anchor.x + 12, y = anchor.y + 12;
    if (x + g.w > mi.rcWork.right)  x = mi.rcWork.right - g.w;
    if (y + g.h > mi.rcWork.bottom) y = mi.rcWork.bottom - g.h;
    if (x < mi.rcWork.left)  x = mi.rcWork.left;
    if (y < mi.rcWork.top)   y = mi.rcWork.top;

    HWND hwnd = CreateWindowExW(
        WS_EX_TOPMOST | WS_EX_TOOLWINDOW, L"QrSnapResult", L"",
        WS_POPUP, x, y, g.w, g.h, nullptr, nullptr,
        GetModuleHandleW(nullptr), nullptr);
    if (!hwnd) return nullptr;
    g.hwnd = hwnd;

    // Win11 圆角
    DWM_WINDOW_CORNER_PREFERENCE pref = DWMWCP_ROUND;
    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &pref, sizeof(pref));

    ShowWindow(hwnd, SW_SHOW);
    SetForegroundWindow(hwnd);
    SetFocus(hwnd);
    SetTimer(hwnd, 1, 10000, nullptr);
    return hwnd;
}

} // namespace

HWND ShowResultWindow(const ParsedContent& content, POINT anchor) {
    return CreateResult(&content, nullptr, anchor);
}

HWND ShowResultError(const std::wstring& msg, POINT anchor) {
    return CreateResult(nullptr, msg.c_str(), anchor);
}
