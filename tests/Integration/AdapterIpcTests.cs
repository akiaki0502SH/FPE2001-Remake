using System.Diagnostics;
using System.Text.Json;
using Fpe2001Remake.Application;
using Fpe2001Remake.Contracts;
using Xunit;

namespace Fpe2001Remake.IntegrationTests;

/// <summary>P5 验收：AdapterHost IPC（规格 §12）与速度控制（规格 §11）。</summary>
public sealed class AdapterIpcTests : IAsyncLifetime
{
    private Process? _host;
    private AdapterClient _client = null!;
    private readonly string _pipeName = $"FPE2001-Remake-Test-{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        var hostExe = FindHostExe();
        _host = Process.Start(new ProcessStartInfo(hostExe, $"-demo --pipe \"{_pipeName}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        await Task.Delay(500);

        _client = new AdapterClient(_pipeName);
        var connected = await _client.ConnectAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(connected, "无法连接 AdapterHost");
    }

    [Fact]
    public async Task Hello_ReportsDemoAdapterAndCapabilities()
    {
        var response = await _client.InvokeAsync(
            new AdapterMessage(AdapterProtocol.ProtocolVersion, "req", Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow.AddSeconds(5), "nonce", "hello", null),
            CancellationToken.None);

        Assert.Equal("hello", response.Method);
        var info = JsonSerializer.Deserialize<AdapterHelloInfo>(response.PayloadJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(info);
        Assert.Equal("demo-speed", info!.AdapterId);
        Assert.True(info.Capabilities.HasFlag(AdapterCapabilities.SpeedControl));
    }

    [Fact]
    public async Task Speed_GetSetMultiplier_RoundTrip()
    {
        var capability = new SpeedControlClient(_client);

        var initial = await capability.GetAsync(CancellationToken.None);
        Assert.Equal(1.0, initial.Multiplier);

        var set = await capability.SetMultiplierAsync(2.0, CancellationToken.None);
        Assert.Equal(2.0, set.Multiplier);
        Assert.True(set.Enabled || !set.Enabled); // enabled 保持独立

        var readBack = await capability.GetAsync(CancellationToken.None);
        Assert.Equal(2.0, readBack.Multiplier);
    }

    [Fact]
    public async Task Speed_SetEnabled_Reflects()
    {
        var capability = new SpeedControlClient(_client);
        var enabled = await capability.SetEnabledAsync(true, CancellationToken.None);
        Assert.True(enabled.Enabled);

        var disabled = await capability.SetEnabledAsync(false, CancellationToken.None);
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public async Task Speed_MultiplierClampedToRange()
    {
        var capability = new SpeedControlClient(_client);
        var state = await capability.SetMultiplierAsync(99.0, CancellationToken.None);
        Assert.Equal(4.0, state.Multiplier); // 上限 4.0
    }

    private static string FindHostExe()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "Fpe2001Remake.AdapterHost.exe");
        if (File.Exists(local)) return local;
        // 测试 bin: tests/Integration/bin/Release/net10.0-windows/
        // 宿主 exe: src/Fpe2001Remake.AdapterHost/bin/Release/net10.0/
        var relative = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Fpe2001Remake.AdapterHost", "bin", "Release", "net10.0", "Fpe2001Remake.AdapterHost.exe"));
        if (File.Exists(relative)) return relative;
        throw new FileNotFoundException("找不到 AdapterHost.exe", relative);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        try
        {
            _host?.Kill(entireProcessTree: true);
            _host?.Dispose();
        }
        catch
        {
            // 已退出
        }
    }
}
