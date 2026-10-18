"""Check exact-name DirectWrite glyph bounds for unembedded WinAnsi TrueType.

The PDFs are authored in memory and contain no font program. Requires pypdf,
Windows and a fresh repository-local scratch directory.
"""

import argparse
import hashlib
import io
import json
import os
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


def source_font_has_postscript_name():
    path = pathlib.Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / "times.ttf"
    if not path.is_file() or path.stat().st_size > 32 * 1024 * 1024:
        return False
    data = path.read_bytes()
    if len(data) < 12:
        return False
    count = struct.unpack_from(">H", data, 4)[0]
    if count > 128 or 12 + 16 * count > len(data):
        return False
    for index in range(count):
        tag, _, offset, length = struct.unpack_from(">4sIII", data, 12 + 16 * index)
        if tag != b"name" or offset > len(data) or length > len(data) - offset or length < 6:
            continue
        strings = struct.unpack_from(">H", data, offset + 2)[0]
        storage = struct.unpack_from(">H", data, offset + 4)[0]
        if strings > 1024 or 6 + 12 * strings > length:
            return False
        for item in range(strings):
            platform, _, _, name_id, size, relative = struct.unpack_from(
                ">HHHHHH", data, offset + 6 + 12 * item)
            start = offset + storage + relative
            if name_id != 6 or platform != 3 or size > 512 or start > len(data) or \
                    size > len(data) - start:
                continue
            if data[start:start + size].decode("utf-16-be", errors="replace") == "TimesNewRomanPSMT":
                return True
    return False


def fixture(*, base="/TimesNewRomanPSMT", encoding="/WinAnsiEncoding",
            last_width=333, symbolic=False, descriptor_top=693):
    writer = PdfWriter()
    page = writer.add_blank_page(width=420, height=300)
    descriptor = dictionary(Type=name("/FontDescriptor"), FontName=name(base),
                            Flags=NumberObject(4 if symbolic else 32),
                            FontBBox=array(-568, -216, 2046, descriptor_top),
                            ItalicAngle=NumberObject(0), Ascent=NumberObject(891),
                            Descent=NumberObject(-216), CapHeight=NumberObject(693),
                            StemV=NumberObject(40))
    widths = [1000] * (121 - 32 + 1)
    for character, width in (("(", 333), ("E", 611), ("m", 778),
                             ("p", 500), (")", last_width)):
        widths[ord(character) - 32] = width
    font = dictionary(Type=name("/Font"), Subtype=name("/TrueType"),
                      BaseFont=name(base), Encoding=name(encoding),
                      FirstChar=NumberObject(32), LastChar=NumberObject(121),
                      Widths=array(*widths), FontDescriptor=writer._add_object(descriptor))
    page[name("/Resources")] = dictionary(Font=dictionary(F1=writer._add_object(font)))
    contents = b"1 1 1 rg\nBT /F1 18.024 Tf 1 0 0 1 100 200 Tm <28456d7029> Tj ET\n"
    contents += b"0 0 0 rg\n150 190 15 20 re f\n"
    stream = DecodedStreamObject()
    stream.set_data(contents)
    page[name("/Contents")] = writer._add_object(stream)
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


results = []
if source_font_has_postscript_name():
    source = fixture()
    digest = hashlib.sha256(source).digest()
    code, inspected, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, ("exact Times New Roman", code, inspected)
    code, converted, output = invoke(source, "--convert")
    assert code == 0 and output and converted == inspected, ("exact Times New Roman", code, converted)
    assert hashlib.sha256(source).digest() == digest
    before = PdfReader(io.BytesIO(source)).pages[0]
    after = PdfReader(io.BytesIO(output)).pages[0]
    assert before.mediabox == after.mediabox
    assert before["/Resources"]["/Font"]["/F1"]["/BaseFont"] == \
        after["/Resources"]["/Font"]["/F1"]["/BaseFont"]
    assert "/FontFile2" not in after["/Resources"]["/Font"]["/F1"]["/FontDescriptor"]
    (scratch / "exact-system-font-source.pdf").write_bytes(source)
    (scratch / "exact-system-font-dark.pdf").write_bytes(output)
    results.append({"case": "exact Times New Roman", "converted": True})
    print("PASS exact Times New Roman")
else:
    results.append({"case": "exact Times New Roman", "skipped": "times.ttf with matching PostScript name is absent"})
    print("SKIP exact Times New Roman: matching local font file absent")

for case, source in (
    ("mismatched-declared-width", fixture(last_width=1000)),
    ("descriptor-outside-tolerance", fixture(descriptor_top=690)),
    ("unknown-postscript-name", fixture(base="/AuthoredDefinitelyMissingPSName")),
    ("unsupported-encoding", fixture(encoding="/MacRomanEncoding")),
    ("symbolic-font", fixture(symbolic=True)),
):
    for mode in ("--inspect", "--convert"):
        code, reason, output = invoke(source, mode)
        assert code == 3 and not output, (case, mode, code, reason)
    results.append({"case": case, "refused": True})
    print("PASS", case, "refused")

(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
