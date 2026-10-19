"""Verify embedded Type1C bounds using independently authored CFF1 programs.

Requires pypdf, Windows DirectWrite, and a fresh repository-local scratch
directory. No installed font, external font source, or source PDF is copied.
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
                           NameObject, NumberObject)


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


def stream(data, **entries):
    value = DecodedStreamObject()
    value.set_data(data)
    value.update(dictionary(**entries))
    return value


def integer(value):
    # The fixed signed-32 DICT representation also keeps table offsets stable.
    return b"\x1d" + struct.pack(">i", value)


def index(entries):
    if not entries:
        return b"\0\0"
    offsets = [1]
    for entry in entries:
        offsets.append(offsets[-1] + len(entry))
    return (struct.pack(">HB", len(entries), 2) +
            b"".join(struct.pack(">H", offset) for offset in offsets) + b"".join(entries))


def operand(value):
    # Type 2 charstrings use shortint rather than DICT's longint representation.
    return b"\x1c" + struct.pack(">h", value)


def authored_font(mutation=None, charset_format=0):
    # .notdef is empty; glyph 1 is a triangle spanning x=10..110, y=0..120;
    # glyph 2 is empty. All three have the private default 1000-unit advance.
    triangle = (operand(10) + operand(0) + b"\x15" +
                b"".join(operand(value) for value in (100, 0, -50, 120, -50, -120)) +
                b"\x05\x0e")
    charstrings = index([b"\x0e", triangle, b"\x0e"])
    if mutation in ("iso-adobe", "iso-adobe-extended", "iso-adobe-overridden"):
        # Standard SID 57 names X; ISOAdobe maps glyph indices directly to SIDs.
        charstrings = index([b"\x0e"] * 57 + [triangle])
    if mutation == "bad-index-extent":
        charstrings = charstrings[:9] + b"\xff\xff" + charstrings[11:]
    if charset_format == 0:
        charset = b"\0" + struct.pack(">HH", 391, 392)
    else:
        charset = bytes([charset_format]) + struct.pack(">H", 391)
        charset += (1).to_bytes(charset_format, "big")
    strings = [b"AuthoredNarrow", b"AuthoredEmpty"]
    if mutation == "duplicate-name":
        strings[1] = strings[0]
    if mutation == "notdef-alias":
        strings[0] = b".notdef"
    if mutation == "missing-string":
        charset = b"\0" + struct.pack(">HH", 393, 392)
    if mutation == "standard-sid":
        charset = b"\0" + struct.pack(">HH", 1, 392)
    prefix = b"\x01\x00\x04\x04" + index([b"AuthoredNarrow"])
    suffix = index(strings) + index([])
    private = integer(1000) + b"\x14"
    optional = b""
    if mutation in ("explicit-default-matrix", "unsupported-matrix"):
        scale = b"\x1e\x0a\x00" + (b"\x2f" if mutation == "unsupported-matrix" else b"\x1f")
        optional = scale + integer(0) + integer(0) + scale + integer(0) + integer(0) + b"\x0c\x07"
    if mutation == "stroke-paint":
        optional = integer(2) + b"\x0c\x05"
    if mutation == "embedded-postscript":
        optional = integer(391) + b"\x0c\x15"

    def top(charset_offset, charstrings_offset, private_offset):
        return (b"".join(integer(value) for value in (-10000, -10000, 10000, 10000)) + b"\x05" +
                integer(charset_offset) + b"\x0f" + integer(charstrings_offset) + b"\x11" +
                integer(len(private)) + integer(private_offset) + b"\x12" + optional)

    charset_offset = len(prefix) + len(index([top(0, 0, 0)])) + len(suffix)
    charstrings_offset = charset_offset + len(charset)
    private_offset = charstrings_offset + len(charstrings)
    declared_charset = 0 if mutation and mutation.startswith("iso-adobe") else charset_offset
    return (prefix + index([top(declared_charset, charstrings_offset, private_offset)]) +
            suffix + charset + charstrings + private)


def fixture(*, mutation=None, charset_format=0, glyph=b"A", mismatch=False,
            fill_rect=(150, 190, 20, 20)):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    program = authored_font(mutation, charset_format)
    if mutation == "decoded-font-limit":
        program += bytes(8 * 1024 * 1024 + 1 - len(program))
    font_program = stream(program, Subtype=name("/Type1C"))
    if mutation == "decoded-font-limit":
        font_program = font_program.flate_encode()
    font_file = writer._add_object(font_program)
    descriptor = dictionary(Type=name("/FontDescriptor"), FontName=name("/AuthoredNarrow"),
                            Flags=NumberObject(32), FontBBox=array(-10000, -10000, 10000, 10000),
                            ItalicAngle=NumberObject(0), Ascent=NumberObject(1000),
                            Descent=NumberObject(-1000), CapHeight=NumberObject(1000),
                            StemV=NumberObject(80), FontFile3=font_file)
    differences = ArrayObject([NumberObject(65), name("/Missing" if mismatch else "/AuthoredNarrow"),
                               name("/AuthoredEmpty")])
    encoding = dictionary(Differences=differences)
    first, last = 65, 66
    if mutation and mutation.startswith("iso-adobe"):
        encoding = name("/MacRomanEncoding")
        first, last = 32, 255
        if mutation == "iso-adobe-overridden":
            encoding = dictionary(BaseEncoding=name("/MacRomanEncoding"),
                                  Differences=ArrayObject([NumberObject(88), name("/Missing")]))
    font = dictionary(Type=name("/Font"), Subtype=name("/Type1"), BaseFont=name("/AuthoredNarrow"),
                      Encoding=encoding, FirstChar=NumberObject(first), LastChar=NumberObject(last),
                      Widths=array(*([1000] * (last - first + 1))),
                      FontDescriptor=writer._add_object(descriptor))
    page[name("/Resources")] = dictionary(Font=dictionary(F1=writer._add_object(font)))
    rectangle = (" ".join(str(value) for value in fill_rect) + " re f\n").encode("ascii")
    content = (b"1 1 1 rg\nBT /F1 20 Tf 1 0 0 1 50 200 Tm <" + glyph.hex().encode("ascii") +
               b"> Tj ET\n0 0 0 rg\n" + rectangle)
    page[name("/Contents")] = writer._add_object(stream(content))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        process = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=45)
    if process.returncode:
        assert not process.stdout
        return process.returncode, process.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", process.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(process.stdout) == 16 + metadata_length + pdf_length
    return (0, json.loads(process.stdout[16:16 + metadata_length]),
            process.stdout[16 + metadata_length:])


results = []


def accepted(case, source):
    digest = hashlib.sha256(source).digest()
    code, inspection, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, code, inspection)
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts == inspection, (case, code, facts)
    assert facts["changedPages"] == 1
    assert hashlib.sha256(source).digest() == digest
    before = PdfReader(io.BytesIO(source)).pages[0]
    after = PdfReader(io.BytesIO(output)).pages[0]
    assert before.mediabox == after.mediabox
    original_font = before["/Resources"]["/Font"]["/F1"]
    converted_font = after["/Resources"]["/Font"]["/F1"]
    assert original_font["/Encoding"] == converted_font["/Encoding"]
    assert original_font["/FontDescriptor"]["/FontFile3"].get_data() == \
        converted_font["/FontDescriptor"]["/FontFile3"].get_data()
    code, repeat, repeated = invoke(output, "--convert")
    assert code == 0 and repeat["changedPages"] == 0 and repeat["alreadyDarkPages"] == 1 and not repeated, \
        (case, code, repeat)
    (scratch / f"{case}-source.pdf").write_bytes(source)
    (scratch / f"{case}-dark.pdf").write_bytes(output)
    results.append({"case": case, "converted": True, "repeatNoOp": True})
    print("PASS", case)


def refused(case, source, expected_detail=None):
    for mode in ("--inspect", "--convert"):
        code, detail, output = invoke(source, mode)
        assert code in (3, 4, 5) and not output, (case, mode, code, detail)
        if expected_detail is not None:
            assert expected_detail in detail, (case, mode, code, detail)
    results.append({"case": case, "refused": True})
    print("PASS", case, "refused")


for format_number in (0, 1, 2):
    accepted(f"custom-charset-format-{format_number}", fixture(charset_format=format_number))
accepted("explicit-default-matrix", fixture(mutation="explicit-default-matrix"))
accepted("empty-glyph", fixture(glyph=b"B"))
accepted("positive-y-no-lower-overlap", fixture(fill_rect=(50, 197, 3, 1)))
accepted("iso-adobe-macroman-x", fixture(mutation="iso-adobe", glyph=b"X"))
refused("iso-adobe-unknown-extended-fallback", fixture(mutation="iso-adobe-extended", glyph=b"\x80"))
refused("iso-adobe-differences-override-fallback", fixture(mutation="iso-adobe-overridden", glyph=b"X"))
refused("encoding-name-mismatch-fallback", fixture(mismatch=True))
refused("actual-glyph-overlap", fixture(fill_rect=(50.5, 200.5, 2, 1)))
for mutation in ("bad-index-extent", "duplicate-name", "notdef-alias", "missing-string",
                 "standard-sid", "unsupported-matrix", "stroke-paint", "embedded-postscript"):
    refused(mutation, fixture(mutation=mutation))
oversized_font = fixture(mutation="decoded-font-limit")
assert len(oversized_font) < 32768, "Oversized decoded font should have a small compressed source"
refused("compressed-decoded-font-limit", oversized_font, "glyph-bound budget")

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine),
    "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(),
    "reproduction": ["python", str(pathlib.Path(__file__).resolve()), "--engine", str(engine),
                     "--scratch", "<fresh repository-local scratch directory>"],
    "cases": results,
}, indent=2), encoding="utf-8")
