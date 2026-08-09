using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using NaraNote.App.Views;
using NaraNote.App.Localization;
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
    private Guid? _lastActiveNoteId;
    private DispatcherTimer? _reminderTimer;
    public AppState State { get; private set; } = new();
    public AppController(IAppStateStore store, FileLogger logger) { _store = store; _logger = logger; }
    public void LogError(string area, Exception exception) => _logger.Error(area, exception);

    public async Task StartAsync()
    {
        State = await _store.LoadAsync();
        _logger.Info("Startup", $"State loaded ({State.Notes.Count} notes).");
        UiText.SetLanguage(State.Settings.Language);
        foreach (var note in State.Notes) ClearMissingExportPath(note);
        var open = State.Notes.Where(n => n.IsOpen).ToList();
        if (open.Count == 0)
        {
            var note = AppStateFactory.CreateNote(State.Settings);
            State.Notes.Add(note); open.Add(note);
        }
        foreach (var note in open)
        {
            _logger.Info("Startup", $"Creating note window {note.Id}.");
            Show(note);
            _logger.Info("Startup", $"Note window {note.Id} shown.");
            if (ShouldAutoHideUntilReminder(note) && _windows.TryGetValue(note.Id, out var window)) window.Hide();
        }
        SetupReminderTimer();
        _ = Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { SetupTray(); SetupHotKeys(); }));
    }
    public void NewNote(NoteData? source = null)
    {
        source ??= GetLastActiveNote();
        var desiredLeft = source is null ? 96 : source.Left + source.Width + 12;
        var desiredTop = source?.Top ?? 96;
        var note = AppStateFactory.CreateNote(State.Settings, desiredLeft, desiredTop);
        PlaceNewNoteBesideAnchor(note, source);
        State.Notes.Add(note); Show(note); ScheduleSave();
    }
    public void NoteActivated(NoteData note) { _lastActiveNoteId = note.Id; note.LastModifiedUtc = DateTimeOffset.UtcNow; }
    public void Show(NoteData note)
    {
        ClearMissingExportPath(note);
        note.IsOpen = true;
        NormalizePlacement(note);
        if (!_windows.TryGetValue(note.Id, out var window))
        {
            _logger.Info("Startup", $"Constructing note window {note.Id}.");
            window = new NoteWindow(note, this);
            _windows[note.Id] = window;
            _logger.Info("Startup", $"Constructed note window {note.Id}.");
        }
        window.ShowInTaskbar = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Visibility = Visibility.Visible;
        window.Activate();
        window.FocusEditor();
        ScheduleSave();
    }
    private void SetupReminderTimer()
    {
        _reminderTimer?.Stop();
        _reminderTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _reminderTimer.Tick += ReminderTimer_Tick;
        CheckRemindersNow();
    }
    private void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        _reminderTimer?.Stop();
        CheckRemindersNow();
    }
    private void ScheduleNextReminderCheck()
    {
        if (_reminderTimer is null) return;
        _reminderTimer.Stop();
        var nextDue = State.Notes
            .Where(note => note.Reminder.IsEnabled)
            .Select(note => (DateTimeOffset?)note.Reminder.NextDueUtc)
            .Min();
        if (nextDue is null) return;

        var delay = nextDue.Value - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(10);
        // Re-evaluate occasionally so sleep/resume and wall-clock changes cannot leave a stale long timer.
        if (delay > TimeSpan.FromHours(1)) delay = TimeSpan.FromHours(1);
        _reminderTimer.Interval = delay;
        _reminderTimer.Start();
    }
    public void CheckRemindersNow()
    {
        var now = DateTimeOffset.UtcNow;
        var due = State.Notes.Where(note => note.Reminder.IsEnabled && note.Reminder.NextDueUtc <= now).ToList();
        if (due.Count == 0) { ScheduleNextReminderCheck(); return; }
        foreach (var note in due)
        {
            Show(note);
            if (_windows.TryGetValue(note.Id, out var window)) window.ActivateForReminder();
            if (note.Reminder.Recurrence == ReminderRecurrence.Once)
                note.Reminder = new ReminderData { Use24HourFormat = note.Reminder.Use24HourFormat };
            else NaraNote.Core.Utilities.ReminderSchedule.AdvanceAfterTrigger(note.Reminder, now);
            if (_windows.TryGetValue(note.Id, out window)) window.RefreshReminderMenu();
        }
        _ = SaveNowAsync();
        ScheduleNextReminderCheck();
    }
    private static bool ShouldAutoHideUntilReminder(NoteData note) =>
        note.Reminder.IsEnabled && note.Reminder.AutoHide && note.Reminder.NextDueUtc > DateTimeOffset.UtcNow;
    private static void ClearMissingExportPath(NoteData note)
    {
        if (string.IsNullOrWhiteSpace(note.ExportFilePath) || File.Exists(note.ExportFilePath)) return;
        note.ExportFilePath = null;
        note.IsExportDirty = false;
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
        UiText.SetLanguage(State.Settings.Language);
        SetupHotKeys(); SetupTray(); foreach (var window in _windows.Values) { window.ApplyAppSettings(); window.ApplyLanguage(); }
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
        menu.Items.Add(UiText.Get("NewNote"), null, (_, _) => Application.Current.Dispatcher.Invoke(() => NewNote()));
        var showAllItem = new ToolStripMenuItem(UiText.Get("ShowAll"));
        showAllItem.Click += (_, _) => Application.Current.Dispatcher.Invoke(() => ToggleAll(true));
        menu.Items.Add(showAllItem);
        menu.Opening += (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            var hiddenCount = _windows.Values.Count(window => !window.IsVisible);
            showAllItem.Text = hiddenCount > 0 ? UiText.Format("ShowAllHidden", hiddenCount) : UiText.Get("ShowAll");
        });
        menu.Items.Add(UiText.Get("HideAll"), null, (_, _) => Application.Current.Dispatcher.Invoke(() => ToggleAll(false)));
        menu.Items.Add(UiText.Get("Exit"), null, (_, _) => Application.Current.Dispatcher.Invoke(Exit));
        _appIcon ??= LoadAppIcon();
        _tray = new NotifyIcon { Icon = _appIcon, Text = "NaraNote", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Application.Current.Dispatcher.Invoke(() => NewNote());
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
    private NoteData? GetLastActiveNote()
    {
        if (_lastActiveNoteId is { } id && State.Notes.FirstOrDefault(note => note.Id == id && note.IsOpen) is { } active) return active;
        return State.Notes.Where(note => note.IsOpen).OrderByDescending(note => note.LastModifiedUtc).FirstOrDefault();
    }
    private void PlaceNewNoteBesideAnchor(NoteData note, NoteData? anchor)
    {
        var (screen, area) = GetMonitorWorkArea(anchor);
        var occupied = _windows
            .Where(pair => Screen.FromHandle(new WindowInteropHelper(pair.Value).Handle).DeviceName == screen.DeviceName)
            .Select(pair => State.Notes.First(noteData => noteData.Id == pair.Key))
            .Select(existing => new RectData(existing.Left, existing.Top, existing.Width, existing.Height));
        var placed = NaraNote.Core.Utilities.WindowPlacement.FindNonOverlapping(new(note.Left, note.Top, note.Width, note.Height), area, occupied);
        note.Left = placed.X; note.Top = placed.Y; note.Width = placed.Width; note.Height = placed.Height;
    }
    private (Screen Screen, RectData WorkArea) GetMonitorWorkArea(NoteData? anchor)
    {
        if (anchor is not null && _windows.TryGetValue(anchor.Id, out var window) && new WindowInteropHelper(window).Handle is var hwnd && hwnd != IntPtr.Zero)
        {
            var screen = Screen.FromHandle(hwnd); var pixels = screen.WorkingArea;
            var topLeft = window.PointFromScreen(new System.Windows.Point(pixels.Left, pixels.Top));
            var bottomRight = window.PointFromScreen(new System.Windows.Point(pixels.Right, pixels.Bottom));
            return (screen, new(window.Left + topLeft.X, window.Top + topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y));
        }
        var fallback = Screen.FromPoint(System.Windows.Forms.Cursor.Position); var area = fallback.WorkingArea;
        return (fallback, new(area.X, area.Y, area.Width, area.Height));
    }
    private void SetupHotKeys()
    {
        var useNewNote = State.Settings.UseGlobalHotKeys && State.Settings.UseNewNoteHotKey;
        var useToggleNotes = State.Settings.UseGlobalHotKeys && State.Settings.UseToggleNotesHotKey;
        if (!useNewNote && !useToggleNotes)
        {
            _hotKeys?.Dispose();
            _hotKeys = null;
            return;
        }
        _hotKeys ??= new GlobalHotKeyManager();
        var newNote = State.Settings.GlobalHotKeys.GetValueOrDefault("NewNote", "Ctrl+Alt+N");
        var toggle = State.Settings.GlobalHotKeys.GetValueOrDefault("ToggleNotes", "Ctrl+Alt+H");
        if (useNewNote)
        {
            if (!_hotKeys.Register(100, newNote, () => Application.Current.Dispatcher.Invoke(() => NewNote()))) _logger.Error("HotKey", new InvalidOperationException($"Could not register {newNote}."));
        }
        else _hotKeys.Unregister(100);
        if (useToggleNotes)
        {
            if (!_hotKeys.Register(101, toggle, () => Application.Current.Dispatcher.Invoke(() => { _allVisible = !_allVisible; ToggleAll(_allVisible); }))) _logger.Error("HotKey", new InvalidOperationException($"Could not register {toggle}."));
        }
        else _hotKeys.Unregister(101);
    }
    public void Dispose() { _reminderTimer?.Stop(); _hotKeys?.Dispose(); _tray?.Dispose(); _appIcon?.Dispose(); _debounce?.Dispose(); _saveGate.Dispose(); }
}
