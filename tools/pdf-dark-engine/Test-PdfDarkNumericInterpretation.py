"""Probe malformed PDF numbers against independent PDFium and MuPDF renders.

This is test-only evidence for a narrow damaged-content recovery policy. Pixel
agreement describes reader behavior, not the author's intended coordinates.
"""

import argparse
import hashlib
import io
import json
import pathlib
import re
import struct
import subprocess

import numpy as np
import pymupdf
from pypdf import PdfWriter
from pypdf.generic import DecodedStreamObject, DictionaryObject, NameObject


REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
CANDIDATE = re.compile(r"0{1,3}\.0{1,3}-[0-9]{1,4}\Z", re.ASCII)
DRAWING_CONTEXTS = ("m", "l", "c", "v", "y", "cm", "re")
OTHER_CONTEXTS = ("Td", "rg", "w")
TOKENS = (
    ("0.00-60", "0.00", "candidate"),
    ("00.0-12", "00.0", "candidate"),
    ("0.0-0", "0.0", "candidate"),
    ("0.000-999", "0.000", "candidate"),
    ("000.000-1", "000.000", "candidate"),
    ("00.00-0007", "00.00", "candidate"),
    ("1.25-60", "1.25", "nonzero-prefix"),
    ("1e-2", "1", "exponent"),
    ("0.00+60", "0.00", "plus-sign"),
    ("0.00--60", "0.00", "multiple-signs"),
    ("-0.00-60", "-0.00", "signed-prefix"),
    ("0.00-60foo", "0.00", "trailing-text"),
    ("000-12", "000", "integer-prefix"),
    ("0000.0-12", "0000.0", "long-integer-part"),
    ("0.0000-12", "0.0000", "long-fraction"),
    ("0.00-10000", "0.00", "long-suffix"),
)


def content(context, value):
    background = "1 1 1 rg 0 0 160 120 re f\n"
    if context == "m":
        drawing = f"0 0 0 RG 3 w {value} 20 m 110 90 l S"
    elif context == "l":
        drawing = f"0 0 0 RG 3 w 20 20 m {value} 90 l S"
    elif context == "c":
        drawing = f"0 0 0 RG 3 w 10 15 m {value} 95 75 95 120 20 c S"
    elif context == "v":
        drawing = f"0 0 0 RG 3 w 10 15 m {value} 95 120 20 v S"
    elif context == "y":
        drawing = f"0 0 0 RG 3 w 10 15 m {value} 95 120 20 y S"
    elif context == "cm":
        drawing = f"q 1 0 0 1 {value} 0 cm 0 0 0 rg 20 20 60 65 re f Q"
    elif context == "re":
        drawing = f"0 0 0 rg {value} 20 75 65 re f"
    elif context == "Td":
        drawing = f"0 0 0 rg BT /F1 28 Tf {value} 45 Td (Visible) Tj ET"
    elif context == "rg":
        drawing = f"{value} 0 0 rg 20 20 75 65 re f"
    elif context == "w":
        drawing = f"0 0 0 RG {value} w 20 25 m 115 85 l S"
    else:
        raise ValueError(f"Unknown context: {context}")
    return (background + drawing + "\n").encode("ascii")


def fixture(context, value):
    writer = PdfWriter()
    page = writer.add_blank_page(width=160, height=120)
    stream = DecodedStreamObject()
    stream.set_data(content(context, value))
    page[NameObject("/Contents")] = writer._add_object(stream)
    if context == "Td":
        font = DictionaryObject({
            NameObject("/Type"): NameObject("/Font"),
            NameObject("/Subtype"): NameObject("/Type1"),
            NameObject("/BaseFont"): NameObject("/Helvetica"),
        })
        page[NameObject("/Resources")] = DictionaryObject({
            NameObject("/Font"): DictionaryObject({NameObject("/F1"): writer._add_object(font)}),
        })
    output = io.BytesIO()
    writer.write(output)
    data = output.getvalue()
    assert len(data) < 1_000_000
    return data


def render_pdfium(renderer, source):
    result = subprocess.run([str(renderer), "--render-page", "0"], input=source,
                            capture_output=True, timeout=20, check=False)
    if result.returncode:
        return None, f"exit {result.returncode}: {result.stderr[:160].decode(errors='replace').strip()}"
    if len(result.stdout) < 32:
        return None, "short PDFium response"
    magic, version, kind, index, width, height, stride, length = struct.unpack_from("<IIIIIIII", result.stdout)
    if (magic, version, kind, index) != (0x31525043, 1, 2, 0) or not (0 < width <= 1024 and 0 < height <= 1024):
        return None, "invalid PDFium header"
    if stride != width * 4 or length != height * stride or len(result.stdout) != 32 + length:
        return None, "invalid PDFium pixel length"
    bgra = np.frombuffer(result.stdout, dtype=np.uint8, offset=32).reshape(height, width, 4)
    return bgra[:, :, [2, 1, 0, 3]], None


def render_mupdf(source):
    try:
        with pymupdf.open(stream=source, filetype="pdf") as document:
            pixmap = document[0].get_pixmap(matrix=pymupdf.Matrix(150 / 72, 150 / 72), alpha=True)
            if pixmap.n != 4 or pixmap.width > 1024 or pixmap.height > 1024:
                return None, "invalid MuPDF pixel shape"
            pixels = np.frombuffer(pixmap.samples, dtype=np.uint8).reshape(pixmap.height, pixmap.width, 4)
            return pixels.copy(), None
    except Exception as error:
        return None, f"{type(error).__name__}: {error}"


def difference(left, right):
    if left is None or right is None:
        return None
    if left.shape != right.shape:
        return None
    return int(np.count_nonzero(np.any(left != right, axis=2)))


def compare(renderer, context, token, normalized, scratch, index):
    source = fixture(context, token)
    repaired = fixture(context, normalized)
    sentinel = fixture(context, ".75" if context == "rg" else "8")
    (scratch / f"{index:03d}-original.pdf").write_bytes(source)
    (scratch / f"{index:03d}-normalized.pdf").write_bytes(repaired)
    row = {"context": context, "token": token, "normalized": normalized,
           "sourceSha256": hashlib.sha256(source).hexdigest()}
    for engine, render in (("pdfium", lambda data: render_pdfium(renderer, data)),
                           ("mupdf", render_mupdf)):
        original_pixels, original_error = render(source)
        normalized_pixels, normalized_error = render(repaired)
        sentinel_pixels, sentinel_error = render(sentinel)
        row[engine] = {
            "originalError": original_error,
            "normalizedError": normalized_error,
            "sentinelError": sentinel_error,
            "sameShape": original_pixels is not None and normalized_pixels is not None and
                         original_pixels.shape == normalized_pixels.shape,
            "changedPixels": difference(original_pixels, normalized_pixels),
            "sentinelPixels": difference(normalized_pixels, sentinel_pixels),
        }
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--renderer", required=True, help="Native PDFium renderer executable")
    parser.add_argument("--scratch", required=True, help="Fresh directory under repository .codex-temp")
    args = parser.parse_args()
    renderer = pathlib.Path(args.renderer).resolve(strict=True)
    scratch = pathlib.Path(args.scratch).resolve()
    if not renderer.is_file() or not scratch.is_relative_to(REPOSITORY / ".codex-temp") or scratch.exists():
        raise ValueError("Use an existing renderer and a fresh repository-local scratch directory")
    scratch.mkdir(parents=True)

    rows = []
    for token, normalized, token_class in TOKENS:
        assert (CANDIDATE.fullmatch(token) is not None) == (token_class == "candidate")
        contexts = DRAWING_CONTEXTS + OTHER_CONTEXTS if token_class == "candidate" else ("m", "cm", "re", "rg")
        for context in contexts:
            row = compare(renderer, context, token, normalized, scratch, len(rows) + 1)
            row["tokenClass"] = token_class
            row["eligibleContext"] = context in DRAWING_CONTEXTS
            row["candidateAdmissible"] = token_class == "candidate" and row["eligibleContext"]
            rows.append(row)

    failures = [row for row in rows if row["candidateAdmissible"] and any(
        not row[engine]["sameShape"] or row[engine]["changedPixels"] != 0 or
        not row[engine]["sentinelPixels"]
        for engine in ("pdfium", "mupdf"))]
    report = {
        "scope": "Independently authored reader interpretation probe; no author-intent claim",
        "candidateGrammar": CANDIDATE.pattern,
        "drawingContexts": DRAWING_CONTEXTS,
        "excludedContexts": OTHER_CONTEXTS,
        "renderer": str(renderer),
        "rendererSha256": hashlib.sha256(renderer.read_bytes()).hexdigest(),
        "pymupdfVersion": pymupdf.VersionBind,
        "cases": len(rows),
        "candidateCases": sum(row["candidateAdmissible"] for row in rows),
        "candidateFailures": len(failures),
        "results": rows,
    }
    (scratch / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("cases", "candidateCases", "candidateFailures")}, indent=2))
    if failures:
        print("Candidate failures:")
        for row in failures:
            print(row["token"], row["context"], row["pdfium"], row["mupdf"])
        raise SystemExit(1)


if __name__ == "__main__":
    main()
