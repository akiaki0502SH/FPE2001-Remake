using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Scan;

/// <summary>预编译的期望值（扫描循环外编码一次；热路径零分配）。</summary>
internal readonly struct ExpectedValue
{
    public readonly bool IsFloat;
    public readonly ulong UInt;
    public readonly double Float;

    public ExpectedValue(bool isFloat, ulong uintValue, double floatValue)
    {
        IsFloat = isFloat;
        UInt = uintValue;
        Float = floatValue;
    }
}

/// <summary>
/// 扫描条件比较器（规格 §8 首次/再次扫描；沿用 FPSN v1 快照比较语义）。
/// previous：快照中的旧值；current：刚读到的当前值。
/// 全部使用 ReadOnlySpan 入参 + 循环外预编译期望值：扫描热路径零分配（候选量百万级时 GC 压力是关键瓶颈）。
/// </summary>
internal static class ValueComparer
{
    /// <summary>
    /// 预编译期望值。无值比较（变化/未变/未知值）返回 null 也表示可用。
    /// 自动模式：输入规格携带 Auto，编译时必须用当前快照的真实类型。
    /// </summary>
    public static ExpectedValue? CompileExpected(ScanCondition condition, ScanDataType dataType, Endianness endianness)
    {
        if (condition.Value is null)
        {
            return null;
        }
        var spec = condition.Value.DataType == ScanDataType.Auto
            ? condition.Value with { DataType = dataType }
            : condition.Value;
        if (!ValueCodec.TryEncode(spec, out var expected))
        {
            return null;
        }
        var isFloat = dataType is ScanDataType.Float32 or ScanDataType.Float64;
        return new ExpectedValue(
            isFloat,
            isFloat ? 0 : ValueCodec.ToUInt64(expected, endianness),
            isFloat ? ValueCodec.ToDouble(expected, dataType, endianness) : 0);
    }

    public static bool Matches(
        ScanCondition condition,
        ExpectedValue? expected,
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> current,
        ScanDataType dataType,
        Endianness endianness)
    {
        return condition.Comparison switch
        {
            ScanComparison.UnknownValue => true,
            ScanComparison.Changed => !previous.SequenceEqual(current),
            ScanComparison.Unchanged => previous.SequenceEqual(current),
            ScanComparison.Equal or ScanComparison.NotEqual or ScanComparison.GreaterThan or ScanComparison.LessThan =>
                Compare(expected, current, condition.Comparison, dataType, endianness),
            _ => false,
        };
    }

    /// <summary>首次扫描：当前值直接与输入规格比较。</summary>
    public static bool MatchesFirst(
        ScanCondition condition,
        ExpectedValue? expected,
        ReadOnlySpan<byte> current,
        ScanDataType dataType,
        Endianness endianness)
    {
        if (condition.Comparison == ScanComparison.UnknownValue)
        {
            return true; // 未知值：记录全部候选
        }
        return Compare(expected, current, condition.Comparison, dataType, endianness);
    }

    private static bool Compare(
        ExpectedValue? expected,
        ReadOnlySpan<byte> current,
        ScanComparison comparison,
        ScanDataType dataType,
        Endianness endianness)
    {
        if (expected is null)
        {
            return false;
        }
        var cmp = expected.Value.IsFloat
            ? ValueCodec.ToDouble(current, dataType, endianness).CompareTo(expected.Value.Float)
            : ValueCodec.ToUInt64(current, endianness).CompareTo(expected.Value.UInt);

        return comparison switch
        {
            ScanComparison.Equal => cmp == 0,
            ScanComparison.NotEqual => cmp != 0,
            ScanComparison.GreaterThan => cmp > 0,
            ScanComparison.LessThan => cmp < 0,
            _ => false,
        };
    }
}
