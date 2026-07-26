namespace NaraNote.Infrastructure.Logging;

public sealed class FileLogger
{
    private readonly string _path;
    private readonly object _gate = new();
    public FileLogger(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaraNote", "logs");
        Directory.CreateDirectory(root); _path = Path.Combine(root, $"app-{DateTime.UtcNow:yyyyMMdd}.log");
    }
    public void Error(string area, Exception exception)
    {
        var line = $"{DateTimeOffset.Now:O}\tERROR\t{area}\t{exception.GetType().Name}\t{exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}";
        try { lock (_gate) File.AppendAllText(_path, line); } catch (IOException) { }
    }
}
