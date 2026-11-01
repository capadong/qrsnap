namespace QrSnap.Models;

/// <summary>识别出的内容类型，用于结果窗决定图标与动作按钮。</summary>
public enum QrContentKind
{
    Url,
    Wifi,
    Email,
    Phone,
    VCard,
    Text
}

/// <summary>一次识别结果。</summary>
public sealed class QrResult
{
    public required string RawText { get; init; }
    public required QrContentKind Kind { get; init; }

    /// <summary>展示用的主标题，例如域名、WiFi 名称、电话号码。</summary>
    public string? Title { get; init; }

    /// <summary>展示用的副信息，例如 WiFi 密码、联系人姓名。</summary>
    public string? Subtitle { get; init; }

    /// <summary>长文本展示区内容（原始内容）。</summary>
    public string DisplayText => RawText;

    /// <summary>识别耗时，毫秒。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>命中的解码格式，例如 QR_CODE。</summary>
    public string Format { get; init; } = "QR_CODE";

    /// <summary>是否为可打开/可复制的动作型内容。</summary>
    public bool HasAction => Kind is QrContentKind.Url or QrContentKind.Email or QrContentKind.Phone;

    /// <summary>动作按钮文案。</summary>
    public string ActionText => Kind switch
    {
        QrContentKind.Url => "打开链接",
        QrContentKind.Email => "写邮件",
        QrContentKind.Phone => "拨打电话",
        _ => "复制内容"
    };

    /// <summary>结果窗顶部小标题。</summary>
    public string KindLabel => Kind switch
    {
        QrContentKind.Url => "网址链接",
        QrContentKind.Wifi => "WiFi 配置",
        QrContentKind.Email => "电子邮箱",
        QrContentKind.Phone => "电话号码",
        QrContentKind.VCard => "联系人名片",
        _ => "文本内容"
    };
}
