using System.ComponentModel;
using System.Runtime.CompilerServices;
using NaraNote.Core.Models;

namespace NaraNote.App.ViewModels;

public sealed class NoteViewModel(NoteData model, Action changed) : INotifyPropertyChanged
{
    public NoteData Model { get; } = model;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Text { get => Model.Text; set { if (Model.Text == value) return; Model.Text = value; Touch(); Notify(); } }
    public double FontSize { get => Model.FontSize; set { Model.FontSize = Math.Clamp(value, 8, 72); Touch(); Notify(); } }
    public string FontFamily { get => Model.FontFamily; set { Model.FontFamily = value; Touch(); Notify(); } }
    public string Color { get => Model.Color; set { Model.Color = value; Touch(); Notify(); } }
    public void Touch()
    {
        Model.LastModifiedUtc = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(Model.ExportFilePath)) Model.IsExportDirty = true;
        changed();
        Notify(nameof(Model.IsExportDirty));
    }
    public void MarkExported(string path)
    {
        Model.ExportFilePath = path;
        Model.LastModifiedUtc = DateTimeOffset.UtcNow;
        Model.IsExportDirty = false;
        changed();
        Notify(nameof(Model.IsExportDirty));
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
