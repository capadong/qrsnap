using System;
using QrSnap.Models;
using QrSnap.Services;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

// 端到端验证：生成二维码 -> 模拟屏幕截图劣化 -> QrDecoder 解码 -> ContentParser 解析
int pass = 0, fail = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { pass++; Console.WriteLine($"  [PASS] {name}"); }
    else { fail++; Console.WriteLine($"  [FAIL] {name}{(detail is null ? "" : $" -> {detail}")}"); }
}

SKBitmap MakeQr(string content, int pixelSize = 300)
{
    var writer = new ZXing.SkiaSharp.BarcodeWriter
    {
        Format = BarcodeFormat.QR_CODE,
        Options = new QrCodeEncodingOptions
        {
            Width = pixelSize,
            Height = pixelSize,
            Margin = 1,
            CharacterSet = "UTF-8"
        }
    };
    return writer.Write(content);
}

SKBitmap Degrade(SKBitmap src, float scale, SKColor background)
{
    int w = Math.Max(8, (int)(src.Width * scale)), h = Math.Max(8, (int)(src.Height * scale));
    var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
    var dst = new SKBitmap(info);
    using var canvas = new SKCanvas(dst);
    canvas.Clear(background);
    using var image = SKImage.FromBitmap(src);
    canvas.DrawImage(image, SKRect.Create(w, h), new SKSamplingOptions(SKFilterMode.Linear));
    return dst;
}

Console.WriteLine("=== 1. 标准二维码解码 ===");
foreach (var (content, label) in new[]
         {
             ("https://www.example.com/path?q=1", "URL"),
             ("hello world 中文测试", "中文文本"),
             ("WIFI:T:WPA;S:MyHome;P:12345678;;", "WiFi 配置"),
             ("BEGIN:VCARD\nVERSION:3.0\nFN:ZhangSan\nTEL:13800138000\nEND:VCARD", "vCard")
         })
{
    using var qr = MakeQr(content);
    var snap = QrDecoder.Decode(qr);
    Check($"{label} 解码", snap?.Text == content, snap?.Text ?? "null");
    if (snap is not null)
    {
        var r = ContentParser.Parse(snap);
        Console.WriteLine($"         -> Kind={r.Kind} Title={r.Title} {r.ElapsedMs}ms [{r.Format}]");
    }
}

Console.WriteLine("\n=== 2. 缩放 + 深色背景（模拟远距离/截图缩小）===");
foreach (var scale in new[] { 0.5f, 0.35f, 0.25f })
{
    using var qr = MakeQr("https://github.com/test/repo", 400);
    using var degraded = Degrade(qr, scale, new SKColor(30, 34, 40));
    var snap = QrDecoder.Decode(degraded);
    Check($"缩放 {scale:P0} ({degraded.Width}x{degraded.Height})", snap?.Text == "https://github.com/test/repo",
        snap?.Text ?? "null");
}

Console.WriteLine("\n=== 3. 贴边二维码（静区被裁掉）===");
using (var qr = MakeQr("https://avaloniaui.net", 300))
using (var canvasBmp = new SKBitmap(500, 500, SKColorType.Bgra8888, SKAlphaType.Opaque))
{
    using (var c = new SKCanvas(canvasBmp))
    {
        c.Clear(new SKColor(240, 242, 245));
        using var img = SKImage.FromBitmap(qr);
        c.DrawImage(img, new SKRect(10, 10, 310, 310));
    }
    var snap = QrDecoder.Decode(canvasBmp);
    Check("贴边二维码", snap?.Text == "https://avaloniaui.net", snap?.Text ?? "null");
}

Console.WriteLine("\n=== 4. 内容解析 ===");
void ParseCase(string raw, QrContentKind expect, string desc)
{
    var r = ContentParser.Parse(new QrSnapshot(raw, 1, "test"));
    Check($"{desc}", r.Kind == expect, $"实际={r.Kind}");
}
ParseCase("https://a.com", QrContentKind.Url, "标准 URL");
ParseCase("a.com/path", QrContentKind.Url, "裸域名");
ParseCase("hello", QrContentKind.Text, "普通文本");
ParseCase("13800138000", QrContentKind.Phone, "电话号码");
ParseCase("tel:+8613800138000", QrContentKind.Phone, "tel 协议");
ParseCase("user@example.com", QrContentKind.Email, "邮箱");
ParseCase("mailto:user@example.com", QrContentKind.Email, "mailto 协议");
ParseCase("WIFI:T:WPA;S:Office;P:pass1234;;", QrContentKind.Wifi, "WiFi");
ParseCase("BEGIN:VCARD\nFN:LiSi\nTEL:123\nEND:VCARD", QrContentKind.VCard, "vCard");

// WiFi 字段抽取
var wifiResult = ContentParser.Parse(new QrSnapshot("WIFI:T:WPA;S:CoffeeShop;P:latte2024;;", 1, "t"));
Check("WiFi SSID 正确", wifiResult.Title == "CoffeeShop", wifiResult.Title);
Check("WiFi 密码正确", wifiResult.Subtitle == "latte2024", wifiResult.Subtitle);

Console.WriteLine("\n=== 5. 异常输入不崩溃 ===");
Check("纯黑图返回 null", QrDecoder.Decode(new SKBitmap(100, 100)) is null);
Check("1x1 图返回 null", QrDecoder.Decode(new SKBitmap(1, 1)) is null);

Console.WriteLine($"\n===== {pass} 通过 / {fail} 失败 =====");
return fail == 0 ? 0 : 1;
