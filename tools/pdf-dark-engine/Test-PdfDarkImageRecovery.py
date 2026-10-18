"""Verify intrinsic JPEG/mask recovery with authored, bounded positive and ambiguity fixtures."""

import argparse
import hashlib
import io
import json
import pathlib
import struct
import subprocess

import pymupdf
from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (ArrayObject, BooleanObject, DecodedStreamObject,
                           DictionaryObject, NameObject, NumberObject)


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


def stream(writer, data, entries):
    result = DecodedStreamObject()
    result.set_data(data)
    result.update({NameObject(key): value for key, value in entries.items()})
    return writer._add_object(result)


def fixture(case, normalized=False):
    writer = PdfWriter()
    page = writer.add_blank_page(240, 180)
    rgb = Image.new("RGB", (64, 48))
    for y in range(48):
        for x in range(64):
            rgb.putpixel((x, y), (30 + x * 2, 40 + y * 3, 70 + (x * 3 + y * 7) % 100))
    encoded = io.BytesIO()
    rgb.save(encoded, format="JPEG", quality=93)
    geometry = {"/Type": NameObject("/XObject"), "/Subtype": NameObject("/Image"),
                "/BitsPerComponent": NumberObject(8)}
    dimensions = (64, 48) if normalized or case == "valid" else (80, 60)
    mask_dimensions = dimensions
    if case == "image-only":
        mask_dimensions = (64, 48)
    elif case == "mask-only":
        dimensions = (64, 48)
        mask_dimensions = (64, 48) if normalized else (80, 60)
    elif case == "same-area":
        dimensions = mask_dimensions = (48, 64)
    elif case == "conflicting-mask":
        mask_dimensions = (70, 50)
    alpha = bytes([255]) * (64 * 48)
    if case == "short-alpha":
        alpha = alpha[:-1]
    elif case == "long-alpha":
        alpha += b"\xff"
    elif case == "declared-alpha":
        alpha = bytes([255]) * (80 * 60)
    mask = stream(writer, alpha, {**geometry, "/ColorSpace": NameObject("/DeviceGray"),
        "/Width": NumberObject(mask_dimensions[0]), "/Height": NumberObject(mask_dimensions[1])})
    if case == "predictor":
        mask.get_object()[NameObject("/DecodeParms")] = DictionaryObject({NameObject("/Predictor"): NumberObject(1)})
    if case == "inverted-alpha":
        mask.get_object()[NameObject("/Decode")] = ArrayObject([NumberObject(1), NumberObject(0)])
    if case == "matte":
        mask.get_object()[NameObject("/Matte")] = ArrayObject([NumberObject(0)] * 3)
    image = stream(writer, encoded.getvalue(), {**geometry,
        "/Width": NumberObject(dimensions[0]), "/Height": NumberObject(dimensions[1]),
        "/ColorSpace": NameObject("/DeviceRGB"), "/Filter": NameObject("/DCTDecode"),
        "/Interpolate": BooleanObject(True), "/SMask": mask})
    if case == "no-mask":
        del image.get_object()["/SMask"]
    resources = DictionaryObject({NameObject("/XObject"): DictionaryObject({NameObject("/Art"): image})})
    if case == "nested":
        form = stream(writer, b"q 160 0 0 120 0 0 cm /Art Do Q\n", {
            "/Type": NameObject("/XObject"), "/Subtype": NameObject("/Form"),
            "/BBox": ArrayObject([NumberObject(v) for v in (0, 0, 160, 120)]), "/Resources": resources})
        resources = DictionaryObject({NameObject("/XObject"): DictionaryObject({NameObject("/Form"): form})})
        content = b"q 1 0 0 1 40 30 cm /Form Do Q\n"
    else:
        content = b"q 160 0 0 120 40 30 cm /Art Do Q\n"
    page[NameObject("/Resources")] = resources
    page[NameObject("/Contents")] = stream(writer, content, {})
    if case == "shared-pages":
        other = writer.add_blank_page(240, 180)
        other[NameObject("/Resources")] = resources
        other[NameObject("/Contents")] = page.raw_get("/Contents")
    writer.add_metadata({"/Title": "Independent intrinsic-dimension recovery fixture"})
    writer.add_attachment("notes.txt", b"Preserve this passive attachment.")
    result = io.BytesIO()
    writer.write(result)
    return result.getvalue(), encoded.getvalue(), alpha


def invoke(data, mode, label):
    path = scratch / f"{label}.pdf"
    path.write_bytes(data)
    with path.open("rb") as source:
        result = subprocess.run([str(engine), mode], stdin=source, capture_output=True, timeout=120)
    assert path.read_bytes() == data
    if result.returncode:
        assert not result.stdout, "Failure emitted a partial output"
        return result.returncode, result.stderr.decode(errors="replace"), b""
    magic, version, json_length, pdf_length = struct.unpack_from("<4I", result.stdout)
    assert (magic, version) == (0x31445043, 1)
    assert len(result.stdout) == 16 + json_length + pdf_length
    return 0, json.loads(result.stdout[16:16 + json_length]), result.stdout[16 + json_length:]


def renders(data):
    with pymupdf.open(stream=data, filetype="pdf") as doc:
        return [page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).samples for page in doc]


results = []
positives = ("valid", "both", "image-only", "mask-only", "shared-pages", "nested")
negatives = ("same-area", "conflicting-mask", "short-alpha", "long-alpha",
             "declared-alpha", "predictor", "inverted-alpha", "matte", "no-mask")
for case in (*positives, *negatives):
    source, jpeg, alpha = fixture(case)
    code, facts, output = invoke(source, "--convert", case + "-source")
    inspected, inspection, empty = invoke(source, "--inspect", case + "-inspection")
    assert inspected == code and not empty and (code != 0 or inspection == facts), case
    if case in negatives:
        assert code in (3, 4) and not output, (case, facts)
    else:
        assert code == 0 and output, (case, facts)
        assert facts["recoveredImages"] == (0 if case == "valid" else 1), (case, facts)
        normalized, _, _ = fixture(case, normalized=True)
        reference_code, reference_facts, reference = invoke(normalized, "--convert", case + "-reference")
        assert reference_code == 0 and reference_facts["recoveredImages"] == 0
        assert renders(output) == renders(reference), case
        result_reader = PdfReader(io.BytesIO(output))
        original_reader = PdfReader(io.BytesIO(source))
        assert result_reader.metadata == original_reader.metadata
        assert dict(result_reader.attachments) == dict(original_reader.attachments)
        for page in result_reader.pages:
            objects = page["/Resources"]["/XObject"]
            if case == "nested":
                objects = objects["/Form"]["/Resources"]["/XObject"]
            image = objects["/Art"]
            mask = image["/SMask"]
            assert (image["/Width"], image["/Height"], mask["/Width"], mask["/Height"]) == (64, 48, 64, 48)
            assert image._data == jpeg and mask.get_data() == alpha, case
        repeated, repeat_facts, repeat_output = invoke(output, "--convert", case + "-dark")
        assert repeated == 0 and repeat_facts["changedPages"] == 0 and not repeat_output, (case, repeat_facts)
        assert repeat_facts["recoveredImages"] == 0
    results.append({"case": case, "exitCode": code, "facts": facts,
                    "sourceSha256": hashlib.sha256(source).hexdigest()})
(scratch / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"Passed {len(results)} intrinsic image recovery cases")
