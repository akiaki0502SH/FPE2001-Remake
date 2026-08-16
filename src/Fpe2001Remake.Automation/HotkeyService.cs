using System.Runtime.InteropServices;
using System.Windows.Interop;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Automation;

/// <summary>
/// 热键服务（规格 §10：优先 RegisterHotKey）：
/// 隐藏 HwndSource 消息窗口接收 WM_HOTKEY，热键 → 宏回调。
/// 热键格式："Ctrl+Shift+F12" / "F8" / "Alt+1"。
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private readonly Dictionary<Guid, int> _macroToId = [];
    private readonly Dictionary<int, Guid> _idToMacro = [];
    private readonly object _gate = new();
    private HwndSource? _source;
    private int _nextId = 1;

    public HotkeyService(Action<Guid>? onHotkey = null)
    {
        OnHotkey = onHotkey;
        CreateMessageWindow();
    }

    public Action<Guid>? OnHotkey { get; set; }

    public ValueTask<bool> RegisterAsync(string hotkey, Guid? macroId, CancellationToken ct)
    {
        if (macroId is null || _source is null)
        {
            return ValueTask.FromResult(false);
        }

        if (!NativeInput.TryParseHotkey(hotkey, out var modifiers, out var vk))
        {
            return ValueTask.FromResult(false);
        }

        lock (_gate)
        {
            UnregisterLocked(macroId.Value);
            var id = _nextId++;
            if (!NativeInput.RegisterHotKey(_source.Handle, id, modifiers | NativeInput.MOD_NOREPEAT, vk))
            {
                return ValueTask.FromResult(false);
            }
            _macroToId[macroId.Value] = id;
            _idToMacro[id] = macroId.Value;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask UnregisterAsync(Guid? macroId, CancellationToken ct)
    {
        lock (_gate)
        {
            if (macroId is Guid id)
            {
                UnregisterLocked(id);
            }
            else
            {
                foreach (var kv in _macroToId.ToList())
                {
                    UnregisterLocked(kv.Key);
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    private void UnregisterLocked(Guid macroId)
    {
        if (_macroToId.Remove(macroId, out var id))
        {
            _idToMacro.Remove(id);
            if (_source is not null)
            {
                NativeInput.UnregisterHotKey(_source.Handle, id);
            }
        }
    }

    private void CreateMessageWindow()
    {
        var hwndSource = new HwndSource(new HwndSourceParameters("FPE2001-Remake-HotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x08000000 /* WS_EX_TOOLWINDOW */,
        });
        hwndSource.AddHook(WndProc);
        _source = hwndSource;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeInput.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            lock (_gate)
            {
                if (_idToMacro.TryGetValue(id, out var macroId))
                {
                    OnHotkey?.Invoke(macroId);
                }
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        _source?.Dispose();
        _source = null;
    }
}
