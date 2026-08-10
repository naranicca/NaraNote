using System.Windows;
using System.IO;
using NaraNote.App.Services;
using NaraNote.Infrastructure.Logging;
using NaraNote.Infrastructure.Persistence;
using NaraNote.Infrastructure.Startup;
using NaraNote.App.Localization;

namespace NaraNote.App;

public partial class App : System.Windows.Application
{
    internal AppController Controller { get; private set; } = null!;
    private SingleInstanceService? _singleInstance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceService.TryAcquire(out _singleInstance))
        {
            var fileArgument = e.Args.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(fileArgument)) fileArgument = Path.GetFullPath(fileArgument);
            await SingleInstanceService.SendToExistingAsync(fileArgument);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        var logger = new FileLogger();
        DispatcherUnhandledException += (_, args) => { logger.Error("UI", args.Exception); args.Handled = true; System.Windows.MessageBox.Show(UiText.Get("UnexpectedError"), "NaraNote"); };
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && string.Equals(Path.GetFileName(executable), "NaraNote.exe", StringComparison.OrdinalIgnoreCase))
                new FileAssociationRegistration().Register(executable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException) { logger.Error("FileAssociation", ex); }
        Controller = new AppController(new JsonAppStateStore(), logger);
        logger.Info("Startup", "Loading application state.");
        await Controller.StartAsync();
        _singleInstance!.Start(Controller);
        if (e.Args.FirstOrDefault() is { Length: > 0 } initialFile)
        {
            var filePath = Path.GetFullPath(initialFile);
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => Controller.OpenFileInNewNote(filePath)));
        }
        logger.Info("Startup", "Initial note windows created.");
        if (Controller.State.Settings.CheckForUpdatesAutomatically)
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => _ = new UpdateService(logger).CheckAsync(false)));
    }
    protected override async void OnExit(ExitEventArgs e) { if (Controller is not null) await Controller.SaveNowAsync(); _singleInstance?.Dispose(); base.OnExit(e); }
}
