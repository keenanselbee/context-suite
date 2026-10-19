"""Preserve readable annotations over retained images on implicit white paper.

All image samples, vector glyphs, and PDF content are independently authored.
Requires Pillow, pypdf, PyMuPDF, and a fresh repository-local scratch directory.
"""

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
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, ByteStringObject, DecodedStreamObject,
                           DictionaryObject, FloatObject, NameObject, NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
engine = pathlib.Path(args.engine).resolve(strict=True)
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)


def dictionary(**entries):
    return DictionaryObject({NameObject("/" + key): value for key, value in entries.items()})


def numbers(*values):
    return ArrayObject([FloatObject(value) for value in values])


def stream(writer, data, **entries):
    result = DecodedStreamObject()
    result.set_data(data)
    result.update(dictionary(**entries))
    return writer._add_object(result)


def authored_image(writer, kind, dark=False):
    width, height = 240, 24 if kind == "jpeg-vectors" else 36
    if kind == "jpeg-vectors":
        image = Image.new("RGB", (width, height))
        image.putdata([((28 + ((x * 37 + y * 17) % 39)) if dark else
                        (180 + ((x * 37 + y * 17) % 51)),) * 3
                       for y in range(height) for x in range(width)])
        output = io.BytesIO()
        image.save(output, format="JPEG", quality=94, subsampling=0)
        return stream(writer, output.getvalue(), Type=NameObject("/XObject"),
                      Subtype=NameObject("/Image"), Width=NumberObject(width),
                      Height=NumberObject(height), BitsPerComponent=NumberObject(8),
                      ColorSpace=NameObject("/DeviceRGB"), Filter=NameObject("/DCTDecode"))
    calibration = dictionary(WhitePoint=numbers(.9505, 1, 1.089),
                             Gamma=numbers(2.22221, 2.22221, 2.22221),
                             Matrix=numbers(.4124, .2126, .0193, .3576, .7152, .1192,
                                            .1805, .0722, .9505))
    base = ArrayObject([NameObject("/CalRGB"), calibration])
    lookup = bytes(channel for index in range(256) for channel in (180 + index * 60 // 255,) * 3)
    indexed = ArrayObject([NameObject("/Indexed"), base, NumberObject(255), ByteStringObject(lookup)])
    samples = bytes(x * 255 // (width - 1) for _ in range(height) for x in range(width))
    return stream(writer, zlib.compress(samples), Type=NameObject("/XObject"),
                  Subtype=NameObject("/Image"), Width=NumberObject(width),
                  Height=NumberObject(height), BitsPerComponent=NumberObject(8),
                  ColorSpace=indexed, Filter=NameObject("/FlateDecode"))


def fixture(kind, variant="safe"):
    writer = PdfWriter()
    page = writer.add_blank_page(width=360, height=240)
    image = authored_image(writer, kind)
    font = writer._add_object(dictionary(Type=NameObject("/Font"), Subtype=NameObject("/Type1"),
                                         BaseFont=NameObject("/Helvetica")))
    resources = dictionary(XObject=dictionary(Im1=image), Font=dictionary(F1=font))
    if variant == "fractional-annotation":
        resources[NameObject("/ExtGState")] = dictionary(Half=writer._add_object(dictionary(
            Type=NameObject("/ExtGState"), ca=FloatObject(.5), CA=FloatObject(.5))))
    page[NameObject("/Resources")] = resources
    content = []  # Deliberately no full-page white paint: paper is implicit.
    image_height = 24 if kind == "jpeg-vectors" else 36
    if variant == "local-white-overhang-earlier-mark":
        content.append("q 0.8 0.25 0.1 rg 19.4 153 0.5 8 re f Q")
    if variant in ("local-white-overhang", "local-white-overhang-earlier-mark"):
        content.append("q 1 1 1 rg 20 140 260 70 re f Q")
    if variant in ("local-white-earlier-stroke", "local-white-disjoint-stroke"):
        content.append("q 1 1 1 rg 20 140 260 70 re f Q")
        # This annotation is drawn before the image. Preserving the local white
        # panel must also preserve every earlier stroke footprint on that panel.
        path = "42 150 m 95 150 l"
        if variant == "local-white-disjoint-stroke":
            path += " 305 120 m 335 120 l"
        content.append(f"q 0 0 0 RG 1 w {path} S Q")
    if variant in ("clipped-prior-white-fill", "clipped-prior-surviving-paint"):
        clip_bottom = 160 if variant == "clipped-prior-white-fill" else 154
        clip_height = image_height + 160 - clip_bottom
        color = "1 1 1" if variant == "clipped-prior-white-fill" else "0.8 0.25 0.1"
        # The raw path extends past the screenshot. Only its proved rectangular
        # clip, in page coordinates, determines which prior paint remains visible.
        content.append(f"q 30 {clip_bottom} 240 {clip_height} re W n {color} rg "
                       f"20 150 260 {image_height + 20} re f Q")
    if variant == "prior-unrelated-paint":
        content.append("q 0.8 0.25 0.1 rg 45 151 45 8 re f Q")
    if variant == "nonrectangular-clip":
        content.append("q 30 160 m 270 160 l 270 190 l 150 175 l 30 190 l h W n")
    image_left = 19.6 if variant in ("local-white-overhang", "local-white-overhang-earlier-mark") else 30
    content.append(f"q 240 0 0 {image_height} {image_left} 160 cm /Im1 Do Q")
    if variant == "nonrectangular-clip":
        content.append("Q")
    content.append("q")
    if variant == "fractional-annotation":
        content.append("/Half gs")
    if kind == "jpeg-vectors":
        # Independently drawn H and E outlines, with lower portions below the
        # image. They are vector paths rather than font outlines or text objects.
        content.extend([
            "0 0 0 rg 50 154 m 53 154 l 53 163 l 61 163 l 61 154 l 64 154 l "
            "64 176 l 61 176 l 61 166 l 53 166 l 53 176 l 50 176 l h f",
            "0 0 0 rg 70 154 m 85 154 l 85 157 l 73 157 l 73 163 l 83 163 l "
            "83 166 l 73 166 l 73 173 l 85 173 l 85 176 l 70 176 l h f",
        ])
    else:
        content.append("0 0 0 rg BT /F1 18 Tf 1 0 0 1 55 154 Tm (Edge label) Tj ET")
    content.extend(["Q", "q 0 0 0 rg BT /F1 12 Tf 1 0 0 1 40 60 Tm (Separate caption) Tj ET Q"])
    page[NameObject("/Contents")] = stream(writer, ("\n".join(content) + "\n").encode("ascii"))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as file:
        file.write(data)
        file.seek(0)
        result = subprocess.run([str(engine), mode], stdin=file, capture_output=True, timeout=90)
        file.seek(0)
        assert file.read() == data, "Native host changed its source"
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, metadata_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + metadata_size + pdf_size
    return (0, json.loads(result.stdout[16:16 + metadata_size]), result.stdout[16 + metadata_size:])


def image_facts(data):
    image = PdfReader(io.BytesIO(data)).pages[0]["/Resources"]["/XObject"]["/Im1"]
    return image._data, image["/ColorSpace"], image["/Width"], image["/Height"]


def geometry_and_pixels(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        text = [(span["font"], span["size"], span["chars"])
                for span in page.get_texttrace()]
        drawing = [(entry["type"], entry["items"])
                   for entry in page.get_drawings() if entry["rect"].y0 >= 60 and entry["rect"].y1 <= 87]
        pixmap = page.get_pixmap(alpha=False)
        return text, drawing, pixmap


def unchanged_guard_fixture(variant):
    writer = PdfWriter()
    page = writer.add_blank_page(width=360, height=240)
    image = authored_image(writer, "jpeg-vectors", dark=True)
    font = writer._add_object(dictionary(Type=NameObject("/Font"), Subtype=NameObject("/Type1"),
                                         BaseFont=NameObject("/Helvetica")))
    page[NameObject("/Resources")] = dictionary(XObject=dictionary(Im1=image), Font=dictionary(F1=font))
    # The retained image covers 86.6% of the page. A white original-composite
    # fallback exceeds its 80% limit, isolating the unchanged-vector proof.
    color = "0 0 0" if variant == "changed-black" else "0.956863 0.921569 0.847059"
    text_mode = "0 0 0 RG .3 w 2 Tr" if variant == "mixed-fill-stroke" else ""
    content = ("q 340 0 0 220 10 10 cm /Im1 Do Q\n"
               f"q {color} rg {text_mode} BT /F1 18 Tf 1 0 0 1 60 165 Tm (LIGHT) Tj ET Q\n")
    page[NameObject("/Contents")] = stream(writer, content.encode("ascii"))
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


results = []
for kind in ("jpeg-vectors", "indexed-calrgb-text"):
    variants = ("safe", "clipped-prior-white-fill", "prior-unrelated-paint",
                "clipped-prior-surviving-paint", "nonrectangular-clip", "fractional-annotation")
    if kind == "jpeg-vectors":
        variants += ("local-white-earlier-stroke", "local-white-disjoint-stroke",
                     "local-white-overhang", "local-white-overhang-earlier-mark")
    for variant in variants:
        case = f"{kind}-{variant}"
        source = fixture(kind, variant)
        digest = hashlib.sha256(source).hexdigest()
        (scratch / f"{case}-source.pdf").write_bytes(source)
        inspect_code, inspection, empty = invoke(source, "--inspect")
        code, facts, output = invoke(source, "--convert")
        assert inspect_code == code and (code != 0 or inspection == facts and not empty), \
            (case, inspect_code, inspection, code, facts)
        if variant not in ("safe", "clipped-prior-white-fill", "local-white-earlier-stroke",
                           "local-white-overhang"):
            assert code in (3, 4, 5) and not output, (case, code, facts)
            details = {"refused": True, "reason": facts}
        else:
            assert code == 0 and output, (case, code, facts)
            before_text, before_paths, original = geometry_and_pixels(source)
            after_text, after_paths, dark = geometry_and_pixels(output)
            assert before_text == after_text, (case, "Searchable text geometry changed")
            assert before_paths == after_paths, (case, "Vector annotation geometry changed")
            assert image_facts(source) == image_facts(output), (case, "Retained image changed")
            # The image and every annotation pixel, including its white paper
            # beyond the image's lower edge, must remain exactly readable.
            left, right = 31, 269
            top, bottom = (57, 86) if kind == "jpeg-vectors" else (45, 92)
            if variant == "local-white-earlier-stroke":
                left, top, right, bottom = 21, 31, 279, 100
            elif variant == "local-white-overhang":
                left, top, right, bottom = 20, 31, 279, 100
                # Column 19 contains the fractional screenshot overhang. Above
                # and below the screenshot it is unpainted page paper, outside
                # the original local white panel, and may follow the dark page.
                assert all(original.pixel(19, y) == dark.pixel(19, y) for y in range(56, 80)), \
                    (case, "Fractional image overhang changed appearance")
            assert all(original.pixel(x, y) == dark.pixel(x, y)
                       for y in range(top, bottom) for x in range(left, right)), \
                (case, "Image/annotation composite changed appearance")
            assert max(dark.pixel(10, 10)) < 60 and max(dark.pixel(320, 85)) < 60, \
                (case, "Unrelated paper failed to darken")
            assert max(dark.pixel(31, 100)) < 60, (case, "Composite backing expanded beyond its local bounds")
            (scratch / f"{case}-dark.pdf").write_bytes(output)
            original.save(scratch / f"{case}-source.png")
            dark.save(scratch / f"{case}-dark.png")
            for mode in ("--inspect", "--convert"):
                repeat_code, repeated, duplicate = invoke(output, mode)
                assert repeat_code == 0 and repeated["changedPages"] == 0 and \
                    repeated["alreadyDarkPages"] == 1 and not duplicate, (case, mode, repeated)
            details = {"converted": True, "compositePixelsExact": True,
                       "imageBytesAndColorSpacePreserved": True, "repeatNoOp": True}
        assert hashlib.sha256(source).hexdigest() == digest
        results.append({"case": case, "sourceSha256": digest, **details})
        print("PASS", case, flush=True)

for variant in ("unchanged-light", "changed-black", "mixed-fill-stroke"):
    case = f"retained-dark-image-{variant}"
    source = unchanged_guard_fixture(variant)
    digest = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    inspect_code, inspection, empty = invoke(source, "--inspect")
    code, facts, output = invoke(source, "--convert")
    assert inspect_code == code and (code != 0 or inspection == facts and not empty), \
        (case, inspect_code, inspection, code, facts)
    if variant == "unchanged-light":
        assert code == 0 and output, (case, code, facts)
        before_text, _, original = geometry_and_pixels(source)
        after_text, _, dark = geometry_and_pixels(output)
        assert before_text == after_text and image_facts(source) == image_facts(output), case
        assert all(original.pixel(x, y) == dark.pixel(x, y)
                   for y in range(10, 230) for x in range(10, 350)), case
        assert max(dark.pixel(5, 5)) < 60, case
        for mode in ("--inspect", "--convert"):
            repeat_code, repeated, duplicate = invoke(output, mode)
            assert repeat_code == 0 and repeated["changedPages"] == 0 and not duplicate, \
                (case, mode, repeat_code, repeated)
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        details = {"converted": True, "originalPixelsAndSamplesPreserved": True, "repeatNoOp": True}
    else:
        assert code == 3 and not output, (case, code, facts)
        details = {"refused": True, "reason": facts}
    assert hashlib.sha256(source).hexdigest() == digest
    results.append({"case": case, "sourceSha256": digest, **details})
    print("PASS", case, flush=True)

(scratch / "results.json").write_text(json.dumps({
    "engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
    "suiteSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest(), "cases": results,
}, indent=2), encoding="utf-8")
print(f"Passed {len(results)} retained-annotation checks.")
