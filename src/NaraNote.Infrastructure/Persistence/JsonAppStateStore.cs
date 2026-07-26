using System.Text.Json;
using NaraNote.Core.Models;
using NaraNote.Core.Services;

namespace NaraNote.Infrastructure.Persistence;

public sealed class JsonAppStateStore : IAppStateStore
{
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _root;
    private readonly string? _legacyRoot;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public JsonAppStateStore(string? root = null)
    {
        if (root is null)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            root = Path.Combine(local, "NaraNote");
            _legacyRoot = Path.Combine(local, "Light" + "StickyNotes");
            MigrateLegacyData(_legacyRoot, root);
        }
        _root = root;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "images"));
        Directory.CreateDirectory(Path.Combine(root, "attachments"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        _path = Path.Combine(root, "app-state.json");
        _backupPath = Path.Combine(root, "app-state.backup.json");
    }

    public async Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        foreach (var path in new[] { _path, _backupPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                await using var stream = File.OpenRead(path);
                var state = await JsonSerializer.DeserializeAsync<AppState>(stream, _options, cancellationToken);
                if (state is not null && state.SchemaVersion <= 1) { RewriteLegacyPaths(state); return AppStateFactory.EnsureUsable(state); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return AppStateFactory.EnsureUsable(null);
    }

    private static void MigrateLegacyData(string legacyRoot, string newRoot)
    {
        if (!Directory.Exists(legacyRoot) || File.Exists(Path.Combine(newRoot, "app-state.json"))) return;
        Directory.CreateDirectory(newRoot);
        foreach (var directory in Directory.EnumerateDirectories(legacyRoot, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(newRoot, Path.GetRelativePath(legacyRoot, directory)));
        foreach (var file in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(newRoot, Path.GetRelativePath(legacyRoot, file)), false);
    }

    private void RewriteLegacyPaths(AppState state)
    {
        if (_legacyRoot is null) return;
        foreach (var image in state.Notes.SelectMany(note => note.Elements).OfType<ImageElement>())
            if (Path.IsPathFullyQualified(image.StoredFilePath) && image.StoredFilePath.StartsWith(_legacyRoot, StringComparison.OrdinalIgnoreCase)) image.StoredFilePath = Path.Combine(_root, Path.GetRelativePath(_legacyRoot, image.StoredFilePath));
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporary = _path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _backupPath, true);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
}
