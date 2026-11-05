#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#define NOMINMAX
#include <windows.h>
#include <windowsx.h>    // GET_X_LPARAM / GET_Y_LPARAM
#include <shellapi.h>
#include <dwmapi.h>      // Win11 圆角
#include <objidl.h>      // IStream, GDI+ 依赖
#include <gdiplus.h>
#include <string>
#include <vector>
#include <functional>

// ================= 共享截屏数据 =================
struct CapturedScreen {
    int  x = 0, y = 0, w = 0, h = 0;   // 虚拟桌面原点与尺寸（物理像素）
    std::vector<BYTE> pixels;          // BGRA 自顶向下, w*h*4
    HBITMAP bitmap = nullptr;          // 与屏幕 DC 兼容的位图（用于覆盖层背景）
    bool valid = false;
};

// ================= 内容解析 =================
enum class ContentKind { Url, Wifi, VCard, Email, Phone, Text };

struct ParsedContent {
    ContentKind kind = ContentKind::Text;
    std::wstring title;   // 网址 / Wi-Fi / 联系人 / 邮箱 / 电话 / 文本
    std::wstring text;    // 内容
};

struct DecodeResult { bool ok = false; std::wstring text; };

// 解码虚拟桌面坐标（物理像素）区域，cap 为已抓取的全屏数据
DecodeResult DecodeRegion(const CapturedScreen& cap, const RECT& physicalRect);
ParsedContent ParseContent(const std::wstring& raw);
std::wstring  Utf8ToWide(const char* s);

// ================= 覆盖层（框选） =================
// 确认回调返回选区（覆盖层客户区坐标 == 虚拟桌面相对物理坐标，可直接索引 cap.pixels）
HWND ShowCaptureOverlay(const CapturedScreen& cap,
                        std::function<void(const RECT&)> onConfirm,
                        std::function<void()> onCancel);

// ================= 结果窗 =================
HWND ShowResultWindow(const ParsedContent& content, POINT anchor);
HWND ShowResultError(const std::wstring& msg, POINT anchor);

// ================= 工具 =================
bool CopyToClipboard(HWND hwnd, const std::wstring& text);
Gdiplus::GraphicsPath* RoundedRectPath(int x, int y, int w, int h, int radius);
void FillRoundedRect(Gdiplus::Graphics& g, int x, int y, int w, int h, int radius,
                     const Gdiplus::Color& c);
std::vector<std::wstring> WrapText(Gdiplus::Graphics& g, const std::wstring& text,
                                   Gdiplus::Font& font, float maxW, int maxLines);
