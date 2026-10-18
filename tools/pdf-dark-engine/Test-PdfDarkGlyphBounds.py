"""Exercise bounded embedded TrueType glyph bounds with authored font bytes.

No system font or lecture PDF is copied. Requires pypdf and a freshly built
development PDF-dark host. Use a fresh repository-local scratch directory.
"""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile

from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           NameObject, NumberObject, TextStringObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
engine = pathlib.Path(args.engine).resolve(strict=True)
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def name(value):
    return NameObject(value)


def array(*values):
    return ArrayObject([NumberObject(value) for value in values])


def dictionary(**values):
    return DictionaryObject({name("/" + key): value for key, value in values.items()})


def stream(value, **entries):
    result = DecodedStreamObject()
    result.set_data(value)
    result.update(dictionary(**entries))
    return result


def checksum(data):
    padded = data + b"\0" * (-len(data) % 4)
    return sum(struct.unpack(">" + "I" * (len(padded) // 4), padded)) & 0xffffffff


def authored_font(mutation=None):
    # Four glyphs: .notdef (empty), narrow A, empty space, and a composite B.
    # A's simple-glyph control points are (10,0), (110,0), (60,120).
    glyph_a = (struct.pack(">hhhhhHH", 1, 10, 0,
                           20 if mutation == "understated-header" else 110, 120, 2, 0) +
               b"\x01\x01\x01" + struct.pack(">hhh", 10, 100, -50) +
               struct.pack(">hhh", 0, 0, 120) + b"\0")
    glyph_b = struct.pack(">hhhhhHHhh", -1, 10, 0, 110, 120, 3, 1, 0, 0) + b"\0\0"
    if mutation == "composite-cycle":
        glyph_b = struct.pack(">hhhhhHHhh", -1, 10, 0, 110, 120, 3, 3, 0, 0)
    if mutation == "composite-point-attachment":
        glyph_b = struct.pack(">hhhhhHHHH", -1, 10, 0, 110, 120, 1, 1, 0, 0)
    if mutation == "composite-understated-header":
        glyph_b = struct.pack(">hhhhhHHhh", -1, 10, 0, 20, 120, 3, 1, 0, 0)
    if mutation == "composite-bad-child":
        glyph_b = struct.pack(">hhhhhHHhh", -1, 10, 0, 110, 120, 3, 9, 0, 0)
    glyf = glyph_a + glyph_b
    loca = struct.pack(">IIIII", 0, 0, len(glyph_a), len(glyph_a), len(glyf))
    if mutation == "bad-loca":
        loca = struct.pack(">IIIII", 0, 0, len(glyf) + 4, len(glyph_a), len(glyf))
    if mutation == "truncated-glyph":
        loca = struct.pack(">IIIII", 0, 0, 8, 8, len(glyf))
    head = bytearray(54)
    struct.pack_into(">I", head, 0, 0x00010000)
    struct.pack_into(">I", head, 4, 0x00010000)
    struct.pack_into(">I", head, 12, 0x5f0f3cf5)
    struct.pack_into(">H", head, 18, 8 if mutation == "bad-units" else 1000)
    struct.pack_into(">hhhh", head, 36, 0, 0, 110, 120)
    struct.pack_into(">h", head, 50, 1)
    maxp = struct.pack(">IH", 0x00010000, 4) + b"\0" * 26
    ends = [32, 65, 66, 65535]
    starts = ends
    deltas = [(2 - 32) & 65535, (1 - 65) & 65535,
              (3 - 66) & 65535, 1]
    format4 = (struct.pack(">HHHHHHH", 4, 48, 0, 8, 8, 2, 0) +
               struct.pack(">HHHHH", *ends, 0) +
               struct.pack(">HHHH", *starts) +
               struct.pack(">HHHH", *deltas) +
               struct.pack(">HHHH", 0, 0, 0, 0))
    if mutation == "format12":
        groups = ((32, 32, 2), (65, 65, 1), (66, 66, 3))
        format12 = (struct.pack(">HHIII", 12, 0, 16 + 12 * len(groups), 0, len(groups)) +
                    b"".join(struct.pack(">III", *group) for group in groups))
        cmap = struct.pack(">HHHHI", 0, 1, 3, 10, 12) + format12
    else:
        cmap = struct.pack(">HHHHI", 0, 1, 3, 1, 12) + format4
    tables = {"cmap": cmap, "glyf": glyf, "head": bytes(head),
              "loca": loca, "maxp": maxp}
    count = len(tables)
    output = bytearray(struct.pack(">IHHHH", 0x00010000, count, 64, 2, count * 16 - 64))
    directory = bytearray()
    payload = bytearray()
    head_offset = None
    for tag, data in sorted(tables.items()):
        offset = 12 + count * 16 + len(payload)
        directory += struct.pack(">4sIII", tag.encode("ascii"), checksum(data), offset, len(data))
        if tag == "head":
            head_offset = offset
        payload += data + b"\0" * (-len(data) % 4)
    output += directory + payload
    struct.pack_into(">I", output, head_offset + 8, (0xb1b0afba - checksum(output)) & 0xffffffff)
    if mutation == "bad-table-offset":
        # The glyf directory entry begins after cmap's first entry.
        glyf_entry = 12 + 16
        struct.pack_into(">I", output, glyf_entry + 8, len(output) + 100)
    if mutation == "too-many-tables":
        struct.pack_into(">H", output, 4, 129)
    return bytes(output)


def fixture(*, cid=False, cid_stream=False, encoding="/WinAnsiEncoding",
            code=b"A", mutation=None, embedded=True,
            fill_rect=(150, 190, 20, 20)):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    font_bytes = authored_font(mutation)
    font_file = writer._add_object(stream(font_bytes, Length1=NumberObject(len(font_bytes))))
    descriptor = dictionary(Type=name("/FontDescriptor"), FontName=name("/AuthoredNarrow"),
                            Flags=NumberObject(32), FontBBox=array(-10000, -10000, 10000, 10000),
                            ItalicAngle=NumberObject(0), Ascent=NumberObject(1000),
                            Descent=NumberObject(-1000), CapHeight=NumberObject(1000),
                            StemV=NumberObject(80))
    if embedded:
        descriptor[name("/FontFile2")] = font_file
    descriptor_ref = writer._add_object(descriptor)
    if cid:
        cid_info = dictionary(Registry=TextStringObject("Adobe"),
                              Ordering=TextStringObject("Identity"), Supplement=NumberObject(0))
        descendant = dictionary(Type=name("/Font"), Subtype=name("/CIDFontType2"),
                                BaseFont=name("/AuthoredNarrow"), CIDSystemInfo=cid_info,
                                FontDescriptor=descriptor_ref, DW=NumberObject(1000))
        if cid_stream:
            descendant[name("/CIDToGIDMap")] = writer._add_object(stream(b"\x00\x00\x00\x01"))
        else:
            descendant[name("/CIDToGIDMap")] = name("/Identity")
        font = dictionary(Type=name("/Font"), Subtype=name("/Type0"),
                          BaseFont=name("/AuthoredNarrow"), Encoding=name("/Identity-H"),
                          DescendantFonts=ArrayObject([writer._add_object(descendant)]))
    else:
        font = dictionary(Type=name("/Font"), Subtype=name("/TrueType"),
                          BaseFont=name("/AuthoredNarrow"), Encoding=name(encoding),
                          FirstChar=NumberObject(32), LastChar=NumberObject(66),
                          Widths=array(*([1000] * 35)), FontDescriptor=descriptor_ref)
    page[name("/Resources")] = dictionary(Font=dictionary(F1=writer._add_object(font)))
    rectangle = (" ".join(str(value) for value in fill_rect) + " re f\n").encode("ascii")
    content = (b"1 1 1 rg\nBT /F1 20 Tf 1 0 0 1 50 200 Tm <" +
               code.hex().encode("ascii") + b"> Tj ET\n" +
               b"0 0 0 rg\n" + rectangle)
    page[name("/Contents")] = writer._add_object(stream(content))
    data = io.BytesIO()
    writer.write(data)
    return data.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file,
                                 capture_output=True, timeout=45)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return 0, json.loads(process.stdout[16:16 + metadata_length]), process.stdout[16 + metadata_length:]


def accepted(case, source):
    digest = hashlib.sha256(source).digest()
    code, inspection, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, code, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection, (case, code, facts)
    assert hashlib.sha256(source).digest() == digest
    before = PdfReader(io.BytesIO(source)).pages[0]
    after = PdfReader(io.BytesIO(output)).pages[0]
    assert before.mediabox == after.mediabox
    assert before["/Resources"]["/Font"]["/F1"]["/BaseFont"] == \
        after["/Resources"]["/Font"]["/F1"]["/BaseFont"]
    (scratch / f"{case}-source.pdf").write_bytes(source)
    (scratch / f"{case}-dark.pdf").write_bytes(output)
    print("PASS", case)


def refused(case, source):
    for mode in ("--inspect", "--convert"):
        code, detail, output = invoke(source, mode)
        assert code in (3, 4, 5) and not output, (case, mode, code, detail)
    print("PASS", case, "refused")


accepted("narrow-winansi-a", fixture())
accepted("narrow-format12-a", fixture(mutation="format12"))
accepted("empty-winansi-space", fixture(code=b" "))
accepted("narrow-identity-cid", fixture(cid=True, code=b"\x00\x01"))
accepted("narrow-mapped-cid", fixture(cid=True, cid_stream=True, code=b"\x00\x01"))
accepted("composite-xy-offset", fixture(code=b"B"))
refused("composite-header-union", fixture(code=b"B", mutation="composite-understated-header",
                                         fill_rect=(51.5, 200, 1, 3)))
for mutation in ("composite-cycle", "composite-point-attachment", "composite-bad-child"):
    refused(mutation, fixture(code=b"B", mutation=mutation))
refused("understated-header-union",
        fixture(mutation="understated-header", fill_rect=(51.5, 200, 1, 3)))
refused("unsupported-encoding-fallback", fixture(encoding="/MacRomanEncoding"))
refused("unembedded-fallback", fixture(embedded=False))
for mutation in ("bad-loca", "truncated-glyph", "bad-units", "bad-table-offset", "too-many-tables"):
    refused(mutation, fixture(mutation=mutation))

(scratch / "results.json").write_text(json.dumps({"passed": True}, indent=2), encoding="utf-8")
