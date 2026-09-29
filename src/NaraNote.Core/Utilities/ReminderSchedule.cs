using NaraNote.Core.Models;

namespace NaraNote.Core.Utilities;

public static class ReminderSchedule
{
    public static void AdvanceAfterTrigger(ReminderData reminder, DateTimeOffset nowUtc, TimeZoneInfo? timeZone = null)
    {
        if (!reminder.IsEnabled) return;
        if (reminder.Recurrence == ReminderRecurrence.Once) { reminder.IsEnabled = false; return; }
        timeZone ??= TimeZoneInfo.Local;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        if (reminder.Recurrence == ReminderRecurrence.Monthly)
        {
            var day = Math.Clamp(reminder.DayOfMonth, 1, 31);
            for (var offset = 0; offset <= 13; offset++)
            {
                var month = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(offset);
                var date = month.AddDays(Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
                var unspecified = DateTime.SpecifyKind(date + reminder.TimeOfDay, DateTimeKind.Unspecified);
                if (timeZone.IsInvalidTime(unspecified)) continue;
                var monthlyCandidateUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
                if (monthlyCandidateUtc <= nowUtc.UtcDateTime) continue;
                reminder.NextDueUtc = new DateTimeOffset(monthlyCandidateUtc, TimeSpan.Zero);
                return;
            }
            reminder.IsEnabled = false;
            return;
        }
        var days = reminder.Recurrence == ReminderRecurrence.Daily
            ? Enum.GetValues<DayOfWeek>()
            : reminder.DaysOfWeek.Distinct().ToArray();
        if (days.Length == 0) { reminder.IsEnabled = false; return; }
        var selected = days.ToHashSet();
        for (var offset = 0; offset <= 8; offset++)
        {
            var date = nowLocal.Date.AddDays(offset);
            if (!selected.Contains(date.DayOfWeek)) continue;
            var unspecified = DateTime.SpecifyKind(date + reminder.TimeOfDay, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(unspecified)) continue;
            var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
            if (candidateUtc <= nowUtc.UtcDateTime) continue;
            reminder.NextDueUtc = new DateTimeOffset(candidateUtc, TimeSpan.Zero);
            return;
        }
        reminder.IsEnabled = false;
    }
}
