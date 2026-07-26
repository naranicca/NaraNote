using Microsoft.Win32;

namespace NaraNote.Infrastructure.Startup;

public sealed class StartupRegistration(string valueName = "NaraNote")
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false); var value = key?.GetValue(valueName) as string;
        return string.Equals(value, Quote(executablePath), StringComparison.OrdinalIgnoreCase);
    }
    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled) key.SetValue(valueName, Quote(executablePath), RegistryValueKind.String); else key.DeleteValue(valueName, false);
        key.DeleteValue("Light" + "StickyNotes", false);
    }
    private static string Quote(string path) => $"\"{path}\"";
}
