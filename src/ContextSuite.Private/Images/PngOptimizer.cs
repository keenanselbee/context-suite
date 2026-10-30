using System.Diagnostics;
using System.Security.Cryptography;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

internal static class PngOptimizer
{
    // Bound to the reviewed upstream Windows x64 asset, not a PATH-resolved executable or editable CLI.
    private const string ExecutableSha256 = "F1A197F0DBC76D20CBE2A9CD7C4DBB98A0FDBA3E98BE445E0EBE10ECF20140AA";

    public static Task<byte[]> EncodeAsync(byte[] source, CancellationToken cancellationToken) =>
        RunAsync(source, "oxipng.exe", ExecutableSha256, ImageContainer.MaximumFileBytes,
            ["--opt", "2", "--nx", "--interlace", "keep", "--threads", "1",
                "--timeout", "60", "--max-raw-size", "320000000", "--stdout", "--quiet", "-"], cancellationToken);

    internal static async Task<byte[]> RunAsync(byte[] source, string executableName, string expectedHash,
        int maximumOutput, string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Path.Combine(AppContext.BaseDirectory, executableName);
        using var executableLease = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(executableLease)) != expectedHash)
            throw new ImageFailureException(ImageFailure.EngineFailure);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        void Stop()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
        using var job = new EncoderJob();
        process.Start();
        using var registration = deadline.Token.Register(Stop);
        try
        {
            // Assign before sending stdin, so even an assignment failure exposes no media to the child.
            job.Assign(process);
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, maximumOutput, deadline.Token);
            var diagnostics = ReadBoundedAsync(process.StandardError.BaseStream, 64 * 1024, deadline.Token);
            var input = WriteInputAsync();
            // Kill promptly if any stream fails; otherwise a full pipe can deadlock the remaining streams.
            foreach (var streamTask in new Task[] { output, diagnostics, input })
                _ = streamTask.ContinueWith(_ => Stop(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await Task.WhenAll(output, diagnostics, input, process.WaitForExitAsync(deadline.Token));
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new ImageFailureException(ImageFailure.EngineFailure);
            return await output;

            async Task WriteInputAsync()
            {
                await process.StandardInput.BaseStream.WriteAsync(source, deadline.Token);
                process.StandardInput.Close();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ImageFailureException(ImageFailure.TimedOut); }
        catch (IOException error) when (error is not ImageFailureException)
        {
            // A killed encoder can close stdin while we are still feeding it. That pipe failure
            // is not a source-file permission error, and cancellation retains its own category.
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("PNG optimization cancelled.", error, cancellationToken);
            throw new ImageFailureException(deadline.IsCancellationRequested ? ImageFailure.TimedOut : ImageFailure.EngineFailure, error);
        }
        finally
        {
            Stop();
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length > maximum - read) throw new ImageFailureException(ImageFailure.ResourceLimit);
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
