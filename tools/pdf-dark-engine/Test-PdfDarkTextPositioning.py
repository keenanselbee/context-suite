"""Check line-relative text positioning after unknown Standard 14 advances.

Fixtures are independently authored, have no embedded font program or /Widths,
and stay in a fresh repository-local scratch directory. Requires pypdf and
PyMuPDF plus a selected development PDF-dark host.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject


REPOSITORY = Path(__file__).resolve().parents[2]
PAGE_HEIGHT = 270
FONT_SIZE = 18


def fixture(operators, positions, win_ansi=False, panels=True):
    writer = PdfWriter()
    page = writer.add_blank_page(width=360, height=PAGE_HEIGHT)
    font = DictionaryObject({
        NameObject("/Type"): NameObject("/Font"),
        NameObject("/Subtype"): NameObject("/Type1"),
        NameObject("/BaseFont"): NameObject("/Helvetica"),
    })
    if win_ansi:
        font[NameObject("/Encoding")] = NameObject("/WinAnsiEncoding")
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/Font"): DictionaryObject({NameObject("/F1"): writer._add_object(font)}),
    })
    content = "1 1 1 rg 0 0 360 270 re f\n"
    for x, y in positions if panels else []:
        # Keep each glyph on its own shifted dark panel. A stale text advance
        # cannot be treated as the next panel's exact starting position.
        content += f"0.07 0.09 0.12 rg {x - 8} {y - 8} 38 32 re f\n"
    foreground = "1 1 1" if panels else "0 0 0"
    content += f"{foreground} rg BT /F1 {FONT_SIZE} Tf 1 0 0 1 {positions[0][0]} {positions[0][1]} Tm\n"
    content = content.encode("ascii")
    content += (operators if isinstance(operators, bytes) else operators.encode("ascii")) + b"\nET\n"
    stream = DecodedStreamObject()
    stream.set_data(content)
    page[NameObject("/Contents")] = writer._add_object(stream)
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(engine, source, mode):
    result = subprocess.run([str(engine), mode], input=source, capture_output=True, timeout=90)
    if result.returncode:
        assert not result.stdout, (mode, "Refusal published PDF bytes")
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    assert len(result.stdout) >= 16, mode
    magic, version, facts_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1) and len(result.stdout) == 16 + facts_size + pdf_size, mode
    facts = json.loads(result.stdout[16:16 + facts_size])
    return 0, facts, result.stdout[16 + facts_size:]


def glyph_geometry(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        glyphs = [(span["font"], span["size"], char[0], char[2], char[3])
                  for span in document[0].get_texttrace() for char in span["chars"]]
    return glyphs


def check_positions(glyphs, positions):
    assert [item[2] for item in glyphs] == list(map(ord, "ABC")), glyphs
    assert all(item[0] == "Helvetica" and abs(item[1] - FONT_SIZE) < .01 for item in glyphs), glyphs
    for item, (x, y) in zip(glyphs, positions):
        origin = item[3]
        assert abs(origin[0] - x) < .01 and abs(origin[1] - (PAGE_HEIGHT - y)) < .01, (item, x, y)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", required=True)
    parser.add_argument("--scratch", required=True)
    args = parser.parse_args()
    engine = Path(args.engine).resolve(strict=True)
    scratch = Path(args.scratch).resolve()
    if not engine.is_file() or scratch.exists() or not scratch.is_relative_to(REPOSITORY / ".codex-temp"):
        raise ValueError("Use an existing engine and a fresh repository-local scratch directory")
    scratch.mkdir(parents=True)

    shifted = [(60, 210), (130, 160), (205, 110)]
    stacked = [(60, 210), (60, 165), (60, 120)]
    positives = {
        "td-chain": ("(A) Tj 70 -50 Td (B) Tj 75 -50 Td (C) Tj", shifted),
        "td-leading": ("(A) Tj 70 -50 TD (B) Tj 75 -50 TD (C) Tj", shifted),
        "next-line": ("45 TL (A) Tj T* (B) Tj T* (C) Tj", stacked),
        "single-quote": ("45 TL (A) Tj (B) ' (C) '", stacked),
        "double-quote": ('45 TL (A) Tj 0 0 (B) " 0 0 (C) "', stacked),
    }
    negatives = {
        "consecutive-show": ("(A) Tj (B) Tj 70 -50 Td (C) Tj", shifted),
        "negative-spacing": ("(A) Tj 70 -50 Td -2 Tc (B) Tj 75 -50 Td (C) Tj", shifted),
    }
    rows = []
    for name, (operators, positions) in positives.items():
        source = fixture(operators, positions)
        (scratch / f"{name}-source.pdf").write_bytes(source)
        digest = hashlib.sha256(source).digest()
        before = glyph_geometry(source)
        check_positions(before, positions)
        inspect_code, inspection, empty = invoke(engine, source, "--inspect")
        code, facts, output = invoke(engine, source, "--convert")
        assert inspect_code == code == 0 and inspection == facts and not empty and output, (name, inspection, facts)
        after = glyph_geometry(output)
        assert after == before, (name, before, after)
        (scratch / f"{name}-dark.pdf").write_bytes(output)
        repeat_code, repeat_facts, repeat_output = invoke(engine, output, "--convert")
        assert repeat_code == 0 and repeat_facts["changedPages"] == 0 and not repeat_output, (name, repeat_facts)
        assert hashlib.sha256(source).digest() == digest, name
        rows.append({"case": name, "converted": True, "glyphsAndPositionsPreserved": True,
                     "repeatNoOp": True, "sourcePreserved": True})
        print("PASS", name, flush=True)

    for name, (operators, positions) in negatives.items():
        source = fixture(operators, positions)
        (scratch / f"{name}-source.pdf").write_bytes(source)
        inspect_code, inspect_reason, empty = invoke(engine, source, "--inspect")
        code, reason, output = invoke(engine, source, "--convert")
        assert inspect_code == code == 3 and not empty and not output, (name, inspect_reason, reason)
        assert "contrast" in reason.lower(), (name, reason)
        rows.append({"case": name, "refused": True, "exitCode": code, "reason": reason})
        print("PASS", name, "refused", flush=True)

    # These are raw PDF literal bytes, not UTF-8 text, Unicode PDF strings, or
    # octal escapes. Comparing decoded unsigned stream bytes with signed chars
    # previously rejected an otherwise correct converted stream at the first e9.
    name = "raw-high-bit-winansi-literals"
    literals = (b"(Caf\xe9 cr\xe8me pi\xf1ata)",)
    source = fixture(literals[0] + b" Tj", [(60, 210)], win_ansi=True, panels=False)
    digest = hashlib.sha256(source).digest()
    (scratch / f"{name}-source.pdf").write_bytes(source)
    before = glyph_geometry(source)
    assert [item[2] for item in before] == list(map(ord, "Caf\u00e9 cr\u00e8me pi\u00f1ata")), before
    assert all(glyph[0] == "Helvetica" and abs(glyph[1] - FONT_SIZE) < .01 for glyph in before), before
    assert abs(before[0][3][0] - 60) < .01 and abs(before[0][3][1] - (PAGE_HEIGHT - 210)) < .01, before
    inspect_code, inspection, empty = invoke(engine, source, "--inspect")
    code, facts, output = invoke(engine, source, "--convert")
    assert inspect_code == code == 0 and inspection == facts and not empty and output, \
        (name, inspect_code, inspection, code, facts)
    assert glyph_geometry(output) == before, name
    before_page = PdfReader(io.BytesIO(source)).pages[0]
    after_page = PdfReader(io.BytesIO(output)).pages[0]
    assert before_page.mediabox == after_page.mediabox
    before_content, after_content = before_page.get_contents().get_data(), after_page.get_contents().get_data()
    for literal in literals:
        assert before_content.count(literal) == after_content.count(literal) == 1, (name, literal)
    assert bytes(value for value in before_content if value >= 128) == \
        bytes(value for value in after_content if value >= 128) == b"\xe9\xe8\xf1", name
    assert before_page["/Resources"]["/Font"]["/F1"]["/Encoding"] == \
        after_page["/Resources"]["/Font"]["/F1"]["/Encoding"] == "/WinAnsiEncoding"
    for mode in ("--inspect", "--convert"):
        repeat_code, repeated, duplicate = invoke(engine, output, mode)
        assert repeat_code == 0 and repeated["changedPages"] == 0 and \
            repeated["alreadyDarkPages"] == 1 and not duplicate, (name, mode, repeat_code, repeated)
    assert hashlib.sha256(source).digest() == digest, name
    (scratch / f"{name}-dark.pdf").write_bytes(output)
    rows.append({"case": name, "converted": True, "glyphsAndPositionsPreserved": True,
                 "rawLiteralBytesPreserved": True, "repeatNoOp": True, "sourcePreserved": True})
    print("PASS", name, flush=True)

    (scratch / "results.json").write_text(json.dumps({
        "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
        "cases": rows,
    }, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {len(rows)} text-positioning cases")


if __name__ == "__main__":
    main()
