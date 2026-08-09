using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.IO;
using NaraNote.App.Localization;
using NaraNote.Infrastructure.Logging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace NaraNote.App.Services;

internal sealed class UpdateService(FileLogger logger)
{
    private const string LatestReleaseApi = "https://api.github.com/repos/naranicca/NaraNote/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task CheckAsync(bool interactive)
    {
        try
        {
            using var response = await Client.GetAsync(LatestReleaseApi);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest)) throw new InvalidDataException("The release version is invalid.");
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (latest <= current)
            {
                if (interactive) MessageBox.Show(UiText.Get("UpdateCurrent"), "NaraNote", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
                string.Equals(item.GetProperty("name").GetString(), "NaraNote.exe", StringComparison.OrdinalIgnoreCase));
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("NaraNote.exe is missing from the release.");
            var answer = MessageBox.Show(UiText.Format("UpdateAvailable", latest), "NaraNote", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;

            var url = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The download URL is invalid.");
            var expectedDigest = asset.TryGetProperty("digest", out var digestNode) ? digestNode.GetString() : null;
            var downloaded = Path.Combine(Path.GetTempPath(), $"NaraNote-update-{Guid.NewGuid():N}.exe");
            await DownloadAndVerifyAsync(uri, downloaded, expectedDigest);
            await ((App)Application.Current).Controller.SaveNowAsync();
            LaunchUpdater(downloaded);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            logger.Error("Update", ex);
            if (interactive) MessageBox.Show(UiText.Get("UpdateFailed"), "NaraNote", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task DownloadAndVerifyAsync(Uri uri, string destination, string? expectedDigest)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await input.CopyToAsync(output);
            await output.FlushAsync();
        }
        if (string.IsNullOrWhiteSpace(expectedDigest) || !expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return;
        await using var file = File.OpenRead(destination);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
        if (!actual.Equals(expectedDigest[7..], StringComparison.OrdinalIgnoreCase)) { File.Delete(destination); throw new InvalidDataException("The update checksum does not match."); }
    }

    private static void LaunchUpdater(string downloaded)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
        var script = Path.Combine(Path.GetTempPath(), $"NaraNote-updater-{Guid.NewGuid():N}.ps1");
        var qCurrent = current.Replace("'", "''");
        var qDownloaded = downloaded.Replace("'", "''");
        var qScript = script.Replace("'", "''");
        File.WriteAllText(script, $"$ErrorActionPreference='Stop'\nWait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue\n$target='{qCurrent}'\n$new='{qDownloaded}'\n$backup=$target+'.update-backup'\ntry {{ if(Test-Path $backup){{Remove-Item $backup -Force}}; Move-Item $target $backup -Force; Move-Item $new $target -Force; Start-Process -FilePath $target -WorkingDirectory (Split-Path $target); Start-Sleep -Seconds 2; Remove-Item $backup -Force -ErrorAction SilentlyContinue }} catch {{ if(Test-Path $backup){{Move-Item $backup $target -Force}} }}\nRemove-Item '{qScript}' -Force -ErrorAction SilentlyContinue\n");
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NaraNote-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
