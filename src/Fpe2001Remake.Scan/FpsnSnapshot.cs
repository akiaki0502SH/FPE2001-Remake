using System.Buffers;
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

    private readonly string _path;
    private FileStream _stream;
    private readonly bool _writable;
    private ulong _count;

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
        // 顺序写：FileStream 维护 Position，避免 Seek(End) 引发的缓冲 flush（候选量大时是性能灾难）
        Span<byte> buf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf, address);
        _stream.Write(buf);
        _stream.Write(value);
        _count++;
    }

    /// <summary>遍历全部记录。</summary>
    public IEnumerable<(ulong Address, byte[] Value)> ReadAll()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Stride);
        try
        {
            _stream.Seek(HeaderSize, SeekOrigin.Begin);
            for (ulong i = 0; i < _count; i++)
            {
                var read = _stream.Read(buffer, 0, Stride);
                if (read < Stride)
                {
                    yield break;
                }
                var address = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer);
                var value = new byte[ValueSize];
                Buffer.BlockCopy(buffer, 8, value, 0, ValueSize);
                yield return (address, value);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
