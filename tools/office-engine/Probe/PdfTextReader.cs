using System.Runtime.InteropServices;

// Independent PDFium text oracle for these tiny generated PDFs; not a customer parser.
internal static class PdfTextReader
{
    public static void Initialize(string library)
    {
        var handle = NativeLibrary.Load(library);
        NativeLibrary.SetDllImportResolver(typeof(PdfTextReader).Assembly, (name, _, _) => name == "pdfium-evaluation" ? handle : IntPtr.Zero);
    }
    public static string[] Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException("PDF text input limit.");
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        FPDF_InitLibrary();
        try
        {
            var document = FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)bytes.Length, IntPtr.Zero);
            if (document == IntPtr.Zero) throw new InvalidDataException("PDF text document failed to load.");
            try
            {
                var count = FPDF_GetPageCount(document);
                if (count is < 1 or > 16) throw new InvalidDataException("PDF text page limit.");
                var result = new List<string>();
                for (var index = 0; index < count; index++)
                {
                    var page = FPDF_LoadPage(document, index);
                    if (page == IntPtr.Zero) throw new InvalidDataException("PDF text page failed to load.");
                    try
                    {
                        var text = FPDFText_LoadPage(page);
                        if (text == IntPtr.Zero) throw new InvalidDataException("PDF text extraction failed.");
                        try
                        {
                            var length = FPDFText_CountChars(text);
                            if (length is < 0 or > 1000000) throw new InvalidDataException("PDF text character limit.");
                            var buffer = new ushort[length + 1];
                            var written = FPDFText_GetText(text, 0, length, buffer);
                            if (written < 1 || written > buffer.Length || buffer[written - 1] != 0) throw new InvalidDataException("Invalid PDF text response.");
                            result.Add(new string(buffer.Take(written - 1).Select(value => (char)value).ToArray()));
                        }
                        finally { FPDFText_ClosePage(text); }
                    }
                    finally { FPDF_ClosePage(page); }
                }
                return result.ToArray();
            }
            finally { FPDF_CloseDocument(document); }
        }
        finally { FPDF_DestroyLibrary(); pinned.Free(); }
    }
    [DllImport("pdfium-evaluation")] private static extern void FPDF_InitLibrary();
    [DllImport("pdfium-evaluation")] private static extern void FPDF_DestroyLibrary();
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint length, IntPtr password);
    [DllImport("pdfium-evaluation")] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport("pdfium-evaluation")] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport("pdfium-evaluation")] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern void FPDFText_ClosePage(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_CountChars(IntPtr page);
    [DllImport("pdfium-evaluation")] private static extern int FPDFText_GetText(IntPtr page, int start, int count, [Out] ushort[] text);
}
