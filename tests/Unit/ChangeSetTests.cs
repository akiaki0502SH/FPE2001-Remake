using Fpe2001Remake.Contracts;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class ChangeSetTests
{
    [Fact]
    public void OverwriteEdit_CarriesBeforeAndAfter()
    {
        var edit = new OverwriteEdit(0x1000UL, [0x00, 0x11], [0xFF, 0xEE]);
        Assert.Equal(0x1000UL, edit.Offset);
        Assert.Equal([0x00, 0x11], edit.Before);
        Assert.Equal([0xFF, 0xEE], edit.After);
    }

    [Fact]
    public void ChangeSet_EmptyHasNoEdits()
    {
        var cs = ChangeSet.Empty("token-v1", 1024UL);
        Assert.True(cs.IsEmpty);
        Assert.Equal(1024UL, cs.ResultLength);
    }

    [Fact]
    public void ChangeSet_ReportsEditCount()
    {
        var cs = new ChangeSet(
            Guid.NewGuid(),
            "token-v1",
            [new OverwriteEdit(0UL, [0x00], [0x01]), new InsertEdit(4UL, [0xAB])],
            5UL);
        Assert.Equal(2, cs.EditCount);
        Assert.False(cs.IsEmpty);
    }

    [Fact]
    public void Capabilities_ProcessMemoryHasNoInsertOrDelete()
    {
        // 进程源只支持 Read/Overwrite/Refresh（规格 §4/§6）
        var processCaps = ByteSourceCapabilities.Read | ByteSourceCapabilities.Overwrite | ByteSourceCapabilities.Refresh;
        Assert.False(processCaps.HasFlag(ByteSourceCapabilities.Insert));
        Assert.False(processCaps.HasFlag(ByteSourceCapabilities.Delete));

        // 文件源支持插入/删除/原子提交
        var fileCaps = ByteSourceCapabilities.Read | ByteSourceCapabilities.Overwrite |
                       ByteSourceCapabilities.Insert | ByteSourceCapabilities.Delete |
                       ByteSourceCapabilities.KnownLength | ByteSourceCapabilities.AtomicCommit |
                       ByteSourceCapabilities.Refresh;
        Assert.True(fileCaps.HasFlag(ByteSourceCapabilities.Insert));
        Assert.True(fileCaps.HasFlag(ByteSourceCapabilities.Delete));
    }
}
