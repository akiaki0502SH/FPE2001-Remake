using System.Diagnostics;
using System.Runtime.CompilerServices;
using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.ByteSources.File;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Fpe2001Remake.Scan;

namespace Fpe2001Remake.FileScan;

/// <summary>
/// 文件目录与内容扫描服务（规格 §7）：
/// 递归枚举（默认不跟随 reparse point）+ 内容谓词匹配（Hex ?? 掩码/Text/Integer/Float）。
/// 单个文件失败计入 Skipped/Errors，不让整个任务失败；支持进度与取消。
/// </summary>
public sealed class FileScanService : IFileScanService
{
    private const int ChunkSize = 256 * 1024;

    public async IAsyncEnumerable<FileMatch> ScanAsync(
        FileScanRequest request,
        IProgress<FileScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(request.RootPath))
        {
            yield break;
        }

        var sw = Stopwatch.StartNew();
        var pattern = CompilePredicate(request.Predicate);
        var maxBytes = request.MaxBytes ?? ulong.MaxValue;
        long filesVisited = 0, skipped = 0, errors = 0, matches = 0;
        ulong bytesRead = 0;
        var matchKind = request.Predicate?.DataType.ToString() ?? "none";

        var pending = new Stack<string>();
        pending.Push(request.RootPath);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            string[] files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => MatchesFilter(f, request.Filter))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors++;
                Report(progress, sw, filesVisited, bytesRead, matches, skipped, errors);
                continue;
            }

            var matchesFromDir = new List<FileMatch>();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                filesVisited++;
                if (bytesRead >= maxBytes)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    var fileMatches = await ScanFileAsync(
                        file, pattern, request.Predicate, request.MaxMatchesPerFile, matchKind, ct);
                    matchesFromDir.AddRange(fileMatches);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    skipped++;
                }
                Report(progress, sw, filesVisited, bytesRead, matches + matchesFromDir.Count, skipped, errors);
            }
            foreach (var m in matchesFromDir)
            {
                matches++;
                yield return m;
            }

            if (request.Recursive)
            {
                string[] subDirs;
                try
                {
                    subDirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors++;
                    Report(progress, sw, filesVisited, bytesRead, matches, skipped, errors);
                    continue;
                }
                foreach (var sub in subDirs)
                {
                    if (!request.FollowReparsePoints &&
                        (File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }
                    pending.Push(sub);
                }
            }
        }
    }

    // ---------- 内部 ----------

    private static bool MatchesFilter(string path, FileFilter? filter)
    {
        if (filter is null) return true;

        if (filter.ExtensionPattern is { Length: > 0 } extPattern)
        {
            var matched = false;
            var fileName = System.IO.Path.GetFileName(path);
            foreach (var part in extPattern.Split(';', ','))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                if (p.StartsWith('.'))
                {
                    p = "*" + p;
                }
                // 先对文件名匹配，再对完整路径匹配（无通配符时文件名精确匹配）
                if (WildcardMatch(fileName, p, StringComparison.OrdinalIgnoreCase) ||
                    WildcardMatch(path, p, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;
        }

        try
        {
            var info = new FileInfo(path);
            if (filter.MinSize is ulong min && (ulong)info.Length < min) return false;
            if (filter.MaxSize is ulong max && (ulong)info.Length > max) return false;
            if (filter.ModifiedAfter is DateTimeOffset after && info.LastWriteTimeUtc < after.UtcDateTime) return false;
            if (filter.ModifiedBefore is DateTimeOffset before && info.LastWriteTimeUtc > before.UtcDateTime) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }

    /// <summary>简单通配符匹配（* 和 ?，文件名级）。</summary>
    private static bool WildcardMatch(string input, string pattern, StringComparison comparison)
    {
        var inputSpan = input.AsSpan();
        var patternSpan = pattern.AsSpan();
        var i = 0;
        var p = 0;
        int star = -1, mark = 0;

        while (i < inputSpan.Length)
        {
            if (p < patternSpan.Length && (patternSpan[p] == '?' || patternSpan[p] == inputSpan[i]))
            {
                i++;
                p++;
            }
            else if (p < patternSpan.Length && patternSpan[p] == '*')
            {
                star = p++;
                mark = i;
            }
            else if (star >= 0)
            {
                p = star + 1;
                i = ++mark;
            }
            else
            {
                return false;
            }
        }
        while (p < patternSpan.Length && patternSpan[p] == '*')
        {
            p++;
        }
        return p == patternSpan.Length;
    }

    /// <summary>谓词 → 匹配模式（byte?[]：null = 通配）。</summary>
    private static byte?[]? CompilePredicate(ContentPredicate? predicate)
    {
        if (predicate is null) return null;

        if (predicate.IsWildcardHex)
        {
            return SearchEngine.ParseHexPattern(predicate.Pattern);
        }

        var bytes = predicate.DataType switch
        {
            ScanDataType.UInt8 or ScanDataType.UInt16 or ScanDataType.UInt32 or ScanDataType.UInt64
                => EncodeInteger(predicate),
            ScanDataType.Float32 or ScanDataType.Float64 => EncodeFloat(predicate),
            ScanDataType.Bytes => Convert.FromHexString(predicate.Pattern.Replace(" ", "")),
            _ => TextCodec.Encode(predicate.Pattern, predicate.Encoding ?? "ASCII"),
        };
        if (!predicate.MatchCase)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] is >= (byte)'A' and <= (byte)'Z')
                {
                    bytes[i] = (byte)(bytes[i] + 32);
                }
            }
        }
        return bytes.Select(b => (byte?)b).ToArray();
    }

    private static byte[] EncodeInteger(ContentPredicate predicate)
    {
        if (!ulong.TryParse(predicate.Pattern, out var value))
        {
            throw new ArgumentException($"无效的整数模式：{predicate.Pattern}");
        }
        var width = predicate.DataType switch
        {
            ScanDataType.UInt8 => 1,
            ScanDataType.UInt16 => 2,
            ScanDataType.UInt32 => 4,
            _ => 8,
        };
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            var shift = predicate.Endianness == Endianness.LittleEndian ? i * 8 : (width - 1 - i) * 8;
            bytes[i] = (byte)((value >> shift) & 0xFF);
        }
        return bytes;
    }

    private static byte[] EncodeFloat(ContentPredicate predicate)
    {
        var is32 = predicate.DataType == ScanDataType.Float32;
        var value = double.Parse(predicate.Pattern, System.Globalization.CultureInfo.InvariantCulture);
        byte[] bytes = is32 ? BitConverter.GetBytes((float)value) : BitConverter.GetBytes(value);
        if (predicate.Endianness == Endianness.BigEndian)
        {
            Array.Reverse(bytes);
        }
        return bytes;
    }

    private static async ValueTask<List<FileMatch>> ScanFileAsync(
        string path,
        byte?[]? pattern,
        ContentPredicate? predicate,
        int maxMatchesPerFile,
        string matchKind,
        CancellationToken ct)
    {
        var result = new List<FileMatch>();
        if (pattern is null || pattern.Length == 0)
        {
            return result;
        }

        var versionToken = FileVersionToken.Compute(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            ChunkSize, FileOptions.SequentialScan);

        var buffer = new byte[ChunkSize + pattern.Length - 1];
        ulong pos = 0;
        var overlap = 0;
        var fileMatches = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var toRead = buffer.Length - overlap;
            var read = await fs.ReadAsync(buffer.AsMemory(overlap, toRead), ct);
            var window = overlap + read;
            if (window <= 0)
            {
                break;
            }

            // 块内滑动匹配
            var limit = window - pattern.Length;
            for (var i = 0; i <= limit; i++)
            {
                var hit = true;
                for (var p = 0; p < pattern.Length; p++)
                {
                    if (pattern[p] is byte expected &&
                        buffer[i + p] != (predicate?.MatchCase == false ? FoldAscii(expected) : expected))
                    {
                        hit = false;
                        break;
                    }
                }
                if (hit)
                {
                    var offset = pos + (ulong)i;
                    var preview = Convert.ToHexString(buffer.AsSpan(i, Math.Min(16, pattern.Length)));
                    result.Add(new FileMatch(path, offset, pattern.Length, preview, versionToken, matchKind));
                    fileMatches++;
                    if (maxMatchesPerFile > 0 && fileMatches >= maxMatchesPerFile)
                    {
                        return result;
                    }
                    i += pattern.Length - 1; // 非重叠推进
                }
            }

            if (read < toRead)
            {
                break;
            }

            // 保留尾部重叠
            overlap = pattern.Length - 1;
            if (overlap > 0)
            {
                Buffer.BlockCopy(buffer, window - overlap, buffer, 0, overlap);
            }
            pos += (ulong)(window - overlap);
        }

        return result;
    }

    private static byte FoldAscii(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;

    private static void Report(
        IProgress<FileScanProgress>? progress,
        Stopwatch sw,
        long filesVisited,
        ulong bytesRead,
        long matches,
        long skipped,
        long errors)
    {
        progress?.Report(new FileScanProgress(filesVisited, bytesRead, matches, skipped, errors, sw.Elapsed));
    }
}
