"""Add a verified four-scene PDF review to a retained document worksheet selection."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("document_review", ROOT / "tools/office-engine/Prepare-DocumentReview.py")
review = importlib.util.module_from_spec(spec); spec.loader.exec_module(review)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--document-manifest", type=Path, required=True)
    parser.add_argument("--pdf-review", type=Path, required=True)
    args = parser.parse_args()
    _, raw = review.read_local(args.document_manifest)
    manifest = json.loads(raw)
    receipt_path, receipt_bytes = review.read_local(args.pdf_review / "verified.json")
    receipt = json.loads(receipt_bytes)
    report_path, report_bytes = review.read_local(args.pdf_review / "runs/results.json")
    report = json.loads(report_bytes)
    if not receipt["Passed"] or not receipt["InputsUnchanged"] or not report["Passed"] or (
            hashlib.sha256(report_bytes).hexdigest().upper() != receipt["ResultSha256"] or report["Worker"] != receipt["Worker"]):
        raise ValueError("Use completed, unchanged PDF review evidence")
    expected = {"Text and vectors", "Synthetic scanned page", "Rotated cropped page", "Transparent image"}
    if len(report["Observations"]) != 4 or {row["Name"] for row in report["Observations"]} != expected:
        raise ValueError("The four declared PDF appearance scenes are required")
    if any(case["Group"] == "PDF" for case in manifest["Cases"]):
        raise ValueError("The document selection already contains PDF review cases")

    def artifact(path, kind, label, expected_hash):
        path, data = review.read_local(path)
        if hashlib.sha256(data).hexdigest().upper() != expected_hash:
            raise ValueError("PDF review artifact changed")
        return {"Path": str(path), "Kind": kind, "Label": label, "Sha256": expected_hash}

    for index, row in enumerate(report["Observations"]):
        files = [artifact(row["Source"], "source", "Original authored PDF", row["SourceSha256"]),
                 artifact(row["Output"], "output", "Published PNG page at 150 DPI", row["OutputSha256"]),
                 artifact(report_path, "evidence", "Application and pixel checks", receipt["ResultSha256"]),
                 artifact(receipt_path, "evidence", "Unchanged exact-worker receipt", hashlib.sha256(receipt_bytes).hexdigest().upper())]
        if row["Reference"]:
            files.append(artifact(row["Reference"], "reference", "Authored expected bitmap", row["ReferenceSha256"]))
        manifest["Cases"].append({"Id": f"pdf-{index + 1:02}", "Group": "PDF", "Title": row["Name"], "Status": "pass",
            "Instructions": row["Instructions"] + " Automated scope: " + row["AutomatedScope"] +
                " PNG page backgrounds retain transparency; use a white or checkerboard viewer background so black text remains readable.", "Files": files})
    manifest["Remaining"]["PDF"] = "Four authored appearance controls are prepared. Human inspection, optimization acceptance and final-candidate replay remain open. The scan is synthetic."
    manifest["ParentManifestSha256"] = hashlib.sha256(raw).hexdigest().upper()
    manifest["PdfReviewReceiptSha256"] = hashlib.sha256(receipt_bytes).hexdigest().upper()
    folder = ROOT / ".codex-temp/pdf-review-manifests"; folder.mkdir(exist_ok=True)
    target = folder / (uuid.uuid4().hex + ".json")
    with target.open("x") as stream:
        json.dump(manifest, stream, indent=2)
        stream.write("\n")
    print("Combined document/PDF selection:", target)


if __name__ == "__main__":
    main()
