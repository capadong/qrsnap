using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace QrSnap.Services;

/// <summary>
/// 二维码解码器。屏幕截图里的二维码常常带渐变、被缩放、对比度不足，
/// 单次解码成功率有限，因此这里采用「多轮重试策略」：
/// 原图 → 灰度 → 放大 → 加白边 → Otsu 二值化 → 锐化，
/// 每轮都尝试解码，命中即返回。
/// </summary>
public static class QrDecoder
{
    private static readonly BarcodeFormat[] Formats =
    [
        BarcodeFormat.QR_CODE,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.EAN_13
    ];

    private static readonly Lazy<IBarcodeReaderGeneric> Reader = new(() => new BarcodeReaderGeneric
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = Formats
        }
    });

    /// <summary>解码 SKBitmap 中的二维码，失败返回 null。</summary>
    public static QrSnapshot? Decode(SKBitmap source)
    {
        if (source.Width < 8 || source.Height < 8)
            return null;

        var sw = Stopwatch.StartNew();
        var owned = new List<SKBitmap>();
        try
        {
            // 选区过小时先整体放大，ZXing 对小图极不敏感
            SKBitmap baseBitmap = source;
            if (source.Width < 240 || source.Height < 240)
            {
                int factor = Math.Max(2, 480 / Math.Max(source.Width, source.Height));
                baseBitmap = Resize(source, factor);
                owned.Add(baseBitmap);
            }

            foreach (var (name, bmp) in BuildCandidates(baseBitmap, owned))
            {
                var text = TryRead(bmp);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sw.Stop();
                    return new QrSnapshot(text, sw.ElapsedMilliseconds, name);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[QrDecoder] 解码异常: {ex}");
        }
        finally
        {
            foreach (var b in owned)
                b.Dispose();
        }

        return null;
    }

    /// <summary>按优先级生成待尝试的图像变体，产生的临时图加入 owned 统一释放。</summary>
    private static IEnumerable<(string Name, SKBitmap Bitmap)> BuildCandidates(SKBitmap src, List<SKBitmap> owned)
    {
        yield return ("原始", src);

        var gray = ToGray(src);
        owned.Add(gray);
        yield return ("灰度", gray);

        var scaled = Resize(gray, 2);
        owned.Add(scaled);
        yield return ("放大", scaled);

        // 静区补偿：截图常把二维码贴边，缺少静区会导致定位失败
        var padded = AddPadding(scaled, Math.Max(16, scaled.Width / 12));
        owned.Add(padded);
        yield return ("静区补偿", padded);

        var binary = OtsuThreshold(padded);
        owned.Add(binary);
        yield return ("二值化", binary);

        var sharpened = Sharpen(binary);
        owned.Add(sharpened);
        yield return ("锐化", sharpened);
    }

    private static string? TryRead(SKBitmap bmp)
    {
        try
        {
            int w = bmp.Width, h = bmp.Height;
            var rgb = new byte[w * h * 3];
            int i = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, y);
                rgb[i++] = c.Red;
                rgb[i++] = c.Green;
                rgb[i++] = c.Blue;
            }

            var source = new RGBLuminanceSource(rgb, w, h, RGBLuminanceSource.BitmapFormat.RGB24);
            var result = Reader.Value.Decode(source);
            return result?.Text;
        }
        catch
        {
            return null;
        }
    }

    // ── 图像处理工具 ────────────────────────────────────────────

    private static SKBitmap ToGray(SKBitmap src)
    {
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
        {
            var c = src.GetPixel(x, y);
            // 标准亮度加权
            byte v = (byte)(0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue);
            dst.SetPixel(x, y, new SKColor(v, v, v));
        }
        return dst;
    }

    private static SKBitmap Resize(SKBitmap src, int factor)
    {
        var dst = new SKBitmap(src.Width * factor, src.Height * factor, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        // 最近邻，避免插值把码点糊掉
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, SKRect.Create(dst.Width, dst.Height), new SKSamplingOptions(SKFilterMode.Nearest));
        return dst;
    }

    private static SKBitmap AddPadding(SKBitmap src, int pad)
    {
        var dst = new SKBitmap(src.Width + pad * 2, src.Height + pad * 2, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(src, pad, pad);
        return dst;
    }

    /// <summary>Otsu 全局阈值二值化，对光照不均的截图效果明显。</summary>
    private static SKBitmap OtsuThreshold(SKBitmap src)
    {
        int w = src.Width, h = src.Height;
        var hist = new int[256];
        var pixels = new byte[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = src.GetPixel(x, y).Red;
            pixels[y * w + x] = c;
            hist[c]++;
        }

        int total = w * h;
        double sum = 0;
        for (int t = 0; t < 256; t++) sum += t * hist[t];

        double sumB = 0;
        int wB = 0;
        double maxVar = -1;
        int bestT = 128;
        for (int t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            int wF = total - wB;
            if (wF == 0) break;
            sumB += t * hist[t];
            double mB = sumB / wB;
            double mF = (sum - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > maxVar)
            {
                maxVar = between;
                bestT = t;
            }
        }

        var dst = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte v = pixels[y * w + x] > bestT ? (byte)255 : (byte)0;
            dst.SetPixel(x, y, new SKColor(v, v, v));
        }
        return dst;
    }

    /// <summary>3x3 锐化卷积，修复缩放导致的模糊边界。</summary>
    private static SKBitmap Sharpen(SKBitmap src)
    {
        int w = src.Width, h = src.Height;
        var dst = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        int[] kernel = { 0, -1, 0, -1, 5, -1, 0, -1, 0 };

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int acc = 0;
            for (int ky = -1; ky <= 1; ky++)
            for (int kx = -1; kx <= 1; kx++)
            {
                int px = Math.Clamp(x + kx, 0, w - 1);
                int py = Math.Clamp(y + ky, 0, h - 1);
                acc += src.GetPixel(px, py).Red * kernel[(ky + 1) * 3 + (kx + 1)];
            }
            byte v = (byte)Math.Clamp(acc, 0, 255);
            dst.SetPixel(x, y, new SKColor(v, v, v));
        }
        return dst;
    }
}

/// <summary>解码层的原始结果。</summary>
public sealed record QrSnapshot(string Text, long ElapsedMs, string StrategyName);
