namespace Fpe2001Remake.Domain;

/// <summary>
/// checked 语义的 64 位算术统一入口（规格 §12：所有地址/偏移加法 checked；
/// Directory.Build.props 已开 CheckForOverflowUnderflow，此处提供显式入口便于审查与测试）。
/// </summary>
public static class CheckedMath
{
    public static ulong Add(ulong a, ulong b) => checked(a + b);

    public static ulong AddLength(ulong offset, ulong length) => checked(offset + length);

    public static ulong Subtract(ulong a, ulong b) => checked(a - b);
}
