using Fpe2001Remake.BinaryEditor.Core;
using Fpe2001Remake.Contracts;
using Fpe2001Remake.Domain;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public class HexDocumentTests
{
    private static HexDocument Create(byte[] data)
        => new(new MemoryByteSource(data));

    [Fact]
    public void ReadBytes_InitialContent()
    {
        using var doc = Create([0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77]);
        Assert.Equal(8UL, doc.Length);
        Assert.Equal(0x11, doc.ReadByte(1));
        var span = new byte[4];
        doc.ReadBytes(2, span);
        Assert.Equal([0x22, 0x33, 0x44, 0x55], span);
    }

    [Fact]
    public void Overwrite_ReadBackAndBefore()
    {
        using var doc = Create([0x00, 0x11, 0x22, 0x33]);
        var edit = doc.Overwrite(1, [0xAA, 0xBB]);
        Assert.Equal([0x11, 0x22], edit.Before);
        Assert.Equal([0xAA, 0xBB], edit.After);
        Assert.Equal(4UL, doc.Length); // 覆盖不改长度
        var span = new byte[4];
        doc.ReadBytes(0, span);
        Assert.Equal([0x00, 0xAA, 0xBB, 0x33], span);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Insert_ShiftsContentAndLength()
    {
        using var doc = Create([0x01, 0x02, 0x03]);
        doc.Insert(1, [0xAA, 0xBB]);
        Assert.Equal(5UL, doc.Length);
        var span = new byte[5];
        doc.ReadBytes(0, span);
        Assert.Equal([0x01, 0xAA, 0xBB, 0x02, 0x03], span);
    }

    [Fact]
    public void Delete_RemovesAndCoalesces()
    {
        using var doc = Create([0x01, 0x02, 0x03, 0x04, 0x05]);
        var edit = doc.Delete(1, 2);
        Assert.Equal([0x02, 0x03], edit.Deleted);
        Assert.Equal(3UL, doc.Length);
        var span = new byte[3];
        doc.ReadBytes(0, span);
        Assert.Equal([0x01, 0x04, 0x05], span);
    }

    [Fact]
    public void InsertDelete_CombinedMapping()
    {
        // HEX-002：插入/删除组合 → 区间映射、游标、书签和结果长度正确
        using var doc = Create([0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        doc.Insert(2, [0xAA, 0xBB]);   // [01 02 AA BB 03 04 05 06] len 8
        doc.Delete(4, 2);              // [01 02 AA BB 05 06] len 6
        doc.Overwrite(0, [0xFF]);      // [FF 02 AA BB 05 06]
        Assert.Equal(6UL, doc.Length);

        var span = new byte[6];
        doc.ReadBytes(0, span);
        Assert.Equal([0xFF, 0x02, 0xAA, 0xBB, 0x05, 0x06], span);

        // 书签在编辑后仍可定位
        doc.AddBookmark(3, "标记", "red");
        Assert.True(doc.HasBookmark(3));
        Assert.Single(doc.Bookmarks);

        // 变更集
        var cs = doc.BuildChangeSet();
        Assert.Equal(3, cs.EditCount);
        Assert.Equal(6UL, cs.ResultLength);
        Assert.Equal("mem:v1", cs.BaseVersionToken);
    }

    [Fact]
    public void Undo_RevertsOverwriteInsertDelete()
    {
        using var doc = Create([0x01, 0x02, 0x03, 0x04]);
        doc.Overwrite(0, [0xFF]);       // [FF 02 03 04]
        doc.Insert(1, [0xAA]);          // [FF AA 02 03 04]
        doc.Delete(2, 1);               // [FF AA 03 04]
        Assert.Equal(4UL, doc.Length);

        var span = new byte[4];
        doc.ReadBytes(0, span);
        Assert.Equal([0xFF, 0xAA, 0x03, 0x04], span);

        var undone1 = doc.Undo(); // 撤销 Delete
        Assert.IsType<DeleteEdit>(undone1);
        Assert.Equal(5UL, doc.Length);

        var undone2 = doc.Undo(); // 撤销 Insert
        Assert.IsType<InsertEdit>(undone2);
        Assert.Equal(4UL, doc.Length);

        var undone3 = doc.Undo(); // 撤销 Overwrite
        Assert.IsType<OverwriteEdit>(undone3);
        doc.ReadBytes(0, span);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], span);
        Assert.False(doc.IsDirty);

        Assert.Null(doc.Undo()); // 空栈
    }

    [Fact]
    public void Undo_InsertAtEndAndDeleteAtStart()
    {
        using var doc = Create([0x01, 0x02, 0x03]);
        doc.Insert(3, [0x99]);          // 末尾插入
        Assert.Equal(4UL, doc.Length);
        doc.Undo();
        Assert.Equal(3UL, doc.Length);

        doc.Delete(0, 1);               // 开头删除
        Assert.Equal(2UL, doc.Length);
        doc.Undo();
        Assert.Equal(3UL, doc.Length);
        var span = new byte[3];
        doc.ReadBytes(0, span);
        Assert.Equal([0x01, 0x02, 0x03], span);
    }

    [Fact]
    public void Overwrite_AfterInsert_UpdatesInsertedSegment()
    {
        using var doc = Create([0x01, 0x02, 0x03]);
        doc.Insert(1, [0xAA, 0xBB]);
        doc.Overwrite(2, [0xCC]);

        var bytes = new byte[5];
        doc.ReadBytes(0, bytes);
        Assert.Equal([0x01, 0xAA, 0xCC, 0x02, 0x03], bytes);
    }

    [Fact]
    public void Undo_MergedInsert_RemovesOnlyLatestRange()
    {
        using var doc = Create([0x01, 0x02]);
        doc.Insert(1, [0xAA]);
        doc.Insert(1, [0xBB]); // 合并到同一插入段
        doc.Undo();

        var bytes = new byte[3];
        doc.ReadBytes(0, bytes);
        Assert.Equal([0x01, 0xAA, 0x02], bytes);
    }
}
