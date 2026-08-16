using Fpe2001Remake.Contracts;
using Fpe2001Remake.FileScan;
using Xunit;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>P3 验收：文件目录与内容扫描（规格 §7）。</summary>
public sealed class FileScanIntegrationTests : IDisposable
{
    private readonly string _root;

    public FileScanIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllBytes(Path.Combine(_root, "a.bin"), [0x00, 0x11, 0xDE, 0xAD, 0xBE, 0xEF, 0x22]);
        File.WriteAllBytes(Path.Combine(_root, "sub", "b.bin"), [0xDE, 0xAD, 0xBE, 0xEF, 0x99]);
        File.WriteAllBytes(Path.Combine(_root, "c.txt"), "no match here"u8.ToArray());
    }

    [Fact]
    public async Task Scan_Recursive_FindsMatchesInSubdirs()
    {
        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: true, FollowReparsePoints: false, Filter: null,
            Predicate: new ContentPredicate(ScanDataType.Bytes, "DE AD BE EF", IsWildcardHex: true),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 10);

        var hits = new List<FileMatch>();
        await foreach (var m in service.ScanAsync(request, null, CancellationToken.None))
        {
            hits.Add(m);
        }

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Path.EndsWith("a.bin") && h.Offset == 2 && h.Length == 4);
        Assert.Contains(hits, h => h.Path.EndsWith(Path.Combine("sub", "b.bin")) && h.Offset == 0);
    }

    [Fact]
    public async Task Scan_NonRecursive_OnlyRootFiles()
    {
        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: false, FollowReparsePoints: false, Filter: null,
            Predicate: new ContentPredicate(ScanDataType.Bytes, "DE AD BE EF", IsWildcardHex: true),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 10);

        var hits = new List<FileMatch>();
        await foreach (var m in service.ScanAsync(request, null, CancellationToken.None))
        {
            hits.Add(m);
        }

        Assert.Single(hits);
        Assert.EndsWith("a.bin", hits[0].Path);
    }

    [Fact]
    public async Task Scan_FilterExtension_OnlySelected()
    {
        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: true, FollowReparsePoints: false,
            Filter: new FileFilter(ExtensionPattern: "*.bin"),
            Predicate: new ContentPredicate(ScanDataType.Bytes, "DE AD BE EF", IsWildcardHex: true),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 10);

        var hits = new List<FileMatch>();
        await foreach (var m in service.ScanAsync(request, null, CancellationToken.None))
        {
            hits.Add(m);
        }

        Assert.Equal(2, hits.Count); // c.txt 被过滤
    }

    [Fact]
    public async Task Scan_TextPredicate_CaseInsensitive()
    {
        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: true, FollowReparsePoints: false, Filter: null,
            Predicate: new ContentPredicate(ScanDataType.Text, "match here", Encoding: "ASCII", MatchCase: false),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 10);

        var hits = new List<FileMatch>();
        await foreach (var m in service.ScanAsync(request, null, CancellationToken.None))
        {
            hits.Add(m);
        }

        Assert.Single(hits);
        Assert.EndsWith("c.txt", hits[0].Path);
    }

    [Fact]
    public async Task Scan_MaxMatchesPerFile_LimitsHits()
    {
        var path = Path.Combine(_root, "many.bin");
        var data = new byte[1024];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = 0xAA;
            data[i + 1] = 0xBB;
            data[i + 2] = 0xCC;
            data[i + 3] = 0xDD;
        }
        File.WriteAllBytes(path, data);

        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: true, FollowReparsePoints: false, Filter: new FileFilter("*.bin"),
            Predicate: new ContentPredicate(ScanDataType.Bytes, "AA BB CC DD", IsWildcardHex: true),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 3);

        var hits = new List<FileMatch>();
        await foreach (var m in service.ScanAsync(request, null, CancellationToken.None))
        {
            hits.Add(m);
        }

        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public async Task Scan_Cancellation_StopsEarly()
    {
        // 64 MiB 数据保证扫描耗时远超取消延迟
        var big = Path.Combine(_root, "big.bin");
        var data = new byte[64 * 1024 * 1024];
        for (var i = 0; i < data.Length; i += 512)
        {
            data[i] = 0xAA;
            data[i + 1] = 0xBB;
        }
        File.WriteAllBytes(big, data);

        using var cts = new CancellationTokenSource();
        var service = new FileScanService();
        var request = new FileScanRequest(
            _root, Recursive: true, FollowReparsePoints: false, Filter: new FileFilter("big.bin"),
            Predicate: new ContentPredicate(ScanDataType.Bytes, "AA BB", IsWildcardHex: true),
            MaxConcurrency: 2, MaxBytes: null, MaxMatchesPerFile: 0 /* 不限 */);

        var task = ConsumeAsync(service, request, cts.Token);
        await Task.Delay(10);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private static async Task ConsumeAsync(FileScanService service, FileScanRequest request, CancellationToken ct)
    {
        await foreach (var _ in service.ScanAsync(request, null, ct))
        {
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不失败测试
        }
    }
}
