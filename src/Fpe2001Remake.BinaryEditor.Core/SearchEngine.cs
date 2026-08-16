using Fpe2001Remake.Domain;

namespace Fpe2001Remake.BinaryEditor.Core;

/// <summary>搜索结果。</summary>
public sealed record SearchHit(ulong Offset, int Length, string Preview);

/// <summary>
/// 十六进制/文本/整数/浮点搜索（规格 §5.3）。
/// Hex 支持 ?? 掩码（byte? null = 任意）；不跨不可读洞；块间保留 patternLength-1 重叠。
/// </summary>
public sealed class SearchEngine
{
    private const int BlockSize = 256 * 1024;

    /// <summary>十六进制模式：支持 "DE AD ?? EF" 与 "DEAD??EF"（空白可选）。</summary>
    public static byte?[] ParseHexPattern(string input)
    {
        var compact = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0 || compact.Length % 2 != 0)
        {
            throw new ArgumentException("十六进制模式长度必须为偶数（每字节两位）。", nameof(input));
        }
        var result = new byte?[compact.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            var pair = compact.Substring(i * 2, 2);
            if (pair == "??" || pair == "**")
            {
                result[i] = null;
            }
            else if (byte.TryParse(pair, System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                result[i] = b;
            }
            else
            {
                throw new ArgumentException($"无效的十六进制对：{pair}", nameof(input));
            }
        }
        return result;
    }

    public SearchHit? FindHex(HexDocument document, byte?[] pattern, ulong startOffset)
    {
        if (pattern.Length == 0)
        {
            throw new ArgumentException("模式不能为空。", nameof(pattern));
        }
        return Scan(document, startOffset, (buffer, pos, count) =>
        {
            for (var i = 0; i + pattern.Length <= count; i++)
            {
                if (Matches(buffer.AsSpan(i, pattern.Length), pattern))
                {
                    return (ulong)i;
                }
            }
            return null;
        }, pattern.Length);
    }

    public SearchHit? FindText(HexDocument document, string text, string encoding, bool matchCase, ulong startOffset)
    {
        if (text.Length == 0)
        {
            throw new ArgumentException("搜索文本不能为空。", nameof(text));
        }
        var bytes = TextCodec.Encode(text, encoding);
        if (matchCase)
        {
            return FindBytesPattern(document, bytes, startOffset);
        }
        // 大小写不敏感：ASCII 字母折叠（多字节编码的非 ASCII 字节精确匹配）
        var folded = FoldAsciiLower(bytes);
        return FindBytesPattern(document, folded, startOffset, FoldAsciiLower);
    }

    private static byte[] FoldAsciiLower(byte[] bytes)
    {
        var result = (byte[])bytes.Clone();
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] is >= (byte)'A' and <= (byte)'Z')
            {
                result[i] = (byte)(result[i] + 32);
            }
        }
        return result;
    }

    public SearchHit? FindInteger(
        HexDocument document, ulong value, int widthBytes, Endianness endianness, ulong startOffset)
    {
        if (widthBytes is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentException("整数宽度必须为 1/2/4/8 字节。", nameof(widthBytes));
        }
        var bytes = new byte[widthBytes];
        for (var i = 0; i < widthBytes; i++)
        {
            var shift = endianness == Endianness.LittleEndian ? i * 8 : (widthBytes - 1 - i) * 8;
            bytes[i] = (byte)((value >> shift) & 0xFF);
        }
        return FindBytesPattern(document, bytes, startOffset);
    }

    public SearchHit? FindFloat(
        HexDocument document, double value, int widthBytes, Endianness endianness, ulong startOffset)
    {
        byte[] bytes = widthBytes switch
        {
            4 => BitConverter.GetBytes((float)value),
            8 => BitConverter.GetBytes(value),
            _ => throw new ArgumentException("浮点宽度必须为 4/8 字节。", nameof(widthBytes)),
        };
        if (endianness == Endianness.BigEndian)
        {
            Array.Reverse(bytes);
        }
        return FindBytesPattern(document, bytes, startOffset);
    }

    // ---------- 内部 ----------

    private static bool Matches(ReadOnlySpan<byte> data, byte?[] pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] is byte expected && data[i] != expected)
            {
                return false;
            }
        }
        return true;
    }

    private SearchHit? FindBytesPattern(HexDocument document, byte[] pattern, ulong startOffset, Func<byte[], byte[]>? fold = null)
    {
        if (pattern.Length == 0)
        {
            throw new ArgumentException("模式不能为空。");
        }
        return Scan(document, startOffset, (buffer, pos, count) =>
        {
            for (var i = 0; i + pattern.Length <= count; i++)
            {
                var slice = buffer.AsSpan(i, pattern.Length);
                var matched = fold is null
                    ? slice.SequenceEqual(pattern)
                    : fold(slice.ToArray()).AsSpan().SequenceEqual(pattern);
                if (matched)
                {
                    return (ulong)i;
                }
            }
            return null;
        }, pattern.Length);
    }

    /// <summary>
    /// 分块扫描文档逻辑视图；overlap 保证跨块匹配。
    /// matcher 返回块内命中偏移（相对 buffer 起点）或 null。
    /// </summary>
    private SearchHit? Scan(
        HexDocument document,
        ulong startOffset,
        Func<byte[], ulong, int, ulong?> matcher,
        int overlap)
    {
        var buffer = new byte[BlockSize + overlap - 1];
        var pos = startOffset;
        while (pos < document.Length)
        {
            var count = (int)Math.Min((ulong)buffer.Length, document.Length - pos);
            var read = document.ReadBytes(pos, buffer.AsSpan(0, count));
            if (read < overlap)
            {
                break; // 不可读洞：停止（不跨洞）
            }
            var hit = matcher(buffer, pos, read);
            if (hit is ulong inner)
            {
                var offset = pos + inner;
                var preview = Preview(document, offset, Math.Min(16, (int)Math.Min((ulong)overlap, document.Length - offset)));
                return new SearchHit(offset, overlap, preview);
            }
            if (read < BlockSize)
            {
                break;
            }
            pos += (ulong)(BlockSize - overlap + 1);
        }
        return null;
    }

    private static string Preview(HexDocument document, ulong offset, int length)
    {
        var data = new byte[Math.Max(0, length)];
        if (data.Length == 0) return "";
        document.ReadBytes(offset, data);
        return Convert.ToHexString(data);
    }
}
