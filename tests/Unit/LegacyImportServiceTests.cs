using Fpe2001Remake.Application;
using Xunit;

namespace Fpe2001Remake.UnitTests;

/// <summary>P3 验收：旧配置导入预览（规格 M08）。</summary>
public class LegacyImportServiceTests
{
    private readonly LegacyImportService _service = new();

    [Fact]
    public async Task Preview_Ini_ParsesSectionsAndKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fpe-legacy-ini-{Guid.NewGuid():N}.ini");
        try
        {
            await File.WriteAllTextAsync(path, """
                ; FPE2001 旧配置
                [Scan]
                DataType=UInt32
                Alignment=4
                [Misc]
                Backup=true
                """);
            var preview = await _service.PreviewAsync(path, CancellationToken.None);
            Assert.Equal("INI", preview.Format);
            Assert.Contains(preview.Fields, f => f.Name == "DataType" && f.RawValue == "UInt32");
            Assert.Contains(preview.Fields, f => f.Name == "节" && f.RawValue == "[Scan]");
            Assert.Contains(preview.Fields, f => f.Name == "Backup" && f.RawValue == "true");
            Assert.Empty(preview.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Preview_Csv_ParsesFirstColumnAsName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fpe-legacy-csv-{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllTextAsync(path, "label,value,note\nHP,100,player\nMP,50,magic\n");
            var preview = await _service.PreviewAsync(path, CancellationToken.None);
            Assert.Equal("CSV", preview.Format);
            Assert.Contains(preview.Fields, f => f.Name == "label" && f.RawValue == "value,note");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Preview_Binary_ExtractsHeaderInfo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fpe-legacy-bin-{Guid.NewGuid():N}.dat");
        try
        {
            await File.WriteAllBytesAsync(path,
            [
                0x46, 0x50, 0x45, 0x32, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05,
                0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            ]);
            var preview = await _service.PreviewAsync(path, CancellationToken.None);
            Assert.Equal("Binary", preview.Format);
            Assert.Contains(preview.Fields, f => f.Name == "魔数(ASCII)" && f.RawValue.Contains("FPE2"));
            Assert.Contains(preview.Fields, f => f.Name == "头部 16 字节");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Preview_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.PreviewAsync("Z:\\nonexistent\\file.ini", CancellationToken.None).AsTask());
    }
}
