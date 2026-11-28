using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NaraNote.App.Localization;
using NaraNote.Core.Models;
using NaraNote.Core.Utilities;

namespace NaraNote.App.Views;

public partial class NoteSearchWindow : Window
{
    private readonly IReadOnlyList<NoteData> _notes;
    private CancellationTokenSource? _search;
    public NoteBodyMatch? SelectedMatch { get; private set; }

    public NoteSearchWindow(IReadOnlyList<NoteData> notes, string initialQuery)
    {
        _notes = notes;
        InitializeComponent();
        Loaded += (_, _) => { Query.Focus(); Query.SelectAll(); };
        Query.Text = initialQuery;
    }

    private async void Query_Changed(object sender, TextChangedEventArgs e)
    {
        _search?.Cancel();
        var query = Query.Text;
        Results.ItemsSource = null;
        if (query.Length == 0) { Status.Text = UiText.Get("SearchNotesPrompt"); return; }
        Status.Text = UiText.Get("SearchingNotes");
        using var search = new CancellationTokenSource();
        _search = search;
        try
        {
            await Task.Delay(120, search.Token);
            var notes = _notes.Where(n => n.IsOpen).Select(n => (n.Id, n.Text)).ToArray();
            var hits = await Task.Run(() => NoteBodySearch.Find(notes, query, search.Token), search.Token);
            if (search.IsCancellationRequested) return;
            Results.ItemsSource = hits;
            Results.SelectedIndex = hits.Count > 0 ? 0 : -1;
            Status.Text = UiText.Format("SearchHitCount", hits.Count);
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_search, search)) _search = null; }
    }

    private void Results_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Results, source) is ListBoxItem { Content: NoteBodyMatch hit })
            Choose(hit);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.Enter && Results.SelectedItem is NoteBodyMatch hit) { e.Handled = true; Choose(hit); }
        else if (e.Key == Key.Down && Query.IsKeyboardFocused && Results.SelectedIndex >= 0)
        {
            e.Handled = true;
            ((ListBoxItem)Results.ItemContainerGenerator.ContainerFromIndex(Results.SelectedIndex))?.Focus();
        }
    }

    private void Choose(NoteBodyMatch hit) { SelectedMatch = hit; DialogResult = true; }
    protected override void OnClosed(EventArgs e) { _search?.Cancel(); base.OnClosed(e); }
}
