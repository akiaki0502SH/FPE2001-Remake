using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Scan;

/// <summary>
/// 扫描条件比较器（规格 §8 首次/再次扫描；沿用 FPSN v1 快照比较语义）。
/// previous：快照中的旧值；current：刚读到的当前值。
/// </summary>
internal static class ValueComparer
{
    public static bool Matches(
        ScanCondition condition,
        byte[] previous,
        byte[] current,
        ScanDataType dataType,
        Endianness endianness)
    {
        return condition.Comparison switch
        {
            ScanComparison.UnknownValue => true,
            ScanComparison.Changed => !previous.AsSpan().SequenceEqual(current),
            ScanComparison.Unchanged => previous.AsSpan().SequenceEqual(current),
            ScanComparison.Equal or ScanComparison.NotEqual or ScanComparison.GreaterThan or ScanComparison.LessThan =>
                CompareToSpec(current, condition, dataType, endianness),
            _ => false,
        };
    }

    /// <summary>首次扫描：当前值直接与输入规格比较。</summary>
    public static bool MatchesFirst(ScanCondition condition, byte[] current, ScanDataType dataType, Endianness endianness)
    {
        if (condition.Comparison == ScanComparison.UnknownValue)
        {
            return true; // 未知值：记录全部候选
        }
        return CompareToSpec(current, condition, dataType, endianness);
    }

    private static bool CompareToSpec(
        byte[] current, ScanCondition condition, ScanDataType dataType, Endianness endianness)
    {
        if (condition.Value is null || !ValueCodec.TryEncode(condition.Value, out var expected))
        {
            return false;
        }

        var cmp = dataType switch
        {
            ScanDataType.Float32 or ScanDataType.Float64 =>
                ValueCodec.ToDouble(current, dataType, endianness)
                    .CompareTo(ValueCodec.ToDouble(expected, dataType, endianness)),
            _ => ValueCodec.ToUInt64(current, endianness)
                    .CompareTo(ValueCodec.ToUInt64(expected, endianness)),
        };

        return condition.Comparison switch
        {
            ScanComparison.Equal => cmp == 0,
            ScanComparison.NotEqual => cmp != 0,
            ScanComparison.GreaterThan => cmp > 0,
            ScanComparison.LessThan => cmp < 0,
            _ => false,
        };
    }
}
