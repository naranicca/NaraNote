using System.IO.Compression;
using System.Text.Json;
using NaraNote.Core.Models;

namespace NaraNote.Infrastructure.Persistence;

public sealed class NoteDocumentExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task ExportAsync(NoteData note, string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("저장할 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            if (string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                await WriteTextAsync(temporary, note.Text, cancellationToken);
            else
                await WritePackageAsync(temporary, note, cancellationToken);
            ReplaceFile(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private static async Task WritePackageAsync(string path, NoteData note, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var manifest = CreateManifest(note);
            foreach (var image in manifest.Elements.Where(element => element.Type == "image"))
            {
                var source = image.SourcePath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                var extension = Path.GetExtension(source); if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
                image.File = $"images/{image.Id:N}{extension.ToLowerInvariant()}";
                await AddFileAsync(archive, image.File, source, cancellationToken);
                image.SourcePath = null;
            }
            foreach (var attachment in manifest.Elements.Where(element => element.Type == "attachment"))
            {
                var source = attachment.SourcePath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) continue;
                var safeName = Path.GetFileName(source);
                attachment.File = $"attachments/{attachment.Id:N}-{safeName}";
                await AddFileAsync(archive, attachment.File, source, cancellationToken);
                attachment.SourcePath = null;
            }
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private static async Task AddFileAsync(ZipArchive archive, string entryName, string sourcePath, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = entry.Open();
        await source.CopyToAsync(target, cancellationToken);
    }

    private static NoteDocumentManifest CreateManifest(NoteData note) => new()
    {
        NoteId = note.Id, Text = note.Text, Width = note.Width, Height = note.Height, Color = note.Color,
        FontFamily = note.FontFamily, FontSize = note.FontSize, LastModifiedUtc = note.LastModifiedUtc,
        Elements = note.Elements.Select(element => element switch
        {
            ImageElement image => new NoteDocumentElement { Type = "image", Id = image.Id, ZIndex = image.ZIndex, X = image.X, Y = image.Y, Width = image.Width, Height = image.Height, Caption = image.Caption, SourcePath = image.StoredFilePath },
            FileAttachmentElement file => new NoteDocumentElement { Type = "attachment", Id = file.Id, ZIndex = file.ZIndex, X = file.X, Y = file.Y, Width = file.Width, Height = file.Height, DisplayName = file.DisplayName, SourcePath = file.OriginalFilePath },
            InkStrokeElement ink => new NoteDocumentElement { Type = "ink", Id = ink.Id, ZIndex = ink.ZIndex, Color = ink.Color, Thickness = ink.Thickness, Points = ink.Points },
            _ => throw new NotSupportedException($"지원하지 않는 노트 요소입니다: {element.GetType().Name}")
        }).ToList()
    };

    private static void ReplaceFile(string temporary, string path)
    {
        if (File.Exists(path)) File.Replace(temporary, path, null, true);
        else File.Move(temporary, path);
    }

    private sealed class NoteDocumentManifest
    {
        public int FormatVersion { get; set; } = 1;
        public Guid NoteId { get; set; }
        public string Text { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public string Color { get; set; } = "";
        public string FontFamily { get; set; } = "";
        public double FontSize { get; set; }
        public DateTimeOffset LastModifiedUtc { get; set; }
        public List<NoteDocumentElement> Elements { get; set; } = [];
    }

    private sealed class NoteDocumentElement
    {
        public string Type { get; set; } = "";
        public Guid Id { get; set; }
        public int ZIndex { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? Caption { get; set; }
        public string? DisplayName { get; set; }
        public string? File { get; set; }
        public string? SourcePath { get; set; }
        public string? Color { get; set; }
        public double Thickness { get; set; }
        public List<InkPointData>? Points { get; set; }
    }
}
