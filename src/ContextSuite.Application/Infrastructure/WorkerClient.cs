using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Runtime;

namespace ContextSuite.Application.Infrastructure;

internal sealed class WorkerClient(string executable) : IAsyncDisposable
{
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    public int? ProcessId => _process?.Id;

    public async Task<MediaCapability[]> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (_process is null)
            {
                var name = $"ContextSuite-Worker-{Guid.NewGuid():N}";
                _pipe = LocalPipe.CreateServer(name);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--pipe");
                start.ArgumentList.Add(name);
                start.ArgumentList.Add("--parent");
                start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _process = Process.Start(start) ?? throw new IOException("The media worker could not start.");
                await _pipe.WaitForConnectionAsync(timeout.Token);
                LocalPipe.VerifyPeer(_pipe, true, _process.Id, Path.GetFullPath(executable));
            }
            var id = Guid.NewGuid();
            await JsonFrames.WriteAsync(_pipe!, new WorkerCommand(1, id, "capabilities"), timeout.Token);
            var reply = await JsonFrames.ReadAsync<WorkerReply>(_pipe!, timeout.Token);
            if (reply.Version != 1 || reply.RequestId != id || reply.Capabilities is null)
                throw new InvalidDataException("The media worker returned an invalid response.");
            return reply.Capabilities;
        }
        catch
        {
            await StopAsync(false);
            throw;
        }
    }

    private async Task StopAsync(bool graceful)
    {
        try
        {
            if (_process is not null && !_process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    if (graceful && _pipe?.IsConnected == true)
                        await JsonFrames.WriteAsync(_pipe, new WorkerCommand(1, Guid.NewGuid(), "shutdown"), timeout.Token);
                    else
                        _process.Kill(true);
                    await _process.WaitForExitAsync(timeout.Token);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException)
                {
                    if (!_process.HasExited) _process.Kill(true);
                    using var forced = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _process.WaitForExitAsync(forced.Token);
                }
            }
        }
        finally
        {
            if (_pipe is not null) await _pipe.DisposeAsync();
            _process?.Dispose();
            _pipe = null;
            _process = null;
        }
    }

    public async ValueTask DisposeAsync() { await StopAsync(true); }
}
