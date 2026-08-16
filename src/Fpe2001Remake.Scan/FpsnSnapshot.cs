using System.Text;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.Scan;

/// <summary>
/// FPSN v1 快照文件（规格 §8：沿用 v1 的 FPSN v1 快照格式）。
/// 布局（128 字节头 + 定长记录）：
///   0   "FPSN"          4B
///   4   version u32=1   4B
///   8   dataType u32    4B
///   12  endianness u32  4B
///   16  valueSize u32   4B
///   20  reserved        4B
///   24  recordCount u64 8B
///   32  快照时间戳 UTF-8 96B
///   128 记录区：address u64 + value (valueSize) 定长紧凑排列
/// 记录寻址 = 128 + index * stride；stride = 8 + valueSize。
/// </summary>
public sealed class FpsnSnapshot : IDisposable
{
    public const string Magic = "FPSN";
    public const uint Version = 1;
    public const int HeaderSize = 128;

    /// <summary>写缓冲大小：候选量百万级时，逐条小 IO 是性能瓶颈，积攒后批量写入。</summary>
    private const int WriteBufferSize = 256 * 1024;

    private readonly string _path;
    private FileStream _stream;
    private readonly bool _writable;
    private ulong _count;
    private byte[]? _writeBuffer;
    private int _writeLen;

    private FpsnSnapshot(string path, FileStream stream, bool writable, ulong count,
        ScanDataType dataType, Endianness endianness, int valueSize)
    {
        _path = path;
        _stream = stream;
        _writable = writable;
        _count = count;
        DataType = dataType;
        Endianness = endianness;
        ValueSize = valueSize;
    }

    public string Path => _path;
    public ScanDataType DataType { get; }
    public Endianness Endianness { get; }
    public int ValueSize { get; }
    public int Stride => 8 + ValueSize;
    public ulong Count => _count;
    public ulong Bytes => (ulong)HeaderSize + _count * (ulong)Stride;

    public static FpsnSnapshot Create(string path, ScanDataType dataType, Endianness endianness)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes(Magic).CopyTo(header, 0);
        WriteU32(header, 4, Version);
        WriteU32(header, 8, (uint)dataType);
        WriteU32(header, 12, (uint)endianness);
        WriteU32(header, 16, (uint)ValueCodec.SizeOf(dataType));
        WriteU32(header, 20, 0);
        WriteU64(header, 24, 0);
        var stamp = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("o"));
        stamp.AsSpan(0, Math.Min(stamp.Length, 96)).CopyTo(header.AsSpan(32));
        stream.Write(header);
        stream.Flush();
        return new FpsnSnapshot(path, stream, true, 0, dataType, endianness, ValueCodec.SizeOf(dataType));
    }

    public static FpsnSnapshot Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            var header = new byte[HeaderSize];
            var read = stream.Read(header, 0, HeaderSize);
            if (read < HeaderSize)
            {
                throw new InvalidDataException("FPSN 快照头不完整。");
            }
            if (Encoding.ASCII.GetString(header, 0, 4) != Magic)
            {
                throw new InvalidDataException("不是 FPSN 快照。");
            }
            if (ReadU32(header, 4) != Version)
            {
                throw new InvalidDataException($"不支持的 FPSN 版本：{ReadU32(header, 4)}");
            }
            var dataType = (ScanDataType)ReadU32(header, 8);
            var endianness = (Endianness)ReadU32(header, 12);
            var valueSize = (int)ReadU32(header, 16);
            var count = ReadU64(header, 24);
            return new FpsnSnapshot(path, stream, false, count, dataType, endianness, valueSize);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Append(ulong address, ReadOnlySpan<byte> value)
    {
        if (!_writable)
        {
            throw new InvalidOperationException("快照以只读方式打开。");
        }
        if (value.Length != ValueSize)
        {
            throw new ArgumentException($"值长度 {value.Length} 与快照宽度 {ValueSize} 不符。");
        }
        // 积攒到写缓冲，满 256 KiB 才落盘：避免每候选两次小 Write 的系统调用开销
        if (_writeBuffer is null || _writeLen + 8 + value.Length > _writeBuffer.Length)
        {
            FlushWriteBuffer();
            _writeBuffer ??= new byte[WriteBufferSize];
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(_writeBuffer.AsSpan(_writeLen), address);
        _writeLen += 8;
        value.CopyTo(_writeBuffer.AsSpan(_writeLen));
        _writeLen += value.Length;
        _count++;
    }

    /// <summary>
    /// 顺序遍历全部记录（大块读 + 回调，零逐条 IO、零逐条分配）。
    /// onRecord 收到的 value span 仅在回调期间有效；返回 false 提前终止遍历。
    /// </summary>
    public void ReadAllBulk(Func<ulong, ReadOnlySpan<byte>, bool> onRecord)
    {
        _stream.Seek(HeaderSize, SeekOrigin.Begin);
        var block = new byte[ReadBlockSize + Stride - 1];
        long totalRead = 0;
        long remaining = checked((long)(_count * (ulong)Stride));
        var tail = 0;
        var stop = false;

        while (totalRead < remaining && !stop)
        {
            var toRead = (int)Math.Min(remaining - totalRead, ReadBlockSize);
            var read = _stream.Read(block, tail, toRead);
            if (read <= 0)
            {
                break;
            }
            var window = tail + read;
            var pos = 0;
            var last = window - Stride;
            while (pos <= last)
            {
                var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(pos));
                if (!onRecord(address, block.AsSpan(pos + 8, ValueSize)))
                {
                    stop = true;
                    break;
                }
                pos += Stride;
            }
            tail = window - pos; // 不足一条记录的尾部，下一轮补上
            if (tail > 0)
            {
                Buffer.BlockCopy(block, pos, block, 0, tail);
            }
            totalRead += read;
        }
    }

    private const int ReadBlockSize = 1024 * 1024;

    /// <summary>把写缓冲落盘（保持流位置语义：写路径仅顺序追加）。</summary>
    private void FlushWriteBuffer()
    {
        if (!_writable || _writeLen == 0)
        {
            return;
        }
        _stream.Write(_writeBuffer!, 0, _writeLen);
        _writeLen = 0;
    }

    /// <summary>关闭当前写句柄（供替换后清理）。</summary>
    public void CloseStream()
    {
        if (_writable)
        {
            WriteCountBack();
        }
        _stream.Dispose();
        _stream = null!;
    }

    public void Dispose()
    {
        if (_writable && _stream is not null)
        {
            WriteCountBack();
        }
        _stream?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>把当前记录数写回头部（供再次扫描/候选读取使用）。</summary>
    private void WriteCountBack()
    {
        if (_stream is null) return;
        FlushWriteBuffer();
        Span<byte> buf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf, _count);
        var pos = _stream.Position;
        _stream.Seek(24, SeekOrigin.Begin);
        _stream.Write(buf);
        _stream.Flush();
        _stream.Seek(pos, SeekOrigin.Begin);
    }

    private static void WriteU32(byte[] buffer, int offset, uint value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteU64(byte[] buffer, int offset, ulong value)
        => System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset), value);

    private static uint ReadU32(byte[] buffer, int offset)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));

    private static ulong ReadU64(byte[] buffer, int offset)
        => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
}
