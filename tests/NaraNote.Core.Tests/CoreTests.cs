using NaraNote.Core.Commands;
using NaraNote.Core.Drawing;
using NaraNote.Core.Models;
using NaraNote.Core.Services;
using NaraNote.Core.Utilities;
using NaraNote.Infrastructure.Persistence;
using System.IO.Compression;

namespace NaraNote.Core.Tests;

public sealed class CoreTests
{
    [Fact] public void Empty_state_gets_default_note() => Assert.Single(AppStateFactory.EnsureUsable(new AppState()).Notes);

    [Fact]
    public async Task Text_note_exports_as_utf8_text()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NaraNote-tests-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "note.txt");
            await new NoteDocumentExporter().ExportAsync(new NoteData { Text = "한글\ntext" }, path);
            Assert.Equal("한글\ntext", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Rich_note_exported_to_non_naranote_format_keeps_only_text()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NaraNote-tests-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "note.md");
            var note = new NoteData { Text = "# text", Elements = [new InkStrokeElement { Points = [new(1, 2)] }] };
            await new NoteDocumentExporter().ExportAsync(note, path);
            Assert.Equal("# text", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Rich_note_package_contains_manifest_and_assets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NaraNote-tests-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "image.png"); var attachment = Path.Combine(root, "report.pdf");
            await File.WriteAllBytesAsync(image, [1, 2, 3]); await File.WriteAllBytesAsync(attachment, [4, 5, 6]);
            var note = new NoteData { Text = "rich", Elements = [new ImageElement { StoredFilePath = image }, new FileAttachmentElement { OriginalFilePath = attachment, DisplayName = "report.pdf" }, new InkStrokeElement { Points = [new(1, 2), new(3, 4)] }] };
            var path = Path.Combine(root, "note.naranote"); await new NoteDocumentExporter().ExportAsync(note, path);
            using var archive = ZipFile.OpenRead(path); var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("manifest.json", names); Assert.Contains(names, name => name.StartsWith("images/")); Assert.Contains(names, name => name.StartsWith("attachments/"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Rich_note_package_can_be_imported_with_elements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NaraNote-import-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "image.png"); await File.WriteAllBytesAsync(image, [1, 2, 3]);
            var original = new NoteData
            {
                Text = "reloaded", FontFamily = "Consolas", FontSize = 18, SyntaxLanguage = "Python",
                Elements = [new ImageElement { StoredFilePath = image, Caption = "caption" }, new InkStrokeElement { Points = [new(1, 2)] }]
            };
            var path = Path.Combine(root, "note.naranote");
            var exporter = new NoteDocumentExporter();
            await exporter.ExportAsync(original, path);
            var imported = await exporter.ImportAsync(path, Path.Combine(root, "assets"));
            Assert.Equal("reloaded", imported.Text);
            Assert.Equal("Python", imported.SyntaxLanguage);
            Assert.Equal("Consolas", imported.FontFamily);
            Assert.Equal(2, imported.Elements.Count);
            Assert.True(File.Exists(Assert.IsType<ImageElement>(imported.Elements[0]).StoredFilePath));
            Assert.Single(Assert.IsType<InkStrokeElement>(imported.Elements[1]).Points);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void New_note_uses_saved_font_defaults()
    {
        var settings = new AppSettings { DefaultFontFamily = "Consolas", DefaultFontSize = 22 };
        var note = AppStateFactory.CreateNote(settings);
        Assert.Equal("Consolas", note.FontFamily); Assert.Equal(22, note.FontSize);
    }

    [Fact]
    public void Daily_reminder_advances_to_next_day_after_trigger()
    {
        var reminder = new ReminderData { IsEnabled = true, Recurrence = ReminderRecurrence.Daily, TimeOfDay = new TimeSpan(9, 0, 0) };
        ReminderSchedule.AdvanceAfterTrigger(reminder, new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), reminder.NextDueUtc);
    }

    [Fact]
    public void Selected_weekday_reminder_uses_next_selected_day()
    {
        var reminder = new ReminderData { IsEnabled = true, Recurrence = ReminderRecurrence.SelectedWeekdays, TimeOfDay = new TimeSpan(8, 30, 0), DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday] };
        ReminderSchedule.AdvanceAfterTrigger(reminder, new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 30, 0, TimeSpan.Zero), reminder.NextDueUtc);
    }

    [Fact]
    public void One_time_reminder_disables_after_trigger()
    {
        var reminder = new ReminderData { IsEnabled = true, Recurrence = ReminderRecurrence.Once };
        ReminderSchedule.AdvanceAfterTrigger(reminder, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        Assert.False(reminder.IsEnabled);
    }

    [Theory]
    [InlineData("a.TXT", DroppedFileKind.Text)] [InlineData("test.py", DroppedFileKind.Text)]
    [InlineData("source.CPP", DroppedFileKind.Text)] [InlineData("script.lua", DroppedFileKind.Text)]
    [InlineData("component.tsx", DroppedFileKind.Text)] [InlineData("settings.toml", DroppedFileKind.Text)]
    [InlineData("Dockerfile", DroppedFileKind.Text)] [InlineData(".gitignore", DroppedFileKind.Text)]
    [InlineData("a.webp", DroppedFileKind.Image)]
    [InlineData("archive.zip", DroppedFileKind.Attachment)] [InlineData("README", DroppedFileKind.Attachment)]
    public void Files_are_classified(string path, DroppedFileKind expected) => Assert.Equal(expected, FileClassifier.Classify(path));

    [Fact]
    public void Unknown_extension_is_classified_from_content()
    {
        var root = Path.Combine(Path.GetTempPath(), $"NaraNote-classifier-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var text = Path.Combine(root, "notes.data");
            var binary = Path.Combine(root, "payload.data");
            var pdf = Path.Combine(root, "document.data");
            File.WriteAllText(text, "한글 text\nsecond line");
            File.WriteAllBytes(binary, new byte[] { 1, 2, 0, 3, 4, 5 });
            File.WriteAllBytes(pdf, "%PDF-1.7\n"u8.ToArray());
            Assert.Equal(DroppedFileKind.Text, FileClassifier.Classify(text));
            Assert.Equal(DroppedFileKind.Attachment, FileClassifier.Classify(binary));
            Assert.Equal(DroppedFileKind.Attachment, FileClassifier.Classify(pdf));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void Resize_keeps_ratio_and_anchor()
    {
        var r = ImageSizing.Resize(20, 30, 200, 100, 50, 0, ResizeCorner.BottomRight);
        Assert.Equal(2, r.Width / r.Height, 8); Assert.Equal(20, r.X); Assert.Equal(30, r.Y);
        var top = ImageSizing.Resize(20, 30, 200, 100, 50, 0, ResizeCorner.TopLeft);
        Assert.Equal(220, top.X + top.Width, 8); Assert.Equal(130, top.Y + top.Height, 8);
    }

    [Fact] public void Resize_enforces_minimum() => Assert.Equal(32, ImageSizing.Resize(0, 0, 100, 50, -500, 0, ResizeCorner.BottomRight).Width);
    [Fact] public void Initial_image_fits_surface() { var x = ImageSizing.Initial(2000, 1000, 300, 200); Assert.Equal(300, x.Width); Assert.Equal(150, x.Height); }

    [Fact]
    public void Attachment_resize_is_free_and_keeps_opposite_anchor()
    {
        var right = ObjectSizing.ResizeFree(20, 30, 160, 44, 40, 20, ResizeHandle.BottomRight);
        Assert.Equal((20d, 30d, 200d, 64d), right);
        var topLeft = ObjectSizing.ResizeFree(20, 30, 160, 44, 30, 10, ResizeHandle.TopLeft);
        Assert.Equal(180, topLeft.X + topLeft.Width, 8); Assert.Equal(74, topLeft.Y + topLeft.Height, 8);
    }

    [Fact]
    public void Attachment_edge_handle_changes_only_one_dimension()
    {
        var resized = ObjectSizing.ResizeFree(20, 30, 160, 44, 50, 99, ResizeHandle.Right);
        Assert.Equal(210, resized.Width); Assert.Equal(44, resized.Height); Assert.Equal(30, resized.Y);
    }

    [Fact] public void Window_is_clamped_in_negative_monitor()
    {
        var r = WindowPlacement.Clamp(new(2000, 2000, 360, 320), new(-1920, 0, 1920, 1080));
        Assert.True(r.X <= -48); Assert.True(r.Y < 1080);
    }

    [Fact]
    public void New_window_is_fully_visible_in_work_area()
    {
        var r = WindowPlacement.ClampFullyVisible(new(1850, 1000, 360, 320), new(0, 0, 1920, 1080));
        Assert.True(r.X >= 0 && r.Y >= 0); Assert.True(r.X + r.Width <= 1920); Assert.True(r.Y + r.Height <= 1080);
        var negative = WindowPlacement.ClampFullyVisible(new(-2200, -200, 360, 320), new(-1920, 0, 1920, 1080));
        Assert.True(negative.X >= -1920); Assert.True(negative.X + negative.Width <= 0); Assert.True(negative.Y >= 0);
    }

    [Fact]
    public void New_window_is_placed_beside_anchor_without_overlap()
    {
        var anchor = new RectData(100, 100, 360, 320);
        var result = WindowPlacement.FindNonOverlapping(new(472, 100, 360, 320), new(0, 0, 1920, 1080), [anchor]);
        Assert.Equal(472, result.X); Assert.Equal(100, result.Y);
    }

    [Fact]
    public void New_window_avoids_other_notes_and_stays_on_monitor()
    {
        var area = new RectData(-1920, 0, 1920, 1080);
        var occupied = new[] { new RectData(-800, 100, 360, 320), new RectData(-428, 100, 360, 320) };
        var result = WindowPlacement.FindNonOverlapping(new(-428, 100, 360, 320), area, occupied);
        Assert.True(result.X >= area.X && result.X + result.Width <= area.X + area.Width);
        Assert.True(result.Y >= area.Y && result.Y + result.Height <= area.Y + area.Height);
        Assert.All(occupied, rect => Assert.False(result.X < rect.X + rect.Width && result.X + result.Width > rect.X && result.Y < rect.Y + rect.Height && result.Y + result.Height > rect.Y));
    }

    [Fact] public void Contrast_handles_dark_and_light() { Assert.True(ColorContrast.UseLightForeground("#111111")); Assert.False(ColorContrast.UseLightForeground("#FFFFFF")); }

    [Fact]
    public void Reminder_auto_hide_is_disabled_by_default() => Assert.False(new ReminderData().AutoHide);

    [Fact]
    public void Language_uses_system_default_until_user_selects_one() => Assert.Equal("system", new AppSettings().Language);

    [Theory]
    [InlineData("Ctrl+Alt+N", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x4E)]
    [InlineData("Ctrl+Shift+F12", HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x7B)]
    public void Hotkeys_are_parsed(string text, HotKeyModifiers modifiers, uint key)
    {
        Assert.True(HotKeyDefinition.TryParse(text, out var parsed)); Assert.Equal(modifiers, parsed.Modifiers); Assert.Equal(key, parsed.VirtualKey);
    }

    [Theory] [InlineData("")] [InlineData("N")] [InlineData("Ctrl+Unknown")]
    public void Invalid_hotkeys_are_rejected(string text) => Assert.False(HotKeyDefinition.TryParse(text, out _));

    [Fact] public void Undo_redo_and_new_command_clear_redo()
    {
        var value = 0; var history = new UndoManager();
        history.Execute(new DelegateCommand(() => value = 1, () => value = 0)); history.Undo(); Assert.True(history.CanRedo);
        history.Execute(new DelegateCommand(() => value = 2, () => value = 0)); Assert.False(history.CanRedo); history.Undo(); history.Redo(); Assert.Equal(2, value);
    }

    [Fact] public void Horizontal_scribble_is_detected_and_targets_overlap()
    {
        var candidate = ZigZag(); var target = new InkStrokeElement { Points = [new(0, 10), new(100, 10)] };
        var result = new ScribbleRecognizer().Analyze(candidate, [target]);
        Assert.True(result.IsScribble); Assert.Contains(target.Id, result.TargetStrokeIds);
    }

    [Fact] public void Straight_line_is_not_scribble()
    {
        var points = Enumerable.Range(0, 20).Select(i => new InkPointData(i * 5, 10, .5f, i * 10)).ToList();
        Assert.False(new ScribbleRecognizer().Analyze(points, []).IsScribble);
    }

    [Fact] public void Vertical_zigzag_is_not_scribble()
    {
        var points = Enumerable.Range(0, 20).Select(i => new InkPointData(10 + i % 2, i * 5, .5f, i * 10)).ToList();
        Assert.False(new ScribbleRecognizer().Analyze(points, []).IsScribble);
    }

    [Fact] public void Scribble_without_overlap_deletes_nothing()
    {
        var far = new InkStrokeElement { Points = [new(500, 500), new(600, 500)] };
        Assert.Empty(new ScribbleRecognizer().Analyze(ZigZag(), [far]).TargetStrokeIds);
    }

    private static List<InkPointData> ZigZag() =>
    [new(0,10,.5f,0),new(80,11,.5f,80),new(5,9,.5f,160),new(85,10,.5f,240),new(4,11,.5f,320),new(90,10,.5f,400),new(2,9,.5f,480),new(75,10,.5f,560)];
}
