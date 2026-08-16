using System.Windows.Media;

namespace Fpe2001Remake.UI.Theme;

/// <summary>
/// 设计令牌（FPE2001-Remake UI Design Kit v2.1 / design-tokens.json）。
/// 颜色、圆角、尺寸、间距与字体统一从这里取，禁止在 XAML 中散落魔法值。
/// </summary>
public static class DesignTokens
{
    public static class Palette
    {
        public static readonly Color Canvas = Rgb(0xD9, 0xDD, 0xDF);
        public static readonly Color Window = Rgb(0xF5, 0xF6, 0xF6);
        public static readonly Color Surface = Rgb(0xFB, 0xFB, 0xFA);
        public static readonly Color Line = Rgb(0xCF, 0xD5, 0xD6);
        public static readonly Color Text = Rgb(0x28, 0x35, 0x38);
        public static readonly Color Muted = Rgb(0x6C, 0x79, 0x7C);
        public static readonly Color Primary = Rgb(0x52, 0x6F, 0x75);
        public static readonly Color PrimarySoft = Rgb(0xDB, 0xE5, 0xE6);
        public static readonly Color Success = Rgb(0x60, 0x7A, 0x69);
        public static readonly Color Warning = Rgb(0x8A, 0x76, 0x55);
        public static readonly Color Danger = Rgb(0x85, 0x5E, 0x5E);
        public static readonly Color White = Colors.White;

        private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    }

    public static class Radius
    {
        public const double Window = 14;
        public const double Card = 9;
        public const double Control = 6;
    }

    public static class Size
    {
        public const double Titlebar = 44;
        public const double ModuleStrip = 66;
        public const double Actionbar = 44;
        public const double Statusbar = 31;
        public const double Control = 33;
    }

    public static class Spacing
    {
        public const double S = 4;
        public const double M = 7;
        public const double L = 10;
        public const double XL = 13;
        public const double XXL = 15;
        public const double XXXL = 20;
        public const double XXXXL = 26;
    }

    public static class Font
    {
        public const string Ui = "Segoe UI Variable, Microsoft YaHei UI, Segoe UI";
        public const string Mono = "Cascadia Mono, Consolas";
        public const string Icon = "Segoe MDL2 Assets";
    }
}
