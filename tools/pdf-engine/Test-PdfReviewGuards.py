"""Check PDF review refuses invalid candidates and incomplete/duplicate review evidence."""

import argparse
import json
from pathlib import Path
import subprocess
import sys
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf-review", type=Path, required=True)
    parser.add_argument("--document-manifest", type=Path, required=True)
    parser.add_argument("--combined-manifest", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    stage = root / ".codex-temp/pdf-review-guards" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    checks = []
    def refuse(name, script, arguments, expected):
        before = {name: set((root / ".codex-temp" / name).iterdir()) for name in ("pdf-review", "pdf-review-manifests")}
        result = subprocess.run([sys.executable, "-B", str(root / "tools/pdf-engine" / script), *map(str, arguments)],
                                cwd=root, capture_output=True, text=True, timeout=30)
        (stage / (name + ".log")).write_text(result.stdout + result.stderr)
        if result.returncode == 0 or expected not in result.stderr:
            raise AssertionError("Unexpected review guard outcome: " + name)
        if any(set((root / ".codex-temp" / name).iterdir()) != entries for name, entries in before.items()):
            raise AssertionError("Rejected input created review evidence")
        checks.append(name)
    refuse("outside-candidate", "Prepare-PdfReview.py", ["--payload", root / "README.md"], "isolated repository")
    refuse("missing-components", "Prepare-PdfReview.py", ["--payload", stage], "inventoried combined payload")
    incomplete = stage / "incomplete"; incomplete.mkdir()
    (incomplete / "runs").mkdir()
    (incomplete / "verified.json").write_text(json.dumps({"Passed": False, "InputsUnchanged": False}))
    (incomplete / "runs/results.json").write_text('{"Passed":false}')
    refuse("failed-review", "Write-PdfReviewManifest.py", ["--pdf-review", incomplete,
        "--document-manifest", args.document_manifest], "completed, unchanged")
    refuse("duplicate-pdf-cases", "Write-PdfReviewManifest.py", ["--pdf-review", args.pdf_review,
        "--document-manifest", args.combined_manifest], "already contains PDF")
    receipt = json.loads((args.pdf_review / "verified.json").read_text())
    receipt["ResultSha256"] = "0" * 64
    (incomplete / "verified.json").write_text(json.dumps(receipt))
    (incomplete / "runs/results.json").write_bytes((args.pdf_review / "runs/results.json").read_bytes())
    refuse("changed-review-report", "Write-PdfReviewManifest.py", ["--pdf-review", incomplete,
        "--document-manifest", args.document_manifest], "completed, unchanged")
    (stage / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks,
        "Scope": "Admission before build, native execution or review creation."}, indent=2) + "\n")
    print(f"Passed {len(checks)} PDF review guards: {stage}")


if __name__ == "__main__":
    main()
