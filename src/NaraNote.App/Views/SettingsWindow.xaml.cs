using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.IO;
using NaraNote.App.ViewModels;
using NaraNote.Core.Models;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace NaraNote.App.Views;

public partial class SettingsWindow : Window
{
    private readonly NoteViewModel _note; private readonly AppSettings _settings; private string _color; private string _penColor;
    private readonly Dictionary<Button, string> _colorButtons = [];
    private readonly Dictionary<Button, string> _penColorButtons = [];
    private static readonly string[] Palette = [AppSettings.DefaultNoteColor, "#FFCFF09E", "#FFBDEBFF", "#FFFFC4D8", "#FFFFC27A", "#FFDCC6FF", "#FFF3F3F3"];
    private static readonly (string Name, string Value)[] PenPalette = [("검정", "#FF000000"), ("진회색", "#FF2F4F4F"), ("빨강", "#FFFF0000"), ("파랑", "#FF0000FF"), ("초록", "#FF008000"), ("주황", "#FFFFA500"), ("보라", "#FF800080")];
    public SettingsWindow(NoteViewModel note, AppSettings settings)
    {
        InitializeComponent(); _note = note; _settings = settings; _color = note.Color; _penColor = settings.DefaultPenColor;
        var executable = Environment.ProcessPath; if (!string.IsNullOrWhiteSpace(executable)) { try { settings.RunAtStartup = new NaraNote.Infrastructure.Startup.StartupRegistration().IsEnabled(executable); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { } }
        var fontFamilies = Fonts.SystemFontFamilies.OrderBy(x => x.Source).ToList(); FontBox.ItemsSource = fontFamilies;
        FontBox.SelectedItem = fontFamilies.FirstOrDefault(x => string.Equals(x.Source, note.FontFamily, StringComparison.OrdinalIgnoreCase)); FontBox.Text = note.FontFamily;
        SizeBox.ItemsSource = new[] { 8d, 9d, 10d, 11d, 12d, 14d, 16d, 18d, 20d, 22d, 24d, 28d, 32d, 36d, 48d, 60d, 72d };
        PenThicknessSlider.Value = Math.Clamp(settings.DefaultPenThickness, 1d, 10d);
        PenThicknessTextBox.Text = PenThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        AlarmSoundPath.Text = string.IsNullOrWhiteSpace(settings.ReminderSoundPath) ? AppSettings.DefaultReminderSoundPath : settings.ReminderSoundPath;
        SizeBox.Text = note.FontSize.ToString("0.#", CultureInfo.CurrentCulture); TrayBox.IsChecked = settings.UseSystemTray; StartupBox.IsChecked = settings.RunAtStartup;
        NewNoteHotKeyEnabledBox.IsChecked = settings.UseGlobalHotKeys && settings.UseNewNoteHotKey;
        ToggleNotesHotKeyEnabledBox.IsChecked = settings.UseGlobalHotKeys && settings.UseToggleNotesHotKey;
        NewNoteHotKey.Text = settings.GlobalHotKeys.GetValueOrDefault("NewNote", "Ctrl+Alt+N"); ToggleHotKey.Text = settings.GlobalHotKeys.GetValueOrDefault("ToggleNotes", "Ctrl+Alt+H");
        UpdateGlobalHotKeyEditors();
        foreach (var color in Palette)
        {
            var button = new Button { Width = 38, Height = 30, Margin = new Thickness(2), Padding = new Thickness(0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), ToolTip = color, Style = (Style)FindResource("ColorPresetButtonStyle") };
            _colorButtons[button] = color;
            button.Click += (_, _) => { _color = color; _note.Color = color; UpdateColorSelection(); };
            Colors.Children.Add(button);
        }
        foreach (var (name, value) in PenPalette)
        {
            var button = new Button { Width = 38, Height = 30, Margin = new Thickness(2), Padding = new Thickness(0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)), ToolTip = name, Style = (Style)FindResource("ColorPresetButtonStyle") };
            _penColorButtons[button] = value;
            button.Click += (_, _) => { _penColor = value; UpdatePenColorSelection(); };
            PenColors.Children.Add(button);
        }
        UpdateColorSelection(); UpdatePenColorSelection();
    }
    private void UpdatePenColorSelection()
    {
        foreach (var pair in _penColorButtons)
        {
            var selected = string.Equals(pair.Value, _penColor, StringComparison.OrdinalIgnoreCase);
            pair.Key.BorderThickness = new Thickness(selected ? 3 : 1);
            pair.Key.BorderBrush = selected ? new SolidColorBrush(System.Windows.Media.Colors.DodgerBlue) : new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        }
    }
    private void PenThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PenThicknessTextBox is not null && !PenThicknessTextBox.IsKeyboardFocusWithin)
            PenThicknessTextBox.Text = e.NewValue.ToString("0.#", CultureInfo.CurrentCulture);
    }
    private void PenThicknessTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => TryApplyPenThicknessInput(false);
    private void PenThicknessTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        if (TryApplyPenThicknessInput(true)) Keyboard.ClearFocus();
        e.Handled = true;
    }
    private bool TryApplyPenThicknessInput(bool showError)
    {
        if (double.TryParse(PenThicknessTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value is >= 1 and <= 10)
        {
            PenThicknessSlider.Value = value;
            PenThicknessTextBox.Text = value.ToString("0.#", CultureInfo.CurrentCulture);
            return true;
        }
        if (showError) System.Windows.MessageBox.Show("펜 굵기는 1에서 10 사이의 숫자로 입력해 주세요.");
        PenThicknessTextBox.Text = PenThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        return false;
    }
    private void UpdateColorSelection()
    {
        foreach (var pair in _colorButtons)
        {
            var selected = string.Equals(pair.Value, _color, StringComparison.OrdinalIgnoreCase);
            pair.Key.BorderThickness = new Thickness(selected ? 3 : 1);
            pair.Key.BorderBrush = selected ? new SolidColorBrush(System.Windows.Media.Colors.DodgerBlue) : new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        }
    }
    private void HotKeyEnabledBox_Changed(object sender, RoutedEventArgs e) => UpdateGlobalHotKeyEditors();
    private void BrowseAlarmSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "리마인더 알람 소리 선택",
            Filter = "WAV 오디오 (*.wav)|*.wav",
            CheckFileExists = true,
            Multiselect = false,
            FileName = AlarmSoundPath.Text
        };
        if (dialog.ShowDialog(this) == true) AlarmSoundPath.Text = dialog.FileName;
    }
    private void ResetAlarmSound_Click(object sender, RoutedEventArgs e) => AlarmSoundPath.Text = AppSettings.DefaultReminderSoundPath;
    private void UpdateGlobalHotKeyEditors()
    {
        if (NewNoteHotKey is null || ToggleHotKey is null) return;
        NewNoteHotKey.IsEnabled = NewNoteHotKeyEnabledBox.IsChecked == true;
        ToggleHotKey.IsEnabled = ToggleNotesHotKeyEnabledBox.IsChecked == true;
    }
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if ((NewNoteHotKeyEnabledBox.IsChecked == true && !NaraNote.Core.Utilities.HotKeyDefinition.TryParse(NewNoteHotKey.Text, out _)) || (ToggleNotesHotKeyEnabledBox.IsChecked == true && !NaraNote.Core.Utilities.HotKeyDefinition.TryParse(ToggleHotKey.Text, out _))) { System.Windows.MessageBox.Show("단축키는 Ctrl+Alt+N과 같은 형식으로 입력해 주세요."); return; }
        if (!TryApplyPenThicknessInput(true)) return;
        var alarmSoundPath = AlarmSoundPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(alarmSoundPath)) alarmSoundPath = AppSettings.DefaultReminderSoundPath;
        if (!File.Exists(alarmSoundPath)) { System.Windows.MessageBox.Show(this, "선택한 알람 소리 파일을 찾을 수 없습니다.", "NaraNote"); return; }
        var selectedFont = FontBox.SelectedItem is FontFamily family ? family.Source : FontBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(selectedFont) || !Fonts.SystemFontFamilies.Any(x => string.Equals(x.Source, selectedFont, StringComparison.OrdinalIgnoreCase))) selectedFont = "Segoe UI";
        if (!double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var selectedSize) || selectedSize is < 8 or > 72) { System.Windows.MessageBox.Show("글꼴 크기는 8에서 72 사이의 숫자로 입력해 주세요."); return; }
        _note.FontFamily = selectedFont; _note.FontSize = selectedSize; _note.Color = _color;
        _settings.DefaultFontFamily = selectedFont; _settings.DefaultFontSize = selectedSize;
        _settings.DefaultPenColor = _penColor;
        _settings.DefaultPenThickness = PenThicknessSlider.Value;
        _settings.ReminderSoundPath = alarmSoundPath;
        _settings.UseGlobalHotKeys = true;
        _settings.UseNewNoteHotKey = NewNoteHotKeyEnabledBox.IsChecked == true;
        _settings.UseToggleNotesHotKey = ToggleNotesHotKeyEnabledBox.IsChecked == true;
        _settings.UseSystemTray = TrayBox.IsChecked == true; _settings.RunAtStartup = StartupBox.IsChecked == true;
        _settings.GlobalHotKeys["NewNote"] = NewNoteHotKey.Text; _settings.GlobalHotKeys["ToggleNotes"] = ToggleHotKey.Text; DialogResult = true;
    }
}
