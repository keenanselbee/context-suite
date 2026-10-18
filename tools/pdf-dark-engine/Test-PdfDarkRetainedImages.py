"""Check retained photos and masked logos against an isolated PDF dark host."""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess
import tempfile
import zlib

import pymupdf
from PIL import Image, ImageCms
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (
    ArrayObject,
    ByteStringObject,
    DecodedStreamObject,
    DictionaryObject,
    FloatObject,
    NameObject,
    NumberObject,
)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
parser.add_argument("--with-expanded", action="store_true",
                    help="Also require the caller-integrated RGB expansion path")
parser.add_argument("--with-overlay", action="store_true",
                    help="Also require preserved-panel overlaid-text contrast")
parser.add_argument("--with-soft-panels", action="store_true",
                    help="Also require empty soft-frame recoloring and repeat stability")
parser.add_argument("--with-islands", action="store_true",
                    help="Also require exact white-island preservation for complex masked art")
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if not scratch.is_relative_to(repository / ".codex-temp") or scratch.exists():
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def stream(writer, data, entries, compress=False):
    result = DecodedStreamObject()
    result.set_data(zlib.compress(data) if compress else data)
    result.update({NameObject(key): value for key, value in entries.items()})
    if compress:
        result[NameObject("/Filter")] = NameObject("/FlateDecode")
    return writer._add_object(result)


def rgb_profile(writer, malformed=False, bad_white_mapping=False):
    profile = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes())
    if malformed:
        profile[36:40] = b"bad!"
    if bad_white_mapping:
        count = int.from_bytes(profile[128:132], "big")
        for index in range(count):
            at = 132 + index * 12
            if profile[at:at + 4] == b"rXYZ":
                offset = int.from_bytes(profile[at + 4:at + 8], "big")
                profile[offset + 12:offset + 16] = b"\x00\x00\x00\x00"
                break
    ref = stream(writer, bytes(profile), {"/N": NumberObject(3)}, compress=True)
    return ArrayObject([NameObject("/ICCBased"), ref])


def photo(writer, malformed_profile=False, mask_opacity=None):
    image = Image.new("RGB", (160, 90))
    for y in range(90):
        for x in range(160):
            image.putpixel((x, y), (min(255, 70 + x), min(255, 90 + y), 150))
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=88)
    entries = {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(160),
        "/Height": NumberObject(90),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": rgb_profile(writer, malformed_profile),
        "/Filter": NameObject("/DCTDecode"),
    }
    if mask_opacity is not None:
        alpha = bytes([mask_opacity]) * (160 * 90)
        entries["/SMask"] = stream(writer, alpha, {
            "/Type": NameObject("/XObject"),
            "/Subtype": NameObject("/Image"),
            "/Width": NumberObject(160),
            "/Height": NumberObject(90),
            "/BitsPerComponent": NumberObject(8),
            "/ColorSpace": NameObject("/DeviceGray"),
        }, compress=True)
    return stream(writer, encoded.getvalue(), entries)


def logo(writer, variant, bad_mask=False, bad_index=False,
         bad_white_mapping=False, neutral_decode=False, bad_predictor=False,
         plain_lookup=False, black_fringe_alpha=0):
    width = height = 12
    alpha = bytearray(255 if 2 <= x < 10 and 2 <= y < 10 else 0
                      for y in range(height) for x in range(width))
    if variant == "palette":
        alpha = bytearray(97 if value else 0 for value in alpha)
    for x in range(black_fringe_alpha):
        alpha[x] = x + 1
    mask = stream(writer, alpha, {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width + int(bad_mask)),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceGray"),
    }, compress=True)
    if variant == "palette":
        pixels = bytes((244, 235, 216)) * (width * height)
        space = NameObject("/DeviceRGB")
    elif variant in ("rgb", "dark"):
        mask.get_object()[NameObject("/Matte")] = ArrayObject(
            [NumberObject(0), NumberObject(0), NumberObject(0)])
        color = (255, 255, 255) if variant == "rgb" else (0, 30, 70)
        pixels = bytes(c * alpha[i] // 255
                       for i in range(width * height) for c in color)
        space = NameObject("/DeviceRGB")
    else:
        pixels = bytes(2 if bad_index and alpha[i] else
                       1 if i < black_fringe_alpha or not alpha[i] else 0
                       for i in range(width * height))
        palette = stream(writer, b"\xff\xff\xff\x00\x00\x00", {},
                         compress=not plain_lookup)
        space = ArrayObject([NameObject("/Indexed"),
                             rgb_profile(writer, bad_white_mapping=bad_white_mapping),
                             NumberObject(1), palette])
    entries = {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": space,
        "/SMask": mask,
    }
    if neutral_decode or bad_predictor:
        entries["/DecodeParms"] = DictionaryObject({
            NameObject("/BitsPerComponent"): NumberObject(8),
            NameObject("/Colors"): NumberObject(3 if variant in ("rgb", "dark") else 1),
            NameObject("/Columns"): NumberObject(width),
            NameObject("/Predictor"): NumberObject(12 if bad_predictor else 1),
        })
        mask.get_object()[NameObject("/DecodeParms")] = DictionaryObject({
            NameObject("/BitsPerComponent"): NumberObject(8),
            NameObject("/Colors"): NumberObject(1),
            NameObject("/Columns"): NumberObject(width),
        })
    return stream(writer, pixels, entries, compress=True)


def fixture(variant, with_photo=False, malformed_profile=False,
            bad_mask=False, bad_index=False, bad_white_mapping=False,
            photo_mask_opacity=None, neutral_decode=False, bad_predictor=False,
            plain_lookup=False, black_fringe_alpha=0):
    writer = PdfWriter()
    page = writer.add_blank_page(width=200, height=120)
    font = writer._add_object(DictionaryObject({
        NameObject("/Type"): NameObject("/Font"),
        NameObject("/Subtype"): NameObject("/Type1"),
        NameObject("/BaseFont"): NameObject("/Helvetica"),
    }))
    xobjects = DictionaryObject()
    if with_photo:
        xobjects[NameObject("/Photo")] = photo(writer, malformed_profile, photo_mask_opacity)
    xobjects[NameObject("/Logo")] = logo(writer, variant, bad_mask,
        bad_index, bad_white_mapping, neutral_decode, bad_predictor,
        plain_lookup, black_fringe_alpha)
    resources = DictionaryObject({
        NameObject("/Font"): DictionaryObject({NameObject("/F1"): font}),
        NameObject("/XObject"): xobjects,
    })
    if with_photo:
        resources[NameObject("/ExtGState")] = DictionaryObject({
            NameObject("/Translucent"): writer._add_object(DictionaryObject({
                NameObject("/Type"): NameObject("/ExtGState"),
                NameObject("/BM"): NameObject("/Normal"),
                NameObject("/ca"): FloatObject(0.7),
            })),
        })
    page[NameObject("/Resources")] = resources
    content = ["q 0 .125 .251 rg 0 0 200 120 re f Q"]
    if with_photo:
        content.extend([
            "q 200 0 0 120 0 0 cm /Photo Do Q",
            "q /Translucent gs 1 1 1 rg 0 20 150 80 re f Q",
            "BT /F1 20 Tf 0 .125 .251 rg 10 75 Td (Photo title) Tj ET",
            "q 14 0 0 14 170 90 cm /Logo Do Q",
        ])
    else:
        content.extend([
            "BT /F1 18 Tf 1 1 1 rg 12 75 Td (Body title) Tj ET",
            "q 14 0 0 14 170 90 cm /Logo Do Q",
        ])
    page[NameObject("/Contents")] = stream(writer,
        "\n".join(content).encode("ascii"), {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def diagram_fixture(variant, inverted_decode=False, lut_profile=False,
                    d65_white=False, bad_chad=False, bad_d65_version=False):
    writer = PdfWriter()
    page = writer.add_blank_page(width=120, height=120)
    width = height = 64
    marks = bytes(2 if x == width - 1 and y == height - 1 else 0 if x < 6 else 1
                  for y in range(height) for x in range(width))
    alpha = bytes([255]) * (width * height)
    mask = stream(writer, alpha, {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceGray"),
        "/DecodeParms": DictionaryObject({
            NameObject("/Colors"): NumberObject(1),
            NameObject("/Columns"): NumberObject(width),
        }),
    }, compress=True)
    if variant == "indexed":
        space = ArrayObject([NameObject("/Indexed"), NameObject("/DeviceRGB"),
                             NumberObject(2), ByteStringObject(bytes([
                                 0, 0, 0, 255, 255, 255, 255, 0, 0]))])
        pixels = marks
    else:
        profile = rgb_profile(writer)
        if lut_profile or variant == "icc-swap" or d65_white or bad_chad:
            raw = bytearray(ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes())
            count = int.from_bytes(raw[128:132], "big")
            offsets = {}
            for index in range(count):
                at = 132 + index * 12
                offsets[bytes(raw[at:at + 4])] = int.from_bytes(raw[at + 4:at + 8], "big")
                if raw[at:at + 4] == b"chrm":
                    if lut_profile:
                        raw[at:at + 4] = b"A2B0"
            if variant == "icc-swap":
                red, blue = offsets[b"rXYZ"], offsets[b"bXYZ"]
                raw[red + 8:red + 20], raw[blue + 8:blue + 20] = (
                    raw[blue + 8:blue + 20], raw[red + 8:red + 20])
            if d65_white:
                if not bad_d65_version:
                    raw[8:12] = b"\x02\x10\x00\x00"
                white = offsets[b"wtpt"]
                for channel, value in enumerate((.95047, 1.0, 1.08883)):
                    raw[white + 8 + 4 * channel:white + 12 + 4 * channel] = (
                        round(value * 65536).to_bytes(4, "big", signed=True))
            if bad_chad:
                chromatic = offsets[b"chad"]
                raw[chromatic + 8:chromatic + 12] = (
                    round(.8 * 65536).to_bytes(4, "big", signed=True))
            profile = ArrayObject([NameObject("/ICCBased"),
                stream(writer, bytes(raw), {"/N": NumberObject(3)}, compress=True)])
        space = profile
        pixels = b"".join((b"\x00\x00\x00" if mark == 0 else
                           b"\xff\x00\x00" if mark == 2 else b"\xff\xff\xff")
                          for mark in marks)
    entries = {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": space,
        "/SMask": mask,
        "/DecodeParms": DictionaryObject({
            NameObject("/BitsPerComponent"): NumberObject(8),
            NameObject("/Colors"): NumberObject(1 if variant == "indexed" else 3),
            NameObject("/Columns"): NumberObject(width),
        }),
    }
    if variant == "indexed":
        entries["/Decode"] = ArrayObject([
            NumberObject(255 if inverted_decode else 0),
            NumberObject(0 if inverted_decode else 255)])
    image = stream(writer, pixels, entries, compress=True)
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Diagram"): image}),
    })
    page[NameObject("/Contents")] = stream(writer,
        b"q 1 1 1 rg 0 0 120 120 re f Q\nq 64 0 0 64 20 20 cm /Diagram Do Q", {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def panel_fixture(kind):
    writer = PdfWriter()
    page = writer.add_blank_page(width=140, height=100)
    width, height = 100, 60
    pixels = bytearray(width * height * 3)
    alpha = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            index = y * width + x
            inside = 10 <= x < 90 and 8 <= y < 52
            if inside:
                alpha[index] = 255
                pixels[index * 3:index * 3 + 3] = b"\xe6\xe6\xe6"
            elif 8 <= x < 92 and 6 <= y < 54:
                alpha[index] = 64 if x in (8, 91) or y in (6, 53) else 160
                pixels[index * 3:index * 3 + 3] = b"\xe6\xe6\xe6"
            if inside and (20 <= x < 27 or 42 <= x < 49) and 18 <= y < 35:
                pixels[index * 3:index * 3 + 3] = b"\x00\x00\x00"
            if kind == "gradient" and inside:
                pixels[index * 3:index * 3 + 3] = bytes((x * 2, y * 4, (x + y) % 256))
    if kind in ("lost-edge", "emerged-edge"):
        pixel = 2 * width + 2
        alpha[pixel] = 255
        pixels[pixel * 3:pixel * 3 + 3] = (b"\x00\x00\x00" if kind == "lost-edge"
                                                  else b"\x00\x20\x40")
    mask = stream(writer, bytes(alpha), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceGray"),
    }, compress=True)
    image = stream(writer, bytes(pixels), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceRGB"),
        "/SMask": mask,
    }, compress=True)
    resources = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Panel"): image}),
    })
    content = b"q 0 .125 .251 rg 0 0 140 100 re f Q\nq 100 0 0 60 20 20 cm /Panel Do Q"
    if kind == "overlay":
        font = writer._add_object(DictionaryObject({
            NameObject("/Type"): NameObject("/Font"),
            NameObject("/Subtype"): NameObject("/Type1"),
            NameObject("/BaseFont"): NameObject("/Helvetica"),
        }))
        resources[NameObject("/Font")] = DictionaryObject({NameObject("/F1"): font})
        content += b"\nBT /F1 11 Tf 0 0 0 rg 52 43 Td (Map) Tj ET"
    page[NameObject("/Resources")] = resources
    page[NameObject("/Contents")] = stream(writer, content, {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def soft_frame_fixture(kind):
    """An authored masked gray frame with a separate vector label, no raster text."""
    writer = PdfWriter()
    page = writer.add_blank_page(width=240, height=160)
    width, height = 180, 100
    pixels = bytearray(width * height * 3)
    alpha = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            index = y * width + x
            inside = 6 <= x < 174 and 6 <= y < 94
            soft = 4 <= x < 176 and 4 <= y < 96
            if inside:
                alpha[index] = 255
                border = x in (6, 173) or y in (6, 93)
                gray = 0 if border else 230
                pixels[index * 3:index * 3 + 3] = bytes((gray, gray, gray))
            elif soft:
                alpha[index] = 64 if x in (4, 175) or y in (4, 95) else 160
                pixels[index * 3:index * 3 + 3] = b"\xe6\xe6\xe6"
            if kind == "gradient" and inside:
                gray = round(200 * x / width)
                pixels[index * 3:index * 3 + 3] = bytes((gray, gray, gray))
            if kind == "interior-mark" and 85 <= x < 92 and 44 <= y < 51:
                pixels[index * 3:index * 3 + 3] = b"\x00\x00\x00"
            if kind == "colored-accent" and 85 <= x < 92 and 44 <= y < 51:
                pixels[index * 3:index * 3 + 3] = b"\xe6\x40\x30"
    if kind == "edge-noise":
        for y, x in ((6, 25), (93, 32), (50, 173), (6, 135), (93, 154)):
            index = y * width + x
            pixels[index * 3:index * 3 + 3] = b"\x90\x99\xa3"
    mask = stream(writer, bytes(alpha), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceGray"),
    }, compress=True)
    image = stream(writer, bytes(pixels), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceRGB"),
        "/SMask": mask,
    }, compress=True)
    font = writer._add_object(DictionaryObject({
        NameObject("/Type"): NameObject("/Font"),
        NameObject("/Subtype"): NameObject("/Type1"),
        NameObject("/BaseFont"): NameObject("/Helvetica"),
    }))
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Frame"): image}),
        NameObject("/Font"): DictionaryObject({NameObject("/F1"): font}),
    })
    content = (b"q 0 .125 .251 rg 0 0 240 160 re f Q\n"
               b"q 180 0 0 100 30 30 cm /Frame Do Q\n"
               b"BT /F1 13 Tf 0 0 0 rg 108 77 Td (Map) Tj ET")
    page[NameObject("/Contents")] = stream(writer, content, {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def white_island_fixture(kind):
    """A complex colored graph with transparent AA over an independent white panel."""
    writer = PdfWriter()
    page = writer.add_blank_page(width=240, height=160)
    width, height = 120, 80
    pixels = bytearray(b"\xff\xff\xff" * (width * height))
    alpha = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            index = y * width + x
            for center, warm in ((30, False), (90, True)):
                radius = (x - center) ** 2 + (y - 40) ** 2
                if radius <= 256:
                    opacity = 255 if radius <= 225 else round((256 - radius) * 255 / 31)
                    if opacity >= alpha[index]:
                        alpha[index] = opacity
                        pixels[index * 3:index * 3 + 3] = (
                            bytes((210, 174 + (y % 12), 141)) if warm else
                            bytes((171 + (y % 15), 185, 195)))
            if 45 <= x < 75 and y in (37, 38, 39, 52, 53):
                alpha[index] = 255
                pixels[index * 3:index * 3 + 3] = (
                    b"\x00\x00\x00" if y < 40 else b"\xb0\x40\x14")
            if 34 <= y < 46 and (24 <= x < 37 or 84 <= x < 97) and (
                    x % 4 == 0 or y in (38, 44)):
                alpha[index] = 255
                pixels[index * 3:index * 3 + 3] = b"\x00\x00\x00"
    mask = stream(writer, bytes(alpha), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceGray"),
    }, compress=True)
    image = stream(writer, bytes(pixels), {
        "/Type": NameObject("/XObject"),
        "/Subtype": NameObject("/Image"),
        "/Width": NumberObject(width),
        "/Height": NumberObject(height),
        "/BitsPerComponent": NumberObject(8),
        "/ColorSpace": NameObject("/DeviceRGB"),
        "/SMask": mask,
    }, compress=True)
    page[NameObject("/Resources")] = DictionaryObject({
        NameObject("/XObject"): DictionaryObject({NameObject("/Graph"): image}),
    })
    content = b"q 0 .125 .251 rg 0 0 240 160 re f Q\n"
    if kind != "missing-island":
        panel_width = (100 if kind == "undersized-island" else
                       140 if kind == "oversized-island" else 120)
        content += f"q 1 g 60 40 {panel_width} 80 re f Q\n".encode("ascii")
    if kind == "intervening-overlay":
        content += b"q 1 0 0 rg 95 70 15 10 re f Q\n"
    content += (b"q 60 40 120 80 re W n\n"
                b"q 120 0 0 80 60 40 cm /Graph Do Q Q")
    page[NameObject("/Contents")] = stream(writer, content, {})
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(source, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(source)
        file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=file,
                                capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_length, pdf_length = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_length + pdf_length
    facts = json.loads(result.stdout[16:16 + metadata_length])
    return 0, facts, result.stdout[16 + metadata_length:]


def image_inventory(source):
    reader = PdfReader(io.BytesIO(source))
    page = reader.pages[0]
    result = {}
    for name, ref in page["/Resources"]["/XObject"].items():
        item = ref.get_object()
        mask = item.get("/SMask")
        space = item["/ColorSpace"]
        palette = None
        profile = None
        if isinstance(space, ArrayObject):
            if space[0] == "/ICCBased":
                profile = space[1].get_object().get_data()
            elif space[0] == "/Indexed":
                lookup = space[3]
                palette_object = lookup.get_object()
                palette = palette_object.get_data() if hasattr(palette_object, "get_data") \
                    else palette_object.original_bytes
                base = space[1]
                if isinstance(base, ArrayObject) and base[0] == "/ICCBased":
                    profile = base[1].get_object().get_data()
        result[str(name)] = {
            "filter": str(item.get("/Filter")),
            "raw": item._data if item.get("/Filter") == "/DCTDecode" else None,
            "decoded": item.get_data() if item.get("/Filter") != "/DCTDecode" else None,
            "mask": mask.get_object().get_data() if mask else None,
            "matte": list(mask.get_object().get("/Matte")) if mask and
                     mask.get_object().get("/Matte") else None,
            "palette": palette,
            "profile": profile,
        }
    return result


results = []
for case, variant, with_photo, options in (
    ("body-rgb-matte-white", "rgb", False, {}),
    ("body-palette-ink-mask", "palette", False, {}),
    ("body-indexed-icc-white", "indexed", False, {}),
    ("body-indexed-plain-lookup", "indexed", False, {"plain_lookup": True}),
    ("body-indexed-soft-black-fringe", "indexed", False, {"black_fringe_alpha": 3}),
    ("body-rgb-neutral-decode", "rgb", False, {"neutral_decode": True}),
    ("photo-rgb-matte-logo", "rgb", True, {}),
    ("photo-indexed-logo", "indexed", True, {}),
    ("photo-dark-logo", "dark", True, {}),
    ("photo-opaque-jpeg-mask", "rgb", True, {"photo_mask_opacity": 255}),
):
    source = fixture(variant, with_photo=with_photo, **options)
    (scratch / f"{case}-original.pdf").write_bytes(source)
    code, facts, empty = invoke(source, "--inspect")
    assert code == 0 and not empty, (case, code, facts)
    code, converted_facts, output = invoke(source, "--convert")
    assert code == 0 and output and converted_facts == facts, (case, code, converted_facts)
    before, after = image_inventory(source), image_inventory(output)
    if before != after:
        differences = []
        for name in before:
            for field, value in before[name].items():
                other = after[name][field]
                if value != other:
                    compact = lambda item: (len(item), hashlib.sha256(item).hexdigest()[:12]) \
                        if isinstance(item, bytes) else item
                    differences.append((name, field, compact(value), compact(other)))
        raise AssertionError((case, differences))
    first = pymupdf.open(stream=source, filetype="pdf")
    second = pymupdf.open(stream=output, filetype="pdf")
    assert first[0].get_text() == second[0].get_text(), case
    assert first[0].get_images(full=True) and second[0].get_images(full=True), case
    second[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"{case}.png")
    code, repeat, repeat_output = invoke(output, "--convert")
    assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, (case, repeat)
    results.append({"case": case, "retainedImageBytes": True, "repeatNoOp": True})

for case, source in (
    ("body-dark-logo", fixture("dark")),
    ("bad-mask-size", fixture("rgb", bad_mask=True)),
    ("bad-index", fixture("indexed", bad_index=True)),
    ("bad-white-mapping", fixture("indexed", bad_white_mapping=True)),
    ("bad-profile", fixture("indexed", with_photo=True, malformed_profile=True)),
    ("bad-predictor", fixture("rgb", bad_predictor=True)),
    ("nonopaque-jpeg-mask", fixture("rgb", with_photo=True, photo_mask_opacity=200)),
    ("dark-fringe-alpha-four", fixture("indexed", black_fringe_alpha=4)),
):
    code, reason, empty = invoke(source, "--convert")
    assert code in (3, 4) and not empty, (case, code, reason)
    results.append({"case": case, "refused": True})

if args.with_expanded:
    for case, variant, settings in (("indexed-rgb-diagram", "indexed", {}),
                                    ("icc-srgb-diagram", "icc", {}),
                                    ("icc-d65-chad-diagram", "icc", {"d65_white": True}),
                                    ("icc-swapped-primaries", "icc-swap", {})):
        source = diagram_fixture(variant, **settings)
        code, facts, output = invoke(source, "--convert")
        assert code == 0 and output and facts["recoloredImages"] >= 1, (case, code, facts)
        before = image_inventory(source)["/Diagram"]
        after = image_inventory(output)["/Diagram"]
        assert before["mask"] == after["mask"]
        assert before["decoded"] != after["decoded"]
        assert len(after["decoded"]) == 64 * 64 * 3
        converted = after["decoded"]
        assert min(converted[:3]) >= 180 and max(converted[18:21]) <= 80, case
        if variant == "icc-swap":
            assert converted[-1] >= converted[-3] + 50 and \
                converted[-1] >= converted[-2] + 50, (case, converted[-3:])
        else:
            assert converted[-3] >= 180 and converted[-2] <= 80 and converted[-1] <= 80, case
        second = pymupdf.open(stream=output, filetype="pdf")
        second[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(scratch / f"{case}.png")
        code, repeat, repeat_output = invoke(output, "--convert")
        assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, (case, repeat)
        results.append({"case": case, "rgbExpansion": True, "repeatNoOp": True})
    for case, source in (
        ("inverted-indexed-decode", diagram_fixture("indexed", inverted_decode=True)),
        ("lut-icc-profile", diagram_fixture("icc", lut_profile=True)),
        ("bad-chad", diagram_fixture("icc", d65_white=True, bad_chad=True)),
        ("v4-d65-white", diagram_fixture("icc", d65_white=True, bad_d65_version=True)),
    ):
        code, reason, empty = invoke(source, "--convert")
        assert code in (3, 4) and not empty, (case, code, reason)
        results.append({"case": case, "refused": True})
    source = panel_fixture("readable")
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts["preservedImages"] >= 1, (code, facts)
    assert image_inventory(source) == image_inventory(output)
    code, repeat, repeat_output = invoke(output, "--convert")
    assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, repeat
    results.append({"case": "dark-paper-readable-panel", "retainedImageBytes": True,
                    "repeatNoOp": True})
    for case in ("lost-edge", "emerged-edge", "gradient"):
        code, reason, empty = invoke(panel_fixture(case), "--convert")
        assert code in (3, 4) and not empty, (case, code, reason)
        results.append({"case": f"dark-paper-{case}", "refused": True})

if args.with_overlay:
    source = panel_fixture("overlay")
    code, facts, output = invoke(source, "--convert")
    if code == 0:
        assert output and facts["preservedImages"] >= 1, (code, facts)
        assert image_inventory(source) == image_inventory(output)
        converted = pymupdf.open(stream=output, filetype="pdf")
        spans = [span for block in converted[0].get_text("dict")["blocks"]
                 if "lines" in block for line in block["lines"] for span in line["spans"]
                 if span["text"] == "Map"]
        assert len(spans) == 1
        color = spans[0]["color"]
        assert max((color >> 16) & 255, (color >> 8) & 255, color & 255) <= 128, color
        code, repeat, repeat_output = invoke(output, "--convert")
        assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, repeat
        results.append({"case": "dark-paper-overlaid-text", "retainedImageBytes": True,
                        "textContrast": True, "repeatNoOp": True})
    else:
        assert code in (3, 4) and not output, (code, facts)
        results.append({"case": "dark-paper-overlaid-text", "safeRefusal": True})

if args.with_soft_panels:
    for case in ("empty", "edge-noise"):
        source = soft_frame_fixture(case)
        code, facts, output = invoke(source, "--convert")
        assert code == 0 and output and facts["recoloredImages"] >= 1, (case, code, facts)
        before, after = image_inventory(source)["/Frame"], image_inventory(output)["/Frame"]
        assert before["mask"] == after["mask"] and before["decoded"] != after["decoded"]
        center = (50 * 180 + 90) * 3
        assert max(after["decoded"][center:center + 3]) < 100, case
        rendered = pymupdf.open(stream=output, filetype="pdf")
        spans = [span for block in rendered[0].get_text("dict")["blocks"]
                 if "lines" in block for line in block["lines"] for span in line["spans"]
                 if span["text"] == "Map"]
        assert len(spans) == 1, (case, spans)
        rendered[0].get_pixmap(matrix=pymupdf.Matrix(2, 2)).save(
            scratch / f"soft-frame-{case}.png")
        code, repeat, repeat_output = invoke(output, "--convert")
        assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, (case, repeat)
        results.append({"case": f"soft-frame-{case}", "recolored": True, "repeatNoOp": True})
    for case in ("gradient", "interior-mark", "colored-accent"):
        code, reason, output = invoke(soft_frame_fixture(case), "--convert")
        assert code in (3, 4) and not output, (case, code, reason)
        results.append({"case": f"soft-frame-{case}", "refused": True})

if args.with_islands:
    source = white_island_fixture("complete")
    code, facts, output = invoke(source, "--convert")
    assert code == 0 and output and facts["preservedImages"] >= 1, (code, facts)
    assert image_inventory(source) == image_inventory(output)
    source_doc = pymupdf.open(stream=source, filetype="pdf")
    converted = pymupdf.open(stream=output, filetype="pdf")
    source_map = source_doc[0].get_pixmap()
    target_map = converted[0].get_pixmap()
    # The entire island including transparent image edges is pixel-identical;
    # a sample of surrounding navy paper must still have become dark charcoal.
    for y in range(40, 120):
        for x in range(60, 180):
            assert source_map.pixel(x, y) == target_map.pixel(x, y), (x, y)
    assert source_map.pixel(10, 10) != target_map.pixel(10, 10)
    target_map.save(scratch / "white-island-graph.png")
    code, repeat, repeat_output = invoke(output, "--convert")
    assert code == 0 and repeat["changedPages"] == 0 and not repeat_output, repeat
    results.append({"case": "white-island-graph", "pixelIdenticalIsland": True,
                    "retainedImageBytes": True, "repeatNoOp": True})
    for case in ("missing-island", "undersized-island", "oversized-island",
                 "intervening-overlay"):
        code, reason, output = invoke(white_island_fixture(case), "--convert")
        assert code in (3, 4) and not output, (case, code, reason)
        results.append({"case": f"white-island-{case}", "refused": True})

(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Retained images: {len(results)} cases passed; copied bytes and alpha verified.")
