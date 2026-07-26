using System.Windows;
using NaraNote.App.Services;
using NaraNote.Infrastructure.Logging;
using NaraNote.Infrastructure.Persistence;

namespace NaraNote.App;

public partial class App : System.Windows.Application
{
    internal AppController Controller { get; private set; } = null!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var logger = new FileLogger();
        DispatcherUnhandledException += (_, args) => { logger.Error("UI", args.Exception); args.Handled = true; System.Windows.MessageBox.Show("작업을 처리하지 못했습니다. 로그를 확인해 주세요.", "NaraNote"); };
        Controller = new AppController(new JsonAppStateStore(), logger);
        await Controller.StartAsync();
    }
    protected override async void OnExit(ExitEventArgs e) { if (Controller is not null) await Controller.SaveNowAsync(); base.OnExit(e); }
}
