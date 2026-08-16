using Fpe2001Remake.Automation;
using Fpe2001Remake.Contracts;
using Xunit;

namespace Fpe2001Remake.UnitTests;

/// <summary>P4 验收：宏自动化服务（规格 §10；不发真实输入；独占 APPDATA，串行）。</summary>
[Collection("IsolatedAppData")]
public class AutomationServiceTests : IDisposable
{
    private readonly string _tempAppData;
    private readonly AutomationService _service;

    public AutomationServiceTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "FPE2001-Remake", "automation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        _service = new AutomationService(hotkeys: null, storeDirectory: _tempAppData);
    }

    [Fact]
    public async Task Save_List_Delete_RoundTrip()
    {
        var macro = new Macro(Guid.NewGuid(), "测试宏",
            new TargetBinding(WindowTitlePattern: "Notepad"),
            "Ctrl+Alt+F9",
            [new MacroStep(StepActionKind.Delay, null, null, 10, false, "wait")],
            Repeat: 1, TimeSpan.FromMinutes(1), Enabled: false);

        await _service.SaveAsync(macro, CancellationToken.None);
        var list = await _service.ListAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("测试宏", list[0].Name);
        Assert.Equal("Ctrl+Alt+F9", list[0].Hotkey);

        await _service.DeleteAsync(macro.Id, CancellationToken.None);
        Assert.Empty(await _service.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_DelayOnlyMacro_Completes()
    {
        var macro = new Macro(Guid.NewGuid(), "延迟宏",
            new TargetBinding(null, null, null, null),
            null,
            [new MacroStep(StepActionKind.Delay, null, null, 20, false, "wait")],
            Repeat: 2, TimeSpan.FromMinutes(1), Enabled: false);

        await _service.SaveAsync(macro, CancellationToken.None);
        var state = await _service.RunAsync(macro.Id, countdown: false, CancellationToken.None);
        Assert.False(state.Running);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task Run_InvalidKeyStep_ReturnsError()
    {
        var macro = new Macro(Guid.NewGuid(), "坏键宏",
            new TargetBinding(null, null, null, null),
            null,
            [new MacroStep(StepActionKind.KeyPress, "NOPE", null, 0, false, null)],
            Repeat: 1, TimeSpan.FromMinutes(1), Enabled: false);

        await _service.SaveAsync(macro, CancellationToken.None);
        var state = await _service.RunAsync(macro.Id, countdown: false, CancellationToken.None);
        Assert.NotNull(state.LastError);
        Assert.Contains("无效的键码", state.LastError);
    }

    [Fact]
    public async Task Run_MissingMacro_ReturnsError()
    {
        var state = await _service.RunAsync(Guid.NewGuid(), countdown: false, CancellationToken.None);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task StopAll_NoRunningMacros_IsNoOp()
    {
        await _service.StopAllAsync(CancellationToken.None); // 不抛即可
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
