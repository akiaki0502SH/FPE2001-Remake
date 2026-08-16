using System.Globalization;
using System.Text;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Scan;

/// <summary>
/// 扫描值编解码（规格 §5.3 搜索模式）：数值宽度、端序、文本格式。
/// 核心领域不使用 int/uint 存值宽度相关的地址运算；宽度本身是 int（OS 边界）。
/// </summary>
internal static class ValueCodec
{
    public static int SizeOf(ScanDataType type) => type switch
    {
        ScanDataType.UInt8 => 1,
        ScanDataType.UInt16 => 2,
        ScanDataType.UInt32 => 4,
        ScanDataType.UInt64 => 8,
        ScanDataType.Float32 => 4,
        ScanDataType.Float64 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} 无固定宽度"),
    };

    public static bool HasFixedSize(ScanDataType type) => type is
        ScanDataType.UInt8 or ScanDataType.UInt16 or ScanDataType.UInt32 or ScanDataType.UInt64 or
        ScanDataType.Float32 or ScanDataType.Float64;

    /// <summary>ValueText → 字节模式（规格 §5.3：整数/浮点按端序编码）。支持 0x 十六进制输入。</summary>
    public static bool TryEncode(ScanValueSpec spec, out byte[] bytes)
    {
        bytes = [];
        if (spec.ValueText is null) return false;

        switch (spec.DataType)
        {
            case ScanDataType.UInt8 when TryParseUInt(spec.ValueText, out var v) && v <= byte.MaxValue:
                bytes = [(byte)v];
                return true;
            case ScanDataType.UInt16 when TryParseUInt(spec.ValueText, out var v) && v <= ushort.MaxValue:
                bytes = BitConverter.GetBytes((ushort)v);
                return true;
            case ScanDataType.UInt32 when TryParseUInt(spec.ValueText, out var v) && v <= uint.MaxValue:
                bytes = BitConverter.GetBytes((uint)v);
                return true;
            case ScanDataType.UInt64 when TryParseUInt(spec.ValueText, out var v):
                bytes = BitConverter.GetBytes(v);
                return true;
            case ScanDataType.Float32 when float.TryParse(spec.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v):
                bytes = BitConverter.GetBytes(v);
                return true;
            case ScanDataType.Float64 when double.TryParse(spec.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v):
                bytes = BitConverter.GetBytes(v);
                return true;
            case ScanDataType.Text:
                var encoding = spec.Encoding switch
                {
                    "utf-8" or "UTF-8" => Encoding.UTF8,
                    "utf-16le" or "UTF-16LE" => Encoding.Unicode,
                    "gbk" or "GBK" => Encoding.GetEncoding("GBK"),
                    _ => Encoding.ASCII,
                };
                bytes = encoding.GetBytes(spec.ValueText);
                return true;
            default:
                return false;
        }
    }

    /// <summary>整数解析：支持 0x 前缀十六进制与普通十进制。</summary>
    private static bool TryParseUInt(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X", StringComparison.Ordinal))
        {
            return ulong.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        return ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>快照值字节 → 显示文本。</summary>
    public static string Format(byte[] value, ScanDataType type, Endianness endianness)
    {
        if (value.Length == 0) return "";
        switch (type)
        {
            case ScanDataType.UInt8: return value[0].ToString();
            case ScanDataType.UInt16: return FromUInt16(value, endianness).ToString();
            case ScanDataType.UInt32: return FromUInt32(value, endianness).ToString();
            case ScanDataType.UInt64: return FromUInt64(value, endianness).ToString();
            case ScanDataType.Float32: return FromFloat32(value, endianness).ToString("G9");
            case ScanDataType.Float64: return FromFloat64(value, endianness).ToString("G17");
            case ScanDataType.Bytes: return Convert.ToHexString(value);
            case ScanDataType.Text: return Encoding.UTF8.GetString(value);
            default: return Convert.ToHexString(value);
        }
    }

    public static ulong ToUInt64(byte[] value, Endianness endianness) => value.Length switch
    {
        1 => value[0],
        2 => FromUInt16(value, endianness),
        4 => FromUInt32(value, endianness),
        8 => FromUInt64(value, endianness),
        _ => throw new ArgumentException($"无法将 {value.Length} 字节转为 ulong。"),
    };

    /// <summary>span 版本：扫描热路径零分配（规格 §5.3 同语义）。</summary>
    public static ulong ToUInt64(ReadOnlySpan<byte> value, Endianness endianness) => value.Length switch
    {
        1 => value[0],
        2 => FromUInt16(value, endianness),
        4 => FromUInt32(value, endianness),
        8 => FromUInt64(value, endianness),
        _ => throw new ArgumentException($"无法将 {value.Length} 字节转为 ulong。"),
    };

    public static double ToDouble(byte[] value, ScanDataType type, Endianness endianness) => type switch
    {
        ScanDataType.Float32 => FromFloat32(value, endianness),
        ScanDataType.Float64 => FromFloat64(value, endianness),
        _ => FromUInt64(value, endianness),
    };

    /// <summary>span 版本：扫描热路径零分配。</summary>
    public static double ToDouble(ReadOnlySpan<byte> value, ScanDataType type, Endianness endianness) => type switch
    {
        ScanDataType.Float32 => FromFloat32(value, endianness),
        ScanDataType.Float64 => FromFloat64(value, endianness),
        _ => FromUInt64(value, endianness),
    };

    public static ushort FromUInt16(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt16(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(value, 0));

    public static ushort FromUInt16(ReadOnlySpan<byte> value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(value)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(value);

    public static uint FromUInt32(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt32(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt32(value, 0));

    public static uint FromUInt32(ReadOnlySpan<byte> value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(value)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(value);

    public static ulong FromUInt64(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToUInt64(value, 0)
            : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt64(value, 0));

    public static ulong FromUInt64(ReadOnlySpan<byte> value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(value)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(value);

    public static float FromFloat32(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToSingle(value, 0)
            : BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToInt32(value, 0)));

    public static float FromFloat32(ReadOnlySpan<byte> value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(value)
            : BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(value));

    public static double FromFloat64(byte[] value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? BitConverter.ToDouble(value, 0)
            : BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(BitConverter.ToInt64(value, 0)));

    public static double FromFloat64(ReadOnlySpan<byte> value, Endianness endianness) =>
        endianness == Endianness.LittleEndian
            ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(value)
            : BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(value));
}
