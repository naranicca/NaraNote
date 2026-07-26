using NaraNote.Core.Commands;
using NaraNote.Core.Drawing;
using NaraNote.Core.Models;
using NaraNote.Core.Services;
using NaraNote.Core.Utilities;

namespace NaraNote.Core.Tests;

public sealed class CoreTests
{
    [Fact] public void Empty_state_gets_default_note() => Assert.Single(AppStateFactory.EnsureUsable(new AppState()).Notes);

    [Fact] public void New_note_uses_saved_font_defaults()
    {
        var settings = new AppSettings { DefaultFontFamily = "Consolas", DefaultFontSize = 22 };
        var note = AppStateFactory.CreateNote(settings);
        Assert.Equal("Consolas", note.FontFamily); Assert.Equal(22, note.FontSize);
    }

    [Theory]
    [InlineData("a.TXT", DroppedFileKind.Text)] [InlineData("a.webp", DroppedFileKind.Image)]
    [InlineData("archive.zip", DroppedFileKind.Attachment)] [InlineData("README", DroppedFileKind.Attachment)]
    public void Files_are_classified(string path, DroppedFileKind expected) => Assert.Equal(expected, FileClassifier.Classify(path));

    [Fact] public void Resize_keeps_ratio_and_anchor()
    {
        var r = ImageSizing.Resize(20, 30, 200, 100, 50, 0, ResizeCorner.BottomRight);
        Assert.Equal(2, r.Width / r.Height, 8); Assert.Equal(20, r.X); Assert.Equal(30, r.Y);
        var top = ImageSizing.Resize(20, 30, 200, 100, 50, 0, ResizeCorner.TopLeft);
        Assert.Equal(220, top.X + top.Width, 8); Assert.Equal(130, top.Y + top.Height, 8);
    }

    [Fact] public void Resize_enforces_minimum() => Assert.Equal(32, ImageSizing.Resize(0, 0, 100, 50, -500, 0, ResizeCorner.BottomRight).Width);
    [Fact] public void Initial_image_fits_surface() { var x = ImageSizing.Initial(2000, 1000, 300, 200); Assert.Equal(300, x.Width); Assert.Equal(150, x.Height); }

    [Fact] public void Window_is_clamped_in_negative_monitor()
    {
        var r = WindowPlacement.Clamp(new(2000, 2000, 360, 320), new(-1920, 0, 1920, 1080));
        Assert.True(r.X <= -48); Assert.True(r.Y < 1080);
    }

    [Fact] public void Contrast_handles_dark_and_light() { Assert.True(ColorContrast.UseLightForeground("#111111")); Assert.False(ColorContrast.UseLightForeground("#FFFFFF")); }

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
