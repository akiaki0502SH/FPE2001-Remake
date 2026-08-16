using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;

namespace Fpe2001Remake.ByteSources.File;

/// <summary>
/// 文件字节源（规格 §4/§5.2）：
/// 长路径、64 位偏移、共享模式（允许外部修改以检测变化）、
/// 安全提交：复验 VersionToken → 备份 → 同目录临时文件 → 流式应用 → 原子替换 → 新 Token。
/// </summary>
public sealed class FileByteSource : IEditableByteSource
{
    private const int CopyBlockSize = 1024 * 1024;

    private readonly string _path;
    private readonly FileStream _stream;
    private readonly ByteSourceIdentity _identity;
    private readonly bool _readOnly;
    private readonly object _readGate = new();
    private ulong _length;
    private bool _disposed;

    private FileByteSource(string path, FileStream stream, ByteSourceIdentity identity, bool readOnly)
    {
        _path = path;
        _stream = stream;
        _identity = identity;
        _readOnly = readOnly;
        _length = identity.Length ?? 0;
    }

    public ByteSourceIdentity Identity => _identity;

    /// <summary>当前长度（提交后更新）。</summary>
    public ulong Length => _length;

    public ByteSourceCapabilities Capabilities => _readOnly
        ? ByteSourceCapabilities.Read | ByteSourceCapabilities.KnownLength | ByteSourceCapabilities.Refresh
        : ByteSourceCapabilities.Read | ByteSourceCapabilities.Overwrite | ByteSourceCapabilities.Insert |
          ByteSourceCapabilities.Delete | ByteSourceCapabilities.KnownLength |
          ByteSourceCapabilities.AtomicCommit | ByteSourceCapabilities.Refresh;

    public string Path => _path;

    public static async ValueTask<FileByteSource> OpenAsync(string path, bool readOnly, CancellationToken ct)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("文件不存在。", fullPath);
        }

        // 共享读写：允许外部修改，提交前检测（规格：外部变化检测）
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, CopyBlockSize, FileOptions.SequentialScan);

        var token = FileVersionToken.Compute(fullPath);
        var identity = new ByteSourceIdentity(
            Guid.NewGuid(), ByteSourceKind.File, fullPath, (ulong)info.Length, token);

        return new FileByteSource(fullPath, stream, identity, readOnly);
    }

    public ValueTask<ByteReadResult> ReadAsync(ulong offset, Memory<byte> destination, CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileByteSource));
        }
        if (destination.IsEmpty)
        {
            return ValueTask.FromResult(ByteReadResult.Full(0));
        }
        if (offset >= _length)
        {
            return ValueTask.FromResult(new ByteReadResult(0, [new ByteRange(offset, (ulong)destination.Length)], false));
        }

        var count = (int)Math.Min((ulong)destination.Length, _length - offset);
        lock (_readGate)
        {
            _stream.Seek((long)offset, SeekOrigin.Begin);
            var read = _stream.Read(destination.Span[..count]);
            return ValueTask.FromResult(ByteReadResult.Full(read));
        }
    }

    public async ValueTask<CommitResult> CommitAsync(ChangeSet changes, CommitOptions options, CancellationToken ct)
    {
        if (_readOnly)
        {
            return CommitResult.Fail(CommitErrorKind.PermissionDenied, "文件以只读方式打开。");
        }

        // 1. 复验 VersionToken（规格 §5.2）
        if (options.VerifyVersionToken)
        {
            var current = FileVersionToken.Compute(_path);
            if (!string.Equals(current, changes.BaseVersionToken, StringComparison.Ordinal))
            {
                return CommitResult.Fail(CommitErrorKind.VersionTokenMismatch,
                    "文件在会话期间被外部修改；请重新加载或另存。");
            }
        }

        // 2. 备份
        var dir = System.IO.Path.GetDirectoryName(_path)!;
        var backupPath = options.BackupPath ?? System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileName(_path)}.{DateTime.Now:yyyyMMddHHmmss}.bak");
        try
        {
            System.IO.File.Copy(_path, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (options.CreateBackup)
            {
                return CommitResult.Fail(CommitErrorKind.PermissionDenied,
                    $"无法创建备份：{ex.Message}", null);
            }
        }

        // 3. 同目录临时文件，流式应用变更
        var tempPath = System.IO.Path.Combine(dir, $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await Task.Run(() => ApplyEdits(_path, tempPath, changes, ct), ct);
            if (ct.IsCancellationRequested)
            {
                TryDelete(tempPath);
                return CommitResult.Fail(CommitErrorKind.Cancelled, "提交已取消。", backupPath);
            }

            // 4. 原子替换（ReplaceFile 语义）
            System.IO.File.Replace(tempPath, _path, backupPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            return CommitResult.Fail(CommitErrorKind.IoError, $"提交失败：{ex.Message}", backupPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return CommitResult.Fail(CommitErrorKind.Cancelled, "提交已取消。", backupPath);
        }

        // 5. 重新打开验证新 VersionToken
        try
        {
            var newToken = FileVersionToken.Compute(_path);
            _length = (ulong)new FileInfo(_path).Length;
            return CommitResult.Ok(newToken, backupPath);
        }
        catch (Exception ex)
        {
            return CommitResult.Fail(CommitErrorKind.IoError,
                $"替换成功但无法验证新文件：{ex.Message}", backupPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _stream.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // ---------- 内部 ----------

    private static void ApplyEdits(string sourcePath, string tempPath, ChangeSet changes, CancellationToken ct)
    {
        using var reader = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var writer = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBlockSize);

        var sorted = changes.Edits.OrderBy(e => e.Offset).ToList();
        ulong sourcePos = 0;
        ulong insertedTotal = 0;
        ulong deletedTotal = 0;
        var buffer = new byte[CopyBlockSize];

        foreach (var edit in sorted)
        {
            ct.ThrowIfCancellationRequested();
            // 逻辑偏移 → 源偏移：S = L - 已插入 + 已删除
            var sourceTarget = checked(edit.Offset - insertedTotal + deletedTotal);
            CopyRange(reader, writer, sourcePos, sourceTarget, buffer);
            sourcePos = sourceTarget;

            switch (edit)
            {
                case OverwriteEdit o:
                    writer.Write(o.After);
                    sourcePos = checked(sourcePos + (ulong)o.After.Length);
                    break;
                case InsertEdit i:
                    writer.Write(i.Data);
                    insertedTotal += (ulong)i.Data.Length;
                    break;
                case DeleteEdit d:
                    sourcePos = checked(sourcePos + (ulong)d.Deleted.Length);
                    deletedTotal += (ulong)d.Deleted.Length;
                    break;
            }
        }

        // 尾部：输出到 ResultLength
        var written = (ulong)writer.Length;
        if (changes.ResultLength > written)
        {
            CopyRange(reader, writer, sourcePos, checked(sourcePos + (changes.ResultLength - written)), buffer);
        }
        writer.Flush(flushToDisk: true);
    }

    private static void CopyRange(FileStream reader, FileStream writer, ulong from, ulong to, byte[] buffer)
    {
        if (to <= from) return;
        reader.Seek((long)from, SeekOrigin.Begin);
        var remaining = to - from;
        while (remaining > 0)
        {
            var take = (int)Math.Min((ulong)buffer.Length, remaining);
            var read = reader.Read(buffer, 0, take);
            if (read <= 0)
            {
                throw new IOException($"读取源文件失败（偏移 0x{from:X16} 之后）。");
            }
            writer.Write(buffer, 0, read);
            remaining -= (ulong)read;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch
        {
            // 保留临时文件供恢复（规格：失败保留临时文件与备份）
        }
    }
}
