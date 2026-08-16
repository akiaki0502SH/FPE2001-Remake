using Fpe2001Remake.Application;
using Xunit;

namespace Fpe2001Remake.UnitTests;

public sealed class ProcessTargetFilterTests
{
    [Fact]
    public void ExcludesWindowsServiceSession()
    {
        var candidate = new ProcessTargetDescriptor(120, "my-service", 0, @"C:\Games\my-service.exe");

        Assert.False(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }

    [Fact]
    public void ExcludesKnownWindowsSystemProcess()
    {
        var candidate = new ProcessTargetDescriptor(121, "svchost.exe", 1, @"C:\Windows\System32\svchost.exe");

        Assert.False(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }

    [Fact]
    public void ExcludesAnyExecutableUnderWindowsDirectory()
    {
        var candidate = new ProcessTargetDescriptor(122, "custom-host", 1, @"C:\Windows\Custom\custom-host.exe");

        Assert.False(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }

    [Fact]
    public void KeepsGameOrEmulatorOutsideWindowsDirectory()
    {
        var candidate = new ProcessTargetDescriptor(123, "MyEmulator.exe", 1, @"D:\Emulators\MyEmulator.exe");

        Assert.True(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }

    [Fact]
    public void KeepsCandidateWhenExecutablePathCannotBeRead()
    {
        var candidate = new ProcessTargetDescriptor(124, "GameBackend", 1, null);

        Assert.True(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }

    [Fact]
    public void ExcludesCurrentApplication()
    {
        var candidate = new ProcessTargetDescriptor(999, "Fpe2001Remake.UI", 1, @"D:\Tools\Fpe2001Remake.UI.exe");

        Assert.False(ProcessTargetFilter.IsEligible(candidate, currentProcessId: 999, windowsDirectory: @"C:\Windows"));
    }
}
