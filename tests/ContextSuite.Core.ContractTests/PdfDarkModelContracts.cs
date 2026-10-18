using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Transport;

namespace ContextSuite.Core.ContractTests;

internal static class PdfDarkModelContracts
{
    internal static void Run(string scratch, Action<bool, string> check)
    {
        var facts = new PdfDarkFacts(3, 2, 1, 4, 2);
        var source = new PdfDarkSource(Guid.NewGuid(), Path.Combine(scratch, "source.pdf"), new string('A', 64), 100, facts);
        var settings = new SuiteSettings { Convert = new(ReplaceOriginals: true) }.Capture("convert");
        var plan = new PdfDarkPlan(Guid.NewGuid(), settings, [source]);
        check(plan.Confirm().Plan == plan, "Dark-mode plan retains its immutable source and settings snapshot");
        check(OutputNames.Create(source.Path, "convert", "pdf", pdfDark: true) == "source - Dark Mode.pdf" &&
            OutputNames.Create(source.Path, "convert", "pdf", 2, pdfDark: true) == "source - Dark Mode (2).pdf",
            "Dark-mode copies have distinct collision-safe names");
        Reject(() => OutputNames.Create(source.Path, "convert", "pdf", replaceSource: true, pdfDark: true), "Dark-mode naming refuses replacement");
        Reject(() => OutputNames.Create(source.Path, "optimize", "pdf", pdfDark: true), "Dark-mode naming refuses optimizer routing");
        Reject(() => OutputNames.Create(source.Path, "convert", "png", pdfDark: true), "Dark-mode naming refuses non-PDF output");
        Reject(() => OutputNames.Create(source.Path, "convert", "pdf", combinedPdf: true, pdfDark: true), "Dark-mode naming refuses combined-image output");
        Reject(() => (facts with { ChangedPages = 4 }).Validate(), "Dark-mode counts cannot exceed page count");
        Reject(() => (facts with { AlreadyDarkPages = 0 }).Validate(), "Dark-mode counts must cover every page");
        Reject(() => (facts with { Policy = "unknown" }).Validate(), "Dark-mode refuses unknown policy");
        Reject(() => (facts with { RecoveredImages = -1 }).Validate(), "Dark-mode recovery count cannot be negative");
        Reject(() => (facts with { RecoveredImages = 32769 }).Validate(), "Dark-mode recovery count has an object bound");
        Reject(() => new PdfDarkFacts(3, 0, 3, 0, 0, RecoveredImages: 1).Validate(),
            "A recovered image requires a changed page and an output copy");
        Reject(() => (facts with { RecoveredCoordinates = -1 }).Validate(), "Dark-mode coordinate recovery cannot be negative");
        Reject(() => (facts with { RecoveredCoordinates = 4097 }).Validate(), "Dark-mode coordinate recovery is bounded");
        Reject(() => new PdfDarkFacts(3, 0, 3, 0, 0, RecoveredCoordinates: 1).Validate(),
            "Recovered coordinates require a changed page and an output copy");
        Reject(() => (source with { FileBytes = PdfDarkSource.MaximumSourceBytes + 1L }).Validate(), "Dark-mode source has a byte limit");
        Reject(() => (source with { Path = Path.ChangeExtension(source.Path, "png") }).Validate(), "Dark-mode source must be PDF");
        Reject(() => (plan with { Sources = [source, source with { ItemId = Guid.NewGuid() }] }).Confirm(), "Dark-mode batch refuses duplicate paths");
        Reject(() => (plan with { Sources = [source, source with { Path = Path.Combine(scratch, "other.pdf") }] }).Confirm(), "Dark-mode batch refuses duplicate identities");
        Reject(() => (plan with { Sources = ImmutableArray<PdfDarkSource>.Empty }).Confirm(), "Dark-mode batch refuses empty sources");
        var work = new PdfDarkWork(source, Path.Combine(scratch, $".context-suite-{source.ItemId:N}.tmp"));
        work.Validate();
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "Dark-mode work cannot target its source");
        Reject(() => (work with { Source = source with { Facts = new(3, 0, 3, 0, 2) } }).Validate(), "Already-dark PDFs cannot be submitted as conversion work");
        var result = new PdfDarkResult(new(source.ItemId, new string('B', 64), true), source.Sha256, 100, 200, facts, "test-identity");
        result.Validate(work);
        check(result.OutputBytes > source.FileBytes, "Dark-mode conversion permits a larger validated copy");
        Reject(() => (result with { SourceSha256 = new string('C', 64) }).Validate(work), "Dark-mode result is bound to inspected source hash");
        Reject(() => (result with { Facts = facts with { PreservedImages = 0 } }).Validate(work), "Dark-mode result cannot hide retained images");
        Reject(() => (result with { Facts = facts with { RecoveredImages = 1 } }).Validate(work),
            "Dark-mode result cannot change the admitted recovery count");
        Reject(() => (result with { Facts = facts with { RecoveredCoordinates = 1 } }).Validate(work),
            "Dark-mode result cannot change the admitted coordinate recovery count");
        Reject(() => (result with { Validation = result.Validation with { MatchesPlan = false } }).Validate(work), "Unvalidated dark-mode results cannot publish");
        new WorkerCommand(1, Guid.NewGuid(), "pdf-dark-probe", PdfDarkProbe: new(source.ItemId, source.Path)).Validate();
        new WorkerCommand(1, Guid.NewGuid(), "pdf-dark-convert", PdfDarkWork: work).Validate();
        check(true, "Dark-mode worker accepts matched typed probe and conversion payloads");
        Reject(() => new WorkerCommand(1, Guid.NewGuid(), "capabilities", PdfDarkWork: work).Validate(), "Dark-mode payload cannot accompany an unrelated command");
        Reject(() => new WorkerCommand(1, Guid.NewGuid(), "pdf-dark-probe", PdfDarkWork: work).Validate(), "Dark-mode probe cannot carry conversion work");
        Reject(() => new WorkerCommand(1, Guid.NewGuid(), "pdf-dark-convert", PdfDarkWork: work, PdfBytes: [1]).Validate(), "Dark-mode command refuses extra media payloads");

        const string json = "{\"policy\":\"pdf-dark-8\",\"pageCount\":3,\"changedPages\":2,\"alreadyDarkPages\":1,\"recoloredImages\":4,\"preservedImages\":2,\"sourceBytes\":100}";
        Reject(() => PdfDarkProtocol.Read(Frame(json.Replace("pdf-dark-8", "pdf-dark-7"), []), false),
            "Admission facts from the earlier dark-mode policy cannot be reused");
        var inspect = Frame(json, []);
        check(PdfDarkProtocol.Read(inspect, false).Facts == facts, "Native inspection frame retains all admission facts");
        var recovered = json.Replace("\"sourceBytes\":100", "\"sourceBytes\":100,\"recoveredImages\":1");
        check(PdfDarkProtocol.Read(Frame(recovered, []), false).Facts == facts with { RecoveredImages = 1 },
            "Native image recovery survives typed admission without changing older frame interpretation");
        var coordinateRecovery = recovered.Replace("\"recoveredImages\":1", "\"recoveredImages\":1,\"recoveredCoordinates\":15");
        check(PdfDarkProtocol.Read(Frame(coordinateRecovery, []), false).Facts == facts with { RecoveredImages = 1, RecoveredCoordinates = 15 },
            "Both native recovery kinds survive typed admission");
        Reject(() => PdfDarkProtocol.Read(Frame(coordinateRecovery.Replace("\"recoveredCoordinates\":15", "\"recoveredCoordinates\":15,\"recoveredCoordinates\":0"), []), false),
            "Duplicate coordinate recovery fields cannot hide a repair");
        Reject(() => PdfDarkProtocol.Read(Frame(recovered.Replace("\"recoveredImages\":1", "\"recoveredImages\":true"), []), false),
            "Native image recovery count must be an integer");
        Reject(() => PdfDarkProtocol.Read(Frame(recovered.Replace("\"recoveredImages\":1", "\"recoveredImages\":1,\"recoveredImages\":0"), []), false),
            "Duplicate recovery fields cannot hide a repair");
        var converted = Frame(json, "%PDF-1.7\n"u8.ToArray());
        check(PdfDarkProtocol.Read(converted, true).Pdf.Length == 9, "Native conversion frame separates bounded PDF bytes");
        Reject(() => PdfDarkProtocol.Read(converted, false), "Native inspection cannot carry output bytes");
        Reject(() => PdfDarkProtocol.Read(inspect, true), "Changed native conversion requires PDF output");
        Reject(() => PdfDarkProtocol.Read(converted[..^1], true), "Truncated native frame is rejected");
        Reject(() => PdfDarkProtocol.Read(new byte[] { }.Concat(converted).Append((byte)0).ToArray(), true), "Trailing native frame bytes are rejected");
        Reject(() => PdfDarkProtocol.Read(Frame(json.Replace("\"pageCount\":3", "\"pageCount\":true"), []), false), "Native counts must be integers");
        Reject(() => PdfDarkProtocol.Read(Frame(json.Replace("\"pageCount\":3", "\"pageCount\":3,\"pageCount\":3"), []), false), "Duplicate native fields are rejected");
        Reject(() => PdfDarkProtocol.Read(Frame(json.Replace("\"sourceBytes\":100", "\"unexpected\":100"), []), false), "Unknown native fields are rejected");
        var badLength = (byte[])converted.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(badLength.AsSpan(12), uint.MaxValue);
        Reject(() => PdfDarkProtocol.Read(badLength, true), "Native frame length cannot overflow its bound");
        Reject(() => PdfDarkProtocol.Read(Frame(json, "NOT-A-PDF"u8.ToArray()), true), "Native output must start with a PDF header");

        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, message); return; }
            check(false, message);
        }
    }

    private static byte[] Frame(string json, byte[] pdf)
    {
        var encoded = Encoding.UTF8.GetBytes(json);
        var bytes = new byte[16 + encoded.Length + pdf.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31445043);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)encoded.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)pdf.Length);
        encoded.CopyTo(bytes, 16); pdf.CopyTo(bytes, 16 + encoded.Length);
        return bytes;
    }
}
