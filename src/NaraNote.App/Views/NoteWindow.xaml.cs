using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Ink;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NaraNote.App.Services;
using NaraNote.App.ViewModels;
using NaraNote.Core.Commands;
using NaraNote.Core.Drawing;
using NaraNote.Core.Models;
using NaraNote.Core.Utilities;
using NaraNote.Infrastructure.Persistence;
using Microsoft.Win32;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using MessageBox = System.Windows.MessageBox;
using Cursors = System.Windows.Input.Cursors;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using IDataObject = System.Windows.IDataObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace NaraNote.App.Views;

public partial class NoteWindow : Window
{
    private const int WmNcHitTest = 0x0084, WmImeStartComposition = 0x010D, WmImeEndComposition = 0x010E, WmImeComposition = 0x010F, Grip = 16;
    private const int GcsCompStr = 0x0008, GcsResultStr = 0x0800, CfsForcePosition = 0x0020;
    private const int NiCompositionStr = 0x0015, CpsCancel = 0x0004;
    private const int DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const double DefaultPenThickness = 3.5;
    private static readonly Guid StrokeIdProperty = new("6DD0CE91-4A58-4F3C-922E-5D891B0B29A4");
    private static readonly double[] PenThicknessSteps = [1d, 2d, DefaultPenThickness, 6d, 10d];
    private static IHighlightingDefinition? _luaHighlighting;
    private readonly NoteData _note; private readonly AppController _controller; private readonly NoteViewModel _vm;
    private bool _loading = true; private readonly ScribbleRecognizer _scribble = new();
    private readonly UndoManager _history = new();
    private readonly NoteDocumentExporter _documentExporter = new();
    private NoteElement? _selectedElement;
    private FrameworkElement? _selectedVisual;
    private System.Windows.Point _dragOrigin;
    private (double X, double Y) _elementOrigin;
    private bool _stylusWindowDragActive;
    private System.Windows.Point _stylusWindowDragStartScreen;
    private System.Windows.Point _stylusWindowDragStartPosition;
    private int _resizeEdge;
    private System.Drawing.Point _resizeStartCursor;
    private System.Windows.Point _stylusResizeStartScreen;
    private Rect _resizeStartBounds;
    private bool _stylusResizeActive;
    private InkCanvasEditingMode _inkModeBeforeStylusResize;
    private bool _inkHitTestBeforeStylusResize;
    private bool _inkInputActive;
    private bool _autoPenInputActive;
    private StylusPointCollection? _autoPenPoints;
    private DrawingAttributes? _autoPenAttributes;
    private ContextMenu? _noteContextMenu;
    private bool _suppressAutoPenUntilStylusLeaves;
    private System.Windows.Point? _shiftLineAnchor;
    private DateTime _lastInkAutoExpandUtc = DateTime.MinValue;
    private bool _imeCompositionActive;
    private HwndSource? _imeHwndSource;
    public NoteWindow(NoteData note, AppController controller)
    {
        if (!note.IsSyntaxLanguageExplicit && note.SyntaxLanguage == "PlainText") note.SyntaxLanguage = "Auto";
        InitializeComponent(); _note = note; _controller = controller; _vm = new(note, controller.ScheduleSave); DataContext = _vm;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.LostKeyboardFocus += Editor_LostKeyboardFocus;
        SourceInitialized += (_, _) => EnableNativeWindowAppearance();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.Color)) ApplyAppearance();
            if (e.PropertyName == nameof(NoteData.IsExportDirty)) UpdateExportPathDisplay();
        };
        Left = note.Left; Top = note.Top; Width = note.Width; Height = note.Height; Topmost = note.IsAlwaysOnTop;
        ApplyPenSettings();
        Editor.Text = note.Text; ApplyAppearance(); RestoreElements(); BuildContextMenu();
        PreviewMouseLeftButtonDown += Resize_MouseLeftButtonDown;
        PreviewMouseMove += Resize_MouseMove;
        PreviewMouseLeftButtonUp += Resize_MouseLeftButtonUp;
        TitleArea.PreviewStylusDown += TitleArea_PreviewStylusDown;
        TitleArea.PreviewStylusMove += TitleArea_PreviewStylusMove;
        TitleArea.PreviewStylusUp += TitleArea_PreviewStylusUp;
        AddHandler(Stylus.PreviewStylusDownEvent, new StylusDownEventHandler(Resize_PreviewStylusDown), true);
        AddHandler(Stylus.PreviewStylusMoveEvent, new StylusEventHandler(Resize_PreviewStylusMove), true);
        AddHandler(Stylus.PreviewStylusUpEvent, new StylusEventHandler(Resize_PreviewStylusUp), true);
        Surface.PreviewMouseLeftButtonDown += Surface_PreviewMouseLeftButtonDown;
        Ink.PreviewStylusDown += (_, _) => _inkInputActive = Ink.EditingMode == InkCanvasEditingMode.Ink;
        Ink.PreviewStylusMove += (_, e) => { if (_inkInputActive) AutoExpandForInk(e.GetPosition(Ink)); };
        Ink.PreviewStylusUp += (_, _) => _inkInputActive = false;
        Ink.PreviewMouseLeftButtonDown += (_, _) => _inkInputActive = Ink.EditingMode == InkCanvasEditingMode.Ink;
        Ink.PreviewMouseMove += (_, e) => { if (_inkInputActive && e.LeftButton == MouseButtonState.Pressed) AutoExpandForInk(e.GetPosition(Ink)); };
        Ink.PreviewMouseLeftButtonUp += (_, _) => _inkInputActive = false;
        Ink.StrokeErasing += Ink_StrokeErasing;
        Ink.AddHandler(Stylus.PreviewStylusDownEvent, new StylusDownEventHandler(Ink_PreviewStylusDownForObjectSelection), true);
        Ink.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Ink_PreviewMouseDownForObjectSelection), true);
        Ink.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Ink_PreviewMouseDownForStraightLine), true);
        Ink.PreviewMouseMove += (_, _) => UpdateShiftLineCursor();
        Surface.PreviewStylusMove += Surface_PreviewStylusForAutoPen;
        Surface.PreviewStylusDown += Surface_PreviewStylusForAutoPen;
        Surface.PreviewStylusUp += Surface_PreviewStylusForAutoPen;
        Surface.StylusLeave += (_, _) => _suppressAutoPenUntilStylusLeaves = false;
        MouseLeave += (_, _) => { if (_resizeEdge == 0) Mouse.OverrideCursor = null; };
        AddHandler(DragDrop.PreviewDragEnterEvent, new System.Windows.DragEventHandler(Surface_DragOver), true);
        AddHandler(DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(Surface_DragOver), true);
        AddHandler(DragDrop.PreviewDropEvent, new System.Windows.DragEventHandler(Surface_Drop), true);
        LocationChanged += (_, _) => { _note.Left = Left; _note.Top = Top; _vm.Touch(); };
        SizeChanged += (_, _) => { _note.Width = Width; _note.Height = Height; UpdateExportPathDisplay(); _vm.Touch(); };
        Activated += (_, _) => _controller.NoteActivated(_note);
        AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(Window_PreviewKeyDown), true);
        PreviewKeyUp += Window_PreviewKeyUp;
        Deactivated += (_, _) => { ResetShiftLineMode(); FinishStylusWindowDrag(); };
        _loading = false;
    }
    public void FocusEditor()
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            FocusEditorTextArea(true);
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => FocusEditorTextArea(false)));
        }));
    }
    private void FocusEditorTextArea(bool moveCaretToEnd)
    {
        if (!IsVisible || !IsActive) return;
        Editor.TextArea.Focus();
        Keyboard.Focus(Editor.TextArea);
        if (moveCaretToEnd) Editor.CaretOffset = Editor.Text.Length;
        CommandManager.InvalidateRequerySuggested();
    }
    public void ApplyAppSettings() => ApplyPenSettings();

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        EndImeComposition();
        Editor.TextArea.Caret.Hide();
    }

    private void ApplyPenSettings()
    {
        var settings = _controller.State.Settings;
        try { Ink.DefaultDrawingAttributes.Color = (Color)ColorConverter.ConvertFromString(settings.DefaultPenColor); }
        catch (FormatException) { Ink.DefaultDrawingAttributes.Color = Color.FromRgb(34, 34, 34); }
        Ink.DefaultDrawingAttributes.IgnorePressure = false;
        Ink.DefaultDrawingAttributes.FitToCurve = true;
        Ink.DefaultDrawingAttributes.StylusTip = StylusTip.Ellipse;
        SetPenThickness(Math.Clamp(settings.DefaultPenThickness, 1d, 10d), false);
    }
    private void ApplyAppearance()
    {
        try
        {
            var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_note.Color));
            Frame.Background = background; Background = background;
        }
        catch (FormatException) { Frame.Background = Brushes.LightYellow; Background = Brushes.LightYellow; }
        var foreground = ColorContrast.UseLightForeground(_note.Color) ? Brushes.White : Brushes.Black;
        var headerForeground = ColorContrast.UseLightForeground(_note.Color)
            ? new SolidColorBrush(Color.FromArgb(210, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(145, 0, 0, 0));
        headerForeground.Freeze(); NewNoteButton.Foreground = headerForeground; SettingsButton.Foreground = headerForeground; CloseNoteButton.Foreground = headerForeground;
        PinButton.Foreground = headerForeground;
        PinButton.Background = Brushes.Transparent;
        PinButton.Opacity = 1;
        PinHeadPath.Fill = _note.IsAlwaysOnTop ? headerForeground : Brushes.Transparent;
        PinIconViewbox.RenderTransform = new RotateTransform(_note.IsAlwaysOnTop ? 0 : 90);
        PinButton.ToolTip = _note.IsAlwaysOnTop ? "항상 위 해제" : "항상 위";
        ExportPathTextBlock.Foreground = headerForeground;
        ExportDirtyIndicator.Foreground = headerForeground;
        UpdateExportPathDisplay();
        Foreground = foreground; Editor.Foreground = foreground; Editor.FontSize = _note.FontSize; Editor.FontFamily = new FontFamily(_note.FontFamily);
        Editor.TextArea.SelectionCornerRadius = Math.Clamp(Editor.FontSize * 0.18, 1.5, 6.0);
        ApplySyntaxHighlighting();
        ApplyCaptionTypography();
    }
    private void ApplyCaptionTypography()
    {
        if (ObjectCanvas is null) return;
        foreach (var caption in ObjectCanvas.Children.OfType<TextBlock>().Where(x => x.Tag is ImageElement))
        {
            caption.FontFamily = Editor.FontFamily; caption.FontSize = Editor.FontSize; caption.Foreground = Editor.Foreground;
        }
        foreach (var captionEditor in ObjectCanvas.Children.OfType<TextBox>())
        {
            captionEditor.FontFamily = Editor.FontFamily; captionEditor.FontSize = Editor.FontSize; captionEditor.Foreground = Editor.Foreground;
        }
    }
    private void New_Click(object sender, RoutedEventArgs e) => _controller.NewNote(_note);
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _note.IsAlwaysOnTop = !_note.IsAlwaysOnTop;
        Topmost = _note.IsAlwaysOnTop;
        ApplyAppearance();
        _vm.Touch();
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_vm, _controller.State.Settings) { Owner = this };
        var confirmed = dialog.ShowDialog() == true;
        ApplyAppearance(); if (confirmed) _controller.ApplySettings(); await _controller.SaveNowAsync();
    }
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        OpenNoteMenu(false);
        e.Handled = true;
    }
    private void OpenNoteMenu(bool focusFirstItem)
    {
        if (_noteContextMenu is null) return;
        _noteContextMenu.PlacementTarget = SettingsButton;
        _noteContextMenu.Placement = PlacementMode.Bottom;
        _noteContextMenu.IsOpen = true;
        if (!focusFirstItem) return;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            _noteContextMenu.Items.OfType<MenuItem>().FirstOrDefault()?.Focus()));
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        CommitImeCompositionBeforeClose();
        _note.IsOpen = false;
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        EndImeComposition();
        if (_imeHwndSource is not null) _imeHwndSource.RemoveHook(ImeWndProc);
        PersistInk(); _controller.Closed(_note); base.OnClosed(e);
    }
    private void TitleArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        DragMove(); e.Handled = true;
    }
    private void TitleArea_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        var point = e.GetPosition(this);
        if (GetInteractiveResizeEdge(point) != 0) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        _stylusWindowDragActive = true;
        _stylusWindowDragStartScreen = PointToScreen(point);
        _stylusWindowDragStartPosition = new System.Windows.Point(Left, Top);
        Stylus.Capture(TitleArea, CaptureMode.SubTree);
        e.Handled = true;
    }
    private void TitleArea_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (!_stylusWindowDragActive || !ReferenceEquals(Stylus.Captured, TitleArea)) return;
        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _stylusWindowDragStartPosition.X + (current.X - _stylusWindowDragStartScreen.X) / dpi.DpiScaleX;
        Top = _stylusWindowDragStartPosition.Y + (current.Y - _stylusWindowDragStartScreen.Y) / dpi.DpiScaleY;
        e.Handled = true;
    }
    private void TitleArea_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (!_stylusWindowDragActive) return;
        FinishStylusWindowDrag();
        e.Handled = true;
    }
    private void FinishStylusWindowDrag()
    {
        if (!_stylusWindowDragActive) return;
        _stylusWindowDragActive = false;
        if (ReferenceEquals(Stylus.Captured, TitleArea)) Stylus.Capture(null);
        _vm.Touch();
    }
    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is T match) return match;
        return null;
    }
    private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsPointerOverHeaderButton(e.GetPosition(this))) return;
        var edge = GetInteractiveResizeEdge(e.GetPosition(this));
        if (edge == 0) return;
        _resizeEdge = edge; _resizeStartCursor = System.Windows.Forms.Cursor.Position; _resizeStartBounds = new Rect(Left, Top, Width, Height);
        Mouse.Capture(this, CaptureMode.SubTree); e.Handled = true;
    }
    private void Resize_MouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdge == 0)
        {
            if (IsPointerOverHeaderButton(e.GetPosition(this))) { Mouse.OverrideCursor = null; return; }
            Mouse.OverrideCursor = CursorForEdge(GetInteractiveResizeEdge(e.GetPosition(this))); return;
        }
        if (e.LeftButton != MouseButtonState.Pressed) { FinishResize(); return; }
        var current = System.Windows.Forms.Cursor.Position; var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (current.X - _resizeStartCursor.X) / dpi.DpiScaleX; var dy = (current.Y - _resizeStartCursor.Y) / dpi.DpiScaleY;
        ApplyResizeDelta(dx, dy);
        e.Handled = true;
    }
    private void Resize_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        var edge = GetInteractiveResizeEdge(e.GetPosition(this));
        if (edge == 0 || IsPointerOverHeaderButton(e.GetPosition(this))) return;
        _resizeEdge = edge;
        _stylusResizeStartScreen = PointToScreen(e.GetPosition(this));
        _resizeStartBounds = new Rect(Left, Top, Width, Height);
        _stylusResizeActive = true;
        _inkModeBeforeStylusResize = Ink.EditingMode;
        _inkHitTestBeforeStylusResize = Ink.IsHitTestVisible;
        Ink.EditingMode = InkCanvasEditingMode.None;
        Ink.IsHitTestVisible = false;
        _inkInputActive = false;
        CancelAutoPenInput();
        ResetShiftLineMode();
        Stylus.Capture(this, CaptureMode.SubTree);
        e.Handled = true;
    }
    private void Resize_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (_resizeEdge == 0 || !ReferenceEquals(Stylus.Captured, this)) return;
        ApplyStylusResizePosition(e);
        e.Handled = true;
    }
    private void Resize_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (_resizeEdge == 0 || !ReferenceEquals(Stylus.Captured, this)) return;
        ApplyStylusResizePosition(e);
        FinishResize();
        e.Handled = true;
    }
    private void ApplyStylusResizePosition(StylusEventArgs e)
    {
        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        ApplyResizeDelta((current.X - _stylusResizeStartScreen.X) / dpi.DpiScaleX, (current.Y - _stylusResizeStartScreen.Y) / dpi.DpiScaleY);
    }
    private void ApplyResizeDelta(double dx, double dy)
    {
        var left = _resizeStartBounds.Left; var top = _resizeStartBounds.Top; var width = _resizeStartBounds.Width; var height = _resizeStartBounds.Height;
        if ((_resizeEdge & 1) != 0) { var proposed = Math.Max(MinWidth, _resizeStartBounds.Width - dx); left = _resizeStartBounds.Right - proposed; width = proposed; }
        if ((_resizeEdge & 2) != 0) width = Math.Max(MinWidth, _resizeStartBounds.Width + dx);
        if ((_resizeEdge & 4) != 0) { var proposed = Math.Max(MinHeight, _resizeStartBounds.Height - dy); top = _resizeStartBounds.Bottom - proposed; height = proposed; }
        if ((_resizeEdge & 8) != 0) height = Math.Max(MinHeight, _resizeStartBounds.Height + dy);
        Left = left; Top = top; Width = width; Height = height;
    }
    private void Resize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_resizeEdge != 0) { FinishResize(); e.Handled = true; } }
    private void FinishResize()
    {
        var restoreStylusMode = _stylusResizeActive;
        var previousMode = _inkModeBeforeStylusResize;
        var previousHitTest = _inkHitTestBeforeStylusResize;
        _stylusResizeActive = false;
        _resizeEdge = 0;
        if (Mouse.Captured is not null) Mouse.Capture(null);
        if (Stylus.Captured is not null) Stylus.Capture(null);
        Mouse.OverrideCursor = null;
        _vm.Touch();
        if (!restoreStylusMode) return;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            Ink.IsHitTestVisible = previousHitTest;
            Ink.EditingMode = previousMode;
        }));
    }
    private bool IsPointerOverHeaderButton(System.Windows.Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit) return false;
        var button = FindAncestor<Button>(hit);
        return ReferenceEquals(button, NewNoteButton) || ReferenceEquals(button, PinButton) || ReferenceEquals(button, SettingsButton) || ReferenceEquals(button, CloseNoteButton);
    }
    private int GetResizeEdge(System.Windows.Point point)
    {
        var edge = 0; if (point.X <= Grip) edge |= 1; else if (point.X >= ActualWidth - Grip) edge |= 2;
        if (point.Y <= Grip) edge |= 4; else if (point.Y >= ActualHeight - Grip) edge |= 8; return edge;
    }
    private int GetInteractiveResizeEdge(System.Windows.Point point)
    {
        var edge = GetResizeEdge(point);
        var overTitleDragArea = point.Y <= TitleArea.ActualHeight && point.X > Grip && point.X < ActualWidth - Grip;
        return overTitleDragArea ? edge & ~4 : edge;
    }
    private static System.Windows.Input.Cursor? CursorForEdge(int edge) => edge switch
    {
        1 or 2 => Cursors.SizeWE, 4 or 8 => Cursors.SizeNS, 5 or 10 => Cursors.SizeNWSE,
        6 or 9 => Cursors.SizeNESW, _ => null
    };
    private void Editor_TextChanged(object? sender, EventArgs e) { if (!_loading) _vm.Text = Editor.Text; }

    private void Editor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.N) { _controller.NewNote(_note); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Add || (e.Key == Key.OemPlus))) { _vm.FontSize += 2; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Subtract || e.Key == Key.OemMinus)) { _vm.FontSize -= 2; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D0) { _vm.FontSize = _controller.State.Settings.DefaultFontSize; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { if (TryPasteRich()) e.Handled = true; ScheduleSyntaxDetection(); }
        else if (e.Key is Key.Enter or Key.Return) ScheduleSyntaxDetection();
        else if (e.Key == Key.Escape && IsDrawingToolActive()) { SwitchToTextMode(); e.Handled = true; }
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift) UpdateShiftLineCursor();
        if (e.Key == Key.F10 || e.SystemKey == Key.F10) { OpenNoteMenu(true); e.Handled = true; return; }
        if (e.Key == Key.Escape && IsDrawingToolActive()) { SwitchToTextMode(); e.Handled = true; return; }
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S) { _ = ExportCurrentNoteAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
        else if (ctrl && Ink.EditingMode == InkCanvasEditingMode.Ink && (e.Key == Key.Add || e.Key == Key.OemPlus)) { ChangePenThickness(1); e.Handled = true; }
        else if (ctrl && Ink.EditingMode == InkCanvasEditingMode.Ink && (e.Key == Key.Subtract || e.Key == Key.OemMinus)) { ChangePenThickness(-1); e.Handled = true; }
        else if (ctrl && Ink.EditingMode == InkCanvasEditingMode.Ink && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { SetPenThickness(DefaultPenThickness); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z && (_selectedElement is not null || Ink.EditingMode != InkCanvasEditingMode.None)) { _history.Undo(); RestoreElements(); RestoreInk(); _vm.Touch(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) && (_selectedElement is not null || Ink.EditingMode != InkCanvasEditingMode.None)) { _history.Redo(); RestoreElements(); RestoreInk(); _vm.Touch(); e.Handled = true; }
        else if (e.Key == Key.Delete && _selectedElement is not null)
        {
            var element = _selectedElement;
            _history.Execute(new DelegateCommand(() => _note.Elements.Remove(element), () => _note.Elements.Add(element)));
            ClearObjectSelection(); RestoreElements(); _vm.Touch(); e.Handled = true;
        }
        else if (e.Key == Key.Escape) ClearObjectSelection();
    }
    private bool IsDrawingToolActive() => Ink.EditingMode != InkCanvasEditingMode.None || Ink.IsHitTestVisible || _inkInputActive || _autoPenInputActive;
    private void SwitchToTextMode()
    {
        Ink.EditingMode = InkCanvasEditingMode.None;
        Ink.IsHitTestVisible = false;
        _inkInputActive = false;
        CancelAutoPenInput();
        _suppressAutoPenUntilStylusLeaves = true;
        ResetShiftLineMode();
        ClearObjectSelection();
        Editor.Focus();
        Keyboard.Focus(Editor);
    }
    private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift) ResetShiftLineMode();
    }
    private void ChangePenThickness(int direction)
    {
        var current = Ink.DefaultDrawingAttributes.Width;
        var next = direction > 0
            ? PenThicknessSteps.FirstOrDefault(value => value > current + .01, PenThicknessSteps[^1])
            : PenThicknessSteps.LastOrDefault(value => value < current - .01, PenThicknessSteps[0]);
        SetPenThickness(next);
    }
    private void SetPenThickness(double thickness, bool saveSetting = true)
    {
        Ink.DefaultDrawingAttributes.Width = thickness;
        Ink.DefaultDrawingAttributes.Height = thickness;
        if (saveSetting)
        {
            _controller.State.Settings.DefaultPenThickness = thickness;
            _controller.ScheduleSave();
        }
    }
    private void AutoExpandForInk(System.Windows.Point point)
    {
        const double edgeThreshold = 18;
        const double expansion = 48;
        const double maximumDimension = 2400;
        var now = DateTime.UtcNow;
        if ((now - _lastInkAutoExpandUtc).TotalMilliseconds < 80) return;
        var expandWidth = point.X >= Ink.ActualWidth - edgeThreshold && Width < maximumDimension;
        var expandHeight = point.Y >= Ink.ActualHeight - edgeThreshold && Height < maximumDimension;
        if (!expandWidth && !expandHeight) return;
        _lastInkAutoExpandUtc = now;
        if (expandWidth) Width = Math.Min(maximumDimension, Width + expansion);
        if (expandHeight) Height = Math.Min(maximumDimension, Height + expansion);
    }
    private void Surface_PreviewStylusForAutoPen(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus) return;
        if (_suppressAutoPenUntilStylusLeaves)
        {
            if (e.RoutedEvent == Stylus.PreviewStylusUpEvent)
            {
                _suppressAutoPenUntilStylusLeaves = false;
                return;
            }
            if (e.RoutedEvent == Stylus.PreviewStylusDownEvent)
                _suppressAutoPenUntilStylusLeaves = false;
            else
                return;
        }
        if (_autoPenInputActive)
        {
            AppendAutoPenPoints(e.GetStylusPoints(Ink));
            if (e.RoutedEvent == Stylus.PreviewStylusMoveEvent) AutoExpandForInk(e.GetPosition(Ink));
            if (e.RoutedEvent == Stylus.PreviewStylusUpEvent) CompleteAutoPenStroke();
            e.Handled = true;
            return;
        }
        if (Ink.EditingMode != InkCanvasEditingMode.None) return;
        Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.Ink;
        if (e.RoutedEvent != Stylus.PreviewStylusDownEvent) return;
        var initialPoints = e.GetStylusPoints(Ink);
        _autoPenInputActive = true;
        _autoPenPoints = new StylusPointCollection(initialPoints.Description, Math.Max(1, initialPoints.Count));
        _autoPenAttributes = Ink.DefaultDrawingAttributes.Clone();
        AppendAutoPenPoints(initialPoints);
        Stylus.Capture(Surface, CaptureMode.SubTree);
        e.Handled = true;
    }
    private void AppendAutoPenPoints(StylusPointCollection points)
    {
        if (_autoPenPoints is null) return;
        foreach (var point in points)
        {
            if (_autoPenPoints.Count > 0 && (point.ToPoint() - _autoPenPoints[^1].ToPoint()).LengthSquared < .01) continue;
            _autoPenPoints.Add(point);
        }
    }
    private void CancelAutoPenInput()
    {
        _autoPenInputActive = false;
        _autoPenPoints = null;
        _autoPenAttributes = null;
        if (ReferenceEquals(Stylus.Captured, Surface)) Stylus.Capture(null);
    }
    private void CompleteAutoPenStroke()
    {
        var points = _autoPenPoints;
        var attributes = _autoPenAttributes;
        _autoPenInputActive = false;
        _autoPenPoints = null;
        _autoPenAttributes = null;
        if (ReferenceEquals(Stylus.Captured, Surface)) Stylus.Capture(null);
        if (points is null || points.Count == 0 || attributes is null) return;
        if (points.Count == 1)
        {
            var point = points[0];
            point.X += .1;
            point.Y += .1;
            points.Add(point);
        }
        var stroke = new Stroke(points) { DrawingAttributes = attributes };
        Ink.Strokes.Add(stroke);
        ProcessInkStroke(stroke);
    }
    private void Ink_PreviewMouseDownForStraightLine(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left || Ink.EditingMode != InkCanvasEditingMode.Ink || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true; _inkInputActive = false; UpdateShiftLineCursor();
        var current = e.GetPosition(Ink);
        if (_shiftLineAnchor is { } start && (current - start).Length >= 1) AddStraightInkLine(start, current);
        _shiftLineAnchor = current;
    }
    private void Ink_PreviewStylusDownForObjectSelection(object sender, StylusDownEventArgs e)
    {
        if (Ink.EditingMode != InkCanvasEditingMode.Ink || !TrySelectObjectAt(e.GetPosition(Surface))) return;
        _inkInputActive = false;
        ResetShiftLineMode();
        e.Handled = true;
    }
    private void Ink_PreviewMouseDownForObjectSelection(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left || Ink.EditingMode != InkCanvasEditingMode.Ink || !TrySelectObjectAt(e.GetPosition(Surface))) return;
        _inkInputActive = false;
        ResetShiftLineMode();
        e.Handled = true;
    }
    private bool TrySelectObjectAt(System.Windows.Point point)
    {
        var element = _note.Elements
            .Where(element => element is ImageElement or FileAttachmentElement)
            .OrderByDescending(element => element.ZIndex)
            .ThenByDescending(element => _note.Elements.IndexOf(element))
            .FirstOrDefault(element => ElementContainsPoint(element, point));
        if (element is null) return false;
        SelectObject(element);
        return true;
    }
    private bool ElementContainsPoint(NoteElement element, System.Windows.Point point) => element switch
    {
        ImageElement image => new Rect(image.X, image.Y, image.Width, image.Height + (string.IsNullOrWhiteSpace(image.Caption) ? 0 : Math.Max(24, Editor.FontSize * 2.5))).Contains(point),
        FileAttachmentElement attachment => new Rect(attachment.X, attachment.Y, attachment.Width, attachment.Height).Contains(point),
        _ => false
    };
    private void AddStraightInkLine(System.Windows.Point start, System.Windows.Point end)
    {
        var now = Environment.TickCount64;
        var points = new List<InkPointData> { new(start.X, start.Y, .5f, now), new(end.X, end.Y, .5f, now + 1) };
        var attributes = Ink.DefaultDrawingAttributes.Clone(); attributes.FitToCurve = false;
        var stroke = new Stroke(new StylusPointCollection(points.Select(point => new StylusPoint(point.X, point.Y, point.Pressure)))) { DrawingAttributes = attributes };
        var added = new InkStrokeElement { Points = points, Color = attributes.Color.ToString(), Thickness = attributes.Width };
        SetStrokeId(stroke, added.Id);
        Ink.Strokes.Add(stroke); _history.Execute(new DelegateCommand(() => { if (!_note.Elements.Contains(added)) _note.Elements.Add(added); }, () => _note.Elements.Remove(added))); _vm.Touch();
    }
    private void UpdateShiftLineCursor()
    {
        var active = Ink.EditingMode == InkCanvasEditingMode.Ink && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Ink.UseCustomCursor = active;
        if (active) Ink.Cursor = Cursors.Cross;
        else _shiftLineAnchor = null;
    }
    private void ResetShiftLineMode()
    {
        _shiftLineAnchor = null; Ink.UseCustomCursor = false;
    }
    private bool TryPasteRich()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (TryGetClipboardImage(data, out var image)) { AddBitmap(image, 24, 24); return true; }
                if (data?.GetDataPresent(DataFormats.FileDrop, true) == true && data.GetData(DataFormats.FileDrop, true) is string[] files)
                {
                    foreach (var file in files) AddFile(file, 24, 24); return true;
                }
                return false;
            }
            catch (ExternalException) when (attempt < 2) { Thread.Sleep(30); }
        }
        return false;
    }
    private static bool TryGetClipboardImage(IDataObject? data, out BitmapSource image)
    {
        image = null!;
        if (data is null) return false;
        if (data.GetDataPresent(DataFormats.Bitmap, true))
        {
            if (data.GetData(DataFormats.Bitmap, true) is BitmapSource bitmapSource) { bitmapSource.Freeze(); image = bitmapSource; return true; }
            var clipboardImage = System.Windows.Clipboard.GetImage(); if (clipboardImage is not null) { clipboardImage.Freeze(); image = clipboardImage; return true; }
        }
        if (data.GetDataPresent("PNG", true) && data.GetData("PNG", true) is Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0; var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); image = decoder.Frames[0]; image.Freeze(); return true;
        }
        return false;
    }
    private void Surface_DragOver(object sender, System.Windows.DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop, true) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Surface_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop, true) is not string[] files) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var p = e.GetPosition(Surface); var offset = 0d;
        foreach (var file in files) { AddFile(file, p.X + offset, p.Y + offset); offset += 12; }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }
    private void AddFile(string path, double x, double y)
    {
        switch (FileClassifier.Classify(path))
        {
            case DroppedFileKind.Text:
                try { var info = new FileInfo(path); if (info.Length <= 5 * 1024 * 1024) { InsertEditorText(File.ReadAllText(path)); ScheduleSyntaxDetection(path); } else MessageBox.Show("5MB보다 큰 텍스트 파일은 삽입할 수 없습니다."); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show("파일을 읽을 수 없습니다."); } break;
            case DroppedFileKind.Image:
                try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); AddBitmap(bitmap, x, y); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { MessageBox.Show("이미지를 열 수 없습니다."); } break;
            default: AddAttachment(new() { OriginalFilePath = path, DisplayName = Path.GetFileName(path), X = x, Y = y }); break;
        }
    }
    private void InsertEditorText(string text)
    {
        var offset = Editor.SelectionStart;
        Editor.Document.Replace(offset, Editor.SelectionLength, text);
        Editor.CaretOffset = offset + text.Length;
    }
    private void AddBitmap(BitmapSource bitmap, double x, double y)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaraNote", "images"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{Guid.NewGuid():N}.png"); using (var stream = File.Create(path)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); }
        var size = ImageSizing.Initial(bitmap.PixelWidth, bitmap.PixelHeight, Math.Max(80, Surface.ActualWidth - 40), Math.Max(80, Surface.ActualHeight - 40));
        var placedX = Math.Clamp(x, 0, Math.Max(0, Surface.ActualWidth - size.Width));
        var placedY = Math.Clamp(y, 0, Math.Max(0, Surface.ActualHeight - size.Height));
        AddImageElement(new() { StoredFilePath = path, X = placedX, Y = placedY, Width = size.Width, Height = size.Height });
    }
    private void AddImageElement(ImageElement element) { _history.Execute(new DelegateCommand(() => _note.Elements.Add(element), () => _note.Elements.Remove(element))); RestoreElements(); SelectObject(element); _vm.Touch(); }
    private void RenderImage(ImageElement element)
    {
        if (!File.Exists(element.StoredFilePath)) return;
        var image = new Image { Width = element.Width, Height = element.Height, Stretch = Stretch.Uniform, Source = new BitmapImage(new Uri(element.StoredFilePath)), ToolTip = element.Caption };
        image.Tag = element; image.Cursor = Cursors.SizeAll;
        Canvas.SetLeft(image, element.X); Canvas.SetTop(image, element.Y); Panel.SetZIndex(image, element.ZIndex); ObjectCanvas.Children.Add(image);
        AttachObjectInteraction(image, element); AddImageHandles(image, element);
        if (!string.IsNullOrWhiteSpace(element.Caption))
        {
            var caption = new TextBlock { Text = element.Caption, Width = element.Width, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Tag = element, Cursor = Cursors.IBeam, FontFamily = Editor.FontFamily, FontSize = Editor.FontSize, Foreground = Editor.Foreground };
            Canvas.SetLeft(caption, element.X); Canvas.SetTop(caption, element.Y + element.Height + 2); Panel.SetZIndex(caption, element.ZIndex); ObjectCanvas.Children.Add(caption);
            caption.MouseLeftButtonDown += (_, e) => { SelectObject(element); BeginCaptionEdit(element); e.Handled = true; };
        }
    }
    private void AddAttachment(FileAttachmentElement element) { _history.Execute(new DelegateCommand(() => _note.Elements.Add(element), () => _note.Elements.Remove(element))); RestoreElements(); SelectObject(element); _vm.Touch(); }
    private void RenderAttachment(FileAttachmentElement element)
    {
        var label = new TextBlock { Text = (File.Exists(element.OriginalFilePath) ? "📎 " : "⚠ ") + element.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
        var button = new Button { Content = label, Width = element.Width, Height = element.Height, Padding = new Thickness(8, 4, 8, 4), ToolTip = element.OriginalFilePath, Cursor = Cursors.SizeAll, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        button.Tag = element; Canvas.SetLeft(button, element.X); Canvas.SetTop(button, element.Y); Panel.SetZIndex(button, element.ZIndex); ObjectCanvas.Children.Add(button); AttachObjectInteraction(button, element);
        AddAttachmentHandles(button, element);
    }
    private static void OpenAttachment(FileAttachmentElement element)
    {
        try { if (!File.Exists(element.OriginalFilePath)) { MessageBox.Show("첨부 파일을 찾을 수 없습니다."); return; } Process.Start(new ProcessStartInfo(element.OriginalFilePath) { UseShellExecute = true }); }
        catch { MessageBox.Show("첨부 파일을 열 수 없습니다."); }
    }
    private void BeginCaptionEdit(ImageElement element)
    {
        const double captionSpace = 34;
        if (element.Y + element.Height + captionSpace > Surface.ActualHeight)
        {
            element.Y = Math.Max(0, Surface.ActualHeight - element.Height - captionSpace); RestoreElements(); SelectObject(element); _vm.Touch();
        }
        var original = element.Caption; var editor = new TextBox { Text = original, Width = element.Width, MinHeight = 28, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Padding = new Thickness(4), FontFamily = Editor.FontFamily, FontSize = Editor.FontSize, Foreground = Editor.Foreground };
        Canvas.SetLeft(editor, element.X); Canvas.SetTop(editor, element.Y + element.Height + 2); Panel.SetZIndex(editor, int.MaxValue); ObjectCanvas.Children.Add(editor);
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }));
        var completed = false;
        void Finish(bool commit)
        {
            if (completed) return; completed = true; ObjectCanvas.Children.Remove(editor);
            if (commit) { var updated = editor.Text.Trim(); if (updated != original) _history.Execute(new DelegateCommand(() => element.Caption = updated, () => element.Caption = original)); _vm.Touch(); }
            RestoreElements(); SelectObject(element);
        }
        editor.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { Finish(true); e.Handled = true; } else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
        editor.LostKeyboardFocus += (_, _) => Finish(true);
    }
    private void AttachObjectInteraction(FrameworkElement visual, NoteElement element)
    {
        visual.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount >= 2 && element is ImageElement imageElement) { SelectObject(element); BeginCaptionEdit(imageElement); e.Handled = true; return; }
            if (e.ClickCount >= 2 && element is FileAttachmentElement attachmentElement) { SelectObject(element); OpenAttachment(attachmentElement); e.Handled = true; return; }
            SelectObject(element); _dragOrigin = e.GetPosition(ObjectCanvas); _elementOrigin = GetElementPosition(element); visual.CaptureMouse(); e.Handled = true;
        }), true);
        visual.AddHandler(Mouse.PreviewMouseMoveEvent, new System.Windows.Input.MouseEventHandler((_, e) =>
        {
            if (!visual.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(ObjectCanvas); var x = Math.Clamp(_elementOrigin.X + point.X - _dragOrigin.X, 0, Math.Max(0, ObjectCanvas.ActualWidth - visual.ActualWidth)); var y = Math.Clamp(_elementOrigin.Y + point.Y - _dragOrigin.Y, 0, Math.Max(0, ObjectCanvas.ActualHeight - visual.ActualHeight));
            SetElementPosition(element, x, y); Canvas.SetLeft(visual, x); Canvas.SetTop(visual, y); if (element is ImageElement image) { PositionHandles(image); foreach (var caption in ObjectCanvas.Children.OfType<TextBlock>().Where(c => ReferenceEquals(c.Tag, image))) { Canvas.SetLeft(caption, x); Canvas.SetTop(caption, y + image.Height + 2); } } else if (element is FileAttachmentElement attachment) PositionAttachmentHandles(attachment);
        }), true);
        visual.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (!visual.IsMouseCaptured) return; visual.ReleaseMouseCapture(); var start = _elementOrigin; var end = GetElementPosition(element);
            if (start != end)
            {
                _history.Execute(new DelegateCommand(() => SetElementPosition(element, end.X, end.Y), () => SetElementPosition(element, start.X, start.Y)));
                _vm.Touch(); RestoreElements(); SelectObject(element);
            }
            e.Handled = true;
        }), true);
    }
    private void AddImageHandles(FrameworkElement image, ImageElement element)
    {
        foreach (var corner in Enum.GetValues<ResizeCorner>())
        {
            var resizeCursor = corner is ResizeCorner.TopLeft or ResizeCorner.BottomRight ? Cursors.SizeNWSE : Cursors.SizeNESW;
            var thumb = new Thumb { Width = 10, Height = 10, Background = Brushes.White, BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(1), Cursor = resizeCursor, Tag = (element, corner), Visibility = ReferenceEquals(_selectedElement, element) ? Visibility.Visible : Visibility.Collapsed };
            (double X, double Y, double Width, double Height) start = default;
            thumb.DragStarted += (_, _) => start = (element.X, element.Y, element.Width, element.Height);
            thumb.DragDelta += (_, e) =>
            {
                var r = ImageSizing.Resize(element.X, element.Y, element.Width, element.Height, e.HorizontalChange, e.VerticalChange, corner);
                element.X = r.X; element.Y = r.Y; element.Width = r.Width; element.Height = r.Height; ConstrainImage(element); image.Width = element.Width; image.Height = element.Height; Canvas.SetLeft(image, element.X); Canvas.SetTop(image, element.Y); PositionHandles(element);
            };
            thumb.DragCompleted += (_, _) =>
            {
                var end = (element.X, element.Y, element.Width, element.Height); _history.Execute(new DelegateCommand(() => ApplyBounds(element, end), () => ApplyBounds(element, start))); _vm.Touch(); RestoreElements(); SelectObject(element);
            };
            ObjectCanvas.Children.Add(thumb);
        }
        PositionHandles(element);
    }
    private void PositionHandles(ImageElement element)
    {
        foreach (var thumb in ObjectCanvas.Children.OfType<Thumb>().Where(x => x.Tag is ValueTuple<ImageElement, ResizeCorner> value && ReferenceEquals(value.Item1, element)))
        {
            var value = ((ImageElement, ResizeCorner))thumb.Tag; var left = value.Item2 is ResizeCorner.TopLeft or ResizeCorner.BottomLeft ? element.X - 5 : element.X + element.Width - 5; var top = value.Item2 is ResizeCorner.TopLeft or ResizeCorner.TopRight ? element.Y - 5 : element.Y + element.Height - 5;
            Canvas.SetLeft(thumb, left); Canvas.SetTop(thumb, top); Panel.SetZIndex(thumb, int.MaxValue);
        }
    }
    private void AddAttachmentHandles(FrameworkElement visual, FileAttachmentElement element)
    {
        foreach (var handle in Enum.GetValues<ResizeHandle>())
        {
            var cursor = handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                _ => Cursors.SizeNS
            };
            var thumb = new Thumb { Width = 10, Height = 10, Background = Brushes.White, BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(1), Cursor = cursor, Tag = (element, handle), Visibility = ReferenceEquals(_selectedElement, element) ? Visibility.Visible : Visibility.Collapsed };
            (double X, double Y, double Width, double Height) start = default;
            thumb.DragStarted += (_, _) => start = (element.X, element.Y, element.Width, element.Height);
            thumb.DragDelta += (_, e) =>
            {
                var resized = ObjectSizing.ResizeFree(element.X, element.Y, element.Width, element.Height, e.HorizontalChange, e.VerticalChange, handle);
                element.X = resized.X; element.Y = resized.Y; element.Width = resized.Width; element.Height = resized.Height; ConstrainAttachment(element);
                visual.Width = element.Width; visual.Height = element.Height; Canvas.SetLeft(visual, element.X); Canvas.SetTop(visual, element.Y); PositionAttachmentHandles(element);
            };
            thumb.DragCompleted += (_, _) =>
            {
                var end = (element.X, element.Y, element.Width, element.Height);
                _history.Execute(new DelegateCommand(() => ApplyAttachmentBounds(element, end), () => ApplyAttachmentBounds(element, start))); _vm.Touch(); RestoreElements(); SelectObject(element);
            };
            ObjectCanvas.Children.Add(thumb);
        }
        PositionAttachmentHandles(element);
    }
    private void PositionAttachmentHandles(FileAttachmentElement element)
    {
        foreach (var thumb in ObjectCanvas.Children.OfType<Thumb>().Where(x => x.Tag is ValueTuple<FileAttachmentElement, ResizeHandle> value && ReferenceEquals(value.Item1, element)))
        {
            var handle = ((FileAttachmentElement, ResizeHandle))thumb.Tag;
            var left = handle.Item2 switch { ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft => element.X - 5, ResizeHandle.Top or ResizeHandle.Bottom => element.X + element.Width / 2 - 5, _ => element.X + element.Width - 5 };
            var top = handle.Item2 switch { ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight => element.Y - 5, ResizeHandle.Left or ResizeHandle.Right => element.Y + element.Height / 2 - 5, _ => element.Y + element.Height - 5 };
            Canvas.SetLeft(thumb, left); Canvas.SetTop(thumb, top); Panel.SetZIndex(thumb, int.MaxValue);
        }
    }
    private void ConstrainAttachment(FileAttachmentElement element)
    {
        var availableWidth = Math.Max(1, ObjectCanvas.ActualWidth); var availableHeight = Math.Max(1, ObjectCanvas.ActualHeight);
        var minimumWidth = Math.Min(80, availableWidth); var minimumHeight = Math.Min(32, availableHeight);
        element.X = Math.Clamp(element.X, 0, Math.Max(0, availableWidth - minimumWidth)); element.Y = Math.Clamp(element.Y, 0, Math.Max(0, availableHeight - minimumHeight));
        element.Width = Math.Clamp(element.Width, minimumWidth, Math.Max(minimumWidth, availableWidth - element.X)); element.Height = Math.Clamp(element.Height, minimumHeight, Math.Max(minimumHeight, availableHeight - element.Y));
    }
    private void SelectObject(NoteElement element)
    {
        _selectedElement = element; _selectedVisual = ObjectCanvas.Children.OfType<FrameworkElement>().FirstOrDefault(x => ReferenceEquals(x.Tag, element));
        foreach (var thumb in ObjectCanvas.Children.OfType<Thumb>()) thumb.Visibility = IsHandleForElement(thumb, element) ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedVisual is not null) _selectedVisual.Opacity = .86;
    }
    private static bool IsHandleForElement(Thumb thumb, NoteElement element) => thumb.Tag switch
    {
        ValueTuple<ImageElement, ResizeCorner> image => ReferenceEquals(image.Item1, element),
        ValueTuple<FileAttachmentElement, ResizeHandle> attachment => ReferenceEquals(attachment.Item1, element),
        _ => false
    };
    private void ClearObjectSelection()
    {
        if (_selectedVisual is not null) _selectedVisual.Opacity = 1; _selectedVisual = null; _selectedElement = null; foreach (var thumb in ObjectCanvas.Children.OfType<Thumb>()) thumb.Visibility = Visibility.Collapsed;
    }
    private void Surface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedElement is null || e.OriginalSource is not DependencyObject source) return;
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Thumb || current is FrameworkElement { Tag: NoteElement }) return;
            if (ReferenceEquals(current, Surface)) break;
        }
        ClearObjectSelection();
    }
    private static (double X, double Y) GetElementPosition(NoteElement element) => element switch { ImageElement image => (image.X, image.Y), FileAttachmentElement file => (file.X, file.Y), _ => (0, 0) };
    private static void SetElementPosition(NoteElement element, double x, double y) { if (element is ImageElement image) { image.X = x; image.Y = y; } else if (element is FileAttachmentElement file) { file.X = x; file.Y = y; } }
    private static void ApplyBounds(ImageElement element, (double X, double Y, double Width, double Height) value) { element.X = value.X; element.Y = value.Y; element.Width = value.Width; element.Height = value.Height; }
    private static void ApplyAttachmentBounds(FileAttachmentElement element, (double X, double Y, double Width, double Height) value) { element.X = value.X; element.Y = value.Y; element.Width = value.Width; element.Height = value.Height; }
    private void ConstrainImage(ImageElement image)
    {
        var availableWidth = Math.Max(32, Surface.ActualWidth); var availableHeight = Math.Max(32, Surface.ActualHeight);
        var scale = Math.Min(1, Math.Min(availableWidth / Math.Max(image.Width, 1), availableHeight / Math.Max(image.Height, 1)));
        image.Width = Math.Max(32, image.Width * scale); image.Height = Math.Max(32, image.Height * scale);
        image.X = Math.Clamp(image.X, 0, Math.Max(0, Surface.ActualWidth - image.Width)); image.Y = Math.Clamp(image.Y, 0, Math.Max(0, Surface.ActualHeight - image.Height));
    }
    private void RestoreElements() { var selected = _selectedElement; ObjectCanvas.Children.Clear(); _selectedVisual = null; foreach (var e in _note.Elements.OrderBy(x => x.ZIndex)) { if (e is ImageElement i) RenderImage(i); else if (e is FileAttachmentElement f) RenderAttachment(f); } if (selected is not null && _note.Elements.Contains(selected)) SelectObject(selected); }
    private void RestoreInk()
    {
        Ink.Strokes.Clear(); foreach (var data in _note.Elements.OfType<InkStrokeElement>()) { var points = new StylusPointCollection(data.Points.Select(p => new StylusPoint(p.X, p.Y, p.Pressure))); var stroke = new Stroke(points) { DrawingAttributes = new DrawingAttributes { Color = (Color)ColorConverter.ConvertFromString(data.Color), Width = data.Thickness, Height = data.Thickness, IgnorePressure = false, FitToCurve = true, StylusTip = StylusTip.Ellipse } }; SetStrokeId(stroke, data.Id); Ink.Strokes.Add(stroke); }
    }
    private void PersistInk() { _note.Elements.RemoveAll(x => x is InkStrokeElement); foreach (var s in Ink.Strokes) _note.Elements.Add(CreateInkElement(s)); }
    private static InkStrokeElement CreateInkElement(Stroke stroke) => new()
    {
        Id = TryGetStrokeId(stroke, out var id) ? id : Guid.NewGuid(),
        Color = stroke.DrawingAttributes.Color.ToString(), Thickness = stroke.DrawingAttributes.Width,
        Points = stroke.StylusPoints.Select(p => new InkPointData(p.X, p.Y, p.PressureFactor)).ToList()
    };
    private void Ink_StrokeErasing(object sender, InkCanvasStrokeErasingEventArgs erased)
    {
        var removed = TryGetStrokeId(erased.Stroke, out var id) ? _note.Elements.OfType<InkStrokeElement>().FirstOrDefault(stroke => stroke.Id == id) : null;
        if (removed is not null)
            _history.Execute(new DelegateCommand(() => _note.Elements.Remove(removed), () => _note.Elements.Add(removed)));
        else
        {
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { PersistInk(); _vm.Touch(); }));
            return;
        }
        _vm.Touch();
    }
    private void Ink_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        => ProcessInkStroke(e.Stroke);
    private void ProcessInkStroke(Stroke stroke)
    {
        var now = Environment.TickCount64; var pts = stroke.StylusPoints.Select((p, i) => new InkPointData(p.X, p.Y, p.PressureFactor, now - (stroke.StylusPoints.Count - i) * 8)).ToList();
        var existing = _note.Elements.OfType<InkStrokeElement>().ToList(); var result = _scribble.Analyze(pts, existing);
        if (result.IsScribble && result.TargetStrokeIds.Count > 0)
        {
            Ink.Strokes.Remove(stroke); var removed = existing.Where(x => result.TargetStrokeIds.Contains(x.Id)).ToList();
            _history.Execute(new DelegateCommand(() => _note.Elements.RemoveAll(x => removed.Contains(x)), () => _note.Elements.AddRange(removed))); RestoreInk();
        }
        else
        {
            var added = new InkStrokeElement { Points = pts, Color = stroke.DrawingAttributes.Color.ToString(), Thickness = stroke.DrawingAttributes.Width };
            SetStrokeId(stroke, added.Id);
            _history.Execute(new DelegateCommand(() => { if (!_note.Elements.Contains(added)) _note.Elements.Add(added); }, () => _note.Elements.Remove(added)));
        }
        _vm.Touch();
    }
    private static void SetStrokeId(Stroke stroke, Guid id) => stroke.AddPropertyData(StrokeIdProperty, id.ToString("D"));
    private static bool TryGetStrokeId(Stroke stroke, out Guid id)
    {
        id = default;
        return stroke.ContainsPropertyData(StrokeIdProperty)
            && stroke.GetPropertyData(StrokeIdProperty) is string value
            && Guid.TryParse(value, out id);
    }
    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        _noteContextMenu = menu;
        AddTextEditingMenus(menu);
        menu.Items.Add(new Separator());
        var select = new MenuItem { Header = "텍스트 모드", IsCheckable = true };
        var pen = new MenuItem { Header = "펜 모드", IsCheckable = true };
        var erase = new MenuItem { Header = "지우개 모드", IsCheckable = true };
        void UpdateToolModeMenu()
        {
            var hasInk = Ink.Strokes.Count > 0;
            if (!hasInk && Ink.EditingMode == InkCanvasEditingMode.EraseByStroke) { Ink.EditingMode = InkCanvasEditingMode.None; Ink.IsHitTestVisible = false; }
            select.IsChecked = Ink.EditingMode == InkCanvasEditingMode.None;
            pen.IsChecked = Ink.EditingMode == InkCanvasEditingMode.Ink;
            erase.IsChecked = Ink.EditingMode == InkCanvasEditingMode.EraseByStroke;
            erase.Visibility = hasInk ? Visibility.Visible : Visibility.Collapsed;
        }
        select.Click += (_, _) => { SwitchToTextMode(); UpdateToolModeMenu(); };
        pen.Click += (_, _) => { Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.Ink; UpdateShiftLineCursor(); UpdateToolModeMenu(); };
        erase.Click += (_, _) => { Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.EraseByStroke; ResetShiftLineMode(); UpdateToolModeMenu(); };
        menu.Opened += (_, _) => UpdateToolModeMenu();
        menu.Items.Add(select); menu.Items.Add(pen); menu.Items.Add(erase); AddPenMenus(menu);
        menu.Items.Add(new Separator());
        AddSyntaxLanguageMenu(menu); AddNoteColorMenu(menu);
        menu.Items.Add(new Separator());
        var settings = new MenuItem { Header = "설정" };
        settings.Click += Settings_Click;
        menu.Items.Add(settings);
        menu.Closed += (_, _) => { menu.PlacementTarget = null; menu.Placement = PlacementMode.MousePoint; };
        Surface.ContextMenu = menu; Editor.ContextMenu = menu; RestoreInk();
    }
    private void AddSyntaxLanguageMenu(ContextMenu menu)
    {
        var syntax = new MenuItem { Header = "구문 강조" };
        var languages = new (string Label, string Value)[]
        {
            ("자동 감지", "Auto"), ("일반 텍스트", "PlainText"), ("C#", "CSharp"), ("C/C++", "Cpp"), ("Python", "Python"), ("Lua", "Lua"),
            ("JSON", "Json"), ("XML", "Xml"), ("HTML", "Html"), ("JavaScript", "JavaScript"),
            ("CSS", "Css"), ("Markdown", "Markdown"), ("PowerShell", "PowerShell")
        };
        var items = new List<MenuItem>();
        foreach (var (label, value) in languages)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            item.Click += (_, _) =>
            {
                _note.SyntaxLanguage = value;
                _note.IsSyntaxLanguageExplicit = value != "Auto";
                ApplySyntaxHighlighting();
                _vm.Touch();
                if (value == "Auto") ScheduleSyntaxDetection();
            };
            items.Add(item);
            syntax.Items.Add(item);
        }
        syntax.SubmenuOpened += (_, _) =>
        {
            for (var i = 0; i < items.Count; i++) items[i].IsChecked = languages[i].Value == _note.SyntaxLanguage;
        };
        menu.Items.Add(syntax);
    }
    private void ScheduleSyntaxDetection(string? fileName = null)
    {
        if (_note.IsSyntaxLanguageExplicit || _note.SyntaxLanguage != "Auto") return;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (_note.IsSyntaxLanguageExplicit || _note.SyntaxLanguage != "Auto") return;
            var detected = SyntaxDetector.Detect(Editor.Text, fileName);
            if (detected is null || detected.Confidence < .75) return;
            _note.SyntaxLanguage = detected.Language;
            ApplySyntaxHighlighting();
            _vm.Touch();
        }));
    }
    private void ApplySyntaxHighlighting()
    {
        var definitionName = _note.SyntaxLanguage switch
        {
            "CSharp" => "C#",
            "Cpp" => "C++",
            "Python" => "Python",
            "Lua" => "Lua",
            "Json" => "Json",
            "Xml" => "XML",
            "Html" => "HTML",
            "JavaScript" => "JavaScript",
            "Css" => "CSS",
            "Markdown" => "MarkDown",
            "PowerShell" => "PowerShell",
            _ => null
        };
        Editor.SyntaxHighlighting = definitionName switch
        {
            null => null,
            "Lua" => LoadLuaHighlighting(),
            _ => HighlightingManager.Instance.GetDefinition(definitionName)
        };
    }
    private static IHighlightingDefinition? LoadLuaHighlighting()
    {
        if (_luaHighlighting is not null) return _luaHighlighting;
        using var stream = typeof(NoteWindow).Assembly.GetManifestResourceStream("NaraNote.App.Resources.Lua.xshd");
        if (stream is null) return null;
        using var reader = XmlReader.Create(stream);
        _luaHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        return _luaHighlighting;
    }
    private void AddTextEditingMenus(ContextMenu menu)
    {
        var save = new MenuItem { Header = "현재 노트 저장", InputGestureText = "Ctrl+S" };
        save.Click += (_, _) => _ = ExportCurrentNoteAsync(false);
        menu.Items.Add(save);
        var saveAs = new MenuItem { Header = "다른 이름으로 저장", InputGestureText = "Ctrl+Shift+S" };
        saveAs.Click += (_, _) => _ = ExportCurrentNoteAsync(true);
        menu.Items.Add(saveAs);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "실행 취소", Command = ApplicationCommands.Undo, CommandTarget = Editor, InputGestureText = "Ctrl+Z" });
        menu.Items.Add(new MenuItem { Header = "다시 실행", Command = ApplicationCommands.Redo, CommandTarget = Editor, InputGestureText = "Ctrl+Y" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "잘라내기", Command = ApplicationCommands.Cut, CommandTarget = Editor, InputGestureText = "Ctrl+X" });
        menu.Items.Add(new MenuItem { Header = "복사", Command = ApplicationCommands.Copy, CommandTarget = Editor, InputGestureText = "Ctrl+C" });
        menu.Items.Add(new MenuItem { Header = "붙여넣기", Command = ApplicationCommands.Paste, CommandTarget = Editor, InputGestureText = "Ctrl+V" });
        menu.Items.Add(new MenuItem { Header = "삭제", Command = ApplicationCommands.Delete, CommandTarget = Editor, InputGestureText = "Delete" });
        menu.Items.Add(new MenuItem { Header = "전체 선택", Command = ApplicationCommands.SelectAll, CommandTarget = Editor, InputGestureText = "Ctrl+A" });
    }
    private async Task ExportCurrentNoteAsync(bool saveAs)
    {
        try
        {
            Keyboard.ClearFocus();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Input);
            var snapshot = CreateExportSnapshot();
            var rich = snapshot.Elements.Count > 0;
            var path = _note.ExportFilePath;
            var showDialog = saveAs || string.IsNullOrWhiteSpace(path);
            while (true)
            {
                if (showDialog)
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = "현재 노트 저장",
                        AddExtension = true,
                        OverwritePrompt = true,
                        DefaultExt = rich ? ".naranote" : ".txt",
                        Filter = rich
                            ? "NaraNote 문서 (*.naranote)|*.naranote|텍스트 파일 (*.txt)|*.txt"
                            : "텍스트 파일 (*.txt)|*.txt|NaraNote 문서 (*.naranote)|*.naranote",
                        FilterIndex = 1,
                        FileName = CreateSuggestedExportName(snapshot.Text)
                    };
                    if (dialog.ShowDialog(this) != true) return;
                    path = dialog.FileName;
                }
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!rich || string.Equals(Path.GetExtension(path), ".naranote", StringComparison.OrdinalIgnoreCase)) break;
                var result = MessageBox.Show(
                    "이 형식으로 저장하면 텍스트만 저장되며 드로잉, 이미지 및 첨부 개체는 파일에 포함되지 않습니다.\n\n계속 저장하시겠습니까?",
                    "개체가 제외됩니다", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (result == MessageBoxResult.Yes) break;
                if (!showDialog) return;
                path = null;
            }
            await _documentExporter.ExportAsync(snapshot, path);
            _vm.MarkExported(path);
            UpdateExportPathDisplay();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            MessageBox.Show("현재 노트를 파일로 저장하지 못했습니다. 저장 위치와 파일 접근 권한을 확인해 주세요.", "NaraNote");
        }
    }
    private NoteData CreateExportSnapshot()
    {
        var elements = _note.Elements.Where(element => element is not InkStrokeElement).Select(element => element switch
        {
            ImageElement image => (NoteElement)new ImageElement { Id = image.Id, ZIndex = image.ZIndex, StoredFilePath = image.StoredFilePath, X = image.X, Y = image.Y, Width = image.Width, Height = image.Height, Caption = image.Caption },
            FileAttachmentElement file => new FileAttachmentElement { Id = file.Id, ZIndex = file.ZIndex, OriginalFilePath = file.OriginalFilePath, DisplayName = file.DisplayName, X = file.X, Y = file.Y, Width = file.Width, Height = file.Height },
            _ => throw new NotSupportedException($"지원하지 않는 노트 요소입니다: {element.GetType().Name}")
        }).ToList();
        elements.AddRange(Ink.Strokes.Select(stroke => (NoteElement)new InkStrokeElement
        {
            Color = stroke.DrawingAttributes.Color.ToString(), Thickness = stroke.DrawingAttributes.Width,
            Points = stroke.StylusPoints.Select(point => new InkPointData(point.X, point.Y, point.PressureFactor)).ToList()
        }));
        return new NoteData
        {
            Id = _note.Id, Width = Width, Height = Height, Color = _note.Color, FontFamily = _note.FontFamily,
            FontSize = _note.FontSize, Text = Editor.Text, SyntaxLanguage = _note.SyntaxLanguage,
            IsSyntaxLanguageExplicit = _note.IsSyntaxLanguageExplicit, LastModifiedUtc = DateTimeOffset.UtcNow, Elements = elements
        };
    }
    private static string CreateSuggestedExportName(string text)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        var name = string.IsNullOrWhiteSpace(firstLine) ? $"NaraNote-{DateTime.Now:yyyyMMdd-HHmm}" : firstLine;
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        if (name.Length > 48) name = name[..48].Trim();
        return string.IsNullOrWhiteSpace(name) ? "NaraNote" : name;
    }
    private void UpdateExportPathDisplay()
    {
        if (ExportPathTextBlock is null) return;
        var path = _note.ExportFilePath;
        ExportPathTextBlock.Text = string.IsNullOrWhiteSpace(path) ? "" : CompactExportPath(path);
        ExportPathTextBlock.ToolTip = string.IsNullOrWhiteSpace(path) ? null : _note.IsExportDirty ? $"{path}\n저장한 파일 이후 수정됨" : path;
        ExportPathTextBlock.Visibility = string.IsNullOrWhiteSpace(path) ? Visibility.Collapsed : Visibility.Visible;
        ExportDirtyIndicator.Visibility = !string.IsNullOrWhiteSpace(path) && _note.IsExportDirty ? Visibility.Visible : Visibility.Collapsed;
    }
    private string CompactExportPath(string path)
    {
        var availableWidth = Math.Max(40, ActualWidth - 132 - (_note.IsExportDirty ? 12 : 0));
        bool Fits(string value)
        {
            var formatted = new FormattedText(value, System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(ExportPathTextBlock.FontFamily, ExportPathTextBlock.FontStyle,
                    ExportPathTextBlock.FontWeight, ExportPathTextBlock.FontStretch),
                ExportPathTextBlock.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace <= availableWidth;
        }

        if (Fits(path)) return path;
        var fileName = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);
        var parent = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var withParent = $"…{Path.DirectorySeparatorChar}{parent}{Path.DirectorySeparatorChar}{fileName}";
            if (Fits(withParent)) return withParent;
        }
        var fileOnly = $"…{Path.DirectorySeparatorChar}{fileName}";
        if (Fits(fileOnly)) return fileOnly;

        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var keep = Math.Max(1, stem.Length - 1); keep >= 1; keep--)
        {
            var prefixLength = (keep + 1) / 2;
            var suffixLength = keep / 2;
            var shortenedStem = suffixLength == 0 ? stem[..prefixLength] : $"{stem[..prefixLength]}…{stem[^suffixLength..]}";
            var candidate = $"…{Path.DirectorySeparatorChar}{shortenedStem}{extension}";
            if (Fits(candidate)) return candidate;
        }
        return extension.Length > 0 ? $"…{extension}" : "…";
    }
    private void AddNoteColorMenu(ContextMenu menu)
    {
        var colorMenu = new MenuItem { Header = "노트 색상" };
        var presets = new[]
        {
            ("노랑", AppSettings.DefaultNoteColor), ("연두", "#FFCFF09E"), ("하늘색", "#FFBDEBFF"),
            ("분홍", "#FFFFC4D8"), ("주황", "#FFFFC27A"), ("연보라", "#FFDCC6FF"), ("연회색", "#FFF3F3F3")
        };
        foreach (var (name, value) in presets)
        {
            var swatch = new System.Windows.Controls.Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), BorderThickness = new Thickness(1), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)) };
            var item = new MenuItem { Header = name, Icon = swatch, IsCheckable = true, Tag = value };
            item.Click += (_, _) => { _vm.Color = value; UpdateNoteColorChecks(colorMenu); };
            colorMenu.Items.Add(item);
        }
        colorMenu.SubmenuOpened += (_, _) => UpdateNoteColorChecks(colorMenu);
        menu.Items.Insert(0, colorMenu); menu.Items.Insert(1, new Separator());
    }
    private void UpdateNoteColorChecks(MenuItem colorMenu)
    {
        foreach (var item in colorMenu.Items.OfType<MenuItem>()) item.IsChecked = string.Equals(item.Tag as string, _note.Color, StringComparison.OrdinalIgnoreCase);
    }
    private void AddPenMenus(ContextMenu menu)
    {
        var colors = new MenuItem { Header = "펜 색상" };
        foreach (var (name, value) in new[] { ("검정", Colors.Black), ("진회색", Colors.DarkSlateGray), ("빨강", Colors.Red), ("파랑", Colors.Blue), ("초록", Colors.Green), ("주황", Colors.Orange), ("보라", Colors.Purple) })
        {
            var swatch = new System.Windows.Controls.Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(value) };
            var item = new MenuItem { Header = name, Icon = swatch, IsCheckable = true, IsChecked = Ink.DefaultDrawingAttributes.Color == value };
            item.Click += (_, _) => { Ink.DefaultDrawingAttributes.Color = value; _controller.State.Settings.DefaultPenColor = value.ToString(); _controller.ScheduleSave(); foreach (MenuItem sibling in colors.Items) sibling.IsChecked = ReferenceEquals(sibling, item); }; colors.Items.Add(item);
        }
        var widths = new MenuItem { Header = "펜 굵기" };
        foreach (var (name, value) in new[] { ("매우 가늘게", 1d), ("가늘게", 2d), ("보통", DefaultPenThickness), ("굵게", 6d), ("매우 굵게", 10d) })
        {
            var item = new MenuItem { Header = name, Tag = value, IsCheckable = true, IsChecked = Math.Abs(Ink.DefaultDrawingAttributes.Width - value) < .01 };
            item.Click += (_, _) => { SetPenThickness(value); foreach (MenuItem sibling in widths.Items) sibling.IsChecked = ReferenceEquals(sibling, item); }; widths.Items.Add(item);
        }
        widths.SubmenuOpened += (_, _) => { foreach (MenuItem item in widths.Items) item.IsChecked = item.Tag is double value && Math.Abs(Ink.DefaultDrawingAttributes.Width - value) < .01; };
        menu.Items.Add(new Separator()); menu.Items.Add(colors); menu.Items.Add(widths);
    }
    private void EnableNativeWindowAppearance()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _imeHwndSource = HwndSource.FromHwnd(hwnd);
        _imeHwndSource?.AddHook(ImeWndProc);
        var margins = new DwmMargins();
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }
    private IntPtr ImeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmImeStartComposition) BeginImeComposition();
        else if (msg == WmImeComposition && _imeCompositionActive)
        {
            UpdateImeComposition(hwnd, lParam);
            handled = true;
        }
        else if (msg == WmImeEndComposition) EndImeComposition();
        return IntPtr.Zero;
    }
    private void BeginImeComposition()
    {
        if (Editor.SelectionLength > 0)
        {
            var selectionStart = Editor.SelectionStart;
            Editor.Document.Remove(selectionStart, Editor.SelectionLength);
            Editor.CaretOffset = selectionStart;
            Editor.Select(selectionStart, 0);
        }
        if (_imeCompositionActive) return;
        _imeCompositionActive = true;
        Editor.TextArea.Caret.Hide();
    }
    private void EndImeComposition()
    {
        if (!_imeCompositionActive) return;
        _imeCompositionActive = false;
        ImeCompositionCanvas.Children.Clear();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (Editor.TextArea.IsKeyboardFocused) Editor.TextArea.Caret.Show();
        }));
    }
    private void CommitImeCompositionBeforeClose()
    {
        if (!_imeCompositionActive) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) { EndImeComposition(); return; }
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) { EndImeComposition(); return; }
        try
        {
            var composition = ReadImeString(context, GcsCompStr);
            if (composition.Length > 0) InsertEditorText(composition);
            _ = ImmNotifyIME(context, NiCompositionStr, CpsCancel, 0);
        }
        finally { _ = ImmReleaseContext(hwnd, context); }
        EndImeComposition();
    }
    private void UpdateImeComposition(IntPtr hwnd, IntPtr compositionFlags)
    {
        Editor.TextArea.Caret.Hide();
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) return;
        try
        {
            HideNativeImeComposition(context);
            if ((compositionFlags.ToInt64() & GcsResultStr) != 0)
            {
                var result = ReadImeString(context, GcsResultStr);
                if (result.Length > 0) InsertEditorText(result);
                ImeCompositionCanvas.Children.Clear();
                if ((compositionFlags.ToInt64() & GcsCompStr) == 0) return;
            }
            var composition = ReadImeString(context, GcsCompStr);
            if (composition.Length == 0) { ImeCompositionCanvas.Children.Clear(); return; }
            // The Microsoft Korean IME reports position 0 through the legacy IMM path
            // even though its visible composition caret belongs after the syllable.
            // Keep the inline pre-edit caret at the end, matching modern Windows editors.
            var cursorPosition = composition.Length;
            RenderImeComposition(composition, cursorPosition);
        }
        finally { _ = ImmReleaseContext(hwnd, context); }
    }
    private static string ReadImeString(IntPtr context, int index)
    {
        var byteCount = ImmGetCompositionString(context, index, IntPtr.Zero, 0);
        if (byteCount <= 0) return string.Empty;
        var buffer = Marshal.AllocHGlobal(byteCount);
        try
        {
            return ImmGetCompositionString(context, index, buffer, byteCount) > 0
                ? Marshal.PtrToStringUni(buffer, byteCount / 2) ?? string.Empty
                : string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void HideNativeImeComposition(IntPtr context)
    {
        var form = new CompositionForm { Style = CfsForcePosition, CurrentPosition = new NativePoint { X = -32000, Y = -32000 } };
        _ = ImmSetCompositionWindow(context, ref form);
    }
    private void RenderImeComposition(string composition, int cursorPosition)
    {
        ImeCompositionCanvas.Children.Clear();
        var view = Editor.TextArea.TextView;
        var caret = Editor.TextArea.Caret.CalculateCaretRectangle();
        var origin = view.TransformToAncestor(Surface).Transform(new System.Windows.Point(
            caret.Left - view.ScrollOffset.X, caret.Top - view.ScrollOffset.Y));
        var foreground = Editor.Foreground ?? Brushes.Black;
        var text = new TextBlock
        {
            Text = composition,
            FontFamily = Editor.FontFamily,
            FontSize = Editor.FontSize,
            Foreground = foreground,
            TextDecorations = TextDecorations.Underline,
            Background = Frame.Background
        };
        Canvas.SetLeft(text, origin.X);
        Canvas.SetTop(text, origin.Y);
        ImeCompositionCanvas.Children.Add(text);
        var beforeCaret = composition[..cursorPosition];
        var formatted = new FormattedText(beforeCaret, System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface(Editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            Editor.FontSize, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var caretLine = new System.Windows.Shapes.Line
        {
            X1 = origin.X + formatted.WidthIncludingTrailingWhitespace,
            X2 = origin.X + formatted.WidthIncludingTrailingWhitespace,
            Y1 = origin.Y + 1,
            Y2 = origin.Y + Math.Max(caret.Height, Editor.FontSize),
            Stroke = foreground,
            StrokeThickness = 1
        };
        ImeCompositionCanvas.Children.Add(caretLine);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm { public int Style; public NativePoint CurrentPosition; public NativeRect Area; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hwnd);
    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetCompositionString(IntPtr context, int index, IntPtr buffer, int bufferLength);
    [DllImport("imm32.dll")]
    private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
    [DllImport("imm32.dll")]
    private static extern bool ImmNotifyIME(IntPtr context, int action, int index, int value);
    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref DwmMargins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int valueSize);
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest) return IntPtr.Zero; var point = PointFromLParam(lParam); var local = PointFromScreen(point); const double g = Grip;
        var left = local.X <= g; var right = local.X >= ActualWidth - g; var top = local.Y <= g; var bottom = local.Y >= ActualHeight - g;
        handled = left || right || top || bottom;
        return (left, right, top, bottom) switch { (true, _, true, _) => 13, (_, true, true, _) => 14, (true, _, _, true) => 16, (_, true, _, true) => 17, (true, _, _, _) => 10, (_, true, _, _) => 11, (_, _, true, _) => 12, (_, _, _, true) => 15, _ => IntPtr.Zero };
    }
    private static System.Windows.Point PointFromLParam(IntPtr value) { var n = value.ToInt64(); return new((short)(n & 0xffff), (short)((n >> 16) & 0xffff)); }
}
