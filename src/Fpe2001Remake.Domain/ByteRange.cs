namespace Fpe2001Remake.Domain;

/// <summary>
/// 64 位字节区间（规格 §3）。
/// EndExclusive 保持 checked 语义（显示/规划用，超出 ulong 范围抛 OverflowException）；
/// Contains/Overlaps 使用无溢出等价形式，高地址触顶时依然正确判断而不崩溃。
/// </summary>
public readonly record struct ByteRange(ulong Offset, ulong Length)
{
    /// <summary>区间终点（不含）。溢出时抛 OverflowException。</summary>
    public ulong EndExclusive
    {
        get
        {
            checked { return Offset + Length; }
        }
    }

    public bool IsEmpty => Length == 0;

    /// <summary>position ∈ [Offset, Offset+Length)，无溢出实现。</summary>
    public bool Contains(ulong position)
    {
        if (position < Offset) return false;
        if (Length == 0) return false;
        // position < Offset + Length ⟺ Length > position - Offset（差不会溢出）
        return Length > position - Offset;
    }

    /// <summary>
    /// 与另一区间重叠判断，无溢出实现：
    /// 起点较小的一方如果长度不超过两者起点差，则完全在对方左侧，不重叠。
    /// </summary>
    public bool Overlaps(ByteRange other)
    {
        if (IsEmpty || other.IsEmpty) return false;

        if (other.Offset >= Offset)
        {
            // 本区间起点更早：本区间在对方起点前结束则不重叠
            var gap = other.Offset - Offset; // 不溢出（other.Offset >= Offset）
            return Length > gap;
        }
        else
        {
            var gap = Offset - other.Offset; // 不溢出（Offset > other.Offset）
            return other.Length > gap;
        }
    }
}
