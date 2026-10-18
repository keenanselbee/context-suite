"""Exercise translucent-panel seams against individually bounded embedded glyphs.

The two PDFs use the same authored TrueType font and text run. Only the panel
seam changes: it lies in the space glyph or cuts through the first A. Requires
pypdf and PyMuPDF, plus a freshly built development PDF-dark host.
"""

import argparse
import io
import json
from pathlib import Path
import struct
import subprocess

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, ContentStream, DecodedStreamObject,
                           DictionaryObject, FloatObject, NameObject,
                           NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = Path(__file__).resolve().parents[2]
scratch = Path(args.scratch).resolve()
engine = Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)

ALPHA = .501968


def dictionary(**values):
    return DictionaryObject({NameObject("/" + key): value for key, value in values.items()})


def numbers(*values):
    return ArrayObject([NumberObject(value) for value in values])


def stream(data):
    result = DecodedStreamObject()
    result.set_data(data)
    return result


def checksum(data):
    padded = data + b"\0" * (-len(data) % 4)
    return sum(struct.unpack(">" + "I" * (len(padded) // 4), padded)) & 0xffffffff


def authored_font():
    # GID 1 is a narrow, triangular A with actual x bounds 10..110/1000 em;
    # GID 2 is an empty space. Both advances are one em in the PDF Widths.
    glyph_a = (struct.pack(">hhhhhHH", 1, 10, 0, 110, 120, 2, 0) +
               b"\x01\x01\x01" + struct.pack(">hhh", 10, 100, -50) +
               struct.pack(">hhh", 0, 0, 120) + b"\0")
    glyf = glyph_a
    loca = struct.pack(">IIII", 0, 0, len(glyph_a), len(glyph_a))
    head = bytearray(54)
    struct.pack_into(">I", head, 0, 0x00010000)
    struct.pack_into(">I", head, 4, 0x00010000)
    struct.pack_into(">I", head, 12, 0x5f0f3cf5)
    struct.pack_into(">H", head, 18, 1000)
    struct.pack_into(">hhhh", head, 36, 0, 0, 110, 120)
    struct.pack_into(">h", head, 50, 1)
    hhea = bytearray(36)
    struct.pack_into(">I", hhea, 0, 0x00010000)
    struct.pack_into(">hhh", hhea, 4, 120, 0, 0)
    struct.pack_into(">Hhhhh", hhea, 10, 1000, 0, 0, 110, 1)
    struct.pack_into(">H", hhea, 34, 3)
    hmtx = struct.pack(">HhHhHh", 1000, 0, 1000, 10, 1000, 0)
    maxp = struct.pack(">IH", 0x00010000, 3) + b"\0" * 26
    ends = [32, 65, 65535]
    starts = ends
    deltas = [(2 - 32) & 65535, (1 - 65) & 65535, 1]
    format4 = (struct.pack(">HHHHHHH", 4, 40, 0, 6, 4, 1, 2) +
               struct.pack(">HHH", *ends) + struct.pack(">H", 0) +
               struct.pack(">HHH", *starts) + struct.pack(">HHH", *deltas) +
               struct.pack(">HHH", 0, 0, 0))
    cmap = struct.pack(">HHHHI", 0, 1, 3, 1, 12) + format4
    tables = {"cmap": cmap, "glyf": glyf, "head": bytes(head),
              "hhea": bytes(hhea), "hmtx": hmtx, "loca": loca, "maxp": maxp}
    count = len(tables)
    output = bytearray(struct.pack(">IHHHH", 0x00010000, count, 64, 2,
                                   count * 16 - 64))
    directory = bytearray()
    payload = bytearray()
    head_offset = None
    for tag, data in sorted(tables.items()):
        offset = 12 + count * 16 + len(payload)
        directory += struct.pack(">4sIII", tag.encode("ascii"), checksum(data),
                                 offset, len(data))
        if tag == "head":
            head_offset = offset
        payload += data + b"\0" * (-len(data) % 4)
    output += directory + payload
    struct.pack_into(">I", output, head_offset + 8,
                     (0xb1b0afba - checksum(output)) & 0xffffffff)
    return bytes(output)


FONT_BYTES = authored_font()


def fixture(seam_crosses_ink):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    font_file = stream(FONT_BYTES)
    font_file[NameObject("/Length1")] = NumberObject(len(FONT_BYTES))
    descriptor = dictionary(
        Type=NameObject("/FontDescriptor"), FontName=NameObject("/AuthoredNarrow"),
        Flags=NumberObject(32), FontBBox=numbers(-10000, -10000, 10000, 10000),
        ItalicAngle=NumberObject(0), Ascent=NumberObject(1000),
        Descent=NumberObject(-1000), CapHeight=NumberObject(1000),
        StemV=NumberObject(80), FontFile2=writer._add_object(font_file))
    font = dictionary(Type=NameObject("/Font"), Subtype=NameObject("/TrueType"),
                      BaseFont=NameObject("/AuthoredNarrow"),
                      Encoding=NameObject("/WinAnsiEncoding"),
                      FirstChar=NumberObject(32), LastChar=NumberObject(65),
                      Widths=numbers(*([1000] * 34)),
                      FontDescriptor=writer._add_object(descriptor))
    alpha = dictionary(Type=NameObject("/ExtGState"), ca=FloatObject(ALPHA),
                       CA=FloatObject(ALPHA))
    page[NameObject("/Resources")] = dictionary(
        Font=dictionary(F1=writer._add_object(font)),
        ExtGState=dictionary(GSf=writer._add_object(alpha)))
    # A A has ink at x100.2..102.2 and x140.2..142.2. The first seam is
    # x125..126, inside the empty space; the moved seam x100.7..101.7
    # intersects the first A's actual ink.
    left_end, right_start = (101.7, 100.7) if seam_crosses_ink else (126, 125)
    content = ("0 0.125 0.251 rg 0 0 420 300 re f\n"
               "1 1 1 rg BT /F1 20 Tf 1 0 0 1 100 150 Tm <412041> Tj ET\n"
               f"q 0 1 0 rg /GSf gs 90 130 {left_end - 90} 35 re f Q\n"
               f"q 0.969 0.588 0.275 rg /GSf gs {right_start} 130 "
               f"{160 - right_start} 35 re f Q\n")
    page[NameObject("/Contents")] = writer._add_object(stream(content.encode("ascii")))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    process = subprocess.run([str(engine), mode], input=source,
                             capture_output=True, timeout=60)
    if process.returncode:
        assert not process.stdout, "Refusal published PDF bytes"
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return (0, json.loads(process.stdout[16:16 + metadata_length]),
            process.stdout[16 + metadata_length:])


def geometry(pdf):
    reader = PdfReader(io.BytesIO(pdf))
    page = reader.pages[0]
    operations = ContentStream(page["/Contents"], reader).operations
    preserved = []
    for operands, operator in operations:
        if operator in (b"rg", b"RG", b"g", b"G", b"k", b"K"):
            continue
        preserved.append((operator, tuple(str(value) for value in operands)))
    document = pymupdf.open(stream=pdf, filetype="pdf")
    drawings = [(d["type"], tuple(round(float(v), 4) for v in d["rect"]),
                 round(float(d["fill_opacity"]), 6)) for d in document[0].get_drawings()]
    text = [("".join(chr(c[0]) for c in span["chars"]),
             tuple((round(float(c[2][0]), 4), round(float(c[2][1]), 4),
                    tuple(round(float(v), 4) for v in c[3]))
                   for c in span["chars"])) for span in document[0].get_texttrace()]
    font_bytes = page["/Resources"]["/Font"]["/F1"]["/FontDescriptor"]["/FontFile2"].get_data()
    return preserved, drawings, text, font_bytes


clear = fixture(False)
(scratch / "clear-seam-source.pdf").write_bytes(clear)
code, inspected, empty = invoke(clear, "--inspect")
assert code == 0 and not empty, (code, inspected)
code, facts, converted = invoke(clear, "--convert")
assert code == 0 and converted and facts == inspected, (code, facts)
assert geometry(clear) == geometry(converted), "Text, path, alpha, or embedded font changed"
code, repeated, empty = invoke(converted, "--convert")
assert code == 0 and not empty and repeated["alreadyDarkPages"] == 1, (code, repeated)
(scratch / "clear-seam-dark.pdf").write_bytes(converted)
print("PASS seam-in-verified-glyph-whitespace")

crossed = fixture(True)
(scratch / "crossed-seam-source.pdf").write_bytes(crossed)
for mode in ("--inspect", "--convert"):
    code, diagnostic, empty = invoke(crossed, mode)
    assert code == 3 and "text crosses overlapping translucent panels" in diagnostic and not empty, \
        (mode, code, diagnostic)
print("PASS seam-crosses-actual-glyph refused")
