namespace Fpe2001Remake.Contracts;

/// <summary>编辑动作基类（规格 §5.1）。</summary>
public abstract record ByteEdit(ulong Offset);

/// <summary>覆盖：Before 为原字节，After 为新字节（长度一致，进程源仅允许此类）。</summary>
public sealed record OverwriteEdit(ulong Offset, byte[] Before, byte[] After) : ByteEdit(Offset);

/// <summary>插入：在 Offset 处插入 Data（仅文件源）。</summary>
public sealed record InsertEdit(ulong Offset, byte[] Data) : ByteEdit(Offset);

/// <summary>删除：删除 Offset 起的 Deleted 字节（仅文件源）。</summary>
public sealed record DeleteEdit(ulong Offset, byte[] Deleted) : ByteEdit(Offset);

/// <summary>
/// 变更集（规格 §5.1）。BaseVersionToken 必须匹配来源当前版本，否则提交被拒。
/// 插入/删除使用区间映射，不为大文件复制完整缓冲。
/// </summary>
public sealed record ChangeSet(
    Guid Id,
    string BaseVersionToken,
    IReadOnlyList<ByteEdit> Edits,
    ulong ResultLength)
{
    public int EditCount => Edits.Count;

    public bool IsEmpty => Edits.Count == 0;

    public static ChangeSet Empty(string baseVersionToken, ulong currentLength)
        => new(Guid.NewGuid(), baseVersionToken, [], currentLength);
}
