using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Fpe2001Remake.UI.Shell;

/// <summary>
/// Delegates the outer border, rounded corners and drop shadow to the Windows 11
/// Desktop Window Manager.  These pixels live outside the WPF client layout, so
/// no artificial margin or same-colour halo is introduced around the content.
/// </summary>
internal static class Windows11WindowFrame
{
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    private const int DwmNcRenderingEnabled = 2;
    private const int DwmWindowCornerPreferenceRound = 2;

    public static void Apply(nint windowHandle, Color borderColor)
    {
        if (!CanUseWindows11Frame(windowHandle))
            return;

        var renderingPolicy = DwmNcRenderingEnabled;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaNcRenderingPolicy,
            ref renderingPolicy,
            sizeof(int));

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));

        SetBorderColor(windowHandle, borderColor);
    }

    public static void SetBorderColor(nint windowHandle, Color borderColor)
    {
        if (!CanUseWindows11Frame(windowHandle))
            return;

        // COLORREF is stored as 0x00BBGGRR.
        var colorRef = borderColor.R | (borderColor.G << 8) | (borderColor.B << 16);
        _ = DwmSetWindowAttribute(
            windowHandle,
            DwmwaBorderColor,
            ref colorRef,
            sizeof(int));
    }

    private static bool CanUseWindows11Frame(nint windowHandle) =>
        windowHandle != 0 && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
