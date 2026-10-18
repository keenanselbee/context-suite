"""Compare paired authored Office exports, including source parts and rendered PDF pages.

Requires the optional local PyMuPDF inspection dependency. This is a fixture
comparison, not production admission for arbitrary PDFs or a general fidelity claim.
"""

import argparse
import hashlib
import json
from pathlib import Path
import zipfile

import pymupdf


ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def parts(path):
    with zipfile.ZipFile(path) as package:
        names = package.namelist()
        if len(set(names)) != len(names):
            raise ValueError("Duplicate authored document parts.")
        return {name: hashlib.sha256(package.read(name)).hexdigest() for name in names}


def exports(report):
    path = Path(report).resolve(strict=True)
    if not path.is_relative_to(ROOT / ".codex-temp/office-execution"):
        raise ValueError("Use retained authored Office execution results.")
    value = json.loads(path.read_bytes())
    if not value["Passed"] or not all(p["Removed"] and p["ContextRetired"] for p in value["Profiles"]):
        raise ValueError("Incomplete publication or native cleanup evidence.")
    result = {}
    for row in value["Results"]:
        source = Path(row["Path"])
        publication = row["Publication"]
        if row["State"] != 5 or not publication["IsCommitted"] or publication["Outcome"] != 0:
            raise ValueError("A fixture did not publish successfully: " + source.name)
        if digest(source) != value["Originals"][str(source)]["Sha256"]:
            raise ValueError("Original fixture changed after execution.")
        if source.name in result:
            raise ValueError("Duplicate fixture name.")
        result[source.name] = (source, Path(publication["OutputPath"]))
    return value["Mode"], result


def compare(full, reduced, output):
    first_mode, first = exports(full)
    second_mode, second = exports(reduced)
    if first_mode != second_mode or first.keys() != second.keys():
        raise ValueError("Paired authored fixture modes/membership differ.")
    rows = []
    for index, name in enumerate(first):
        left_source, left_pdf = first[name]
        right_source, right_pdf = second[name]
        if parts(left_source) != parts(right_source):
            raise ValueError("Paired source document parts differ: " + name)
        identities = {str(p): digest(p) for p in (left_source, right_source, left_pdf, right_pdf)}
        observations = []
        with pymupdf.open(left_pdf) as left, pymupdf.open(right_pdf) as right:
            if len(left) != len(right) or not 0 < len(left) <= 200:
                raise ValueError("Unexpected or changed page count: " + name)
            for page in range(len(left)):
                a, b = left[page], right[page]
                geometry = a.rect == b.rect and a.rotation == b.rotation
                text = a.get_text("dict") == b.get_text("dict")
                ap = a.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=True)
                bp = b.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=True)
                pixels = (ap.width, ap.height, ap.stride, ap.samples) == (bp.width, bp.height, bp.stride, bp.samples)
                observations.append({"page": page + 1, "geometryEqual": geometry,
                                     "textAndBoundsEqual": text, "pixelsEqual144Dpi": pixels})
                if index == 0 and page == 0 or not pixels:
                    a.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).save(
                        output / f"{first_mode}-{index}-{page}-full.png")
                    b.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).save(
                        output / f"{first_mode}-{index}-{page}-reduced.png")
        if any(digest(Path(p)) != expected for p, expected in identities.items()):
            raise ValueError("Paired source/PDF changed during inspection.")
        rows.append({"name": name, "sourcesHaveEqualParts": True, "identities": identities, "pages": observations})
    passed = all(all(p[key] for key in ("geometryEqual", "textAndBoundsEqual", "pixelsEqual144Dpi"))
                 for row in rows for p in row["pages"])
    return {"mode": first_mode, "passed": passed, "documents": rows,
            "fullReportSha256": digest(Path(full)), "reducedReportSha256": digest(Path(reduced))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--full", required=True, type=Path)
    parser.add_argument("--reduced", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    if not output.is_relative_to(ROOT / ".codex-temp") or output.exists():
        parser.error("Use a fresh repository scratch output directory.")
    output.mkdir(parents=True)
    report = compare(args.full, args.reduced, output)
    report["renderer"] = "PyMuPDF " + pymupdf.VersionBind
    (output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"mode": report["mode"], "passed": report["passed"],
                      "documents": len(report["documents"]), "output": str(output)}))
    if not report["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
