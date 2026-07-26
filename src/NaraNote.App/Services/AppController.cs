using System.Windows;
using System.Windows.Forms;
using NaraNote.App.Views;
using NaraNote.Core.Models;
using NaraNote.Core.Services;
using NaraNote.Infrastructure.Logging;
using Application = System.Windows.Application;

namespace NaraNote.App.Services;

public sealed class AppController : IDisposable
{
    private readonly IAppStateStore _store; private readonly FileLogger _logger;
    private readonly Dictionary<Guid, NoteWindow> _windows = [];
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _debounce; private NotifyIcon? _tray;
    private System.Drawing.Icon? _appIcon;
    private GlobalHotKeyManager? _hotKeys; private bool _allVisible = true;
    public AppState State { get; private set; } = new();
    public AppController(IAppStateStore store, FileLogger logger) { _store = store; _logger = logger; }

    public async Task StartAsync()
    {
        State = await _store.LoadAsync();
        var open = State.Notes.Where(n => n.IsOpen).ToList();
        if (open.Count == 0)
        {
            var note = AppStateFactory.CreateNote(State.Settings);
            State.Notes.Add(note); open.Add(note);
        }
        foreach (var note in open) Show(note);
        _ = Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { SetupTray(); SetupHotKeys(); }));
    }
    public void NewNote(NoteData? source = null)
    {
        var note = AppStateFactory.CreateNote(State.Settings, (source?.Left ?? 96) + 24, (source?.Top ?? 96) + 24);
        State.Notes.Add(note); Show(note); ScheduleSave();
    }
    public void Show(NoteData note)
    {
        note.IsOpen = true;
        NormalizePlacement(note);
        if (!_windows.TryGetValue(note.Id, out var window)) { window = new NoteWindow(note, this); _windows[note.Id] = window; }
        window.Show(); window.Activate(); window.FocusEditor(); ScheduleSave();
    }
    public void Closed(NoteData note)
    {
        note.IsOpen = false; _windows.Remove(note.Id); _ = SaveNowAsync();
        if (_windows.Count == 0 && !State.Settings.UseSystemTray) Exit();
    }
    public void ScheduleSave()
    {
        _debounce?.Cancel(); _debounce?.Dispose(); _debounce = new(); var token = _debounce.Token;
        _ = Task.Run(async () => { try { await Task.Delay(500, token); await SaveNowAsync(token); } catch (OperationCanceledException) { } });
    }
    public async Task SaveNowAsync(CancellationToken token = default)
    {
        await _saveGate.WaitAsync(token); try { await _store.SaveAsync(State, token); } catch (Exception ex) { _logger.Error("Persistence", ex); } finally { _saveGate.Release(); }
    }
    public void ToggleAll(bool visible) { foreach (var w in _windows.Values) { if (visible) w.Show(); else w.Hide(); } }
    public void ApplySettings()
    {
        SetupHotKeys(); SetupTray(); foreach (var window in _windows.Values) window.ApplyAppSettings();
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable)) new NaraNote.Infrastructure.Startup.StartupRegistration().SetEnabled(State.Settings.RunAtStartup, executable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { _logger.Error("Startup", ex); }
        ScheduleSave();
    }
    public async void Exit() { await SaveNowAsync(); Dispose(); Application.Current.Shutdown(); }
    private void SetupTray()
    {
        _tray?.Dispose(); _tray = null; if (!State.Settings.UseSystemTray) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("새 노트", null, (_, _) => Application.Current.Dispatcher.Invoke(() => NewNote()));
        menu.Items.Add("모두 표시", null, (_, _) => Application.Current.Dispatcher.Invoke(() => ToggleAll(true)));
        menu.Items.Add("모두 숨기기", null, (_, _) => Application.Current.Dispatcher.Invoke(() => ToggleAll(false)));
        menu.Items.Add("종료", null, (_, _) => Application.Current.Dispatcher.Invoke(Exit));
        _appIcon ??= LoadAppIcon();
        _tray = new NotifyIcon { Icon = _appIcon, Text = "NaraNote", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Application.Current.Dispatcher.Invoke(() => { var n = State.Notes.Where(x => x.IsOpen).OrderByDescending(x => x.LastModifiedUtc).FirstOrDefault(); if (n is not null) Show(n); });
    }
    private static System.Drawing.Icon LoadAppIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/NaraNote;component/Resources/NaraNote.ico"));
        if (resource?.Stream is null) return System.Drawing.SystemIcons.Application;
        using (resource.Stream) { using var icon = new System.Drawing.Icon(resource.Stream); return (System.Drawing.Icon)icon.Clone(); }
    }
    private static void NormalizePlacement(NoteData note)
    {
        var proposed = new System.Drawing.Rectangle((int)note.Left, (int)note.Top, Math.Max(220, (int)note.Width), Math.Max(160, (int)note.Height));
        var screen = Screen.AllScreens.OrderByDescending(s => System.Drawing.Rectangle.Intersect(s.WorkingArea, proposed).Width * System.Drawing.Rectangle.Intersect(s.WorkingArea, proposed).Height).FirstOrDefault() ?? Screen.PrimaryScreen;
        if (screen is null) return; var area = screen.WorkingArea;
        var clamped = NaraNote.Core.Utilities.WindowPlacement.Clamp(new(note.Left, note.Top, note.Width, note.Height), new(area.X, area.Y, area.Width, area.Height));
        note.Left = clamped.X; note.Top = clamped.Y; note.Width = clamped.Width; note.Height = clamped.Height;
    }
    private void SetupHotKeys()
    {
        _hotKeys ??= new GlobalHotKeyManager();
        var newNote = State.Settings.GlobalHotKeys.GetValueOrDefault("NewNote", "Ctrl+Alt+N");
        var toggle = State.Settings.GlobalHotKeys.GetValueOrDefault("ToggleNotes", "Ctrl+Alt+H");
        if (!_hotKeys.Register(100, newNote, () => Application.Current.Dispatcher.Invoke(() => NewNote()))) _logger.Error("HotKey", new InvalidOperationException($"Could not register {newNote}."));
        if (!_hotKeys.Register(101, toggle, () => Application.Current.Dispatcher.Invoke(() => { _allVisible = !_allVisible; ToggleAll(_allVisible); }))) _logger.Error("HotKey", new InvalidOperationException($"Could not register {toggle}."));
    }
    public void Dispose() { _hotKeys?.Dispose(); _tray?.Dispose(); _appIcon?.Dispose(); _debounce?.Dispose(); _saveGate.Dispose(); }
}
