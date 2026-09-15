using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Office;
using ContextSuite.Runtime;

namespace ContextSuite.Application.Infrastructure;

internal sealed class WorkerClient(string executable, string? scratchRoot = null, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private WorkerProcessJob? _job;
    private NamedPipeServerStream? _pipe;
    private string? _scratchDirectory;
    public int? ProcessId => _process?.Id;
    public bool HasAudioProbe => File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "audio-engine", "ffprobe.exe"));
    public bool HasFlacOptimizer => HasAudioProbe && File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "audio-engine", "ffmpeg.exe"));
    public bool HasAudioConverter => HasFlacOptimizer;
    public bool HasPdfProbe => File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "pdf-engine", "qpdf.exe"));
    public bool HasPdfOptimizer => HasPdfProbe;
    public bool HasPdfRenderer => File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "pdf-renderer", "ContextSuite.PdfRenderer.exe"));
    public bool HasImagePdfConverter => File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "pdf-validator", "ContextSuite.ImagePdfValidator.exe"));

    public async Task<OfficeExportCandidate> ExportOfficeAsync(OfficeExportWork work, CancellationToken token)
    {
        var command = new WorkerCommand(1, Guid.NewGuid(), "office-export", OfficeWork: work);
        var reply = await SendAsync(command, token);
        var candidate = reply.OfficeCandidate ?? throw new InvalidDataException("Missing Office export candidate.");
        candidate.Validate(work, command.RequestId);
        return candidate;
    }

    public async Task<ImagePdfResult> ConvertImagesToPdfAsync(ImagePdfWork work, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "images-to-pdf", ImagePdf: work), token);
        if (reply.ImagePdfResult is not { } result || result.Validation is null || result.Validation.ItemId != work.OutputId ||
            !result.Validation.MatchesPlan || result.Validation.Sha256 is not { Length: 64 } || !result.Validation.Sha256.All(char.IsAsciiHexDigit) ||
            result.BatchId != work.Plan.BatchId || result.PageCount != work.Plan.Pages.Length || result.SourceHashes.IsDefault ||
            !result.SourceHashes.SequenceEqual(work.Plan.Pages.Select(page => page.Source.Sha256)) ||
            result.OutputBytes is <= 0 or > ImagePdfPlan.MaximumOutputBytes || result.Policy != work.Policy ||
            string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 256)
            throw new InvalidDataException("Invalid combined PDF validation response.");
        return result;
    }

    public async Task<PdfRasterSource> ProbePdfPagesAsync(PdfFileProbe request, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "pdf-raster-probe", PdfFile: request), token);
        if (reply.PdfRaster is not { } source || source.ItemId != request.ItemId || source.Path != request.Path)
            throw new InvalidDataException("Invalid PDF page source response.");
        source.Validate();
        return source;
    }

    public async Task<PdfPageResult> RenderPdfPageAsync(PdfPageWork work, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "pdf-render-page", PdfPage: work), token);
        if (reply.PdfPageResult is not { } result || result.Validation is null || result.Validation.ItemId != work.OutputId ||
            !result.Validation.MatchesPlan || result.Validation.Sha256 is not { Length: 64 } || !result.Validation.Sha256.All(char.IsAsciiHexDigit) ||
            result.Page != work.Source.Document.Pages[work.PageIndex] || result.SourceSha256 != work.Source.Sha256 ||
            result.OutputBytes is <= 0 or > PdfPageWork.MaximumOutputBytes || result.PixelSha256 is not { Length: 64 } ||
            !result.PixelSha256.All(char.IsAsciiHexDigit) || result.Policy != work.Policy ||
            string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 256)
            throw new InvalidDataException("Invalid PDF page validation response.");
        return result;
    }

    public async Task<PdfFileSource> ProbePdfFileAsync(PdfFileProbe request, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "pdf-file-probe", PdfFile: request), token);
        if (reply.PdfSource is not { } source || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null ||
            reply.Engine is not null || reply.Capabilities.Length != 0 || source.ItemId != request.ItemId || source.Path != request.Path)
            throw new InvalidDataException("Invalid PDF file source response.");
        source.Validate();
        return source;
    }

    public async Task<PdfWorkResult> OptimizePdfAsync(PdfOptimizationWork work, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "pdf-optimize", PdfWork: work), token);
        if (reply.PdfResult is not { } result || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null ||
            reply.Engine is not null || reply.Capabilities.Length != 0 || result.Validation is null ||
            result.Validation.ItemId != work.Source.ItemId || !result.Validation.MatchesPlan || result.Validation.Sha256 is not { Length: 64 } ||
            !result.Validation.Sha256.All(char.IsAsciiHexDigit) || result.SourceSha256 != work.Source.Sha256 || result.SourceBytes != work.Source.FileBytes ||
            result.OutputBytes <= 0 || result.OutputBytes > result.SourceBytes ||
            (result.OutputBytes == result.SourceBytes && result.Validation.Sha256 != result.SourceSha256) || result.Policy != work.Policy ||
            string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 256)
            throw new InvalidDataException("Invalid PDF optimization validation response.");
        return result;
    }

    public async Task<PdfProbeFacts> ProbePdfAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "pdf-probe", PdfBytes: bytes.ToArray()), cancellationToken);
        if (reply.Pdf is null || reply.Audio is not null || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null ||
            reply.Engine is not null || reply.Capabilities.Length != 0) throw new InvalidDataException("Invalid PDF probe response.");
        reply.Pdf.Validate();
        return reply.Pdf;
    }

    public async Task<AudioProbeFacts> ProbeAudioAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "audio-probe", AudioBytes: bytes.ToArray()), cancellationToken);
        if (reply.Audio is null || reply.Pdf is not null || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null ||
            reply.Engine is not null || reply.Capabilities.Length != 0) throw new InvalidDataException("Invalid audio probe response.");
        reply.Audio.Validate();
        return reply.Audio;
    }

    public async Task<MediaCapability[]> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "capabilities"), cancellationToken);
        if (reply.Capabilities is null) throw new InvalidDataException("The media worker returned no capabilities.");
        return reply.Capabilities;
    }

    public async Task<AudioFileSource> ProbeFlacAsync(AudioFileProbe request, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "flac-probe", AudioFile: request), token);
        if (reply.AudioSource is not { } source || reply.AudioResult is not null || reply.Source is not null || reply.ImageResult is not null ||
            reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0 || source.ItemId != request.ItemId || source.Path != request.Path)
            throw new InvalidDataException("Invalid FLAC source response.");
        source.Validate();
        return source;
    }

    public async Task<AudioWorkResult> OptimizeFlacAsync(FlacOptimizationWork work, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "flac-optimize", FlacWork: work), token);
        if (reply.AudioResult is not { } result || reply.AudioSource is not null || reply.Source is not null || reply.ImageResult is not null ||
            reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0 || result.Validation is null ||
            result.Validation.ItemId != work.Source.ItemId || !result.Validation.MatchesPlan || result.Validation.Sha256 is not { Length: 64 } ||
            !result.Validation.Sha256.All(char.IsAsciiHexDigit) || result.SourceSha256 != work.Source.Sha256 || result.SourceBytes != work.Source.FileBytes ||
            result.OutputBytes is <= 0 or > AudioFileSource.MaximumFileBytes || result.DecodedFrames <= 0 || result.Policy != work.Policy ||
            string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 256)
            throw new InvalidDataException("Invalid FLAC validation response.");
        return result;
    }

    public async Task<AudioFileSource> ProbeAudioFileAsync(AudioFileProbe request, AudioFormat target, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "audio-file-probe", AudioFile: request, AudioTarget: target), token);
        if (reply.AudioSource is not { } source || reply.AudioResult is not null || reply.Source is not null || reply.ImageResult is not null ||
            reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0 || source.ItemId != request.ItemId || source.Path != request.Path)
            throw new InvalidDataException("Invalid audio conversion source response.");
        source.Validate(); return source;
    }

    public async Task<AudioWorkResult> ConvertAudioAsync(AudioConversionWork work, CancellationToken token)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "audio-convert", AudioWork: work), token);
        if (reply.AudioResult is not { } result || reply.AudioSource is not null || reply.Source is not null || reply.ImageResult is not null ||
            reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0 || result.Validation is null ||
            result.Validation.ItemId != work.Source.ItemId || !result.Validation.MatchesPlan || result.Validation.Sha256 is not { Length: 64 } ||
            !result.Validation.Sha256.All(char.IsAsciiHexDigit) || result.SourceSha256 != work.Source.Sha256 || result.SourceBytes != work.Source.FileBytes ||
            result.OutputBytes is <= 0 or > AudioFileSource.MaximumFileBytes || result.DecodedFrames <= 0 || result.Policy != work.Policy ||
            string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 256)
            throw new InvalidDataException("Invalid audio conversion validation response.");
        return result;
    }

    public async Task<ImageEngineIdentity> GetEngineIdentityAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "engine-info"), cancellationToken);
        return reply.Engine ?? throw new InvalidDataException("The media worker returned no engine identity.");
    }

    public async Task<ImageSourceFacts> ProbeAsync(ImageProbe request, CancellationToken cancellationToken, bool forOptimization = false)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), forOptimization ? "png-probe" : "image-probe", Probe: request), cancellationToken);
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

    public async Task<ImageWorkResult> OptimizeAsync(PngOptimizationWork request, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new(1, Guid.NewGuid(), "png-optimize", Optimization: request), cancellationToken);
        var result = reply.ImageResult ?? throw new InvalidDataException("Worker returned no optimization result.");
        if (result.Validation.ItemId != request.Source.ItemId || !result.Validation.MatchesPlan ||
            result.Width != request.Source.Width || result.Height != request.Source.Height || result.BitDepth != request.Source.BitDepth ||
            result.OutputBytes is <= 0 or > 128 * 1024 * 1024 || string.IsNullOrWhiteSpace(result.EngineIdentity) || result.EngineIdentity.Length > 128 ||
            result.OptimizationMethod is not ("Lossless" or "RGB7 (lossy)" or "Palette (lossy)") || result.OptimizationReason?.Length > 512 ||
            result.OptimizationAttempts is < 1 or > 2 || result.PngFdECRemoved != request.Source.PngFdECRemovalRequired ||
            (request.Policy == PngOptimizationPlan.Policy && result.OptimizationMethod != "Lossless") ||
            (request.Policy != PngOptimizationPlan.SmallestPolicy && result.OptimizationMethod == "Palette (lossy)"))
            throw new InvalidDataException("Worker result does not match the optimization plan.");
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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(command.Command is "images-to-pdf" or "office-export" ? 180 : command.Command is "flac-optimize" or "audio-convert" ? 150 : command.Command is "image-convert" or "png-optimize" or "pdf-optimize" or "pdf-raster-probe" or "pdf-render-page" ? 120 : 30),
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
                _job = new WorkerProcessJob();
                _process = Process.Start(start) ?? throw new IOException("The media worker could not start.");
                // The worker waits for a command before loading any engine.
                // Assign its lifetime job before sending the first request.
                _job.Assign(_process);
                await _pipe.WaitForConnectionAsync(timeout.Token);
                LocalPipe.VerifyPeer(_pipe, true, _process.Id, Path.GetFullPath(executable));
            }
            await JsonFrames.WriteAsync(_pipe!, command, timeout.Token);
            var reply = await JsonFrames.ReadAsync<WorkerReply>(_pipe!, timeout.Token);
            if (reply.Version != 1 || reply.RequestId != command.RequestId || reply.Capabilities is null)
                throw new InvalidDataException("The media worker returned an invalid response.");
            if (command.Command != "office-export" && reply.OfficeCandidate is not null || command.Command == "office-export" &&
                (reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0))
                throw new InvalidDataException("The media worker returned contradictory Office export data.");
            if (command.Command != "audio-probe" && reply.Audio is not null)
                throw new InvalidDataException("The media worker returned unexpected audio data.");
            if (command.Command != "images-to-pdf" && reply.ImagePdfResult is not null || command.Command == "images-to-pdf" &&
                (reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0))
                throw new InvalidDataException("The media worker returned contradictory combined PDF data.");
            if (command.Command != "pdf-probe" && reply.Pdf is not null)
                throw new InvalidDataException("The media worker returned unexpected PDF data.");
            if (command.Command != "pdf-raster-probe" && reply.PdfRaster is not null ||
                command.Command != "pdf-render-page" && reply.PdfPageResult is not null)
                throw new InvalidDataException("The media worker returned unexpected PDF page data.");
            if (command.Command is "pdf-raster-probe" or "pdf-render-page" &&
                (reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null || reply.Engine is not null || reply.Capabilities.Length != 0))
                throw new InvalidDataException("The media worker returned contradictory PDF page data.");
            if (command.Command != "pdf-file-probe" && reply.PdfSource is not null || command.Command != "pdf-optimize" && reply.PdfResult is not null)
                throw new InvalidDataException("The media worker returned unexpected PDF file data.");
            if (command.Command is not ("flac-probe" or "audio-file-probe") && reply.AudioSource is not null ||
                command.Command is not ("flac-optimize" or "audio-convert") && reply.AudioResult is not null)
                throw new InvalidDataException("The media worker returned unexpected file-audio data.");
            if (reply.Failure is { } failure)
            {
                if (!Enum.IsDefined(failure) || reply.Source is not null || reply.ImageResult is not null || reply.Preview is not null || reply.Engine is not null || reply.Audio is not null || reply.Pdf is not null || reply.AudioSource is not null || reply.AudioResult is not null || reply.PdfSource is not null || reply.PdfResult is not null || reply.PdfRaster is not null || reply.PdfPageResult is not null || reply.ImagePdfResult is not null || reply.OfficeCandidate is not null || reply.Capabilities.Length != 0)
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
            var stopped = false;
            try
            {
                if (_job is not null) await _job.StopAsync();
                stopped = _process is null || _process.HasExited;
            }
            finally
            {
                _job?.Dispose(); _job = null;
                try
                {
                    if (_pipe is not null) await _pipe.DisposeAsync();
                    if (stopped) await CleanScratchAsync();
                }
                finally
                {
                    _process?.Dispose();
                    _pipe = null;
                    _process = null;
                }
            }
        }
    }

    private async Task CleanScratchAsync()
    {
        // Only this client's unique process directory, after process exit. Never follow a substituted link
        // or sweep other workers' directories; a failed cleanup leaves temporary evidence, not lost inputs.
        var directory = _scratchDirectory;
        _scratchDirectory = null;
        if (directory is null || !Directory.Exists(directory)) return;
        for (var attempt = 0; attempt < 21; attempt++)
        {
            try
            {
                if (!Directory.Exists(directory)) return;
                var name = Path.GetFileName(directory);
                if (!name.StartsWith("worker-", StringComparison.Ordinal) || !Guid.TryParseExact(name[7..], "N", out _)) return;
                for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return;
                RemoveOwnedDirectory(directory);
                return;
            }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) when (attempt < 20)
            {
                // Job-owned native children can release file handles slightly
                // after their parent exits. Retry only this validated directory.
                await Task.Delay(100);
            }
            catch (IOException) { return; }
        }

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
