using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.UI.ViewModels.Modules;

/// <summary>M09 关于（原版 TabAbout；规格 §12：版本、来源、许可证、诊断入口）。</summary>
public sealed class AboutViewModel : ModuleViewModelBase
{
    public AboutViewModel(IVersionService? version = null, IDiagnosticsService? diagnostics = null)
        : base(ModuleCatalog.About)
    {
        Version = version;
        Diagnostics = diagnostics;

        Actions.Add(new ActionItem("复制系统信息", null, "\uE8C8", IsEnabled: false, ToolTip: "P4 阶段实现"));
        Actions.Add(new ActionItem("导出诊断", null, "\uE74E", IsEnabled: false, ToolTip: "P4 阶段实现"));
        Actions.Add(new ActionItem("打开日志目录", null, "\uE8B7", IsEnabled: false, ToolTip: "P4 阶段实现"));

        State = PageState.Ready;
        StateDetail = "FPE2001-Remake v2.1 — 本地离线单机工具。";
    }

    public IVersionService? Version { get; }

    public IDiagnosticsService? Diagnostics { get; }

    public override string WorkspacePlaceholder =>
        "M09 关于：产品/协议版本、原版来源与重构说明、第三方许可证、诊断入口（默认脱敏）。";
}
