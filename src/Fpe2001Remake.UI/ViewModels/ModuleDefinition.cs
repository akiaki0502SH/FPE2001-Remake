namespace Fpe2001Remake.UI.ViewModels;

/// <summary>模块定义（UIUX 规格 §2：顺序不变；图标+文字；Ctrl+1…9 切换）。</summary>
public sealed record ModuleDefinition(int Index, string Id, string Title, string IconGlyph)
{
    public string ShortcutText => $"Ctrl+{Index}";
}

/// <summary>九模块元数据（原版顺序：扫描→地址表→十六进制→文件→图片→宏→速度→设置→关于）。</summary>
public static class ModuleCatalog
{
    // 图标字形：Segoe MDL2 Assets
    public static readonly ModuleDefinition Scan = new(1, "scan", "扫描", "\uE721");
    public static readonly ModuleDefinition Addresses = new(2, "addresses", "地址表", "\uE8EC");
    public static readonly ModuleDefinition Editor = new(3, "editor", "十六进制", "\uE943");
    public static readonly ModuleDefinition Files = new(4, "files", "文件", "\uE8B7");
    public static readonly ModuleDefinition Picture = new(5, "picture", "图片", "\uE91B");
    public static readonly ModuleDefinition Macro = new(6, "macro", "宏", "\uE714");
    public static readonly ModuleDefinition Speed = new(7, "speed", "速度", "\uE768");
    public static readonly ModuleDefinition Settings = new(8, "settings", "设置", "\uE713");
    public static readonly ModuleDefinition About = new(9, "about", "关于", "\uE946");

    public static IReadOnlyList<ModuleDefinition> All { get; } =
        [Scan, Addresses, Editor, Files, Picture, Macro, Speed, Settings, About];
}
