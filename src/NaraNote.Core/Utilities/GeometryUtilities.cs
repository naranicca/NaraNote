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

    public static RectData FindNonOverlapping(RectData desired, RectData workArea, IEnumerable<RectData> occupied, double gap = 12)
    {
        desired = ClampFullyVisible(desired, workArea);
        var blocked = occupied.Where(rect => Intersects(rect, workArea)).ToList();
        var xs = new HashSet<double> { desired.X, workArea.X, workArea.X + workArea.Width - desired.Width };
        var ys = new HashSet<double> { desired.Y, workArea.Y, workArea.Y + workArea.Height - desired.Height };
        foreach (var rect in blocked)
        {
            xs.Add(rect.X + rect.Width + gap); xs.Add(rect.X - desired.Width - gap); xs.Add(rect.X);
            ys.Add(rect.Y + rect.Height + gap); ys.Add(rect.Y - desired.Height - gap); ys.Add(rect.Y);
        }
        var candidates = from x in xs from y in ys
                         let candidate = new RectData(x, y, desired.Width, desired.Height)
                         where FullyInside(candidate, workArea)
                         orderby DistanceSquared(candidate, desired)
                         select candidate;
        var available = candidates.FirstOrDefault(candidate => blocked.All(rect => !Intersects(candidate, rect)));
        if (available.Width > 0 && available.Height > 0) return available;
        for (var y = workArea.Y; y <= workArea.Y + workArea.Height - desired.Height; y += 8)
            for (var x = workArea.X; x <= workArea.X + workArea.Width - desired.Width; x += 8)
            {
                var candidate = new RectData(x, y, desired.Width, desired.Height);
                if (blocked.All(rect => !Intersects(candidate, rect))) return candidate;
            }
        return desired;
    }

    private static bool FullyInside(RectData rect, RectData area) => rect.X >= area.X && rect.Y >= area.Y && rect.X + rect.Width <= area.X + area.Width && rect.Y + rect.Height <= area.Y + area.Height;
    private static bool Intersects(RectData a, RectData b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    private static double DistanceSquared(RectData a, RectData b) => Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2);
}

public enum DroppedFileKind { Text, Image, Attachment }
public static class FileClassifier
{
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini" };
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    public static DroppedFileKind Classify(string path) => Text.Contains(Path.GetExtension(path)) ? DroppedFileKind.Text : Images.Contains(Path.GetExtension(path)) ? DroppedFileKind.Image : DroppedFileKind.Attachment;
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
