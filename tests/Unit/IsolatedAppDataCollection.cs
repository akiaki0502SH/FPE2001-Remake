using Xunit;

namespace Fpe2001Remake.UnitTests;

/// <summary>
/// 需要独占进程级 APPDATA 环境变量的测试集合（串行执行，避免互相污染）。
/// </summary>
[CollectionDefinition("IsolatedAppData", DisableParallelization = true)]
public class IsolatedAppDataCollection
{
}
