using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Images;

internal static class PdfRasterContracts
{
    public static void Run(Action<bool, string> check)
    {
        var inspection = new byte[64];
        Header(inspection, 1, 1);
        Put(inspection, 32, 0); Put(inspection, 36, 1); Put(inspection, 40, 3); Put(inspection, 44, 5);
        BinaryPrimitives.WriteDoubleLittleEndian(inspection.AsSpan(48), 1);
        BinaryPrimitives.WriteDoubleLittleEndian(inspection.AsSpan(56), 2);
        var page = PdfRasterProtocol.ReadInspection(inspection).Pages.Single();
        check(page.Index == 0 && page.Rotation == 1 && page.Width == 3 && page.Height == 5, "PDF raster: geometry uses fixed DPI and preserves declared rotation");
        var bitmap = new byte[32 + 3 * 5 * 4]; Header(bitmap, 2, 0);
        Put(bitmap, 16, 3); Put(bitmap, 20, 5); Put(bitmap, 24, 12);
        bitmap[32] = 200; bitmap[35] = 128;
        check(PdfRasterProtocol.ReadPage(bitmap, page).AsSpan().SequenceEqual(bitmap.AsSpan(32)), "PDF raster: straight BGRA bytes retain alpha without reinterpretation");
        var limit = new byte[32]; Header(limit, 3, 1);
        foreach (var inspectionMode in new[] { true, false })
        {
            try
            {
                if (inspectionMode) PdfRasterProtocol.ReadInspection(limit); else PdfRasterProtocol.ReadPage(limit, page);
                check(false, "PDF raster: size-limit reply cannot become successful data");
            }
            catch (ImageFailureException error)
            { check(error.Failure == ImageFailure.ResourceLimit, "PDF raster: exact size-limit reply maps to resource limit " + inspectionMode); }
            foreach (var offset in new[] { 0, 4, 8, 12, 16, 20, 24, 28 })
            {
                var invalid = limit.ToArray(); Put(invalid, offset, uint.MaxValue);
                Reject(() => { if (inspectionMode) PdfRasterProtocol.ReadInspection(invalid); else PdfRasterProtocol.ReadPage(invalid, page); },
                    "malformed limit reply stays invalid " + inspectionMode + "/" + offset);
            }
            Reject(() => { if (inspectionMode) PdfRasterProtocol.ReadInspection(limit[..31]); else PdfRasterProtocol.ReadPage(limit[..31], page); },
                "truncated size-limit reply " + inspectionMode);
            var extra = new byte[33]; limit.CopyTo(extra, 0); Put(extra, 28, 1);
            Reject(() => { if (inspectionMode) PdfRasterProtocol.ReadInspection(extra); else PdfRasterProtocol.ReadPage(extra, page); },
                "size-limit reply cannot carry pixels or trailing data " + inspectionMode);
        }
        foreach (var offset in new[] { 0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44 })
        {
            var forged = inspection.ToArray(); Put(forged, offset, uint.MaxValue);
            Reject(() => PdfRasterProtocol.ReadInspection(forged), "invalid inventory integer at " + offset);
        }
        foreach (var number in new[] { double.NaN, double.PositiveInfinity, -1, 0, 14401 })
        {
            var forged = inspection.ToArray(); BinaryPrimitives.WriteDoubleLittleEndian(forged.AsSpan(48), number);
            Reject(() => PdfRasterProtocol.ReadInspection(forged), "nonfinite/out-of-policy physical size");
        }
        Reject(() => PdfRasterProtocol.ReadInspection(inspection[..63]), "truncated inventory");
        Reject(() => PdfRasterProtocol.ReadInspection([.. inspection, 0]), "trailing inventory bytes");
        Reject(() => PdfRasterProtocol.ReadPage(bitmap[..^1], page), "truncated page");
        Reject(() => PdfRasterProtocol.ReadPage(bitmap, page with { Index = 1 }), "wrong page index");
        var wrongStride = bitmap.ToArray(); Put(wrongStride, 24, 13);
        Reject(() => PdfRasterProtocol.ReadPage(wrongStride, page), "unreviewed row padding");
        Reject(() => new PdfRasterDocument([page with { Index = 1 }]).Validate(), "missing first page");
        Reject(() => new PdfRasterDocument([page, page]).Validate(), "duplicate page");
        Reject(() => new PdfRasterPage(0, 0, 10000, 10000, 4800, 4800).Validate(), "page exceeds pixel budget");
        Reject(() => new PdfRasterPage(0, 0, 20000, 1, 9600, 0.48).Validate(), "page exceeds encoder dimension budget");
        var source = new PdfRasterSource(Guid.NewGuid(), Path.GetFullPath("fixture.pdf"), new('A', 64), 100, new([page]));
        var settings = new ContextSuite.Core.Settings.BatchSettings("convert", new(ReplaceOriginals: true));
        var plan = PdfPageConversionPlan.Create(Guid.NewGuid(), [source], settings).Confirm();
        check(plan.Plan.Sources.Single() == source, "PDF raster: confirmed page selection retains source facts with overwrite preference");
        Reject(() => PdfPageConversionPlan.Create(Guid.NewGuid(), [source, source], settings), "duplicate PDF source identity");
        Reject(() => PdfPageConversionPlan.Create(Guid.NewGuid(), [source], settings with { Operation = "optimize" }), "wrong operation settings");
        Reject(() => PdfPageConversionPlan.Create(Guid.NewGuid(), [], settings), "empty PDF selection");
        var largePages = Enumerable.Range(0, 33).Select(index => new PdfRasterPage(index, 0, 4000, 4000, 1920, 1920)).ToImmutableArray();
        Reject(() => PdfPageConversionPlan.Create(Guid.NewGuid(), [source with { Document = new(largePages) }], settings), "aggregate pixel budget");
        var manyPages = Enumerable.Range(0, 4096).Select(index => page with { Index = index }).ToImmutableArray();
        Reject(() => PdfPageConversionPlan.Create(Guid.NewGuid(), [source with { Document = new(manyPages) }, source with { ItemId = Guid.NewGuid() }], settings), "aggregate page budget");
        var id = Guid.NewGuid();
        var work = new PdfPageWork(source, 0, id, Path.Combine(Path.GetDirectoryName(source.Path)!, $".context-suite-{id:N}.tmp"));
        new ContextSuite.Core.Transport.WorkerCommand(1, Guid.NewGuid(), "pdf-render-page", PdfPage: work).Validate();
        check(true, "PDF raster: typed page command accepts a distinct output reservation");
        Reject(() => (work with { OutputId = source.ItemId }).Validate(), "source identity cannot identify page output");
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "page output cannot target source");
        Reject(() => (work with { Policy = "other" }).Validate(), "unknown page policy");
        Reject(() => new ContextSuite.Core.Transport.WorkerCommand(1, Guid.NewGuid(), "pdf-render-page", PdfPage: work, PdfFile: new(source.ItemId, source.Path)).Validate(), "contradictory page request");
        Reject(() => new ContextSuite.Core.Transport.WorkerCommand(1, Guid.NewGuid(), "capabilities", PdfPage: work).Validate(), "page payload on unrelated command");
        check(ContextSuite.Core.Operations.OutputNames.Create(source.Path, "convert", "png", pageNumber: 1) == "fixture - Page 001.png" &&
            ContextSuite.Core.Operations.OutputNames.Create(source.Path, "convert", "png", 2, pageNumber: 12) == "fixture - Page 012 (2).png",
            "PDF raster: page numbers remain distinct from collision ordinals");
        Reject(() => ContextSuite.Core.Operations.OutputNames.Create(source.Path, "convert", "png", replaceSource: true, pageNumber: 1), "page naming cannot authorize replacement");
        Reject(() => ContextSuite.Core.Operations.OutputNames.Create(source.Path, "convert", "png", pageNumber: 0), "page numbers start at one");
        var document = source with { Document = new([page, page with { Index = 1 }]) };
        var row = new ContextSuite.Application.FileRow(1, "convert", source.Path, settings, "png");
        row.BeginPdfPages(document);
        var copy = new ContextSuite.Core.Operations.PublicationResult(source.Path, ContextSuite.Core.Operations.PublicationOutcome.CopyCreated, "Copy created.", source.Path + ".png");
        row.ApplyPdfPage(0, copy.ToFileResult());
        row.ApplyResult(new(source.Path, ContextSuite.Core.Operations.OperationState.Failed, "Later page failed.",
            new(source.Path, ContextSuite.Core.Operations.PublicationOutcome.Failed, "Uncommitted failure.", CleanupWarning: true)));
        check(row.SavedPdfPages == 1 && row.Result.Publication?.IsCommitted == true && row.Result.PartialOutput && row.HasOutput,
            "PDF raster: uncommitted later failure cannot hide an earlier saved page");
        var resumedRow = new ContextSuite.Application.FileRow(2, "convert", source.Path, settings, "png");
        resumedRow.ResumePdfFrom(row); resumedRow.BeginPdfPages(document);
        resumedRow.ApplyPdfPage(1, (copy with { OutputPath = source.Path + "-2.png" }).ToFileResult());
        check(resumedRow.SavedPdfPages == 2 && !resumedRow.Result.PartialOutput && resumedRow.Result.State == ContextSuite.Core.Operations.OperationState.Succeeded,
            "PDF raster: carried page receipt and newly completed page form a complete document result");

        void Reject(Action action, string message)
        {
            try { action(); check(false, "PDF raster: " + message); }
            catch (InvalidDataException) { check(true, "PDF raster: " + message); }
        }
    }

    private static void Header(byte[] bytes, uint kind, uint countOrIndex)
    {
        Put(bytes, 0, 0x31525043); Put(bytes, 4, 1); Put(bytes, 8, kind); Put(bytes, 12, countOrIndex); Put(bytes, 28, (uint)bytes.Length - 32);
    }
    private static void Put(byte[] bytes, int offset, uint value) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value); }
}
