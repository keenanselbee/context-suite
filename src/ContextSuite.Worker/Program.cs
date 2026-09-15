using System.Diagnostics;
using System.IO.Pipes;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;
using ContextSuite.Private;
using ContextSuite.Private.Images;
using ContextSuite.Private.Audio;
using ContextSuite.Private.Pdf;
using ContextSuite.Private.Office;
using ContextSuite.Runtime;

if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--parent" || args[4] != "--scratch" || !Path.IsPathFullyQualified(args[5]) ||
    !int.TryParse(args[3], out var parentId) || !args[1].StartsWith("ContextSuite-Worker-", StringComparison.Ordinal))
    return 2;

try
{
    using var parent = Process.GetProcessById(parentId);
    using var lifetime = new CancellationTokenSource();
    parent.EnableRaisingEvents = true;
    // Native decoder calls are not guaranteed to poll cancellation. Do not leave a native worker
    // running after the owning app is gone; uncommitted output remains journaled by the publisher.
    parent.Exited += (_, _) => Environment.Exit(3);
    if (parent.HasExited) return 3;
    await using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
    {
        startup.CancelAfter(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(startup.Token);
    }
    LocalPipe.VerifyPeer(pipe, false, parentId, parent.MainModule!.FileName!);
    var catalog = new ProductionCatalog();
    ImageAdapter? adapter = null;
    AudioProbeAdapter? audio = null;
    AudioFileAdapter? audioFiles = null;
    PdfProbeAdapter? pdf = null;
    PdfRasterAdapter? pdfRaster = null;
    ImagePdfValidator? imagePdfValidator = null;
    try
    {
    while (!lifetime.IsCancellationRequested)
    {
        var command = await JsonFrames.ReadAsync<WorkerCommand>(pipe, lifetime.Token);
        command.Validate();
        if (command.Command == "shutdown") return 0;
        if (command.Command == "engine-info")
        {
            await JsonFrames.WriteAsync(pipe,
                new WorkerReply(1, command.RequestId, [], ImageEngineRuntime.GetIdentity()), lifetime.Token);
            continue;
        }
        WorkerReply reply;
        try
        {
            if (command.Command == "capabilities") reply = new(1, command.RequestId, catalog.Capabilities.ToArray());
            else if (command.Command == "office-export")
                reply = new(1, command.RequestId, [], OfficeCandidate: await OfficeFileAdapter.ExportAsync(
                    Path.Combine(AppContext.BaseDirectory, "office-engine"), command.OfficeWork!, command.RequestId, lifetime.Token));
            else if (command.Command == "office-pdf-validate")
            {
                pdf ??= new PdfProbeAdapter(Path.Combine(AppContext.BaseDirectory, "pdf-engine"), args[5]);
                pdfRaster ??= new PdfRasterAdapter(Path.Combine(AppContext.BaseDirectory, "pdf-renderer"), args[5]);
                reply = new(1, command.RequestId, [], OfficePdfResult: await new OfficePdfFileAdapter(pdf, pdfRaster)
                    .ValidateAsync(command.OfficePdf!, lifetime.Token));
            }
            else if (command.Command == "images-to-pdf")
            {
                adapter ??= new ImageAdapter(args[5]);
                imagePdfValidator ??= new ImagePdfValidator(Path.Combine(AppContext.BaseDirectory, "pdf-validator"), args[5]);
                reply = new(1, command.RequestId, [], ImagePdfResult: await new ImagePdfFileAdapter(adapter, imagePdfValidator).ConvertAsync(command.ImagePdf!, lifetime.Token));
            }
            else if (command.Command is "flac-probe" or "flac-optimize" or "audio-file-probe" or "audio-convert")
            {
                audioFiles ??= new AudioFileAdapter(Path.Combine(AppContext.BaseDirectory, "audio-engine"), args[5]);
                reply = command.Command switch
                {
                    "flac-probe" => new(1, command.RequestId, [], AudioSource: await audioFiles.ProbeFlacAsync(command.AudioFile!, lifetime.Token)),
                    "flac-optimize" => new(1, command.RequestId, [], AudioResult: await audioFiles.OptimizeFlacAsync(command.FlacWork!, lifetime.Token)),
                    "audio-file-probe" => new(1, command.RequestId, [], AudioSource: await audioFiles.ProbeConversionAsync(command.AudioFile!, command.AudioTarget!.Value, lifetime.Token)),
                    _ => new(1, command.RequestId, [], AudioResult: await audioFiles.ConvertAsync(command.AudioWork!, lifetime.Token))
                };
            }
            else if (command.Command == "audio-probe")
            {
                audio ??= new AudioProbeAdapter(Path.Combine(AppContext.BaseDirectory, "audio-engine"));
                reply = new(1, command.RequestId, [], Audio: await audio.ProbeAsync(command.AudioBytes!, lifetime.Token));
            }
            else if (command.Command is "pdf-raster-probe" or "pdf-render-page")
            {
                pdfRaster ??= new PdfRasterAdapter(Path.Combine(AppContext.BaseDirectory, "pdf-renderer"), args[5]);
                reply = command.Command == "pdf-raster-probe"
                    ? new(1, command.RequestId, [], PdfRaster: await pdfRaster.ProbeAsync(command.PdfFile!, lifetime.Token))
                    : new(1, command.RequestId, [], PdfPageResult: await pdfRaster.RenderFileAsync(command.PdfPage!, lifetime.Token));
            }
            else if (command.Command is "pdf-probe" or "pdf-file-probe" or "pdf-optimize")
            {
                pdf ??= new PdfProbeAdapter(Path.Combine(AppContext.BaseDirectory, "pdf-engine"), args[5]);
                reply = command.Command switch
                {
                    "pdf-probe" => new(1, command.RequestId, [], Pdf: await pdf.ProbeAsync(command.PdfBytes!, lifetime.Token)),
                    "pdf-file-probe" => new(1, command.RequestId, [], PdfSource: await pdf.ProbeFileAsync(command.PdfFile!, lifetime.Token)),
                    _ => new(1, command.RequestId, [], PdfResult: await pdf.OptimizeFileAsync(command.PdfWork!, lifetime.Token))
                };
            }
            else
            {
                adapter ??= new ImageAdapter(args[5]);
                reply = command.Command switch
                {
                    "image-probe" => new(1, command.RequestId, [], Source: adapter.Probe(command.Probe!, lifetime.Token)),
                    "png-probe" => new(1, command.RequestId, [], Source: adapter.ProbeOptimization(command.Probe!, lifetime.Token)),
                    "png-optimize" => new(1, command.RequestId, [], ImageResult: await adapter.OptimizeAsync(command.Optimization!, lifetime.Token)),
                    "image-preview" => new(1, command.RequestId, [], Preview: adapter.Preview(command.Preview!, lifetime.Token)),
                    "image-convert" => new(1, command.RequestId, [], ImageResult: adapter.Convert(command.Work!, lifetime.Token)),
                    _ => throw new InvalidDataException("Unknown image operation.")
                };
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or
            UnauthorizedAccessException or ImageMagick.MagickException or System.Xml.XmlException or TimeoutException or System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or OutOfMemoryException or OperationCanceledException)
        {
            // A damaged/unsupported item is not a worker crash. Do not return engine strings containing file paths.
            var failure = error switch
            {
                ImageFailureException known => known.Failure,
                NotSupportedException => ImageFailure.UnsupportedInput,
                TimeoutException or OperationCanceledException => ImageFailure.TimedOut,
                InvalidDataException or ArgumentException or System.Xml.XmlException => ImageFailure.InvalidInput,
                UnauthorizedAccessException or IOException => ImageFailure.FileAccess,
                ImageMagick.MagickResourceLimitErrorException => ImageFailure.ResourceLimit,
                OutOfMemoryException => ImageFailure.ResourceLimit,
                ImageMagick.MagickCorruptImageErrorException => ImageFailure.InvalidInput,
                _ => ImageFailure.EngineFailure
            };
            reply = new(1, command.RequestId, [], Failure: failure);
        }
        await JsonFrames.WriteAsync(pipe, reply, lifetime.Token);
    }
    }
    finally { audioFiles?.Dispose(); audio?.Dispose(); pdf?.Dispose(); pdfRaster?.Dispose(); imagePdfValidator?.Dispose(); }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or ArgumentException or
    InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
{
    // Do not print paths or protocol payloads into diagnostics.
    return 3;
}
