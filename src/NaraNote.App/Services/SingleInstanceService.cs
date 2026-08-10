using System.IO;
using System.IO.Pipes;
using System.Text;

namespace NaraNote.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "NaraNote.SingleInstance.9A4F3D6B";
    private const string PipeName = "NaraNote.SingleInstance.9A4F3D6B";
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Mutex _mutex;
    private Task? _serverTask;

    private SingleInstanceService(Mutex mutex) => _mutex = mutex;

    public static bool TryAcquire(out SingleInstanceService? instance)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            instance = null;
            return false;
        }
        instance = new SingleInstanceService(mutex);
        return true;
    }

    public static async Task<bool> SendToExistingAsync(string? filePath, CancellationToken cancellationToken = default)
    {
        var payload = string.IsNullOrWhiteSpace(filePath)
            ? "NEW"
            : "FILE:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(filePath));
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(200, cancellationToken);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(payload);
                return true;
            }
            catch (TimeoutException) when (attempt < 9) { await Task.Delay(100, cancellationToken); }
            catch (IOException) when (attempt < 9) { await Task.Delay(100, cancellationToken); }
        }
        return false;
    }

    public void Start(AppController controller)
    {
        _serverTask = Task.Run(async () =>
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(_shutdown.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var command = await reader.ReadLineAsync(_shutdown.Token);
                    if (!string.IsNullOrWhiteSpace(command))
                        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => controller.HandleSingleInstanceCommand(command)));
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (IOException) when (!_shutdown.IsCancellationRequested) { }
            }
        }, _shutdown.Token);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _serverTask?.Wait(500); } catch (AggregateException) { }
        _shutdown.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
