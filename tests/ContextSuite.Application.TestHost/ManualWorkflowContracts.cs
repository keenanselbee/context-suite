using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Activation;

namespace ContextSuite.Application.TestHost;

internal static class ManualWorkflowContracts
{
    internal static int VerifySelection(string manifest)
    {
        using var selection = ManualWorkflowSelection.Read(manifest);
        var root = FreshRoot();
        LicenseWorkflowFixture.Prepare(root);
        var request = selection.CreateRequest(root, Guid.NewGuid());
        var parsed = ActivationParser.Parse(Encoding.UTF8.GetBytes(request));
        File.WriteAllText(Path.Combine(root, "reviewed.request"), request);
        Console.WriteLine($"Reviewed {parsed.Operation}/{parsed.Action}: {parsed.Paths.Length} copied inputs. No window, worker or Polar request. Files retained: {root}");
        return 0;
    }

    internal static int Run()
    {
        var root = FreshRoot(); LicenseWorkflowFixture.Prepare(root);
        var source = Path.Combine(root, "fixture.png");
        var original = File.ReadAllBytes(source); var written = File.GetLastWriteTimeUtc(source);
        var hash = Convert.ToHexString(SHA256.HashData(original));
        var checks = new List<string>();
        var manifest = Path.Combine(root, "selection.json");
        Write("convert", "pdf", [(source, hash, false), (source, hash, false)]);
        string[] copies;
        var destination = FreshRoot(); LicenseWorkflowFixture.Prepare(destination);
        using (var selection = ManualWorkflowSelection.Read(manifest))
        {
            var request = ActivationParser.Parse(Encoding.UTF8.GetBytes(selection.CreateRequest(destination, Guid.NewGuid())));
            copies = request.Paths.ToArray();
            Check(request.Operation == "convert" && request.Action == "pdf" && copies.Length == 2,
                "reviewed operation, target and page order survive request creation");
            Check(copies.Select(Path.GetFileName).SequenceEqual(["01 - fixture.png", "02 - fixture.png"]),
                "duplicate source names get distinct ordered test copies");
            Check(copies.All(path => Path.GetDirectoryName(path) == Path.Combine(destination, "Inputs") &&
                File.ReadAllBytes(path).SequenceEqual(original)), "all inputs are byte-identical isolated copies");
            try { selection.CreateRequest(destination, Guid.NewGuid()); throw new Exception("Reused input directory accepted."); }
            catch (IOException) { Check(true, "used manual input directory cannot be overwritten"); }
        }
        Write("analyze", "open-details", [(source, hash, true)]);
        destination = FreshRoot(); LicenseWorkflowFixture.Prepare(destination);
        using (var selection = ManualWorkflowSelection.Read(manifest))
        {
            var request = ActivationParser.Parse(Encoding.UTF8.GetBytes(selection.CreateRequest(destination, Guid.NewGuid())));
            copies = request.Paths.ToArray();
            try { using var stream = File.OpenRead(copies[0]); throw new Exception("Unreadable control was readable."); }
            catch (IOException) { Check(true, "Analyze unreadable control locks only its disposable copy"); }
            Check(File.ReadAllBytes(source).SequenceEqual(original), "locked control does not lock its retained original");
        }
        Check(File.ReadAllBytes(copies[0]).SequenceEqual(original), "copy becomes readable after test disposal");
        Write("convert", "png", [(source, hash, true)]);
        Refused("unreadable controls cannot enter transformation workflows");
        Write("convert", "png", [(source, new string('0', 64), false)]);
        Refused("changed fixture identity refuses before copying");
        Write("unsupported", "png", [(source, hash, false)]);
        Refused("unknown commands refuse before profile creation");
        Write("convert", "png", []);
        Refused("empty selections refuse");
        Write("convert", "png", Enumerable.Repeat((source, hash, false), 33).ToArray());
        Refused("more than 32 selections refuse");
        Write("convert", "png", [(Path.Combine(root, "..", Path.GetFileName(root), "fixture.png"), hash, false)]);
        Refused("traversal input refuses");
        Write("convert", "png", [(Path.Combine(Repository(), "README.md"), hash, false)]);
        Refused("non-scratch input refuses");
        Check(File.ReadAllBytes(source).SequenceEqual(original) && File.GetLastWriteTimeUtc(source) == written,
            "all selection checks preserve the source bytes and timestamp");
        File.WriteAllText(Path.Combine(root, "manual-checks.json"), JsonSerializer.Serialize(new
        { Passed = true, Checks = checks, Scope = "Test-only request/copy/lock admission; no application window, worker, native profile or provider contact." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} manual workflow selection checks. Evidence: {root}");
        return 0;

        void Write(string operation, string action, (string Path, string Sha256, bool Locked)[] files) =>
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { Operation = operation, Action = action,
                Files = files.Select(file => new { file.Path, file.Sha256, file.Locked }) }));
        void Refused(string message)
        {
            try { using var selection = ManualWorkflowSelection.Read(manifest); throw new Exception("Invalid manual selection accepted."); }
            catch (InvalidDataException) { Check(true, message); }
        }
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks.Add(message); Console.WriteLine("PASS " + message);
        }
    }

    private static string FreshRoot()
    {
        var root = Path.Combine(Repository(), ".codex-temp", "license-workflow", "manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); return root;
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ContextSuite.Production.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Run the repository test host.");
    }
}
