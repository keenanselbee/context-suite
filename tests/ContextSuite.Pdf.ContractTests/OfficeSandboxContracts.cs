using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;
using ContextSuite.Private.Office;

internal static class OfficeSandboxContracts
{
    internal static async Task RunAsync(string executable, string directory, string expectedHostHash, string mode)
    {
        var folder = Path.GetFullPath(directory);
        if (!folder.Contains("\\.codex-temp\\office-isolation\\", StringComparison.OrdinalIgnoreCase) ||
            mode is not ("export" or "cancel-startup" or "timeout" or "pre-cancel" or "extra-environment" or "process-fault"))
            throw new ArgumentException("Use an owned Office process evaluation case.");
        var names = new[] { "APPDATA", "LOCALAPPDATA", "PATH", "SYSTEMROOT", "TEMP", "TMP", "USERPROFILE", "WINDIR",
            "CS_OFFICE_PROFILE_NAME", "CS_OFFICE_REQUEST_ID", "CS_OFFICE_FORMAT", "CS_OFFICE_CALCULATION",
            "CS_OFFICE_SOURCE", "CS_OFFICE_OUTPUT", "CS_OFFICE_PROFILE", "CS_OFFICE_PROGRAM", "CS_OFFICE_SOURCE_IDENTITY",
            "CS_OFFICE_OUTPUT_IDENTITY", "CS_OFFICE_PROFILE_IDENTITY", "CS_OFFICE_PROGRAM_IDENTITY" };
        var environment = names.ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name) ?? throw new IOException("Missing test environment."));
        using var host = MediaFiles.OpenRead(executable);
        if (Convert.ToHexString(SHA256.HashData(host)) != expectedHostHash) throw new IOException("Host build differs from its receipt.");
        using var input = MediaFiles.OpenRead(environment["CS_OFFICE_SOURCE"]);
        var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(input));
        var sourceTime = File.GetLastWriteTimeUtc(input.Name);
        var admission = await OfficeSourcePreflight.InspectOpenXmlAsync(input.Name, input, CancellationToken.None);
        if (admission.FormatId != environment["CS_OFFICE_FORMAT"] || admission.Refusal is not null) throw new IOException("Authored source preflight failed.");
        using var stop = new CancellationTokenSource();
        if (mode == "pre-cancel") stop.Cancel();
        if (mode == "extra-environment") environment.Add("SAL_LOG", "+INFO");
        var execution = OfficeSandboxProcess.RunAsync(executable, folder, environment, stop.Token,
            mode == "timeout" ? TimeSpan.FromMilliseconds(1) : null);
        var cancellationObserved = false;
        if (mode == "cancel-startup")
        {
            var settings = Path.Combine(environment["CS_OFFICE_PROFILE"], "user", "registrymodifications.xcu");
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                while (!File.Exists(settings) && !execution.IsCompleted) await Task.Delay(10, observation.Token);
                if (execution.IsCompleted || !File.Exists(settings)) throw new IOException("Startup cancellation point was not observed.");
                cancellationObserved = true;
            }
            finally { stop.Cancel(); }
        }
        byte[]? reply = null;
        var refused = false;
        string? failure = null;
        try { reply = await execution; }
        catch (OperationCanceledException) when (mode is "pre-cancel" or "cancel-startup") { refused = true; }
        catch (TimeoutException) when (mode == "timeout") { refused = true; }
        catch (InvalidDataException) when (mode == "extra-environment") { refused = true; }
        catch (InvalidDataException error) when (mode == "process-fault")
        {
            failure = error.Message;
            var expected = environment["CS_OFFICE_FORMAT"] == "pptx"
                ? "Office export did not complete successfully." : "Office output or diagnostics exceeded its bound.";
            if (failure != expected) throw new IOException("Process fault was rejected for an unrelated reason.", error);
            refused = true;
        }
        input.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(input)) != sourceHash || File.GetLastWriteTimeUtc(input.Name) != sourceTime)
            throw new IOException("Authored source changed.");
        if (mode == "export")
        {
            if (reply is null) throw new IOException("Missing host completion.");
            var completion = OfficeHostProtocol.ReadCompletion(reply, 0, Guid.ParseExact(environment["CS_OFFICE_REQUEST_ID"], "N"),
                admission.FormatId!, environment["CS_OFFICE_CALCULATION"], input.Length, sourceHash);
            using var pdf = MediaFiles.OpenRead(environment["CS_OFFICE_OUTPUT"]);
            if (pdf.Length != completion.OutputBytes || Convert.ToHexString(await SHA256.HashDataAsync(pdf)) != completion.OutputSha256)
                throw new IOException("Candidate differs from its host completion.");
            Console.Write(Encoding.UTF8.GetString(reply));
        }
        else
        {
            if (!refused || reply is not null || File.Exists(environment["CS_OFFICE_OUTPUT"])) throw new IOException("Stopped request produced an accepted result.");
            if (mode == "process-fault" && environment["CS_OFFICE_FORMAT"] == "pptx")
            {
                var child = File.ReadAllText(environment["CS_OFFICE_PROFILE"]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (child.Length != 2) throw new IOException("Missing owned descendant identity.");
                var pid = int.Parse(child[0]); var created = long.Parse(child[1]);
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (process.StartTime.ToUniversalTime().ToFileTimeUtc() == created && !process.HasExited)
                        throw new IOException("Owned descendant survived launcher cleanup.");
                }
                catch (ArgumentException) { }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Mode = mode, Refused = true, Failure = failure, StartupObserved = cancellationObserved, SourceUnchanged = true }));
        }
    }
}
