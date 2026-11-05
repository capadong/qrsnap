#include "common.h"
extern "C" {
#include "../quirc/quirc.h"
}
#include <cmath>

// ================= 内容解析 =================
static bool IsEmailLike(const std::wstring& s) {
    size_t at = s.find(L'@');
    if (at == std::wstring::npos || at == 0 || at + 1 >= s.size()) return false;
    if (s.find_first_of(L" \t") != std::wstring::npos) return false;
    return s.find(L'.', at) != std::wstring::npos;
}

ParsedContent ParseContent(const std::wstring& rawIn) {
    std::wstring raw = rawIn;
    // 去首尾空白
    size_t b = raw.find_first_not_of(L" \t\r\n");
    size_t e = raw.find_last_not_of(L" \t\r\n");
    raw = (b == std::wstring::npos) ? L"" : raw.substr(b, e - b + 1);

    ParsedContent p;
    p.text = raw;
    if (raw.rfind(L"BEGIN:VCARD", 0) == 0) {
        p.kind = ContentKind::VCard; p.title = L"联系人";
        return p;
    }
    if (raw.rfind(L"WIFI:", 0) == 0) {
        p.kind = ContentKind::Wifi; p.title = L"Wi-Fi";
        return p;
    }
    if (raw.rfind(L"http://", 0) == 0 || raw.rfind(L"https://", 0) == 0) {
        p.kind = ContentKind::Url; p.title = L"网址";
        return p;
    }
    if (raw.rfind(L"www.", 0) == 0) {
        p.kind = ContentKind::Url; p.title = L"网址";
        p.text = L"https://" + raw;
        return p;
    }
    if (raw.rfind(L"mailto:", 0) == 0) {
        p.kind = ContentKind::Email; p.title = L"邮箱";
        return p;
    }
    if (raw.rfind(L"tel:", 0) == 0) {
        p.kind = ContentKind::Phone; p.title = L"电话";
        return p;
    }
    if (IsEmailLike(raw) && raw.size() < 254) {
        p.kind = ContentKind::Email; p.title = L"邮箱";
        return p;
    }
    // 纯数字（可能含 + - 空格）当电话
    bool digits = !raw.empty() && raw.size() <= 20;
    for (wchar_t c : raw) {
        if (!(c >= L'0' && c <= L'9') && c != L'+' && c != L'-' && c != L' ' ) { digits = false; break; }
    }
    if (digits) { p.kind = ContentKind::Phone; p.title = L"电话"; return p; }

    p.kind = ContentKind::Text; p.title = L"文本";
    return p;
}

// ================= quirc 解码 =================
static bool TryDecode(const BYTE* bgra, int w, int h, int stride, std::wstring& out) {
    struct quirc* q = quirc_new();
    if (!q) return false;
    bool ok = false;
    if (quirc_resize(q, w, h) >= 0) {
        int qw, qh;
        uint8_t* buf = quirc_begin(q, &qw, &qh);
        if (buf) {
            // BGRA -> 灰度（取 G 通道，速度与质量兼顾）
            for (int y = 0; y < h; y++) {
                const BYTE* src = bgra + (size_t)y * stride;
                uint8_t* dst = buf + (size_t)y * qw;
                for (int x = 0; x < w; x++) dst[x] = src[x * 4 + 1];
            }
            quirc_end(q);
            int n = quirc_count(q);
            for (int i = 0; i < n && !ok; i++) {
                struct quirc_code code;
                struct quirc_data data;
                quirc_decode_error_t err;
                quirc_extract(q, i, &code);
                err = quirc_decode(&code, &data);
                if (!err) { out = Utf8ToWide((const char*)data.payload); ok = !out.empty(); }
            }
        }
    }
    quirc_destroy(q);
    return ok;
}

DecodeResult DecodeRegion(const CapturedScreen& cap, const RECT& physicalRect) {
    DecodeResult r;
    int rw = physicalRect.right - physicalRect.left;
    int rh = physicalRect.bottom - physicalRect.top;
    if (!cap.valid || rw < 20 || rh < 20) return r;
    if (physicalRect.left < 0 || physicalRect.top < 0 ||
        physicalRect.right > cap.w || physicalRect.bottom > cap.h) return r;

    // 提取区域灰度图
    std::vector<BYTE> gray((size_t)rw * rh);
    for (int y = 0; y < rh; y++) {
        const BYTE* src = cap.pixels.data() +
            ((size_t)(physicalRect.top + y) * cap.w + physicalRect.left) * 4;
        BYTE* dst = gray.data() + (size_t)y * rw;
        for (int x = 0; x < rw; x++) dst[x] = src[x * 4 + 1];
    }

    // 多轮重试：原图 -> 放大 2x -> 放大 3x（小目标）
    int scales[3] = {1, 0, 0};
    if (rw < 600 || rh < 600) scales[1] = 2;
    if (rw < 300 || rh < 300) scales[2] = 3;

    for (int si = 0; si < 3; si++) {
        int sc = scales[si];
        if (!sc) continue;
        if (sc == 1) {
            if (TryDecode(gray.data(), rw, rh, rw, r.text)) { r.ok = true; return r; }
        } else {
            std::vector<BYTE> up((size_t)rw * sc * rh * sc);
            for (int y = 0; y < rh * sc; y++) {
                const BYTE* srow = gray.data() + (size_t)(y / sc) * rw;
                BYTE* drow = up.data() + (size_t)y * rw * sc;
                for (int x = 0; x < rw * sc; x++) drow[x] = srow[x / sc];
            }
            if (TryDecode(up.data(), rw * sc, rh * sc, rw * sc, r.text)) { r.ok = true; return r; }
        }
    }
    return r;
}
