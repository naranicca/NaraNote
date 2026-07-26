using NaraNote.Core.Models;
using NaraNote.Infrastructure.Persistence;

namespace NaraNote.Core.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NaraNoteTests", Guid.NewGuid().ToString("N"));
    [Fact] public async Task Notes_and_elements_round_trip()
    {
        var store = new JsonAppStateStore(_root); var state = new AppState();
        state.Notes.Add(new NoteData { Text = "hello", FontFamily = "Consolas", FontSize = 22, Elements = [new ImageElement { Caption = "caption", X = 4, Width = 99 }, new InkStrokeElement { Points = [new(1, 2)] }] });
        await store.SaveAsync(state); var restored = await store.LoadAsync();
        Assert.Equal("hello", restored.Notes[0].Text); Assert.Equal("Consolas", restored.Notes[0].FontFamily); Assert.Equal(22, restored.Notes[0].FontSize); Assert.Equal("caption", Assert.IsType<ImageElement>(restored.Notes[0].Elements[0]).Caption); Assert.Single(Assert.IsType<InkStrokeElement>(restored.Notes[0].Elements[1]).Points);
    }
    [Fact] public async Task Corrupt_primary_recovers_backup()
    {
        var store = new JsonAppStateStore(_root); var state = new AppState { Notes = [new NoteData { Text = "backup" }] };
        await store.SaveAsync(state); state.Notes[0].Text = "current"; await store.SaveAsync(state);
        await File.WriteAllTextAsync(Path.Combine(_root, "app-state.json"), "{");
        Assert.Equal("backup", (await store.LoadAsync()).Notes[0].Text);
    }
    public void Dispose() { try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { } }
}
