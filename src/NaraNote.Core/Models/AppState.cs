using System.Text.Json.Serialization;

namespace NaraNote.Core.Models;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<NoteData> Notes { get; set; } = [];
}

public sealed class AppSettings
{
    public const string DefaultNoteColor = "#FFF3A6";
    public const string LegacyDefaultNoteColor = "#FFFF88";
    public const string DefaultReminderSoundPath = @"C:\Windows\Media\Alarm01.wav";
    public string DefaultFontFamily { get; set; } = "Segoe UI";
    public double DefaultFontSize { get; set; } = 16;
    public string DefaultColor { get; set; } = DefaultNoteColor;
    public string DefaultPenColor { get; set; } = "#FF222222";
    public double DefaultPenThickness { get; set; } = 3.5;
    public string ReminderSoundPath { get; set; } = DefaultReminderSoundPath;
    public string Language { get; set; } = "system";
    public bool UseGlobalHotKeys { get; set; } = true;
    public bool UseNewNoteHotKey { get; set; } = true;
    public bool UseToggleNotesHotKey { get; set; } = true;
    public bool UseSystemTray { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public Dictionary<string, string> GlobalHotKeys { get; set; } = new()
    {
        ["NewNote"] = "Ctrl+Alt+N", ["ToggleNotes"] = "Ctrl+Alt+H"
    };
}

public sealed class NoteData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 320;
    public string Color { get; set; } = AppSettings.DefaultNoteColor;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;
    public string Text { get; set; } = "";
    public string SyntaxLanguage { get; set; } = "Auto";
    public bool IsSyntaxLanguageExplicit { get; set; }
    public string? ExportFilePath { get; set; }
    public bool IsExportDirty { get; set; }
    public bool IsOpen { get; set; } = true;
    public bool IsAlwaysOnTop { get; set; }
    public ReminderData Reminder { get; set; } = new();
    public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<NoteElement> Elements { get; set; } = [];
}

public enum ReminderRecurrence { Once, Daily, Weekly, SelectedWeekdays }

public sealed class ReminderData
{
    public bool IsEnabled { get; set; }
    public bool AutoHide { get; set; }
    public DateTimeOffset NextDueUtc { get; set; }
    public ReminderRecurrence Recurrence { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public bool Use24HourFormat { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(FileAttachmentElement), "file")]
[JsonDerivedType(typeof(InkStrokeElement), "ink")]
public abstract class NoteElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ZIndex { get; set; }
}

public sealed class ImageElement : NoteElement
{
    public string StoredFilePath { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 120;
    public string Caption { get; set; } = "";
}

public sealed class FileAttachmentElement : NoteElement
{
    public string OriginalFilePath { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 176;
    public double Height { get; set; } = 120;
}

public sealed class InkStrokeElement : NoteElement
{
    public List<InkPointData> Points { get; set; } = [];
    public string Color { get; set; } = "#FF222222";
    public double Thickness { get; set; } = 3.5;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public readonly record struct InkPointData(double X, double Y, float Pressure = 0.5f, long TimestampMs = 0);
public readonly record struct RectData(double X, double Y, double Width, double Height);
