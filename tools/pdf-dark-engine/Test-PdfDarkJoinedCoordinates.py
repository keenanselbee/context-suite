"""Verify page-context coordinate recovery across content streams and aliases."""

import argparse
import importlib.util
import io
import json
import pathlib
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DecodedStreamObject, DictionaryObject, NameObject, NumberObject


def fixture(chunks, pages=None, distinct_newline=False):
    writer = PdfWriter()
    streams = []
    for chunk in chunks:
        stream = DecodedStreamObject()
        stream.set_data(chunk)
        streams.append(writer._add_object(stream.flate_encode()))
    font_dictionary = DictionaryObject({NameObject("/Type"): NameObject("/Font"),
        NameObject("/Subtype"): NameObject("/Type1"), NameObject("/BaseFont"): NameObject("/Helvetica")})
    if distinct_newline:
        font_dictionary[NameObject("/Encoding")] = DictionaryObject({NameObject("/Type"): NameObject("/Encoding"),
            NameObject("/BaseEncoding"): NameObject("/WinAnsiEncoding"),
            NameObject("/Differences"): ArrayObject([NumberObject(10), NameObject("/A")])})
    font = writer._add_object(font_dictionary)
    for indices in pages or [range(len(chunks))]:
        page = writer.add_blank_page(160, 120)
        page[NameObject("/Contents")] = ArrayObject([streams[index] for index in indices])
        page[NameObject("/Resources")] = DictionaryObject({NameObject("/Font"):
            DictionaryObject({NameObject("/F1"): font})})
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", required=True)
    parser.add_argument("--renderer", required=True, help="Independent native PDFium renderer")
    parser.add_argument("--scratch", required=True)
    args = parser.parse_args()
    repository = pathlib.Path(__file__).resolve().parents[2]
    scratch = pathlib.Path(args.scratch).resolve()
    if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
        raise ValueError("Use a fresh repository-local scratch directory")
    scratch.mkdir(parents=True)
    engine = pathlib.Path(args.engine).resolve(strict=True)
    renderer = pathlib.Path(args.renderer).resolve(strict=True)
    spec = importlib.util.spec_from_file_location("numeric", pathlib.Path(__file__).with_name("Test-PdfDarkNumericInterpretation.py"))
    numeric = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(numeric)
    results = []

    def invoke(data, label, mode="--convert"):
        path = scratch / (label + ".pdf")
        path.write_bytes(data)
        with path.open("rb") as source:
            reply = subprocess.run([str(engine), mode], stdin=source, capture_output=True, timeout=90)
        assert path.read_bytes() == data
        if reply.returncode:
            assert not reply.stdout
            return reply.returncode, reply.stderr.decode(errors="replace"), b""
        magic, version, count, size = struct.unpack_from("<4I", reply.stdout)
        assert (magic, version) == (0x31445043, 1) and len(reply.stdout) == 16 + count + size
        return 0, json.loads(reply.stdout[16:16 + count]), reply.stdout[16 + count:]

    def same_render(left, right):
        for render in (lambda data: numeric.render_pdfium(renderer, data), numeric.render_mupdf):
            a, a_error = render(left)
            b, b_error = render(right)
            assert not a_error and not b_error and numeric.difference(a, b) == 0
        with pymupdf.open(stream=left, filetype="pdf") as a, pymupdf.open(stream=right, filetype="pdf") as b:
            assert len(a) == len(b)
            for ap, bp in zip(a, b):
                assert ap.get_pixmap(matrix=pymupdf.Matrix(2, 2)).samples == bp.get_pixmap(matrix=pymupdf.Matrix(2, 2)).samples
                assert ap.get_text() == bp.get_text()

    def positive(label, chunks, count, pages=None):
        source = fixture(chunks, pages)
        normalized = fixture([chunk.replace(b"0.00-60", b"0.00") for chunk in chunks], pages)
        same_render(source, normalized)
        code, facts, output = invoke(source, label)
        assert code == 0 and output and facts["recoveredCoordinates"] == count, (label, facts)
        inspected, inspect_facts, absent = invoke(source, label + "-inspect", "--inspect")
        assert inspected == 0 and inspect_facts == facts and not absent
        reference_code, _, reference = invoke(normalized, label + "-reference")
        assert reference_code == 0 and reference
        same_render(output, reference)
        with pymupdf.open(stream=source, filetype="pdf") as before, pymupdf.open(stream=output, filetype="pdf") as after:
            def glyphs(page):
                return [(span["font"], span["size"], span["chars"]) for span in page.get_texttrace()]
            assert [glyphs(page) for page in before] == [glyphs(page) for page in after], label
        assert [p.extract_text() for p in PdfReader(io.BytesIO(source)).pages] == [p.extract_text() for p in PdfReader(io.BytesIO(output)).pages]
        repeated, repeat_facts, repeat_output = invoke(output, label + "-repeat")
        assert repeated == 0 and repeat_facts["changedPages"] == 0 and repeat_facts["recoveredCoordinates"] == 0 and not repeat_output
        results.append({"case": label, "facts": facts, "independentReaders": ["PDFium", "MuPDF"]})

    def negative(label, chunks, pages=None, expected=(3, 4), distinct_newline=False):
        data = fixture(chunks, pages, distinct_newline)
        code, diagnostic, output = invoke(data, label)
        assert code in expected and not output, (label, code, diagnostic)
        inspected, _, absent = invoke(data, label + "-inspect", "--inspect")
        assert inspected == code and not absent
        results.append({"case": label, "exit": code, "diagnostic": diagnostic})

    for context in numeric.DRAWING_CONTEXTS:
        content = numeric.content(context, "0.00-60")
        start = content.index(b"0.00-60")
        for label, cut in (("before-token", start), ("after-token", start + 7),
                           ("before-operator", content.index((" " + context + " ").encode(), start) + 1)):
            positive(context + "-" + label, [content[:cut], content[cut:]], 1)
        for label, cut in (("before-minus", start + 4), ("before-digits", start + 5), ("between-digits", start + 6)):
            negative(context + "-" + label, [content[:cut], content[cut:]])

    base = b"1 1 1 rg 0 0 160 120 re f 0 0 0 rg BT /F1 12 Tf 10 50 Td "
    # Complete text tokens stay text; arrays and hex strings may cross a boundary.
    # Literal strings crossing a boundary have reader-dependent newline semantics.
    for label, chunks in {
        "complete-string": [base, b"(0.00-60) Tj ET"],
        "split-comment": [base + b"(Text) Tj ET % 0.00-", b"% 60\n"],
        "split-name": [base + b"(Text) Tj ET /0.00-", b"MP\n"],
        "split-text-array": [base + b"[(Text)", b" 0 (0.00-60)] TJ ET"],
        "split-hex-string": [base + b"<302E30302D", b"3630> Tj ET % 0.00-60\n"],
    }.items():
        # Keep candidate-like string values verbatim in the reference.
        if label in ("complete-string", "split-text-array"):
            data = fixture(chunks)
            code, facts, output = invoke(data, label)
            assert code == 0 and output and facts["recoveredCoordinates"] == 0
            with pymupdf.open(stream=data, filetype="pdf") as a, pymupdf.open(stream=output, filetype="pdf") as b:
                assert a[0].get_text() == b[0].get_text()
                assert [s["chars"] for s in a[0].get_texttrace()] == [s["chars"] for s in b[0].get_texttrace()]
            results.append({"case": label, "facts": facts})
        else:
            positive(label, chunks, 0)

    negative("split-literal-string", [base + b"(0.00-", b"60) Tj ET"])
    negative("split-literal-in-array", [base + b"[(0.00-", b"60)] TJ ET"])
    negative("shared-number-inside-literal", [b"0.00-60", base + b"(A", b"B) Tj ET",
             b"1 1 1 rg 0 0 160 120 re f 0 0 0 RG 3 w ", b"20 m 110 90 l S"],
             [[3, 0, 4], [1, 0, 2]])
    for label, chunks in {
        "split-literal-no-coordinate": [base + b"(A", b"B) Tj ET"],
        "split-literal-visible-glyph": [base + b"(A", b"0.00-60", b"B) Tj ET"],
    }.items():
        source = fixture(chunks, distinct_newline=True)
        spaces = fixture([b" ".join(chunks)], distinct_newline=True)
        newlines = fixture([b"\n".join(chunks)], distinct_newline=True)
        same_render(source, spaces)
        for render in (lambda data: numeric.render_pdfium(renderer, data), numeric.render_mupdf):
            source_pixels, error = render(source)
            newline_pixels, newline_error = render(newlines)
            assert not error and not newline_error and numeric.difference(source_pixels, newline_pixels) > 0
        negative(label, chunks, distinct_newline=True)

    prefix = b"1 1 1 rg 0 0 160 120 re f 0 0 0 RG 3 w 0.00-60 "
    positive("shared-stream-two-pages", [prefix, b"20 m 110 90 l S", b"40 m 110 90 l S"], 2,
             [[0, 1], [0, 2]])
    negative("shared-stream-invalid-second-page", [prefix, b"20 m 110 90 l S", b"20 30 m"], [[0, 1], [0, 2]])
    negative("split-array-number", [base + b"[(Text) 0.00-60", b"] TJ ET"])
    negative("split-wrong-arity", [prefix, b"20 30 m"])
    negative("split-unknown-operator", [prefix, b"20 xyz"])
    negative("joined-content-limit", [prefix, b" " * (16 * 1024 * 1024), b"20 m"], expected=(5,))
    negative("recovery-count-limit", [b"0.00-60 20 m\n" * 4097], expected=(5,))
    (scratch / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {len(results)} joined coordinate cases with independent PDFium and MuPDF renders")


if __name__ == "__main__":
    main()
