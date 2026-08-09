using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NaraNote.App.Services;

internal static class ShellIconProvider
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static ImageSource? GetIcon(string path)
    {
        var isDirectory = Directory.Exists(path);
        var exists = isDirectory || File.Exists(path);
        var extension = Path.GetExtension(path);
        var cacheKey = isDirectory ? "<folder>" : string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) && exists
            ? path
            : string.IsNullOrWhiteSpace(extension) ? "<file>" : extension;
        lock (CacheLock)
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon | (exists ? 0u : ShgfiUseFileAttributes);
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero) return null;
        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(info.IconHandle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
            icon.Freeze();
            lock (CacheLock) Cache[cacheKey] = icon;
            return icon;
        }
        finally { _ = DestroyIcon(info.IconHandle); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShellFileInfo fileInfo, uint fileInfoSize, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
