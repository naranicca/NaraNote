using System.Runtime.InteropServices;
using System.Windows.Interop;
using NaraNote.Core.Utilities;

namespace NaraNote.App.Services;

public sealed class GlobalHotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    public GlobalHotKeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("NaraNote.HotKeys") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
        _source.AddHook(WndProc);
    }
    public bool Register(int id, string text, Action action)
    {
        Unregister(id); if (!HotKeyDefinition.TryParse(text, out var hotKey)) return false;
        if (!RegisterHotKey(_source.Handle, id, (uint)hotKey.Modifiers | 0x4000, hotKey.VirtualKey)) return false;
        _actions[id] = action; return true;
    }
    public void Unregister(int id) { UnregisterHotKey(_source.Handle, id); _actions.Remove(id); }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) { if (msg == WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out var action)) { action(); handled = true; } return IntPtr.Zero; }
    public void Dispose() { foreach (var id in _actions.Keys.ToArray()) UnregisterHotKey(_source.Handle, id); _actions.Clear(); _source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
