using NaraNote.Core.Models;

namespace NaraNote.Core.Services;

public interface IAppStateStore
{
    Task<AppState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppState state, CancellationToken cancellationToken = default);
}

public static class AppStateFactory
{
    public static AppState EnsureUsable(AppState? state)
    {
        state ??= new AppState();
        state.Settings ??= new AppSettings();
        state.Notes ??= [];
        if (state.Notes.Count == 0) state.Notes.Add(CreateNote(state.Settings));
        return state;
    }

    public static NoteData CreateNote(AppSettings settings, double left = 120, double top = 120) => new()
    {
        Left = left, Top = top, Color = settings.DefaultColor,
        FontFamily = settings.DefaultFontFamily, FontSize = settings.DefaultFontSize
    };
}
