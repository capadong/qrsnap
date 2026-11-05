#include "common.h"
#include <cstdio>

// ================= UTF-8 转换 =================
std::wstring Utf8ToWide(const char* s) {
    if (!s) return L"";
    int len = (int)strlen(s);
    if (len <= 0) return L"";
    int wl = MultiByteToWideChar(CP_UTF8, 0, s, len, nullptr, 0);
    std::wstring out(wl, 0);
    MultiByteToWideChar(CP_UTF8, 0, s, len, &out[0], wl);
    return out;
}

// ================= 剪贴板 =================
bool CopyToClipboard(HWND hwnd, const std::wstring& text) {
    if (!OpenClipboard(hwnd)) return false;
    EmptyClipboard();
    size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    HGLOBAL h = GlobalAlloc(GMEM_MOVEABLE, bytes);
    if (!h) { CloseClipboard(); return false; }
    void* p = GlobalLock(h);
    memcpy(p, text.c_str(), bytes);
    GlobalUnlock(h);
    SetClipboardData(CF_UNICODETEXT, h);
    CloseClipboard();
    return true;
}

// ================= GDI+ 绘图助手 =================
Gdiplus::GraphicsPath* RoundedRectPath(int x, int y, int w, int h, int radius) {
    auto* path = new Gdiplus::GraphicsPath();
    if (radius <= 0 || w <= 0 || h <= 0) { path->AddRectangle(Gdiplus::Rect(x, y, w, h)); return path; }
    int r = radius; if (r * 2 > w) r = w / 2; if (r * 2 > h) r = h / 2;
    int d = r * 2;
    path->AddArc(x, y, d, d, 180, 90);
    path->AddArc(x + w - d, y, d, d, 270, 90);
    path->AddArc(x + w - d, y + h - d, d, d, 0, 90);
    path->AddArc(x, y + h - d, d, d, 90, 90);
    path->CloseFigure();
    return path;
}

void FillRoundedRect(Gdiplus::Graphics& g, int x, int y, int w, int h, int radius,
                     const Gdiplus::Color& c) {
    Gdiplus::SolidBrush b(c);
    auto* path = RoundedRectPath(x, y, w, h, radius);
    g.FillPath(&b, path);
    delete path;
}

// 逐字符折行（中英文混排通用），最多 maxLines 行，超出时末行加省略号
std::vector<std::wstring> WrapText(Gdiplus::Graphics& g, const std::wstring& text,
                                   Gdiplus::Font& font, float maxW, int maxLines) {
    std::vector<std::wstring> lines;
    std::wstring cur;
    Gdiplus::RectF bound;
    auto width = [&](const std::wstring& s) {
        g.MeasureString(s.c_str(), (INT)s.size(), &font, Gdiplus::PointF(0, 0), &bound);
        return bound.Width;
    };
    for (wchar_t ch : text) {
        if (ch == L'\n') {
            lines.push_back(cur); cur.clear();
            if ((int)lines.size() >= maxLines) break;
            continue;
        }
        std::wstring next = cur + ch;
        if (!cur.empty() && width(next) > maxW) {
            lines.push_back(cur);
            if ((int)lines.size() >= maxLines) { break; }
            cur.clear(); cur += ch;
        } else {
            cur = next;
        }
    }
    if ((int)lines.size() < maxLines && !cur.empty()) lines.push_back(cur);
    if ((int)lines.size() >= maxLines) {
        // 截断：确保最后一行带省略号
        while ((int)lines.size() > maxLines) lines.pop_back();
        std::wstring& last = lines.back();
        while (last.size() > 1 && width(last + L"…") > maxW) last.pop_back();
        last += L"…";
    }
    if (lines.empty()) lines.push_back(L"");
    return lines;
}
