using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.BinaryEditor.Core;

/// <summary>
/// 十六进制文档（规格 §5）：分页缓存 + 区间映射。
/// 覆盖/插入/删除使用区间映射，不为大文件复制完整缓冲；
/// 修改高亮由编辑区间推导；分页缓存默认 64 KiB 页、64 MiB 上限。
/// </summary>
public sealed class HexDocument : IDisposable
{
    public const int PageSize = 64 * 1024;
    public const long MaxCacheBytes = 64 * 1024 * 1024;

    private readonly IByteSource _source;
    private readonly Dictionary<long, Page> _pages = [];
    private readonly List<Segment> _segments = [];      // 逻辑段（LogicalStart 升序、连续覆盖 [0, Length)）
    private readonly List<ByteEdit> _edits = [];        // 编辑历史（撤销/变更集）
    private readonly Dictionary<ulong, Bookmark> _bookmarks = [];
    private ulong _length;
    private long _cacheBytes;
    private readonly string _baseVersionToken;
    private bool _disposed;

    private sealed class Page
    {
        public readonly byte[] Data = new byte[PageSize];
        public readonly bool[] Unavailable = new bool[PageSize];
        public bool Loaded;
        public long LastAccess;
    }

    private sealed class Segment
    {
        public ulong LogicalStart;
        public ulong Length;
        public ulong PhysicalStart;   // Original 段：源物理偏移；Inserted 段无意义
        public bool IsInserted;
        public byte[]? Data;          // Inserted 段数据
    }

    public HexDocument(IByteSource source)
    {
        _source = source;
        _baseVersionToken = source.Identity.VersionToken;
        var knownLength = source.Identity.Length ?? throw new InvalidOperationException("来源必须声明长度（KnownLength）。");
        _length = knownLength;
        _segments.Add(new Segment
        {
            LogicalStart = 0,
            Length = knownLength,
            PhysicalStart = 0,
            IsInserted = false,
        });
    }

    public ulong Length => _length;

    public bool IsDirty => _edits.Count > 0;

    public string BaseVersionToken => _baseVersionToken;

    public bool SupportsInsertDelete => _source.Capabilities.HasFlag(ByteSourceCapabilities.Insert | ByteSourceCapabilities.Delete);

    public IReadOnlyList<ByteEdit> Edits => _edits;

    public int EditCount => _edits.Count;

    /// <summary>当前逻辑长度与 BaseVersionToken 组装变更集（规格 §5.1）。</summary>
    public ChangeSet BuildChangeSet()
        => new(Guid.NewGuid(), _baseVersionToken, _edits.ToList(), _length);

    /// <summary>指定逻辑范围是否被编辑直接命中（修改高亮，规格 §6 修改高亮）。</summary>
    public bool IsDirtyInRange(ulong offset, int length)
    {
        if (_edits.Count == 0) return false;
        var end = checked(offset + (ulong)length);
        foreach (var edit in _edits)
        {
            var (editStart, editLen) = edit switch
            {
                OverwriteEdit o => (o.Offset, (ulong)o.After.Length),
                InsertEdit i => (i.Offset, (ulong)i.Data.Length),
                DeleteEdit d => (d.Offset, (ulong)d.Deleted.Length),
                _ => (edit.Offset, 0UL),
            };
            if (editStart < end && checked(editStart + editLen) > offset)
            {
                return true;
            }
        }
        return false;
    }

    // ---------- 读取 ----------

    /// <summary>读单字节（逻辑偏移）。</summary>
    public byte ReadByte(ulong offset)
    {
        if (offset >= _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "偏移超出文档长度。");
        }
        var segment = FindSegment(offset);
        var inner = offset - segment.LogicalStart;
        if (segment.IsInserted)
        {
            return segment.Data![checked((int)inner)];
        }
        return ReadPhysicalByte(checked(segment.PhysicalStart + inner));
    }

    /// <summary>读连续字节（逻辑偏移，跨段/跨页自动处理）。</summary>
    public int ReadBytes(ulong offset, Span<byte> destination)
    {
        var total = 0;
        var pos = offset;
        while (total < destination.Length && pos < _length)
        {
            var segment = FindSegment(pos);
            var inner = pos - segment.LogicalStart;   // ulong：大文件偏移可能超过 int.MaxValue
            var take = Math.Min(segment.Length - inner, (ulong)(destination.Length - total));

            if (segment.IsInserted)
            {
                segment.Data.AsSpan((int)inner, (int)take).CopyTo(destination[total..]);
            }
            else
            {
                var physical = checked(segment.PhysicalStart + inner);
                ReadPhysical(physical, destination[total..], (int)take);
            }
            total += (int)take;
            pos += take;
        }
        return total;
    }

    // ---------- 编辑 ----------

    /// <summary>覆盖：After 长度必须等于原长度（进程源等长覆盖语义）。</summary>
    public OverwriteEdit Overwrite(ulong offset, byte[] after)
    {
        if (offset + (ulong)after.Length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "覆盖超出文档长度。");
        }
        var before = new byte[after.Length];
        ReadBytes(offset, before);

        // 逻辑偏移在插入/删除后不再等于来源物理偏移。
        WriteLogical(offset, after);

        var edit = new OverwriteEdit(offset, before, after);
        _edits.Add(edit);
        return edit;
    }

    /// <summary>插入（仅文件源；进程源抛 NotSupportedException，规格 §6）。</summary>
    public InsertEdit Insert(ulong offset, byte[] data)
    {
        if (!SupportsInsertDelete)
        {
            throw new NotSupportedException("当前来源（进程内存）不支持插入；仅支持等长覆盖。");
        }
        if (offset > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "插入偏移超出文档长度。");
        }
        if (data.Length == 0)
        {
            throw new ArgumentException("插入数据不能为空。", nameof(data));
        }

        var segment = FindSegment(Math.Min(offset, _length - 1));
        var inner = offset - segment.LogicalStart;
        if (segment.IsInserted)
        {
            // 插入到已插入段内部：直接并入
            var at = (int)inner;
            var merged = new byte[segment.Data!.Length + data.Length];
            segment.Data.AsSpan(0, at).CopyTo(merged);
            data.CopyTo(merged, at);
            segment.Data.AsSpan(at).CopyTo(merged.AsSpan(at + data.Length));
            segment.Data = merged;
            segment.Length = (ulong)merged.Length;
        }
        else
        {
            SplitAndInsert(segment, inner, data);
        }
        _length = checked(_length + (ulong)data.Length);
        var edit = new InsertEdit(offset, data);
        _edits.Add(edit);
        return edit;
    }

    /// <summary>删除（仅文件源；进程源抛 NotSupportedException，规格 §6）。</summary>
    public DeleteEdit Delete(ulong offset, ulong length)
    {
        if (!SupportsInsertDelete)
        {
            throw new NotSupportedException("当前来源（进程内存）不支持删除；仅支持等长覆盖。");
        }
        if (offset + length > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "删除超出文档长度。");
        }
        if (length == 0)
        {
            throw new ArgumentException("删除长度不能为 0。", nameof(length));
        }

        var deleted = new byte[checked((int)length)];
        ReadBytes(offset, deleted);

        RemoveRange(offset, length);
        _length = checked(_length - length);
        var edit = new DeleteEdit(offset, deleted);
        _edits.Add(edit);
        return edit;
    }

    private void RemoveRange(ulong offset, ulong length)
    {
        var remaining = length;
        var pos = offset;
        while (remaining > 0)
        {
            var segment = FindSegment(pos);
            var inner = pos - segment.LogicalStart;
            var take = Math.Min(segment.Length - inner, remaining);

            if (segment.IsInserted)
            {
                RemoveFromInserted(segment, checked((int)inner), checked((int)take));
            }
            else if (inner > 0)
            {
                var rightLength = segment.Length - inner - take;
                if (rightLength > 0)
                {
                    var right = new Segment
                    {
                        LogicalStart = segment.LogicalStart + inner,
                        Length = rightLength,
                        PhysicalStart = segment.PhysicalStart + inner + take,
                        IsInserted = false,
                    };
                    _segments.Insert(_segments.IndexOf(segment) + 1, right);
                }
                segment.Length = inner;
            }
            else
            {
                segment.PhysicalStart += take;
                segment.Length -= take;
                if (segment.Length == 0) _segments.Remove(segment);
            }
            pos += take;
            remaining -= take;
            CoalesceSegments();
        }

    }

    // ---------- 撤销/重做 ----------

    /// <summary>撤销最后一个编辑；返回被撤销的编辑。</summary>
    public ByteEdit? Undo()
    {
        if (_edits.Count == 0) return null;
        var edit = _edits[^1];
        switch (edit)
        {
            case OverwriteEdit o:
                WriteLogical(o.Offset, o.Before);
                break;
            case InsertEdit i:
            {
                RemoveRange(i.Offset, (ulong)i.Data.Length);
                _length = checked(_length - (ulong)i.Data.Length);
                CoalesceSegments();
                break;
            }
            case DeleteEdit d:
            {
                var segment = FindSegment(d.Offset == _length ? d.Offset - 1 : d.Offset);
                SplitAndInsert(segment, d.Offset - segment.LogicalStart, d.Deleted);
                _length = checked(_length + (ulong)d.Deleted.Length);
                CoalesceSegments();
                break;
            }
        }
        _edits.RemoveAt(_edits.Count - 1);
        return edit;
    }

    // ---------- 书签 ----------

    public void AddBookmark(ulong offset, string? label, string? colorKey)
        => _bookmarks[offset] = new Bookmark(offset, label ?? "", colorKey);

    public bool RemoveBookmark(ulong offset) => _bookmarks.Remove(offset);

    public IReadOnlyList<Bookmark> Bookmarks => _bookmarks.Values.OrderBy(b => b.Offset).ToList();

    public bool HasBookmark(ulong offset) => _bookmarks.ContainsKey(offset);

    // ---------- 内部：段操作 ----------

    private Segment FindSegment(ulong logicalOffset)
    {
        // 二分：找最后一个 LogicalStart <= offset 的段
        int lo = 0, hi = _segments.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_segments[mid].LogicalStart <= logicalOffset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return _segments[lo];
    }

    private void SplitAndInsert(Segment at, ulong innerOffset, byte[] data)
    {
        var index = _segments.IndexOf(at);
        var left = at;
        var insertedStart = left.LogicalStart + innerOffset;
        var rightLength = left.Length - innerOffset;

        if (innerOffset == 0)
        {
            // 段首插入：left 整体后移 data.Length（保持段 LogicalStart 严格递增）
            left.LogicalStart = insertedStart + (ulong)data.Length;
            var leading = new Segment
            {
                LogicalStart = insertedStart,
                Length = (ulong)data.Length,
                PhysicalStart = 0,
                IsInserted = true,
                Data = data,
            };
            _segments.Insert(index, leading);
            CoalesceSegments();
            return;
        }

        // 段内/段尾插入：拆出 right = left[innerOffset..]（插入数据之后）
        if (rightLength > 0)
        {
            var right = new Segment
            {
                LogicalStart = insertedStart + (ulong)data.Length,
                Length = rightLength,
                PhysicalStart = left.PhysicalStart + innerOffset,
                IsInserted = left.IsInserted,
                Data = left.IsInserted ? left.Data![checked((int)innerOffset)..] : null,
            };
            if (left.IsInserted)
            {
                left.Data = left.Data![..checked((int)innerOffset)];
            }
            left.Length = innerOffset;
            _segments.Insert(index + 1, right);
        }

        var inserted = new Segment
        {
            LogicalStart = insertedStart,
            Length = (ulong)data.Length,
            PhysicalStart = 0,
            IsInserted = true,
            Data = data,
        };
        _segments.Insert(index + 1, inserted);
        CoalesceSegments();
    }

    private void RemoveFromInserted(Segment segment, int innerOffset, int count)
    {
        var remaining = segment.Data!.Length - count;
        if (remaining == 0)
        {
            RemoveSegment(segment);
            return;
        }
        var newData = new byte[remaining];
        segment.Data.AsSpan(0, innerOffset).CopyTo(newData);
        segment.Data.AsSpan(innerOffset + count).CopyTo(newData.AsSpan(innerOffset));
        segment.Data = newData;
        segment.Length = (ulong)remaining;
    }

    private void RemoveSegment(Segment segment)
    {
        _segments.Remove(segment);
        CoalesceSegments();
    }

    /// <summary>合并相邻的 Original 段（删除后产生空洞时），并重建全部段 LogicalStart（保持严格递增且连续）。</summary>
    private void CoalesceSegments()
    {
        for (var i = _segments.Count - 1; i > 0; i--)
        {
            var prev = _segments[i - 1];
            var cur = _segments[i];
            if (!prev.IsInserted && !cur.IsInserted &&
                prev.PhysicalStart + prev.Length == cur.PhysicalStart)
            {
                prev.Length += cur.Length;
                _segments.RemoveAt(i);
            }
        }

        // 重建 LogicalStart：段数量与编辑数同阶，O(n) 重算彻底避免偏移不同步
        ulong pos = 0;
        foreach (var s in _segments)
        {
            s.LogicalStart = pos;
            pos = checked(pos + s.Length);
        }
    }

    // ---------- 内部：分页缓存 ----------

    private byte ReadPhysicalByte(ulong physicalOffset)
    {
        var pageIndex = (long)(physicalOffset / PageSize);
        var inPage = (int)(physicalOffset % PageSize);
        var page = GetPage(pageIndex);
        return page.Data[inPage];
    }

    private void ReadPhysical(ulong physicalOffset, Span<byte> destination, int count)
    {
        var pos = physicalOffset;
        var written = 0;
        while (written < count)
        {
            var pageIndex = (long)(pos / PageSize);
            var inPage = (int)(pos % PageSize);
            var page = GetPage(pageIndex);
            var take = Math.Min(PageSize - inPage, count - written);
            page.Data.AsSpan(inPage, take).CopyTo(destination[written..]);
            written += take;
            pos += (ulong)take;
        }
    }

    public bool IsRangeAvailable(ulong offset, int length)
    {
        if (length < 0 || offset + (ulong)length > _length) return false;
        for (var i = 0; i < length; i++)
        {
            var logical = offset + (ulong)i;
            var segment = FindSegment(logical);
            if (segment.IsInserted) continue;
            var physical = segment.PhysicalStart + logical - segment.LogicalStart;
            var page = GetPage((long)(physical / PageSize));
            if (page.Unavailable[(int)(physical % PageSize)]) return false;
        }
        return true;
    }

    private void WriteLogical(ulong logicalOffset, ReadOnlySpan<byte> data)
    {
        var position = logicalOffset;
        var written = 0;
        while (written < data.Length)
        {
            var segment = FindSegment(position);
            var inner = position - segment.LogicalStart;
            var take = (int)Math.Min(segment.Length - inner, (ulong)(data.Length - written));
            if (segment.IsInserted)
            {
                data.Slice(written, take).CopyTo(segment.Data!.AsSpan((int)inner, take));
            }
            else
            {
                var physical = checked(segment.PhysicalStart + inner);
                if (!IsPhysicalRangeAvailable(physical, take))
                {
                    throw new IOException("编辑范围包含不可读内存，已拒绝写入。");
                }
                WritePhysical(physical, data.Slice(written, take));
            }
            position += (ulong)take;
            written += take;
        }
    }

    private bool IsPhysicalRangeAvailable(ulong physicalOffset, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var physical = physicalOffset + (ulong)i;
            var page = GetPage((long)(physical / PageSize));
            if (page.Unavailable[(int)(physical % PageSize)]) return false;
        }
        return true;
    }

    private void WritePhysical(ulong physicalOffset, ReadOnlySpan<byte> data)
    {
        var pos = physicalOffset;
        var written = 0;
        while (written < data.Length)
        {
            var pageIndex = (long)(pos / PageSize);
            var inPage = (int)(pos % PageSize);
            var page = GetPage(pageIndex);
            var take = Math.Min(PageSize - inPage, data.Length - written);
            data.Slice(written, take).CopyTo(page.Data.AsSpan(inPage, take));
            written += take;
            pos += (ulong)take;
        }
    }

    private Page GetPage(long pageIndex)
    {
        if (_pages.TryGetValue(pageIndex, out var page))
        {
            page.LastAccess = Environment.TickCount64;
            return page;
        }

        page = new Page();
        LoadPage(page, pageIndex);
        _pages[pageIndex] = page;
        _cacheBytes += PageSize;
        EvictIfNeeded();
        return page;
    }

    private void LoadPage(Page page, long pageIndex)
    {
        var offset = (ulong)pageIndex * PageSize;
        var destination = page.Data.AsMemory();
        // 源读取（64 位偏移）
        var read = _source.ReadAsync(offset, destination, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        var completed = read.CompletedLength;
        page.Data.AsSpan(completed).Clear();
        Array.Clear(page.Unavailable);
        foreach (var range in read.UnavailableRanges)
        {
            var start = Math.Max(range.Offset, offset);
            var end = Math.Min(range.EndExclusive, offset + PageSize);
            for (var position = start; position < end; position++)
            {
                page.Unavailable[(int)(position - offset)] = true;
            }
        }
        page.Loaded = true;
    }

    private void EvictIfNeeded()
    {
        while (_cacheBytes > MaxCacheBytes && _pages.Count > 1)
        {
            // 逐出最久未访问的已加载页
            long oldest = long.MaxValue;
            long oldestKey = 0;
            foreach (var (key, page) in _pages)
            {
                if (page.LastAccess < oldest)
                {
                    oldest = page.LastAccess;
                    oldestKey = key;
                }
            }
            _pages.Remove(oldestKey);
            _cacheBytes -= PageSize;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
