using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kage.Desktop;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipe;
    private readonly CancellationTokenSource shutdown = new();
    private Task? listener;
    public bool IsOwner { get; }

    internal SingleInstance(string stateDirectory)
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        pipe = "KageDesktopTool-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + Path.GetFullPath(stateDirectory).ToUpperInvariant())))[..24];
        mutex = new Mutex(false, @"Global\" + pipe);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    internal async Task SendSettingsAsync()
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000);
        using var writer = new StreamWriter(client);
        await writer.WriteLineAsync("settings");
        await writer.FlushAsync();
    }

    internal void Listen(Action showSettings, Action<string> report)
    {
        listener = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(shutdown.Token);
                    using var reader = new StreamReader(server);
                    if (await reader.ReadLineAsync(shutdown.Token) == "settings") showSettings();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    report($"重复启动请求接收失败：{e.Message}");
                    break;
                }
            }
        });
    }

    public void Dispose()
    {
        shutdown.Cancel();
        listener?.GetAwaiter().GetResult();
        shutdown.Dispose();
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
