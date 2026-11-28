namespace NaraNote.Core.Utilities;

public sealed record NoteBodyMatch(Guid NoteId, string Title, string Text, int Start, int Length)
{
    public string Match => Text.Substring(Start, Length);
    public override string ToString() => $"{Title} {Before}{Match}{After}";
    // A virtualized result list only needs previews for the rows on screen.
    public string Before
    {
        get
        {
            var left = Start;
            while (left > 0 && Start - left < 55 && !EndsSentence(left - 1)) left--;
            var before = Text[left..Start].TrimStart();
            return left > 0 && !EndsSentence(left - 1) ? "…" + before : before;
        }
    }
    public string After
    {
        get
        {
            var end = Start + Length;
            var right = end;
            while (right < Text.Length && right - end < 85 && Text[right] is not ('\r' or '\n') && !EndsSentence(right - 1)) right++;
            var after = Text[end..right].TrimEnd();
            return right < Text.Length && Text[right] is not ('\r' or '\n') && !EndsSentence(right - 1) ? after + "…" : after;
        }
    }
    private bool EndsSentence(int index) => Text[index] switch
    {
        '\r' or '\n' or '。' or '！' or '？' => true,
        '.' or '!' or '?' => index + 1 == Text.Length || char.IsWhiteSpace(Text[index + 1]),
        _ => false
    };
}

public static class NoteBodySearch
{
    public static List<NoteBodyMatch> Find(IEnumerable<(Guid Id, string Text)> notes, string query, CancellationToken token = default)
    {
        var hits = new List<NoteBodyMatch>();
        if (query.Length == 0) return hits;

        var results = new NoteSearchResults();
        foreach (var (id, text) in notes)
        {
            token.ThrowIfCancellationRequested();
            results.Update(text, query, false, false, token);
            if (results.Count == 0) continue;
            var firstLine = text.AsSpan().EnumerateLines();
            firstLine.MoveNext();
            var title = firstLine.Current.Trim();
            var displayTitle = title.Length <= 100 ? title.ToString() : string.Concat(title[..100], "…");
            for (var index = 0; index < results.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var (start, length) = results[index];
                hits.Add(new(id, displayTitle, text, start, length));
            }
        }
        return hits;
    }
}
