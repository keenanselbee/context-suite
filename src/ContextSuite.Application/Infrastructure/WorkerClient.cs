using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;
using ContextSuite.Runtime;

namespace ContextSuite.Application.Infrastructure;

internal sealed class WorkerClient(string executable, string? scratchRoot = null, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private string? _scratchDirectory;
    public int? ProcessId => _process?.Id;

    public async Task<MediaCapability[]> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "capabilities"), cancellationToken);
        if (reply.Capabilities is null) throw new InvalidDataException("The media worker returned no capabilities.");
        return reply.Capabilities;
    }

    public async Task<ImageEngineIdentity> GetEngineIdentityAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "engine-info"), cancellationToken);
        return reply.Engine ?? throw new InvalidDataException("The media worker returned no engine identity.");
    }

    public async Task<ImageSourceFacts> ProbeAsync(ImageProbe request, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "image-probe", Probe: request), cancellationToken);
        var source = reply.Source ?? throw new InvalidDataException("Worker returned no image facts.");
        source.Validate();
        if (source.ItemId != request.ItemId || !string.Equals(source.Path, Path.GetFullPath(request.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Worker returned facts for a different source.");
        return source;
    }

    public async Task<ImageWorkResult> ConvertAsync(ImageWork request, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "image-convert", Work: request), cancellationToken);
        var result = reply.ImageResult ?? throw new InvalidDataException("Worker returned no image result.");
        if (result.Validation.ItemId != request.Plan.Source.ItemId || !result.Validation.MatchesPlan ||
            result.Width != request.Plan.OutputWidth || result.Height != request.Plan.OutputHeight ||
            result.BitDepth != request.Plan.OutputDepth || result.OutputBytes is <= 0 or > 128 * 1024 * 1024)
            throw new InvalidDataException("Worker result does not match the planned conversion.");
        return result;
    }

    public async Task<ImagePreview> PreviewAsync(ImagePreviewRequest request, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "image-preview", Preview: request), cancellationToken);
        var preview = reply.Preview ?? throw new InvalidDataException("Worker returned no preview.");
        if (preview.Width is 0 or > 512 || preview.Height is 0 or > 512 || preview.BgraPixels is null ||
            preview.BgraPixels.Length != preview.Width * preview.Height * 4)
            throw new InvalidDataException("Worker preview exceeds the display bounds.");
        return preview;
    }

    private async Task<WorkerReply> SendAsync(WorkerCommand command, CancellationToken cancellationToken)
    {
        command.Validate();
        await _gate.WaitAsync(cancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(command.Command == "image-convert" ? 120 : 30),
            timeProvider ?? TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
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
                start.ArgumentList.Add("--scratch");
                _scratchDirectory = Path.Combine(Path.GetFullPath(scratchRoot ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "WorkerScratch")),
                    "worker-" + Guid.NewGuid().ToString("N"));
                start.ArgumentList.Add(_scratchDirectory);
                _process = Process.Start(start) ?? throw new IOException("The media worker could not start.");
                await _pipe.WaitForConnectionAsync(timeout.Token);
                LocalPipe.VerifyPeer(_pipe, true, _process.Id, Path.GetFullPath(executable));
            }
            await JsonFrames.WriteAsync(_pipe!, command, timeout.Token);
            var reply = await JsonFrames.ReadAsync<WorkerReply>(_pipe!, timeout.Token);
            if (reply.Version != 1 || reply.RequestId != command.RequestId || reply.Capabilities is null)
                throw new InvalidDataException("The media worker returned an invalid response.");
            if (reply.Failure is { } failure)
            {
                if (!Enum.IsDefined(failure) || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0)
                    throw new InvalidDataException("Worker failure response contains invalid or contradictory data.");
                throw new MediaWorkerException(failure);
            }
            return reply;
        }
        catch (MediaWorkerException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync(false);
            throw new MediaWorkerException(ImageFailure.TimedOut);
        }
        catch (IOException error)
        {
            await StopAsync(false);
            throw new MediaWorkerException(ImageFailure.WorkerTerminated, error);
        }
        catch
        {
            await StopAsync(false);
            throw;
        }
        finally { _gate.Release(); }
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
            if (_process is null || _process.HasExited) CleanScratch();
            _process?.Dispose();
            _pipe = null;
            _process = null;
        }
    }

    private void CleanScratch()
    {
        // Only this client's unique process directory, after process exit. Never follow a substituted link
        // or sweep other workers' directories; a failed cleanup leaves temporary evidence, not lost inputs.
        var directory = _scratchDirectory;
        _scratchDirectory = null;
        if (directory is null || !Directory.Exists(directory)) return;
        try
        {
            var name = Path.GetFileName(directory);
            if (!name.StartsWith("worker-", StringComparison.Ordinal) || !Guid.TryParseExact(name[7..], "N", out _)) return;
            for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return;
            RemoveOwnedDirectory(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        static void RemoveOwnedDirectory(string path)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked scratch entry retained.");
                if ((attributes & FileAttributes.Directory) != 0) RemoveOwnedDirectory(entry);
                else File.Delete(entry);
            }
            Directory.Delete(path, false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await StopAsync(true); }
        finally { _gate.Release(); }
    }
}

internal sealed class MediaWorkerException(ImageFailure failure, Exception? inner = null)
    : IOException(ImageFailureException.Describe(failure), inner)
{
    public ImageFailure Failure { get; } = failure;
}
