using System.Globalization;

namespace NaraNote.Core.Utilities;

public sealed class NoteSearchResults
{
    private readonly List<(int Start, int Length)> _matches = [];
    public int Count => _matches.Count;
    public (int Start, int Length) this[int index] => _matches[index];
    public void Clear() => _matches.Clear();

    public void Update(string text, string query, bool matchCase, bool wholeWord, CancellationToken cancellationToken = default)
    {
        Clear();
        if (query.Length == 0) return;

        var comparer = CultureInfo.CurrentCulture.CompareInfo;
        var options = matchCase ? CompareOptions.None : CompareOptions.IgnoreCase;
        for (var offset = 0; offset < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = comparer.IndexOf(text.AsSpan(offset), query.AsSpan(), options, out var length);
            if (relative < 0 || length == 0) break;
            var start = offset + relative;
            var end = start + length;
            if (!wholeWord || ((start == 0 || !IsWordCharacter(text[start - 1])) &&
                               (end == text.Length || !IsWordCharacter(text[end]))))
            {
                _matches.Add((start, length));
                offset = end;
            }
            else offset = start + 1;
        }
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    public int FirstAtOrAfter(int offset)
    {
        var low = 0;
        var high = Count;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (_matches[mid].Start < offset) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    public int CurrentIndex(int caret, int selectionStart, int selectionLength)
    {
        // Selecting a result can put the caret at its exclusive end, even at the next result's start.
        if (selectionLength > 0 && (caret == selectionStart || caret == selectionStart + selectionLength))
        {
            var selected = FirstAtOrAfter(selectionStart);
            if (selected < Count && _matches[selected] == (selectionStart, selectionLength)) return selected;
        }
        var next = FirstAtOrAfter(caret);
        if (next < Count && _matches[next].Start == caret) return next;
        if (next == 0) return -1;
        var preceding = _matches[next - 1];
        return caret < preceding.Start + preceding.Length ? next - 1 : -1;
    }
}
