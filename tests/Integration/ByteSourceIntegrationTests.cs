using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.ByteSources.File;
using Fpe2001Remake.ByteSources.Process;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>
/// P2 验收（规格 15.1 / 测试矩阵 HEX-001~004）：
/// 大文件高偏移、插入/删除组合、外部修改保护、进程源约束。
/// </summary>
public sealed class ByteSourceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _files = [];

    public ByteSourceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "hex-tests");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task Hex001_SparseFile_Beyond4GiB_EditUndoCommit()
    {
        // HEX-001：>4GB 稀疏文件高偏移 → 跳转、读取、覆盖、撤销、另存后字节与长度正确
        var path = NewFile("hex001.bin");
        const ulong fileLength = 5UL * 1024 * 1024 * 1024; // 5 GiB
        const ulong targetOffset = 4UL * 1024 * 1024 * 1024 + 0x1000; // 4 GiB + 4 KiB

        // 创建稀疏大文件并在高偏移写入标记
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            fs.SetLength((long)fileLength);
            fs.Seek((long)targetOffset, SeekOrigin.Begin);
            fs.Write([0xDE, 0xAD, 0xBE, 0xEF]);
        }

        await using (var source = await FileByteSource.OpenAsync(path, readOnly: false, CancellationToken.None))
        {
            Assert.Equal(fileLength, source.Length);
            Assert.True(source.Capabilities.HasFlag(ByteSourceCapabilities.AtomicCommit));

            using var doc = new HexDocument(source);
            Assert.Equal(fileLength, doc.Length);

            // 高偏移读取
            var high = new byte[4];
            doc.ReadBytes(targetOffset, high);
            Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], high);

            // 覆盖
            doc.Overwrite(targetOffset, [0x11, 0x22, 0x33, 0x44]);
            doc.ReadBytes(targetOffset, high);
            Assert.Equal([0x11, 0x22, 0x33, 0x44], high);

            // 撤销 → 恢复原始
            doc.Undo();
            doc.ReadBytes(targetOffset, high);
            Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], high);

            // 重新覆盖并提交（原子替换）
            doc.Overwrite(targetOffset, [0xAA, 0xBB, 0xCC, 0xDD]);
            var result = await source.CommitAsync(doc.BuildChangeSet(), new CommitOptions(), CancellationToken.None);
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.RecoveryPath); // 备份存在
            Assert.True(File.Exists(result.RecoveryPath));
        }

        // 提交后验证：长度与内容正确
        using (var verify = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal((long)fileLength, verify.Length);
            verify.Seek((long)targetOffset, SeekOrigin.Begin);
            var tail = new byte[4];
            verify.ReadExactly(tail);
            Assert.Equal([0xAA, 0xBB, 0xCC, 0xDD], tail);
        }
    }

    [Fact]
    public async Task Hex002_InsertDeleteCombination_Commit()
    {
        // HEX-002：插入/删除组合 → 区间映射、游标、书签和结果长度正确
        var path = NewFile("hex002.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);

        await using (var source = await FileByteSource.OpenAsync(path, readOnly: false, CancellationToken.None))
        using (var doc = new HexDocument(source))
        {
            doc.Insert(2, [0xAA, 0xBB]);
            doc.Delete(4, 2);
            doc.Overwrite(0, [0xFF]);
            doc.AddBookmark(3, "合并点", null);
            Assert.Equal(6UL, doc.Length);

            var result = await source.CommitAsync(doc.BuildChangeSet(), new CommitOptions(), CancellationToken.None);
            Assert.True(result.Success, result.Message);
        }

        var saved = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0x02, 0xAA, 0xBB, 0x05, 0x06], saved);
    }

    [Fact]
    public async Task Hex003_ExternalModification_BlocksCommit()
    {
        // HEX-003：外部修改 → 提交被阻止；可重新加载或另存；原文件不损坏
        var path = NewFile("hex003.bin");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04, 0x05]);

        await using (var source = await FileByteSource.OpenAsync(path, readOnly: false, CancellationToken.None))
        using (var doc = new HexDocument(source))
        {
            doc.Overwrite(0, [0xFF]);

            // 外部修改（改变 mtime/长度/内容）
            await Task.Delay(20);
            File.WriteAllText(path, "external-change");

            var result = await source.CommitAsync(doc.BuildChangeSet(), new CommitOptions(), CancellationToken.None);
            Assert.False(result.Success);
            Assert.Equal(CommitErrorKind.VersionTokenMismatch, result.ErrorKind);

            // 原文件保持外部修改后的内容（未被破坏/覆盖）
            Assert.Equal("external-change", File.ReadAllText(path));
        }
    }

    [Fact]
    public async Task Hex004_ProcessSource_ConstraintsAndIdentity()
    {
        // HEX-004：进程源插入/删除禁用；重启后写入被拒绝
        var target = ProcessEx.StartSyntheticTarget();
        try
        {
            var baseAddress = ProcessEx.ReadBaseAddress(target);
            const ulong magicOffset = 0x1000; // 视图内相对偏移（进程源 0-based 视图）

            await using (var source = new ProcessMemoryByteSource(
                target.Id, $"pid:{target.Id}", baseAddress, 64UL * 1024 * 1024))
            {
                Assert.False(source.Capabilities.HasFlag(ByteSourceCapabilities.Insert));
                Assert.False(source.Capabilities.HasFlag(ByteSourceCapabilities.Delete));

                using var doc = new HexDocument(source);
                Assert.False(doc.SupportsInsertDelete);
                Assert.Throws<NotSupportedException>(() => doc.Insert(magicOffset, [0x00]));
                Assert.Throws<NotSupportedException>(() => doc.Delete(magicOffset, 1));

                // 覆盖写入：Magic → 0 并提交
                var magic = new byte[8];
                doc.ReadBytes(magicOffset, magic);
                Assert.Equal(0x1122334455667788UL, BitConverter.ToUInt64(magic));

                doc.Overwrite(magicOffset, new byte[8]);
                var result = await source.CommitAsync(doc.BuildChangeSet(), new CommitOptions(), CancellationToken.None);
                Assert.True(result.Success, result.Message);

                // 读回验证
                var readBack = new byte[8];
                doc.ReadBytes(magicOffset, readBack);
                Assert.Equal(0UL, BitConverter.ToUInt64(readBack));
            }

            // 目标重启后写入被拒绝：杀进程再启（PID 变化）→ 旧源提交拒绝
            var oldPid = target.Id;
            target.Kill(entireProcessTree: true);
            target.WaitForExit();
            target.Dispose();

            var newTarget = ProcessEx.StartSyntheticTarget();
            try
            {
                await using (var staleSource = new ProcessMemoryByteSource(oldPid, $"pid:{oldPid}", baseAddress, 64UL * 1024 * 1024))
                using (var staleDoc = new HexDocument(staleSource))
                {
                    // 已失效进程的页面显示为不可读，编辑器应在提交前就拒绝写入。
                    Assert.Throws<IOException>(() => staleDoc.Overwrite(magicOffset, new byte[8]));

                    // 即使调用方绕过编辑器构造变更集，来源层仍必须拒绝对旧进程实例的提交。
                    var rejected = await staleSource.CommitAsync(
                        new ChangeSet(Guid.NewGuid(), staleSource.Identity.VersionToken,
                            [new OverwriteEdit(magicOffset, new byte[8], new byte[8])], 64UL * 1024 * 1024),
                        new CommitOptions(), CancellationToken.None);
                    Assert.False(rejected.Success);
                    Assert.Equal(CommitErrorKind.TargetProcessChanged, rejected.ErrorKind);
                }
            }
            finally
            {
                newTarget.Kill(entireProcessTree: true);
                newTarget.Dispose();
            }
        }
        finally
        {
            try { target.Kill(entireProcessTree: true); target.Dispose(); } catch { /* 已退出 */ }
        }
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in _files)
            {
                if (File.Exists(f)) File.Delete(f);
                var bak = f + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
            }
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // 清理失败不失败测试
        }
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        _files.Add(path);
        return path;
    }
}
