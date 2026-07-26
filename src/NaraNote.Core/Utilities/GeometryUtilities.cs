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
