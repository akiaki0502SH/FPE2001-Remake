using System.Globalization;

namespace Fpe2001Remake.Domain;

/// <summary>
/// 地址/偏移的显示与解析（规格 §12）。
/// 宿主地址与文件偏移：0x + 至少 16 位十六进制；JSON 中的 ulong 使用十六进制字符串（无 0x 前缀）。
/// </summary>
public static class AddressFormatting
{
    /// <summary>固定 16 位十六进制，形如 0x0000000000001000。</summary>
    public static string ToHex16(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>0x + 至少 minDigits 位十六进制。</summary>
    public static string ToHex(ulong value, int minDigits = 16)
        => "0x" + value.ToString("X" + Math.Max(1, minDigits), CultureInfo.InvariantCulture);

    /// <summary>JSON 使用的十六进制字符串（无前缀，小写不必要，保持大写）。</summary>
    public static string ToHexJson(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

    public static ulong ParseHexJson(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        return ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static bool TryParseHex(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }
        return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>十六进制 + 十进制同时显示，用于文件偏移（规格 §12）。</summary>
    public static string ToOffsetDisplay(ulong offset) => $"{ToHex(offset)} ({offset.ToString(CultureInfo.InvariantCulture)})";
}
