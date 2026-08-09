using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NaraNote.Core.Models;
using NaraNote.App.Localization;
using MessageBox = System.Windows.MessageBox;

namespace NaraNote.App.Views;

public partial class ReminderWindow : Window
{
    private bool _initializing = true;
    public ReminderData Result { get; private set; }
    public bool Use24HourFormat => Use24HourCheck.IsChecked == true;

    public ReminderWindow(ReminderData current)
    {
        InitializeComponent();
        if (UiText.Language != "ko") Width = 480;
        var initial = current.IsEnabled ? current.NextDueUtc.ToLocalTime() : DateTimeOffset.Now;
        ReminderCalendar.DisplayDateStart = DateTime.Today;
        ReminderCalendar.SelectedDate = initial.Date;
        ReminderCalendar.DisplayDate = initial.Date;
        Use24HourCheck.IsChecked = current.Use24HourFormat;
        AmPmBox.SelectedIndex = initial.Hour < 12 ? 0 : 1;
        AmPmBox.Visibility = current.Use24HourFormat ? Visibility.Collapsed : Visibility.Visible;
        PopulateTimeChoices(current.Use24HourFormat);
        HourBox.ToolTip = UiText.Get(current.Use24HourFormat ? "Hour023" : "Hour112");
        HourBox.Text = current.Use24HourFormat
            ? initial.Hour.ToString("00", CultureInfo.CurrentCulture)
            : (initial.Hour % 12 == 0 ? 12 : initial.Hour % 12).ToString(CultureInfo.CurrentCulture);
        MinuteBox.Text = initial.Minute.ToString("00", CultureInfo.CurrentCulture);
        RecurrenceBox.SelectedIndex = (int)current.Recurrence;
        AutoHideCheck.IsChecked = current.AutoHide;
        Result = Clone(current);
        foreach (var day in current.DaysOfWeek) SetDay(day, true);
        DisableButton.Visibility = current.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        _initializing = false;
    }

    private void TimeFormatCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || AmPmBox is null || HourBox is null) return;
        if (!int.TryParse(HourBox.Text.Trim(), out var hour)) hour = 0;
        var use24Hour = Use24HourCheck.IsChecked == true;
        if (use24Hour)
        {
            if (hour is >= 1 and <= 12) hour = hour % 12 + (AmPmBox.SelectedIndex == 1 ? 12 : 0);
            PopulateTimeChoices(true);
            HourBox.Text = Math.Clamp(hour, 0, 23).ToString("00", CultureInfo.CurrentCulture);
            AmPmBox.Visibility = Visibility.Collapsed;
            HourBox.ToolTip = UiText.Get("Hour023");
        }
        else
        {
            hour = Math.Clamp(hour, 0, 23);
            AmPmBox.SelectedIndex = hour < 12 ? 0 : 1;
            PopulateTimeChoices(false);
            HourBox.Text = (hour % 12 == 0 ? 12 : hour % 12).ToString(CultureInfo.CurrentCulture);
            AmPmBox.Visibility = Visibility.Visible;
            HourBox.ToolTip = UiText.Get("Hour112");
        }
    }
    private void PopulateTimeChoices(bool use24Hour)
    {
        HourBox.ItemsSource = (use24Hour ? Enumerable.Range(0, 24) : Enumerable.Range(1, 12)).Select(value => value.ToString("00", CultureInfo.CurrentCulture)).ToList();
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(value => value.ToString("00", CultureInfo.CurrentCulture)).ToList();
    }
    private void TimePart_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox box || !int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var value)) return;
        var normalized = value.ToString("00", CultureInfo.CurrentCulture);
        var matching = box.Items.OfType<string>().FirstOrDefault(item => item == normalized);
        if (matching is null) return;
        box.SelectedItem = matching;
        box.Text = matching;
    }

    private void RecurrenceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WeekdayPanel is null) return;
        WeekdayPanel.Visibility = RecurrenceBox.SelectedIndex == (int)ReminderRecurrence.SelectedWeekdays ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var use24Hour = Use24HourCheck.IsChecked == true;
        if (ReminderCalendar.SelectedDate is not { } date ||
            !int.TryParse(HourBox.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var hour) ||
            (use24Hour ? hour is < 0 or > 23 : hour is < 1 or > 12) ||
            !int.TryParse(MinuteBox.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var minute) || minute is < 0 or > 59)
        { MessageBox.Show(this, UiText.Get(use24Hour ? "TimeError24" : "TimeError12"), "NaraNote"); return; }
        var hour24 = use24Hour ? hour : hour % 12 + (AmPmBox.SelectedIndex == 1 ? 12 : 0);
        var time = new TimeSpan(hour24, minute, 0);
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Local);
        var due = new DateTimeOffset(local);
        var recurrence = (ReminderRecurrence)Math.Max(0, RecurrenceBox.SelectedIndex);
        var days = GetSelectedDays();
        if (recurrence == ReminderRecurrence.Weekly) days = [due.DayOfWeek];
        if (recurrence == ReminderRecurrence.SelectedWeekdays && days.Count == 0)
        { MessageBox.Show(this, UiText.Get("WeekdayError"), "NaraNote"); return; }
        Result = new ReminderData { IsEnabled = true, AutoHide = AutoHideCheck.IsChecked == true, NextDueUtc = due.ToUniversalTime(), Recurrence = recurrence, TimeOfDay = time, Use24HourFormat = use24Hour, DaysOfWeek = days };
        DialogResult = true;
    }

    private void Disable_Click(object sender, RoutedEventArgs e) { Result = new ReminderData { Use24HourFormat = Use24HourFormat }; DialogResult = true; }
    private List<DayOfWeek> GetSelectedDays() => Enum.GetValues<DayOfWeek>().Where(day => IsDayChecked(day)).ToList();
    private bool IsDayChecked(DayOfWeek day) => GetDay(day).IsChecked == true;
    private void SetDay(DayOfWeek day, bool value) => GetDay(day).IsChecked = value;
    private System.Windows.Controls.CheckBox GetDay(DayOfWeek day) => day switch { DayOfWeek.Sunday => Sunday, DayOfWeek.Monday => Monday, DayOfWeek.Tuesday => Tuesday, DayOfWeek.Wednesday => Wednesday, DayOfWeek.Thursday => Thursday, DayOfWeek.Friday => Friday, _ => Saturday };
    private static ReminderData Clone(ReminderData source) => new() { IsEnabled = source.IsEnabled, AutoHide = source.AutoHide, NextDueUtc = source.NextDueUtc, Recurrence = source.Recurrence, TimeOfDay = source.TimeOfDay, Use24HourFormat = source.Use24HourFormat, DaysOfWeek = [.. source.DaysOfWeek] };
}
