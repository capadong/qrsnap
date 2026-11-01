// 生成 QrSnap 应用图标（多尺寸 .ico）
using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

var outPath = args.Length > 0 ? args[0] : "app.ico";
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var pngs = new List<byte[]>();

foreach (var s in sizes)
{
    using var surface = SKSurface.Create(new SKImageInfo(s, s, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    float pad = s * 0.04f;
    float r = s * 0.235f;
    var rect = new SKRect(pad, pad, s - pad, s - pad);

    // 背景：青蓝渐变
    using (var shader = SKShader.CreateLinearGradient(
               new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
               [new SKColor(0x38, 0xBD, 0xF8), new SKColor(0x2F, 0x7A, 0xFF)],
               [0f, 1f], SKShaderTileMode.Clamp))
    using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
    {
        canvas.DrawRoundRect(rect, r, r, paint);
    }

    // 顶部高光，增加立体感
    using (var glow = SKShader.CreateLinearGradient(
               new SKPoint(0, rect.Top), new SKPoint(0, rect.MidY),
               [new SKColor(255, 255, 255, 70), new SKColor(255, 255, 255, 0)],
               [0f, 1f], SKShaderTileMode.Clamp))
    using (var paint = new SKPaint { Shader = glow, IsAntialias = true })
    {
        canvas.DrawRoundRect(rect, r, r, paint);
    }

    // 二维码图形：三个定位角 + 中心块
    float m = s * 0.235f;              // 边距
    float size = s - m * 2;            // 图形区域边长
    float cell = size / 9f;            // 9x9 网格
    using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, StrokeWidth = 0 };
    using var solid = new SKPaint { Color = SKColors.White, IsAntialias = true };

    void Finder(float gx, float gy)
    {
        float x = m + gx * cell, y = m + gy * cell;
        float box = cell * 3;
        // 外框
        using var p = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1f, cell * 0.62f)
        };
        canvas.DrawRoundRect(new SKRect(x + cell * 0.31f, y + cell * 0.31f,
            x + box - cell * 0.31f, y + box - cell * 0.31f), cell * 0.22f, cell * 0.22f, p);
        // 中心点
        canvas.DrawRoundRect(new SKRect(x + cell * 1.16f, y + cell * 1.16f,
            x + cell * 1.84f, y + cell * 1.84f), cell * 0.1f, cell * 0.1f, solid);
    }

    Finder(0, 0);   // 左上
    Finder(6, 0);   // 右上
    Finder(0, 6);   // 左下

    // 右下与中间的数据点
    void Dot(float gx, float gy, float w = 1f, float h = 1f)
        => canvas.DrawRoundRect(new SKRect(m + gx * cell, m + gy * cell,
            m + (gx + w) * cell, m + (gy + h) * cell), cell * 0.16f, cell * 0.16f, solid);

    Dot(5f, 5f, 1.4f, 1.4f);
    Dot(7f, 5f, 1f, 1f);
    Dot(5f, 7f, 1f, 1f);
    Dot(6.8f, 6.8f, 1.5f, 1.5f);
    Dot(4f, 4f, 1f, 1f);

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    pngs.Add(data.ToArray());
}

// 写 ICO：头 + 目录项 + PNG 数据
using var fs = File.Create(outPath);
using var bw = new BinaryWriter(fs);
bw.Write((ushort)0);      // reserved
bw.Write((ushort)1);      // type = icon
bw.Write((ushort)sizes.Length);

int offset = 6 + 16 * sizes.Length;
for (int i = 0; i < sizes.Length; i++)
{
    int s = sizes[i];
    bw.Write((byte)(s >= 256 ? 0 : s));  // width
    bw.Write((byte)(s >= 256 ? 0 : s));  // height
    bw.Write((byte)0);                   // palette
    bw.Write((byte)0);                   // reserved
    bw.Write((ushort)1);                 // planes
    bw.Write((ushort)32);                // bpp
    bw.Write(pngs[i].Length);
    bw.Write(offset);
    offset += pngs[i].Length;
}
foreach (var p in pngs) bw.Write(p);

Console.WriteLine($"icon written: {outPath} ({sizes.Length} sizes, {new FileInfo(outPath).Length} bytes)");
