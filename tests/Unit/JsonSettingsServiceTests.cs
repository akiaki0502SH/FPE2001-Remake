using Fpe2001Remake.Application;
using Xunit;

namespace Fpe2001Remake.UnitTests;

/// <summary>P4 验收：JSON 设置服务（独占 APPDATA，串行）。</summary>
[Collection("IsolatedAppData")]
public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempAppData;
    private readonly JsonSettingsService _service;

    public JsonSettingsServiceTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        _service = new JsonSettingsService(_tempAppData);
    }

    [Fact]
    public async Task SetGet_RoundTrips()
    {
        await _service.SetAsync("scan.blockSizeKb", 512, CancellationToken.None);
        var value = await _service.GetAsync("scan.blockSizeKb", 256, CancellationToken.None);
        Assert.Equal(512, value);
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsFallback()
    {
        var value = await _service.GetAsync("nonexistent.key", 42, CancellationToken.None);
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task Persistence_SurvivesRestart()
    {
        await _service.SetAsync("files.backupEnabled", false, CancellationToken.None);

        var fresh = new JsonSettingsService();
        var value = await fresh.GetAsync("files.backupEnabled", true, CancellationToken.None);
        Assert.False(value);
    }

    [Fact]
    public async Task Set_StringAndBool()
    {
        await _service.SetAsync("files.tempDirectory", "D:\\tmp", CancellationToken.None);
        Assert.Equal("D:\\tmp", await _service.GetAsync("files.tempDirectory", "", CancellationToken.None));

        await _service.SetAsync("lock.freezeIntervalMs", 100, CancellationToken.None);
        Assert.Equal(100, await _service.GetAsync("lock.freezeIntervalMs", 250, CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
        catch
        {
            // 忽略
        }
    }
}
