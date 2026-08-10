using NaraNote.Core.Models;

namespace NaraNote.Core.Utilities;

public enum ResizeCorner { TopLeft, TopRight, BottomLeft, BottomRight }
public enum ResizeHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

public static class ImageSizing
{
    public static (double X, double Y, double Width, double Height) Resize(
        double x, double y, double width, double height, double deltaX, double deltaY,
        ResizeCorner corner, double minimum = 32)
    {
        var ratio = width / Math.Max(height, 0.001);
        var signed = corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft ? -deltaX : deltaX;
        var newWidth = Math.Max(minimum, width + signed);
        var newHeight = newWidth / ratio;
        var newX = corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft ? x + width - newWidth : x;
        var newY = corner is ResizeCorner.TopLeft or ResizeCorner.TopRight ? y + height - newHeight : y;
        return (newX, newY, newWidth, newHeight);
    }

    public static (double Width, double Height) Initial(double width, double height, double availableWidth, double availableHeight)
    {
        var scale = Math.Min(1, Math.Min(availableWidth / width, availableHeight / height));
        return (width * scale, height * scale);
    }
}

public static class ObjectSizing
{
    public static (double X, double Y, double Width, double Height) ResizeFree(
        double x, double y, double width, double height, double deltaX, double deltaY,
        ResizeHandle handle, double minimumWidth = 80, double minimumHeight = 32)
    {
        var right = x + width;
        var bottom = y + height;
        var resizeLeft = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var resizeRight = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var resizeTop = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var resizeBottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;
        if (resizeLeft) { x = Math.Min(x + deltaX, right - minimumWidth); width = right - x; }
        else if (resizeRight) width = Math.Max(minimumWidth, width + deltaX);
        if (resizeTop) { y = Math.Min(y + deltaY, bottom - minimumHeight); height = bottom - y; }
        else if (resizeBottom) height = Math.Max(minimumHeight, height + deltaY);
        return (x, y, width, height);
    }
}

public static class WindowPlacement
{
    public static RectData Clamp(RectData window, RectData workArea, double minimumVisible = 48)
    {
        var width = Math.Clamp(window.Width, 220, workArea.Width);
        var height = Math.Clamp(window.Height, 160, workArea.Height);
        var x = Math.Clamp(window.X, workArea.X - width + minimumVisible, workArea.X + workArea.Width - minimumVisible);
        var y = Math.Clamp(window.Y, workArea.Y, workArea.Y + workArea.Height - minimumVisible);
        return new(x, y, width, height);
    }

    public static RectData ClampFullyVisible(RectData window, RectData workArea)
    {
        var width = Math.Clamp(window.Width, Math.Min(220, workArea.Width), workArea.Width);
        var height = Math.Clamp(window.Height, Math.Min(160, workArea.Height), workArea.Height);
        var x = Math.Clamp(window.X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(window.Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new(x, y, width, height);
    }

    public static RectData FindNonOverlapping(RectData desired, RectData workArea, IEnumerable<RectData> occupied, double gap = 12, double edgeMargin = 0)
    {
        var horizontalMargin = Math.Min(Math.Max(0, edgeMargin), Math.Max(0, (workArea.Width - 220) / 2));
        var verticalMargin = Math.Min(Math.Max(0, edgeMargin), Math.Max(0, (workArea.Height - 160) / 2));
        var placementArea = new RectData(
            workArea.X + horizontalMargin,
            workArea.Y + verticalMargin,
            workArea.Width - horizontalMargin * 2,
            workArea.Height - verticalMargin * 2);
        desired = ClampFullyVisible(desired, placementArea);
        var blocked = occupied.Where(rect => Intersects(rect, placementArea)).ToList();
        var xs = new HashSet<double> { desired.X, placementArea.X, placementArea.X + placementArea.Width - desired.Width };
        var ys = new HashSet<double> { desired.Y, placementArea.Y, placementArea.Y + placementArea.Height - desired.Height };
        foreach (var rect in blocked)
        {
            xs.Add(rect.X + rect.Width + gap); xs.Add(rect.X - desired.Width - gap); xs.Add(rect.X);
            ys.Add(rect.Y + rect.Height + gap); ys.Add(rect.Y - desired.Height - gap); ys.Add(rect.Y);
        }
        var candidates = from x in xs from y in ys
                         let candidate = new RectData(x, y, desired.Width, desired.Height)
                         where FullyInside(candidate, placementArea)
                         orderby DistanceSquared(candidate, desired)
                         select candidate;
        var available = candidates.FirstOrDefault(candidate => blocked.All(rect => !Intersects(candidate, Expand(rect, gap))));
        if (available.Width > 0 && available.Height > 0) return available;

        RectData? bestFallback = null;
        var bestOverlap = double.MaxValue;
        var bestDistance = double.MaxValue;
        for (var y = placementArea.Y; y <= placementArea.Y + placementArea.Height - desired.Height; y += 8)
            for (var x = placementArea.X; x <= placementArea.X + placementArea.Width - desired.Width; x += 8)
            {
                var candidate = new RectData(x, y, desired.Width, desired.Height);
                if (blocked.All(rect => !Intersects(candidate, Expand(rect, gap)))) return candidate;
                var overlap = blocked.Sum(rect => OverlapArea(candidate, Expand(rect, gap)));
                if (blocked.Any(rect => SamePosition(candidate, rect))) overlap += candidate.Width * candidate.Height;
                var distance = DistanceSquared(candidate, desired);
                if (overlap < bestOverlap || (Math.Abs(overlap - bestOverlap) < 0.01 && distance < bestDistance))
                {
                    bestFallback = candidate;
                    bestOverlap = overlap;
                    bestDistance = distance;
                }
            }
        return bestFallback ?? desired;
    }

    private static RectData Expand(RectData rect, double amount) => new(
        rect.X - amount,
        rect.Y - amount,
        rect.Width + amount * 2,
        rect.Height + amount * 2);

    private static bool FullyInside(RectData rect, RectData area) => rect.X >= area.X && rect.Y >= area.Y && rect.X + rect.Width <= area.X + area.Width && rect.Y + rect.Height <= area.Y + area.Height;
    private static bool Intersects(RectData a, RectData b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    private static bool SamePosition(RectData a, RectData b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;
    private static double OverlapArea(RectData a, RectData b) =>
        Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) *
        Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
    private static double DistanceSquared(RectData a, RectData b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
}

public enum DroppedFileKind { Text, Image, Attachment }
public static class FileClassifier
{
    private const int SampleSize = 32 * 1024;
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".rst", ".adoc", ".tex", ".log", ".csv", ".tsv",
        ".json", ".jsonc", ".xml", ".xaml", ".svg", ".yaml", ".yml", ".toml", ".ini",
        ".cfg", ".conf", ".config", ".properties", ".env",
        ".py", ".pyw", ".cs", ".csx", ".vb", ".fs", ".fsx",
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hxx", ".lua",
        ".java", ".kt", ".kts", ".scala", ".go", ".rs", ".rb", ".php", ".swift", ".dart",
        ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".html", ".htm", ".css",
        ".scss", ".sass", ".less", ".vue", ".svelte",
        ".ps1", ".psm1", ".psd1", ".sh", ".bash", ".zsh", ".fish", ".bat", ".cmd",
        ".sql", ".r", ".pl", ".pm", ".groovy", ".gradle", ".proto", ".graphql", ".gql",
        ".sln", ".csproj", ".vbproj", ".fsproj", ".vcxproj", ".props", ".targets"
    };
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "Makefile", "CMakeLists.txt", ".gitignore", ".gitattributes", ".editorconfig", ".env"
    };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    public static DroppedFileKind Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (Text.Contains(extension) || TextFileNames.Contains(Path.GetFileName(path))) return DroppedFileKind.Text;
        if (Images.Contains(extension)) return DroppedFileKind.Image;
        return LooksLikeTextFile(path) ? DroppedFileKind.Text : DroppedFileKind.Attachment;
    }

    private static bool LooksLikeTextFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var sample = new byte[Math.Min(SampleSize, (int)Math.Min(stream.Length, int.MaxValue))];
            var count = stream.Read(sample, 0, sample.Length);
            if (count == 0) return true;
            var bytes = sample.AsSpan(0, count);
            if (HasKnownBinarySignature(bytes)) return false;
            if (HasTextBom(bytes)) return true;

            var zeroCount = 0;
            var suspiciousControls = 0;
            var evenZeros = 0;
            var oddZeros = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                if (value == 0)
                {
                    zeroCount++;
                    if ((i & 1) == 0) evenZeros++; else oddZeros++;
                }
                else if (value < 0x20 && value is not (0x08 or 0x09 or 0x0A or 0x0C or 0x0D)) suspiciousControls++;
            }

            if (zeroCount > 0)
            {
                var pairs = Math.Max(1, bytes.Length / 2);
                var looksUtf16Le = oddZeros >= 2 && oddZeros > pairs * 0.3 && evenZeros < pairs * 0.05;
                var looksUtf16Be = evenZeros >= 2 && evenZeros > pairs * 0.3 && oddZeros < pairs * 0.05;
                return looksUtf16Le || looksUtf16Be;
            }

            // This also accepts legacy ANSI text: high bytes are not treated as binary,
            // while embedded control bytes still cause an unknown file to remain an attachment.
            return suspiciousControls <= Math.Max(1, bytes.Length / 100);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool HasTextBom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ||
        bytes.StartsWith(new byte[] { 0xFF, 0xFE }) || bytes.StartsWith(new byte[] { 0xFE, 0xFF }) ||
        bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }) || bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 });

    private static bool HasKnownBinarySignature(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("%PDF-"u8) || bytes.StartsWith("PK\x03\x04"u8) || bytes.StartsWith("MZ"u8) ||
        bytes.StartsWith(new byte[] { 0x1F, 0x8B }) || bytes.StartsWith(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }) ||
        bytes.StartsWith(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }) ||
        bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) || bytes.StartsWith("GIF8"u8) ||
        bytes.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 }) ||
        bytes.StartsWith(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C });
}

public static class ColorContrast
{
    public static bool UseLightForeground(string hex)
    {
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 8) hex = hex[2..];
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return false;
        static double Linear(byte c) { var s = c / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        var l = .2126 * Linear((byte)(rgb >> 16)) + .7152 * Linear((byte)(rgb >> 8)) + .0722 * Linear((byte)rgb);
        return l < .179;
    }
}
