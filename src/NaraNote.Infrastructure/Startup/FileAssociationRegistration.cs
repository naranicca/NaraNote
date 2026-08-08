using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NaraNote.Infrastructure.Startup;

public sealed class FileAssociationRegistration
{
    private const string Extension = ".naranote";
    private const string ProgId = "NaraNote.Document";
    private const string ClassesRoot = @"Software\Classes";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public void Register(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("실행 파일 경로가 올바르지 않습니다.", nameof(executablePath));
        var openCommand = $"\"{executablePath}\" \"%1\"";
        using (var extension = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{Extension}", true))
        {
            extension.SetValue(null, ProgId, RegistryValueKind.String);
            using var openWith = extension.CreateSubKey("OpenWithProgids", true);
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        using (var document = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ProgId}", true))
        {
            document.SetValue(null, "NaraNote 문서", RegistryValueKind.String);
            document.SetValue("FriendlyTypeName", "NaraNote 문서", RegistryValueKind.String);
            using var icon = document.CreateSubKey("DefaultIcon", true);
            icon.SetValue(null, $"\"{executablePath}\",0", RegistryValueKind.String);
            using var command = document.CreateSubKey(@"shell\open\command", true);
            command.SetValue(null, openCommand, RegistryValueKind.String);
        }
        using (var application = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\Applications\NaraNote.exe", true))
        {
            application.SetValue("FriendlyAppName", "NaraNote", RegistryValueKind.String);
            using var supportedTypes = application.CreateSubKey("SupportedTypes", true);
            supportedTypes.SetValue(Extension, "", RegistryValueKind.String);
            using var command = application.CreateSubKey(@"shell\open\command", true);
            command.SetValue(null, openCommand, RegistryValueKind.String);
        }
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
