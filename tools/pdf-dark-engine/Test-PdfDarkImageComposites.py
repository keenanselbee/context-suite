"""Check opaque-JPEG annotation composites and their local light backings.

All images and PDF pages are authored here. Test-only dependencies: Pillow,
pypdf and PyMuPDF. Outputs stay in a fresh repository-local scratch directory.
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
from PIL import Image, ImageDraw
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, DecodedStreamObject, DictionaryObject,
                           FloatObject, NameObject, NumberObject)


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
scratch = pathlib.Path(args.scratch).resolve()
if scratch.exists() or not scratch.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use a fresh repository-local scratch directory")
scratch.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def dictionary(**entries):
    return DictionaryObject({NameObject("/" + key): value for key, value in entries.items()})


def stream(writer, data, entries):
    result = DecodedStreamObject()
    result.set_data(data)
    result.update(entries)
    return writer._add_object(result)


def jpeg(second=False):
    image = Image.new("RGB", (72 if second else 160, 64 if second else 100), (250, 250, 249))
    draw = ImageDraw.Draw(image)
    if second:
        draw.rounded_rectangle((5, 6, 66, 57), radius=9, fill=(187, 226, 237),
                               outline=(32, 84, 101), width=3)
        draw.line((15, 29, 57, 29), fill=(44, 96, 113), width=3)
    else:
        draw.rounded_rectangle((8, 9, 150, 88), radius=9, fill=(229, 233, 235),
                               outline=(36, 58, 71), width=3)
        draw.rectangle((20, 24, 137, 38), fill=(168, 207, 225))
        draw.line((20, 58, 136, 58), fill=(40, 61, 74), width=3)
        draw.line((20, 70, 91, 70), fill=(40, 61, 74), width=3)
    encoded = io.BytesIO()
    image.save(encoded, format="JPEG", quality=94, subsampling=0)
    return encoded.getvalue(), image.size


def image_object(writer, second=False, masked=False, lossless=False):
    encoded, (width, height) = jpeg(second)
    if lossless:
        decoded = Image.open(io.BytesIO(encoded)).convert("RGB")
        encoded = zlib.compress(decoded.tobytes())
    entries = dictionary(Type=NameObject("/XObject"), Subtype=NameObject("/Image"),
                         Width=NumberObject(width), Height=NumberObject(height),
                         BitsPerComponent=NumberObject(8),
                         ColorSpace=NameObject("/DeviceRGB"),
                         Filter=NameObject("/FlateDecode" if lossless else "/DCTDecode"))
    if masked:
        alpha = bytearray([255] * (width * height))
        alpha[(height // 2) * width + width // 2] = 128
        entries[NameObject("/SMask")] = stream(writer, bytes(alpha), dictionary(
            Type=NameObject("/XObject"), Subtype=NameObject("/Image"),
            Width=NumberObject(width), Height=NumberObject(height),
            BitsPerComponent=NumberObject(8), ColorSpace=NameObject("/DeviceGray")))
    return stream(writer, encoded, entries)


def fixture(case):
    writer = PdfWriter()
    page = writer.add_blank_page(width=300, height=200)
    first = image_object(writer, masked=case in ("soft-mask", "masked-prior-partial"))
    xobjects = dictionary(Im1=first)
    if case in ("second-connected-image", "lossless-connected-image"):
        xobjects[NameObject("/Im2")] = image_object(
            writer, second=True, lossless=case == "lossless-connected-image")
    font = writer._add_object(dictionary(Type=NameObject("/Font"),
                                         Subtype=NameObject("/Type1"),
                                         BaseFont=NameObject("/Helvetica")))
    resources = dictionary(XObject=xobjects, Font=dictionary(F1=font))
    if case == "fractional-label":
        resources[NameObject("/ExtGState")] = dictionary(Half=writer._add_object(
            dictionary(Type=NameObject("/ExtGState"), ca=FloatObject(.5),
                       CA=FloatObject(.5))))
    page[NameObject("/Resources")] = resources
    paper = "0.9 0.9 0.8" if case == "nonwhite-paper" else "1 1 1"
    if case == "transformed-paper":
        content = ["q 2 0 0 2 0 0 cm 1 1 1 rg 0 0 150 100 re f Q"]
    else:
        content = [f"q {paper} rg 0 0 300 200 re f Q"]
    if case == "prior-uncovered-paint":
        content.append("q 0.8 0.25 0.1 rg 207 84 15 12 re f Q")
    if case == "masked-prior-partial":
        content.append("q 0.8 0.25 0.1 rg 108 93 5 5 re f Q")
    if case == "curved-clip":
        content.append("q 30 45 m 120 20 190 80 190 145 c 30 145 l h W n")
    if case == "rectangular-clip":
        content.append("q 31 46 158 98 re W n")
    content.append("q 160 0 0 100 30 45 cm /Im1 Do Q")
    if case in ("curved-clip", "rectangular-clip"):
        content.append("Q")
    if case in ("second-connected-image", "lossless-connected-image"):
        content.append("q 44 0 0 38 180 82 cm /Im2 Do Q")
    if case == "reused-image":
        content.append("q 45 0 0 28 246 155 cm /Im1 Do Q")
    if case == "fractional-label":
        content.append("q /Half gs 0.96 0.38 0.12 rg 178 74 55 22 re f Q")
    content.extend([
        "q 0.96 0.38 0.12 rg BT /F1 14 Tf 1 0 0 1 174 83 Tm (GO) Tj ET Q",
        "q 0.96 0.38 0.12 RG 2 w 168 107 m 228 107 l S Q",
        "q 0.86 0.86 0.86 rg 261 18 29 26 re f Q",
    ])
    page[NameObject("/Contents")] = stream(writer, ("\n".join(content) + "\n").encode("ascii"),
                                            DictionaryObject())
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue()


def invoke(data, mode):
    with tempfile.TemporaryFile(dir=scratch) as source:
        source.write(data)
        source.seek(0)
        result = subprocess.run([str(engine), mode], stdin=source,
                                capture_output=True, timeout=90)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace").strip(), b""
    magic, version, facts_size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + facts_size + pdf_size
    return 0, json.loads(result.stdout[16:16 + facts_size]), result.stdout[16 + facts_size:]


def image_bytes(data):
    page = PdfReader(io.BytesIO(data)).pages[0]
    result = {}
    for key, value in page["/Resources"]["/XObject"].items():
        image = value.get_object()
        mask = image.get("/SMask")
        result[key] = (image._data, mask.get_object().get_data() if mask else None)
    return result


def image_and_text_facts(data):
    with pymupdf.open(stream=data, filetype="pdf") as document:
        page = document[0]
        trace = [(span["font"], span["size"], span["chars"], span["color"])
                 for span in page.get_texttrace()]
        pixmap = page.get_pixmap(alpha=False)
        return trace, pixmap


positives = ("soft-mask",)
safe_general = ("single", "transformed-paper", "second-connected-image",
                "lossless-connected-image", "rectangular-clip", "prior-uncovered-paint",
                "nonwhite-paper", "curved-clip", "reused-image")
negatives = ("masked-prior-partial", "fractional-label")
results = []
for case in (*positives, *safe_general, *negatives):
    source = fixture(case)
    original_hash = hashlib.sha256(source).hexdigest()
    (scratch / f"{case}-source.pdf").write_bytes(source)
    code, facts, output = invoke(source, "--convert")
    inspect_code, inspection, empty = invoke(source, "--inspect")
    assert code == inspect_code and (code != 0 or facts == inspection and not empty)
    details = {"exitCode": code, "diagnostic": facts if code else None}
    if case in negatives:
        passed = code == 3 and not output
        if output:
            (scratch / f"{case}-unexpected-dark.pdf").write_bytes(output)
    elif case in safe_general:
        # These structures may convert by another safe policy; they cannot use
        # the bounded original-color composite's inserted white backing.
        if code:
            passed = code == 3 and not output
        else:
            (scratch / f"{case}-general-dark.pdf").write_bytes(output)
            before_trace, _ = image_and_text_facts(source)
            after_trace, dark = image_and_text_facts(output)
            page = PdfReader(io.BytesIO(output)).pages[0]
            content = page.get_contents().get_data()
            repeat_code, repeat, duplicate = invoke(output, "--convert")
            details.update({"noCompositeBacking": b"/Artifact BMC" not in content,
                            "imageAndMaskBytesPreserved": image_bytes(source) == image_bytes(output),
                            "textGeometryPreserved": [item[:3] for item in before_trace] ==
                            [item[:3] for item in after_trace],
                            "outsidePaperDark": max(dark.pixel(5, 5)) < 60,
                            "adjacentPaperDark": max(dark.pixel(240, 60)) < 60,
                            "repeatNoOp": repeat_code == 0 and
                            repeat["changedPages"] == 0 and not duplicate})
            passed = all(details[key] for key in ("noCompositeBacking", "imageAndMaskBytesPreserved",
                                                   "textGeometryPreserved", "outsidePaperDark",
                                                   "adjacentPaperDark", "repeatNoOp"))
    elif code:
        passed = False
    else:
        (scratch / f"{case}-dark.pdf").write_bytes(output)
        before_trace, original = image_and_text_facts(source)
        after_trace, dark = image_and_text_facts(output)
        assert before_trace[-1][2][0][3][0] < 190 < \
            before_trace[-1][2][-1][3][2], "Text fixture must cross the image edge"
        original.save(scratch / f"{case}-source.png")
        dark.save(scratch / f"{case}-dark.png")
        # The local composite includes the image, connected orange vector
        # annotation and text beyond the image's right boundary.
        exact = all(original.pixel(x, y) == dark.pixel(x, y)
                    for y in range(56, 154) for x in range(31, 230))
        outside_dark = max(dark.pixel(5, 5)) < 60
        nearby_dark = max(dark.pixel(240, 60)) < 60
        separate_panel_dark = max(dark.pixel(274, 169)) < 120
        content = PdfReader(io.BytesIO(output)).pages[0].get_contents().get_data()
        repeat_code, repeat, duplicate = invoke(output, "--convert")
        details.update({"compositePixelsExact": exact,
                        "localBackingInserted": b"/Artifact BMC" in content,
                        "imageAndMaskBytesPreserved": image_bytes(source) == image_bytes(output),
                        "textGeometryAndColorPreserved": before_trace == after_trace,
                        "outsidePaperDark": outside_dark,
                        "nearbyPaperDark": nearby_dark,
                        "separatePanelDark": separate_panel_dark,
                        "repeatNoOp": repeat_code == 0 and
                        repeat["changedPages"] == 0 and not duplicate})
        passed = all(details[key] for key in ("compositePixelsExact", "localBackingInserted",
                                               "imageAndMaskBytesPreserved",
                                               "textGeometryAndColorPreserved",
                                               "outsidePaperDark", "nearbyPaperDark",
                                               "separatePanelDark", "repeatNoOp"))
    details["sourceHashUnchanged"] = original_hash == hashlib.sha256(source).hexdigest()
    results.append({"case": case, "passed": passed, "details": details})
    print("PASS" if passed else "FAIL", case, details, flush=True)

report = {"engine": str(engine), "engineSha256": hashlib.sha256(engine.read_bytes()).hexdigest(),
          "results": results}
(scratch / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
if not all(result["passed"] for result in results):
    raise AssertionError("Image-composite cases failed; see scratch report.json")
print(f"Passed {len(results)} image-composite checks.")
