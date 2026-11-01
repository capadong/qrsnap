using System;
using System.Text.RegularExpressions;
using QrSnap.Models;

namespace QrSnap.Services;

/// <summary>把二维码原始文本解析为结构化结果，决定结果窗的展示形态。</summary>
public static partial class ContentParser
{
    public static QrResult Parse(QrSnapshot snapshot)
    {
        var raw = snapshot.Text.Trim();

        if (TryParseWifi(raw, out var wifi))
            return Build(raw, QrContentKind.Wifi, wifi.Name, wifi.Password, snapshot);

        if (TryParseVCard(raw, out var vcard))
            return Build(raw, QrContentKind.VCard, vcard.Name, vcard.Phone, snapshot);

        if (TryParseUrl(raw, out var uri))
            return Build(raw, QrContentKind.Url, uri.Host, Shorten(raw, 80), snapshot);

        if (TryParseEmail(raw, out var email))
            return Build(raw, QrContentKind.Email, email, "点击撰写邮件", snapshot);

        if (TryParsePhone(raw, out var phone))
            return Build(raw, QrContentKind.Phone, phone, "点击拨打电话", snapshot);

        return Build(raw, QrContentKind.Text, null, null, snapshot);
    }

    private static QrResult Build(string raw, QrContentKind kind, string? title, string? subtitle, QrSnapshot s) =>
        new()
        {
            RawText = raw,
            Kind = kind,
            Title = title,
            Subtitle = subtitle,
            ElapsedMs = s.ElapsedMs,
            Format = s.StrategyName
        };

    private static bool TryParseUrl(string raw, out Uri uri)
    {
        uri = null!;
        var candidate = raw;
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            // 裸域名补协议：example.com/path
            if (!BareDomainRegex().IsMatch(candidate)) return false;
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out uri!)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool TryParseEmail(string raw, out string address)
    {
        address = raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? raw[7..].Split('?')[0]
            : raw;
        return EmailRegex().IsMatch(address);
    }

    private static bool TryParsePhone(string raw, out string phone)
    {
        phone = raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ? raw[4..] : raw;
        return PhoneRegex().IsMatch(phone);
    }

    /// <summary>解析 WIFI:T:WPA;S:名称;P:密码;; 格式。</summary>
    private static bool TryParseWifi(string raw, out (string Name, string Password) info)
    {
        info = default;
        if (!raw.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = raw[5..];
        var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in WifiFieldRegex().Matches(body))
            map[m.Groups[1].Value] = m.Groups[2].Value.Replace("\\;", ";").Replace("\\,", ",");

        if (!map.TryGetValue("S", out var name) || string.IsNullOrWhiteSpace(name))
            return false;

        map.TryGetValue("P", out var pwd);
        info = (name, pwd ?? string.Empty);
        return true;
    }

    private static bool TryParseVCard(string raw, out (string Name, string Phone) info)
    {
        info = default;
        if (!raw.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            return false;

        string? name = null, phone = null, org = null;
        foreach (var line in raw.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("FN:", StringComparison.OrdinalIgnoreCase)) name = l[3..].Trim();
            else if (l.StartsWith("ORG:", StringComparison.OrdinalIgnoreCase)) org = l[4..].Trim();
            else if (l.StartsWith("TEL", StringComparison.OrdinalIgnoreCase) && l.Contains(':')) phone = l[(l.IndexOf(':') + 1)..].Trim();
        }

        info = (name ?? org ?? "未知联系人", phone ?? "");
        return true;
    }

    private static string Shorten(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(@"^(www\.)?([a-z0-9-]+\.)+[a-z]{2,}(:\d+)?(/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDomainRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\+?[\d\s\-()]{5,20}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"([A-Z]):((?:\\.|[^;\\])*);?")]
    private static partial Regex WifiFieldRegex();
}
