#include "common.h"
#include <windowsx.h>
#include <cstdio>

// ================= 框选覆盖层 =================
// 全屏窗口显示抓取的静态截图，支持拖拽框选/移动/8 手柄缩放，
// 双击/回车/点击"识别"按钮确认，Esc/右键取消。

namespace {

constexpr COLORREF kAccent = RGB(46, 124, 246);   // #2E7CF6

struct HandlePt { int x, y; };

struct OverlayState {
    HWND hwnd = nullptr;
    const CapturedScreen* cap = nullptr;
    std::function<void(const RECT&)> onConfirm;
    std::function<void()> onCancel;

    RECT  sel{};            // 选区（客户区坐标 == 虚拟桌面相对物理坐标）
    bool  hasSel = false;
    bool  dragging = false; // 正在新建选区
    bool  moving = false;   // 正在整体移动
    int   resizeIdx = -1;   // 正在缩放 0..7
    POINT startPt{}, curPt{}, moveGrab{};
    RECT  moveOrig{};

    RECT  btnRect{};        // "识别"按钮
    bool  btnHover = false;
} g;

// ---------- 几何 ----------
RECT Normalize(POINT a, POINT b) {
    RECT r;
    r.left   = a.x < b.x ? a.x : b.x;
    r.right  = a.x < b.x ? b.x : a.x;
    r.top    = a.y < b.y ? a.y : b.y;
    r.bottom = a.y < b.y ? b.y : a.y;
    return r;
}

void GetHandles(HandlePt out[8]) {
    int mx = (g.sel.left + g.sel.right) / 2;
    int my = (g.sel.top + g.sel.bottom) / 2;
    out[0] = {g.sel.left,  g.sel.top};
    out[1] = {mx,          g.sel.top};
    out[2] = {g.sel.right, g.sel.top};
    out[3] = {g.sel.right, my};
    out[4] = {g.sel.right, g.sel.bottom};
    out[5] = {mx,          g.sel.bottom};
    out[6] = {g.sel.left,  g.sel.bottom};
    out[7] = {g.sel.left,  my};
}

int HitHandle(POINT pt) {
    if (!g.hasSel) return -1;
    HandlePt hs[8]; GetHandles(hs);
    for (int i = 0; i < 8; i++) {
        if (abs(pt.x - hs[i].x) <= 6 && abs(pt.y - hs[i].y) <= 6) return i;
    }
    return -1;
}

bool PtInSel(POINT pt) {
    return g.hasSel && pt.x >= g.sel.left && pt.x <= g.sel.right &&
           pt.y >= g.sel.top  && pt.y <= g.sel.bottom;
}

void UpdateSelByResize(int idx, POINT pt) {
    // 固定对角点
    int ox, oy;
    switch (idx) {
        case 0: ox = g.sel.right; oy = g.sel.bottom; break;
        case 1: ox = g.sel.right; oy = g.sel.top;    break;
        case 2: ox = g.sel.left;  oy = g.sel.bottom; break;
        case 3: ox = g.sel.left;  oy = g.sel.top;    break;
        case 4: ox = g.sel.left;  oy = g.sel.top;    break;
        case 5: ox = g.sel.left;  oy = g.sel.top;    break;
        case 6: ox = g.sel.right; oy = g.sel.top;    break;
        default: ox = g.sel.right; oy = g.sel.bottom; break;
    }
    g.sel = Normalize({ox, oy}, pt);
}

// ---------- 确认 / 取消 ----------
void Confirm() {
    if (!g.hasSel) return;
    int w = g.sel.right - g.sel.left, h = g.sel.bottom - g.sel.top;
    if (w < 20 || h < 20) return;
    RECT r = g.sel;
    auto cb = g.onConfirm;
    if (g.hwnd) DestroyWindow(g.hwnd);
    if (cb) cb(r);
}

void Cancel() {
    auto cb = g.onCancel;
    if (g.hwnd) DestroyWindow(g.hwnd);
    if (cb) cb();
}

// ---------- 绘制 ----------
void DrawGdiplus(HDC hdc) {
    Gdiplus::Graphics gfx(hdc);
    gfx.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    gfx.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAlias);

    const CapturedScreen& cap = *g.cap;

    // 暗色遮罩
    Gdiplus::SolidBrush dim(Gdiplus::Color(110, 0, 0, 0));
    gfx.FillRectangle(&dim, 0, 0, cap.w, cap.h);

    Gdiplus::Font font(L"Segoe UI", 13.0f);
    Gdiplus::Font smallFont(L"Segoe UI", 11.5f);
    Gdiplus::SolidBrush white(Gdiplus::Color(255, 255, 255, 255));

    if (g.hasSel) {
        // 三分线
        Gdiplus::Pen thirds(Gdiplus::Color(70, 255, 255, 255), 1.0f);
        int w = g.sel.right - g.sel.left, h = g.sel.bottom - g.sel.top;
        for (int i = 1; i <= 2; i++) {
            int x = g.sel.left + w * i / 3, y = g.sel.top + h * i / 3;
            gfx.DrawLine(&thirds, x, g.sel.top, x, g.sel.bottom);
            gfx.DrawLine(&thirds, g.sel.left, y, g.sel.right, y);
        }
        // 边框
        Gdiplus::Pen border(Gdiplus::Color(255, 46, 124, 246), 2.0f);
        gfx.DrawRectangle(&border, g.sel.left, g.sel.top, w, h);
        // 8 手柄
        HandlePt hs[8]; GetHandles(hs);
        Gdiplus::SolidBrush hFill(Gdiplus::Color(255, 255, 255, 255));
        Gdiplus::Pen hBorder(Gdiplus::Color(255, 46, 124, 246), 1.6f);
        for (auto& p : hs) {
            gfx.FillRectangle(&hFill, p.x - 5, p.y - 5, 10, 10);
            gfx.DrawRectangle(&hBorder, p.x - 5, p.y - 5, 10, 10);
        }

        // 尺寸提示（选区左上角上方）
        wchar_t sizeTxt[64];
        swprintf(sizeTxt, 64, L"%d × %d", w, h);
        Gdiplus::RectF sb;
        gfx.MeasureString(sizeTxt, -1, &smallFont, Gdiplus::PointF(0, 0), &sb);
        int capW = (int)sb.Width + 20, capH = 22;
        int cx = g.sel.left, cy = g.sel.top - capH - 6;
        if (cy < 2) cy = g.sel.top + 6;
        FillRoundedRect(gfx, cx, cy, capW, capH, 11, Gdiplus::Color(230, 24, 26, 34));
        gfx.DrawString(sizeTxt, -1, &smallFont,
                     Gdiplus::PointF((float)cx + 10, (float)cy + 2), &white);

        // 提示胶囊 + "识别"按钮（选区下方，放不下则上方）
        const wchar_t* hintText = L"双击 / 回车识别";
        Gdiplus::RectF hb;
        gfx.MeasureString(hintText, -1, &font, Gdiplus::PointF(0, 0), &hb);
        int btnW = 76, btnH = 34, gap = 8, pad = 14;
        int textW = (int)hb.Width;
        int totalW = pad + textW + pad + gap + btnW;
        int bx = (g.sel.left + g.sel.right) / 2 - totalW / 2;
        int by = g.sel.bottom + 14;
        if (by + btnH > cap.h - 4) by = g.sel.top - btnH - 14;
        if (bx < 4) bx = 4;
        if (bx + totalW > cap.w - 4) bx = cap.w - 4 - totalW;
        if (by < 4) by = 4;

        FillRoundedRect(gfx, bx, by, pad * 2 + textW + gap + btnW - btnW, btnH, 17,
                        Gdiplus::Color(230, 24, 26, 34));
        gfx.DrawString(hintText, -1, &font,
                     Gdiplus::PointF((float)bx + pad, (float)by + 5), &white);

        int btnX = bx + pad + textW + gap;
        g.btnRect = {btnX, by, btnX + btnW, by + btnH};
        Gdiplus::Color btnC = g.btnHover ? Gdiplus::Color(255, 90, 152, 250)
                                         : Gdiplus::Color(255, 46, 124, 246);
        FillRoundedRect(gfx, btnX, by, btnW, btnH, 17, btnC);
        Gdiplus::StringFormat sf;
        sf.SetAlignment(Gdiplus::StringAlignmentCenter);
        sf.SetLineAlignment(Gdiplus::StringAlignmentCenter);
        gfx.DrawString(L"识别", -1, &font, Gdiplus::RectF((float)btnX, (float)by - 1,
                     (float)btnW, (float)btnH), &sf, &white);
    } else {
        // 未选区：屏幕上方居中提示
        const wchar_t* hintText = L"拖拽框选二维码区域 · 双击或回车识别 · Esc 取消";
        Gdiplus::RectF hb;
        gfx.MeasureString(hintText, -1, &font, Gdiplus::PointF(0, 0), &hb);
        int capW = (int)hb.Width + 36, capH = 38;
        int bx = (cap.w - capW) / 2, by = 24;
        g.btnRect = {};
        FillRoundedRect(gfx, bx, by, capW, capH, 19, Gdiplus::Color(230, 24, 26, 34));
        gfx.DrawString(hintText, -1, &font,
                     Gdiplus::PointF((float)bx + 18, (float)by + 7), &white);
    }
}

void Paint(HDC hdc) {
    const CapturedScreen& cap = *g.cap;
    HDC mem = CreateCompatibleDC(hdc);
    HGDIOBJ oldBmp = SelectObject(mem, cap.bitmap);
    BitBlt(hdc, 0, 0, cap.w, cap.h, mem, 0, 0, SRCCOPY);
    if (g.hasSel) {
        // 恢复选区内原亮度：直接重贴对应区域
        BitBlt(hdc, g.sel.left, g.sel.top,
               g.sel.right - g.sel.left, g.sel.bottom - g.sel.top,
               mem, g.sel.left, g.sel.top, SRCCOPY);
    }
    SelectObject(mem, oldBmp);
    DeleteDC(mem);
    DrawGdiplus(hdc);
}

// ---------- 光标 ----------
void UpdateCursor(POINT pt) {
    if (!g.btnRect.right) { /* fallthrough */ }
    if (PtInRect(&g.btnRect, pt))                 SetCursor(LoadCursor(nullptr, IDC_HAND));
    else if (HitHandle(pt) >= 0)                  SetCursor(LoadCursor(nullptr, IDC_SIZEALL));
    else if (PtInSel(pt))                         SetCursor(LoadCursor(nullptr, IDC_SIZEALL));
    else                                          SetCursor(LoadCursor(nullptr, IDC_CROSS));
}

// ---------- 窗口过程 ----------
LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_ERASEBKGND: return 1;
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        Paint(hdc);
        EndPaint(hwnd, &ps);
        return 0;
    }
    case WM_SETCURSOR: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        UpdateCursor(pt);
        return TRUE;
    }
    case WM_LBUTTONDOWN: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        if (PtInRect(&g.btnRect, pt)) { Confirm(); return 0; }
        SetCapture(hwnd);
        int h = HitHandle(pt);
        if (h >= 0)              { g.resizeIdx = h; }
        else if (PtInSel(pt))    { g.moving = true; g.moveOrig = g.sel;
                                   g.moveGrab = {pt.x - g.sel.left, pt.y - g.sel.top}; }
        else                     { g.dragging = true; g.startPt = pt; g.hasSel = false; }
        return 0;
    }
    case WM_MOUSEMOVE: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        g.curPt = pt;
        bool btnHover = PtInRect(&g.btnRect, pt) != 0;
        bool invalidate = btnHover != g.btnHover;
        g.btnHover = btnHover;
        if (g.dragging) {
            g.sel = Normalize(g.startPt, pt);
            g.hasSel = (g.sel.right - g.sel.left >= 8) && (g.sel.bottom - g.sel.top >= 8);
        } else if (g.resizeIdx >= 0) {
            UpdateSelByResize(g.resizeIdx, pt);
        } else if (g.moving) {
            int w = g.moveOrig.right - g.moveOrig.left;
            int hh = g.moveOrig.bottom - g.moveOrig.top;
            int nx = pt.x - g.moveGrab.x, ny = pt.y - g.moveGrab.y;
            const CapturedScreen& cap = *g.cap;
            if (nx < 0) nx = 0; if (ny < 0) ny = 0;
            if (nx + w > cap.w) nx = cap.w - w;
            if (ny + hh > cap.h) ny = cap.h - hh;
            g.sel = {nx, ny, nx + w, ny + hh};
        }
        UpdateCursor(pt);
        invalidate = true;
        if (invalidate) InvalidateRect(hwnd, nullptr, FALSE);
        return 0;
    }
    case WM_LBUTTONUP: {
        g.dragging = false; g.moving = false; g.resizeIdx = -1;
        if (GetCapture() == hwnd) ReleaseCapture();
        return 0;
    }
    case WM_LBUTTONDBLCLK: {
        POINT pt{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
        if (PtInSel(pt)) Confirm();
        return 0;
    }
    case WM_RBUTTONDOWN:
        Cancel();
        return 0;
    case WM_KEYDOWN:
        if (wParam == VK_RETURN) Confirm();
        else if (wParam == VK_ESCAPE) Cancel();
        return 0;
    case WM_DESTROY:
        g.hwnd = nullptr;
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

} // namespace

HWND ShowCaptureOverlay(const CapturedScreen& cap,
                        std::function<void(const RECT&)> onConfirm,
                        std::function<void()> onCancel) {
    static bool registered = false;
    if (!registered) {
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = OverlayProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"QrSnapOverlay";
        wc.hCursor = LoadCursor(nullptr, IDC_CROSS);
        RegisterClassExW(&wc);
        registered = true;
    }

    g = OverlayState{};
    g.cap = &cap;
    g.onConfirm = std::move(onConfirm);
    g.onCancel = std::move(onCancel);

    HWND hwnd = CreateWindowExW(
        WS_EX_TOPMOST | WS_EX_TOOLWINDOW, L"QrSnapOverlay", L"",
        WS_POPUP, cap.x, cap.y, cap.w, cap.h, nullptr, nullptr,
        GetModuleHandleW(nullptr), nullptr);
    if (!hwnd) return nullptr;
    g.hwnd = hwnd;

    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    ShowWindow(hwnd, SW_SHOW);
    SetForegroundWindow(hwnd);
    SetFocus(hwnd); // 接收回车/Esc
    return hwnd;
}
