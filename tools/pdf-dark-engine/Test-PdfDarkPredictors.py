"""Check retained PNG predictor samples with independently authored PDF images."""
import argparse
import io
import json
import pathlib
import struct
import subprocess
import zlib

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import (DecodedStreamObject, DictionaryObject, EncodedStreamObject,
                           NameObject, NumberObject)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--engine", required=True)
parser.add_argument("--scratch", required=True)
args = parser.parse_args()
root = pathlib.Path(args.scratch).resolve()
repository = pathlib.Path(__file__).resolve().parents[2]
if root.exists() or not root.is_relative_to(repository / ".codex-temp"):
    raise ValueError("Use fresh repository-local scratch")
root.mkdir(parents=True)
engine = pathlib.Path(args.engine).resolve(strict=True)


def dictionary(**items):
    return DictionaryObject({NameObject("/" + key): value for key, value in items.items()})


def filtered(samples, width, channels, mode):
    if mode == 1:
        return samples
    stride = width * channels
    result = bytearray()
    previous = bytes(stride)
    for y in range(len(samples) // stride):
        row = samples[y * stride:(y + 1) * stride]
        kind = y % 5 if mode == 15 else mode - 10
        result.append(kind)
        for x, value in enumerate(row):
            left = row[x - channels] if x >= channels else 0
            up = previous[x]
            corner = previous[x - channels] if x >= channels else 0
            prediction = left + up - corner
            distances = [abs(prediction - candidate) for candidate in (left, up, corner)]
            paeth = (left, up, corner)[distances.index(min(distances))]
            base = (0, left, up, (left + up) // 2, paeth)[kind]
            result.append((value - base) & 255)
        previous = row
    return bytes(result)


def fixture(mode, with_mask=False, mutation=None):
    writer = PdfWriter()
    page = writer.add_blank_page(width=240, height=180)
    width, height = 13, 11
    samples = bytes((x * 17 + y * 23 + c * 73) % 256
                    for y in range(height) for x in range(width) for c in range(3))

    def image(data, channels):
        params = dictionary(Predictor=NumberObject(mode), Columns=NumberObject(width),
                            Colors=NumberObject(channels))  # BitsPerComponent defaults to eight.
        encoded = filtered(data, width, channels, mode)
        if channels == 3:
            if mutation == "missing-colors": del params["/Colors"]
            if mutation == "missing-columns": del params["/Columns"]
            if mutation == "wrong-colors": params[NameObject("/Colors")] = NumberObject(1)
            if mutation == "wrong-bits": params[NameObject("/BitsPerComponent")] = NumberObject(4)
            if mutation == "unsupported-predictor": params[NameObject("/Predictor")] = NumberObject(2)
            if mutation == "bad-row": encoded = b"\x05" + encoded[1:]
            if mutation == "truncated": encoded = encoded[:-1]
        stream = EncodedStreamObject()
        stream.update(dictionary(Type=NameObject("/XObject"), Subtype=NameObject("/Image"),
            Width=NumberObject(width), Height=NumberObject(height), BitsPerComponent=NumberObject(8),
            ColorSpace=NameObject("/DeviceRGB" if channels == 3 else "/DeviceGray"),
            Filter=NameObject("/FlateDecode"), DecodeParms=params))
        stream._data = zlib.compress(encoded)
        return stream

    obj = image(samples, 3)
    if with_mask:
        obj[NameObject("/SMask")] = writer._add_object(image(bytes([255]) * width * height, 1))
    page[NameObject("/Resources")] = dictionary(XObject=dictionary(Im=writer._add_object(obj)))
    content = DecodedStreamObject()
    content.set_data(b"1 g 0 0 240 180 re f q 65 0 0 55 30 40 cm /Im Do Q\n")
    page[NameObject("/Contents")] = writer._add_object(content)
    output = io.BytesIO()
    writer.write(output)
    return output.getvalue(), samples


def invoke(data, mode):
    result = subprocess.run([str(engine), mode], input=data, capture_output=True, timeout=45)
    if result.returncode:
        assert not result.stdout
        return result.returncode, result.stderr.decode(errors="replace"), b""
    magic, version, size, pdf_size = struct.unpack_from("<IIII", result.stdout)
    assert (magic, version) == (0x31445043, 1) and len(result.stdout) == 16 + size + pdf_size
    return 0, json.loads(result.stdout[16:16 + size]), result.stdout[16 + size:]


results = []
for mode in (1, 10, 11, 12, 13, 14, 15):
    for mask in (False, True):
        source, samples = fixture(mode, mask)
        code, facts, output = invoke(source, "--convert")
        assert code == 0 and output, (mode, mask, facts)
        image = PdfReader(io.BytesIO(output)).pages[0]["/Resources"]["/XObject"]["/Im"]
        assert image.get_data() == samples
        if mask:
            assert image["/SMask"].get_data() == bytes([255]) * 13 * 11
        before, after = pymupdf.open(stream=source), pymupdf.open(stream=output)
        # Opaque retained image pixels must be identical through an independent renderer.
        clip = pymupdf.Rect(30, 85, 95, 140)
        assert before[0].get_pixmap(clip=clip).samples == after[0].get_pixmap(clip=clip).samples
        code, repeat, empty = invoke(output, "--inspect")
        assert code == 0 and repeat["changedPages"] == 0 and not empty
        (root / f"predictor-{mode}-mask-{mask}.pdf").write_bytes(output)
        results.append(dict(predictor=mode, mask=mask, samples=True, renderedPixels=True, repeat=True))

for mutation in ("missing-colors", "missing-columns", "wrong-colors", "wrong-bits",
                 "unsupported-predictor", "bad-row", "truncated"):
    source, _ = fixture(15, mutation=mutation)
    code, reason, output = invoke(source, "--convert")
    assert code != 0 and not output, (mutation, code, reason)
    results.append(dict(case=mutation, refused=True, reason=reason))
(root / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(f"PNG predictors: {len(results)} cases passed; samples, rendering, and refusal guards checked.")
