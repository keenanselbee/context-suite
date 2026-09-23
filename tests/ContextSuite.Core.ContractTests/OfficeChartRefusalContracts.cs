using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;

internal static class OfficeChartRefusalContracts
{
    internal static string[] CreateUnsupportedFixtures(string directory)
    {
        var files = ExcelLayoutFixtures.Create(directory).Select(name => Path.Combine(directory, name)).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            using var archive = ZipFile.Open(files[index], ZipArchiveMode.Update);
            var part = index == 0 ? "[Content_Types].xml" : "xl/drawings/_rels/drawing1.xml.rels";
            var entry = archive.GetEntry(part)!;
            string original;
            using (var reader = new StreamReader(entry.Open())) original = reader.ReadToEnd();
            var changed = index switch
            {
                0 => original.Replace("application/vnd.openxmlformats-officedocument.drawingml.chart+xml", "application/vnd.ms-office.chartex+xml"),
                1 => original.Replace("http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", "http://schemas.microsoft.com/office/2014/relationships/chartEx"),
                2 => original.Replace("Target=\"../charts/chart1.xml\"", "Target=\"https://example.invalid/chart.xml\" TargetMode=\"External\""),
                _ => "<Relationships"
            };
            if (changed == original) throw new InvalidDataException("Authored unsupported-chart fixture was not changed.");
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(part).Open(), new UTF8Encoding(false));
            writer.Write(changed);
        }
        return files;
    }

    internal static async Task RunAsync(string executable, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        if (!root.Contains("\\.codex-temp\\office-chart-refusal\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use a fresh repository Office chart-refusal evidence directory.");
        if (Directory.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "office-engine")))
            throw new InvalidDataException("Use a scratch worker without the native Office engine for this pre-runtime refusal test.");
        var originals = Path.Combine(root, "originals");
        var files = CreateUnsupportedFixtures(originals);
        var checks = new List<string>();
        await using (var worker = new WorkerClient(executable, Path.Combine(root, "worker")))
        {
            await worker.GetCapabilitiesAsync(default); var process = worker.ProcessId;
            foreach (var file in files)
            foreach (var mode in new[] { "cached", "recalculate" })
            {
                var bytes = File.ReadAllBytes(file); var written = File.GetLastWriteTimeUtc(file);
                var item = Guid.NewGuid();
                var work = new OfficeExportWork(item, Path.Combine(root, "office-" + item.ToString("N")),
                    "ContextSuite.Office.Evaluation." + item.ToString("N"), "xlsx", mode, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
                foreach (var name in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(work.DirectoryPath, name));
                File.WriteAllBytes(work.SourcePath, bytes); File.SetAttributes(work.SourcePath, FileAttributes.ReadOnly);
                var snapshotTime = File.GetLastWriteTimeUtc(work.SourcePath);
                using (var input = File.OpenRead(work.SourcePath))
                    Check((await OfficeSourcePreflight.InspectOpenXmlAsync(work.SourcePath, input, default)).Refusal?.Contains("charts") == true,
                        "unsupported or incomplete chart fixture is identified before worker transport: " + Path.GetFileName(file) + "/" + mode);
                try { await worker.ExportOfficeAsync(work, default); Check(false, "Worker accepted a chart workbook"); }
                catch (MediaWorkerException error)
                { Check(error.Failure == ImageFailure.InvalidInput, "actual worker refuses chart workbook before native runtime access: " + mode); }
                Check(Directory.GetFiles(work.DirectoryPath, "*", SearchOption.AllDirectories).SequenceEqual(new[] { work.SourcePath }) &&
                    !Directory.Exists(OfficeOwnershipJournal.ExpectedProfileDirectory(work.ProfileName)) &&
                    File.ReadAllBytes(work.SourcePath).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(work.SourcePath) == snapshotTime &&
                    File.ReadAllBytes(file).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(file) == written,
                    "refusal creates no output or native profile and preserves source/snapshot");
            }
            var controls = Path.Combine(root, "controls"); Directory.CreateDirectory(controls); OfficeFixtures.Create(controls);
            var control = Directory.GetFiles(controls, "*.xlsx").Single();
            using (var input = File.OpenRead(control))
                Check((await OfficeSourcePreflight.InspectOpenXmlAsync(control, input, default)).Refusal is null,
                    "ordinary chart-free workbook remains admitted by source preflight");
            var controlBytes = File.ReadAllBytes(control); var controlId = Guid.NewGuid();
            var controlWork = new OfficeExportWork(controlId, Path.Combine(root, "office-" + controlId.ToString("N")),
                "ContextSuite.Office.Evaluation." + controlId.ToString("N"), "xlsx", "cached", controlBytes.Length, Convert.ToHexString(SHA256.HashData(controlBytes)));
            foreach (var name in new[] { "input", "output", "profile", "temp" }) Directory.CreateDirectory(Path.Combine(controlWork.DirectoryPath, name));
            File.WriteAllBytes(controlWork.SourcePath, controlBytes); File.SetAttributes(controlWork.SourcePath, FileAttributes.ReadOnly);
            try { await worker.ExportOfficeAsync(controlWork, default); Check(false, "Absent native runtime was accepted"); }
            catch (MediaWorkerException error)
            { Check(error.Failure == ImageFailure.EngineFailure, "chart-free control reaches the deliberately absent runtime instead of a chart refusal"); }
            await worker.GetCapabilitiesAsync(default);
            Check(worker.ProcessId == process, "same worker remains usable after chart refusals and the missing-runtime control");
        }
        Check(!Directory.Exists(Path.Combine(root, "worker")) || !Directory.EnumerateFileSystemEntries(Path.Combine(root, "worker")).Any(),
            "worker exits and releases scratch state");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks,
            Scope = "Newer/external chart and incomplete-inspection refusals only. Ordinary D07 chart fidelity has separate application and PDF acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office chart-refusal worker checks.");
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }
}
