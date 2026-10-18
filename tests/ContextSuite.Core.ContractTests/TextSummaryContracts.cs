using System.Text;
using ContextSuite.Core.Analysis;

internal static class TextSummaryContracts
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (content, expected) in new (string Content, long Lines)[]
        {
            ("one", 1), ("one\n", 1), ("one\r", 1), ("one\r\n", 1),
            ("one\ntwo", 2), ("one\rtwo", 2), ("one\r\ntwo", 2),
            ("\r\n\n\r", 3), ("one\r\ntwo\nthree\rfour", 4)
        })
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var report = HeaderAnalyzer.Analyze("sample.txt", bytes, bytes.Length);
            check(report.Facts.Single(fact => fact.Id == "text.lines").Integer == expected &&
                report.Facts.Single(fact => fact.Id == "text.lines-complete").Boolean == true,
                "text summary: complete file counts logical lines for " + Convert.ToHexString(bytes));
        }

        var prefix = "one\r\ntwo\r"u8.ToArray();
        var partial = HeaderAnalyzer.Analyze("sample.txt", prefix, prefix.Length + 10);
        check(partial.Facts.Single(fact => fact.Id == "text.lines").Integer == 2 &&
            partial.Facts.Single(fact => fact.Id == "text.lines-complete").Boolean == false,
            "text summary: partial CR at the byte boundary counts once and remains a lower bound");
        var paired = HeaderAnalyzer.Analyze("sample.txt", "one\r\ntwo\r\nthree"u8, 15);
        check(paired.Facts.Single(fact => fact.Id == "text.lines").Integer == 3,
            "text summary: CRLF is one terminator when both bytes are sampled");

        var utf8 = Encoding.UTF8.GetBytes(new string('a', HeaderAnalyzer.MaximumBytes - 1) + "\u20ac\nmore");
        var cutoff = HeaderAnalyzer.Analyze("sample.txt", utf8.AsSpan(0, HeaderAnalyzer.MaximumBytes), utf8.Length);
        check(cutoff.Facts.Single(fact => fact.Id == "text.lines").Integer == 1 &&
            cutoff.Facts.Single(fact => fact.Id == "text.lines-complete").Boolean == false,
            "text summary: UTF-8 character split at the bounded read is ignored without inventing a line");

        foreach (var encoding in new Encoding[]
        {
            new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true),
            new UTF32Encoding(false, true), new UTF32Encoding(true, true)
        })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("alpha\r\nbeta\n")).ToArray();
            var report = HeaderAnalyzer.Analyze("sample.txt", bytes, bytes.Length);
            check(report.Facts.Single(fact => fact.Id == "text.lines").Integer == 2 &&
                report.Facts.Single(fact => fact.Id == "text.lines-complete").Boolean == true &&
                report.Facts.Any(fact => fact.Id == "text.encoding"),
                "text summary: BOM encoding retains decoded line count for " + encoding.WebName);
            var bomOnly = encoding.GetPreamble();
            check(HeaderAnalyzer.Analyze("sample.txt", bomOnly, bomOnly.Length).Facts
                    .Single(fact => fact.Id == "text.lines").Integer == 0,
                "text summary: BOM alone contains no line for " + encoding.WebName);
        }

        foreach (var bytes in new byte[][] { [], [0, 1, 2, 255], [0x61, 0xff], [0xef, 0xbb, 0xbf, 0xff] })
        {
            var report = HeaderAnalyzer.Analyze("sample.txt", bytes, bytes.Length);
            check(report.Facts.All(fact => !fact.Id.StartsWith("text.", StringComparison.Ordinal)),
                "text summary: empty, binary or malformed bytes do not produce text facts");
        }
    }
}
