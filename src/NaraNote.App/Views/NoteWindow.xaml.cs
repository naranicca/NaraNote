using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
using Microsoft.Win32;
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

namespace NaraNote.App.Views;

public partial class NoteWindow : Window
{
    private const int WmNcHitTest = 0x0084, Grip = 16;
    private const int DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const double DefaultPenThickness = 3.5;
    private static readonly double[] PenThicknessSteps = [1d, 2d, DefaultPenThickness, 6d, 10d];
    private readonly NoteData _note; private readonly AppController _controller; private readonly NoteViewModel _vm;
    private bool _loading = true; private readonly ScribbleRecognizer _scribble = new();
    private readonly UndoManager _history = new();
    private NoteElement? _selectedElement;
    private FrameworkElement? _selectedVisual;
    private System.Windows.Point _dragOrigin;
    private (double X, double Y) _elementOrigin;
    private int _resizeEdge;
    private System.Drawing.Point _resizeStartCursor;
    private System.Windows.Point _stylusResizeStartScreen;
    private Rect _resizeStartBounds;
    private bool _inkInputActive;
    private bool _autoPenInputActive;
    private StylusPointCollection? _autoPenPoints;
    private DrawingAttributes? _autoPenAttributes;
    private bool _suppressAutoPenUntilStylusLeaves;
    private System.Windows.Point? _shiftLineAnchor;
    private DateTime _lastInkAutoExpandUtc = DateTime.MinValue;
    public NoteWindow(NoteData note, AppController controller)
    {
        InitializeComponent(); _note = note; _controller = controller; _vm = new(note, controller.ScheduleSave); DataContext = _vm;
        SourceInitialized += (_, _) => EnableNativeWindowAppearance();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NoteViewModel.Color)) ApplyAppearance(); };
        Left = note.Left; Top = note.Top; Width = note.Width; Height = note.Height; Topmost = note.IsAlwaysOnTop;
        ApplyPenSettings();
        Editor.Text = note.Text; ApplyAppearance(); RestoreElements(); BuildContextMenu();
        PreviewMouseLeftButtonDown += Resize_MouseLeftButtonDown;
        PreviewMouseMove += Resize_MouseMove;
        PreviewMouseLeftButtonUp += Resize_MouseLeftButtonUp;
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
        SizeChanged += (_, _) => { _note.Width = Width; _note.Height = Height; _vm.Touch(); };
        Activated += (_, _) => _note.LastModifiedUtc = DateTimeOffset.UtcNow;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewKeyUp += Window_PreviewKeyUp;
        Deactivated += (_, _) => ResetShiftLineMode();
        _loading = false;
    }
    public void FocusEditor()
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            Editor.Focus(); Keyboard.Focus(Editor); Editor.CaretIndex = Editor.Text.Length;
        }));
    }
    public void ApplyAppSettings() => ApplyPenSettings();
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
        Foreground = foreground; Editor.Foreground = foreground; Editor.FontSize = _note.FontSize; Editor.FontFamily = new FontFamily(_note.FontFamily);
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
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_vm, _controller.State.Settings) { Owner = this };
        var confirmed = dialog.ShowDialog() == true;
        ApplyAppearance(); if (confirmed) _controller.ApplySettings(); await _controller.SaveNowAsync();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e) { _note.IsOpen = false; base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { PersistInk(); _controller.Closed(_note); base.OnClosed(e); }
    private void TitleArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        DragMove(); e.Handled = true;
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
        _inkInputActive = false;
        Stylus.Capture(this, CaptureMode.SubTree);
        e.Handled = true;
    }
    private void Resize_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (_resizeEdge == 0 || !ReferenceEquals(Stylus.Captured, this)) return;
        var current = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        ApplyResizeDelta((current.X - _stylusResizeStartScreen.X) / dpi.DpiScaleX, (current.Y - _stylusResizeStartScreen.Y) / dpi.DpiScaleY);
        e.Handled = true;
    }
    private void Resize_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (_resizeEdge == 0 || !ReferenceEquals(Stylus.Captured, this)) return;
        FinishResize();
        e.Handled = true;
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
    private void FinishResize() { _resizeEdge = 0; if (Mouse.Captured is not null) Mouse.Capture(null); if (Stylus.Captured is not null) Stylus.Capture(null); Mouse.OverrideCursor = null; _vm.Touch(); }
    private bool IsPointerOverHeaderButton(System.Windows.Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit) return false;
        var button = FindAncestor<Button>(hit);
        return ReferenceEquals(button, NewNoteButton) || ReferenceEquals(button, SettingsButton) || ReferenceEquals(button, CloseNoteButton);
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
    private void Editor_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) _vm.Text = Editor.Text; }

    private void Editor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.N) { _controller.NewNote(_note); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Add || (e.Key == Key.OemPlus))) { _vm.FontSize += 2; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Subtract || e.Key == Key.OemMinus)) { _vm.FontSize -= 2; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D0) { _vm.FontSize = _controller.State.Settings.DefaultFontSize; ApplyAppearance(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V && TryPasteRich()) e.Handled = true;
        else if (e.Key == Key.Escape) { Ink.EditingMode = InkCanvasEditingMode.None; Ink.IsHitTestVisible = false; Editor.Focus(); e.Handled = true; }
    }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift) UpdateShiftLineCursor();
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && Ink.EditingMode == InkCanvasEditingMode.Ink && (e.Key == Key.Add || e.Key == Key.OemPlus)) { ChangePenThickness(1); e.Handled = true; }
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
        else if (e.Key == Key.Escape && Ink.EditingMode != InkCanvasEditingMode.None)
        {
            Ink.EditingMode = InkCanvasEditingMode.None; Ink.IsHitTestVisible = false; _inkInputActive = false;
            _suppressAutoPenUntilStylusLeaves = true;
            ResetShiftLineMode();
            ClearObjectSelection(); Editor.Focus(); Keyboard.Focus(Editor); e.Handled = true;
        }
        else if (e.Key == Key.Escape) ClearObjectSelection();
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
        if (_autoPenInputActive)
        {
            AppendAutoPenPoints(e.GetStylusPoints(Ink));
            if (e.RoutedEvent == Stylus.PreviewStylusMoveEvent) AutoExpandForInk(e.GetPosition(Ink));
            if (e.RoutedEvent == Stylus.PreviewStylusUpEvent) CompleteAutoPenStroke();
            e.Handled = true;
            return;
        }
        if (_suppressAutoPenUntilStylusLeaves || Ink.EditingMode != InkCanvasEditingMode.None) return;
        Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.Ink;
        if (e.RoutedEvent != Stylus.PreviewStylusDownEvent) return;
        _autoPenInputActive = true;
        _autoPenPoints = new StylusPointCollection();
        _autoPenAttributes = Ink.DefaultDrawingAttributes.Clone();
        AppendAutoPenPoints(e.GetStylusPoints(Ink));
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
            points.Add(new StylusPoint(point.X + .1, point.Y + .1, point.PressureFactor));
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
                try { var info = new FileInfo(path); if (info.Length <= 5 * 1024 * 1024) Editor.SelectedText = File.ReadAllText(path); else MessageBox.Show("5MB보다 큰 텍스트 파일은 삽입할 수 없습니다."); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show("파일을 읽을 수 없습니다."); } break;
            case DroppedFileKind.Image:
                try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); AddBitmap(bitmap, x, y); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { MessageBox.Show("이미지를 열 수 없습니다."); } break;
            default: AddAttachment(new() { OriginalFilePath = path, DisplayName = Path.GetFileName(path), X = x, Y = y }); break;
        }
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
        Ink.Strokes.Clear(); foreach (var data in _note.Elements.OfType<InkStrokeElement>()) { var points = new StylusPointCollection(data.Points.Select(p => new StylusPoint(p.X, p.Y, p.Pressure))); var stroke = new Stroke(points) { DrawingAttributes = new DrawingAttributes { Color = (Color)ColorConverter.ConvertFromString(data.Color), Width = data.Thickness, Height = data.Thickness, IgnorePressure = false, FitToCurve = true, StylusTip = StylusTip.Ellipse } }; Ink.Strokes.Add(stroke); }
    }
    private void PersistInk() { _note.Elements.RemoveAll(x => x is InkStrokeElement); foreach (var s in Ink.Strokes) _note.Elements.Add(new InkStrokeElement { Color = s.DrawingAttributes.Color.ToString(), Thickness = s.DrawingAttributes.Width, Points = s.StylusPoints.Select(p => new InkPointData(p.X, p.Y, p.PressureFactor)).ToList() }); }
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
            _history.Execute(new DelegateCommand(() => { if (!_note.Elements.Contains(added)) _note.Elements.Add(added); }, () => _note.Elements.Remove(added)));
        }
        _vm.Touch();
    }
    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
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
        select.Click += (_, _) => { Ink.EditingMode = InkCanvasEditingMode.None; Ink.IsHitTestVisible = false; _inkInputActive = false; _suppressAutoPenUntilStylusLeaves = true; ResetShiftLineMode(); Editor.Focus(); UpdateToolModeMenu(); };
        pen.Click += (_, _) => { Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.Ink; UpdateShiftLineCursor(); UpdateToolModeMenu(); };
        erase.Click += (_, _) => { Ink.IsHitTestVisible = true; Ink.EditingMode = InkCanvasEditingMode.EraseByStroke; ResetShiftLineMode(); UpdateToolModeMenu(); };
        menu.Opened += (_, _) => UpdateToolModeMenu();
        menu.Items.Add(select); menu.Items.Add(pen); menu.Items.Add(erase); AddPenMenus(menu); AddNoteColorMenu(menu); Surface.ContextMenu = menu; Editor.ContextMenu = menu; RestoreInk();
    }
    private void AddTextEditingMenus(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem { Header = "실행 취소", Command = ApplicationCommands.Undo, CommandTarget = Editor, InputGestureText = "Ctrl+Z" });
        menu.Items.Add(new MenuItem { Header = "다시 실행", Command = ApplicationCommands.Redo, CommandTarget = Editor, InputGestureText = "Ctrl+Y" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "잘라내기", Command = ApplicationCommands.Cut, CommandTarget = Editor, InputGestureText = "Ctrl+X" });
        menu.Items.Add(new MenuItem { Header = "복사", Command = ApplicationCommands.Copy, CommandTarget = Editor, InputGestureText = "Ctrl+C" });
        menu.Items.Add(new MenuItem { Header = "붙여넣기", Command = ApplicationCommands.Paste, CommandTarget = Editor, InputGestureText = "Ctrl+V" });
        menu.Items.Add(new MenuItem { Header = "삭제", Command = ApplicationCommands.Delete, CommandTarget = Editor, InputGestureText = "Delete" });
        menu.Items.Add(new MenuItem { Header = "전체 선택", Command = ApplicationCommands.SelectAll, CommandTarget = Editor, InputGestureText = "Ctrl+A" });
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
        var margins = new DwmMargins();
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }
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
