namespace NaraNote.Core.Commands;

public interface IUndoableCommand { void Execute(); void Undo(); }

public sealed class UndoManager(int capacity = 150)
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Execute(IUndoableCommand command) { command.Execute(); _undo.Push(command); _redo.Clear(); Trim(); }
    public void Undo() { if (!_undo.TryPop(out var c)) return; c.Undo(); _redo.Push(c); }
    public void Redo() { if (!_redo.TryPop(out var c)) return; c.Execute(); _undo.Push(c); }
    private void Trim()
    {
        if (_undo.Count <= capacity) return;
        var keep = _undo.Take(capacity).Reverse().ToArray(); _undo.Clear();
        foreach (var item in keep) _undo.Push(item);
    }
}

public sealed class DelegateCommand(Action execute, Action undo) : IUndoableCommand
{
    public void Execute() => execute();
    public void Undo() => undo();
}
