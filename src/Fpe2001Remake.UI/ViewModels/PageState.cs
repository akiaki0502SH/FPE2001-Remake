namespace Fpe2001Remake.UI.ViewModels;

/// <summary>
/// 页面状态（UIUX 规格 §16）：每页至少具备 Loading/Empty/Ready/Running/Cancelled/Failed/Disconnected/Unavailable。
/// 编辑器另有 ReadOnly/Dirty/Saving/ExternalChanged/SaveConflict/Recovered；文件页另有 AccessDenied 等。
/// </summary>
public enum PageState
{
    Loading,
    Empty,
    Ready,
    Running,
    Cancelled,
    Failed,
    Disconnected,
    Unavailable,
}

public static class PageStateText
{
    public static string ToDisplay(this PageState state) => state switch
    {
        PageState.Loading => "加载中",
        PageState.Empty => "空",
        PageState.Ready => "就绪",
        PageState.Running => "运行中",
        PageState.Cancelled => "已取消",
        PageState.Failed => "失败",
        PageState.Disconnected => "目标已断开",
        PageState.Unavailable => "不可用",
        _ => state.ToString(),
    };
}
