"""Opt-in active Office interruption checks against a retained, verified worker."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--create-disposable-profile", action="store_true")
    parser.add_argument("--worker", type=Path, required=True)
    parser.add_argument("--build-receipt", type=Path, required=True)
    parser.add_argument("--large-fixtures", type=Path, required=True)
    parser.add_argument("--fixtures", type=Path, required=True)
    parser.add_argument("--mode", choices=("all", "cancel", "worker-loss", "deadline"), default="all")
    args = parser.parse_args()
    if not args.create_disposable_profile:
        parser.error("Explicit disposable profile authorization is required before preparation or execution.")
    root = Path(__file__).resolve().parents[2]
    worker = args.worker.resolve(strict=True)
    worker.relative_to(root / ".codex-temp/office-worker")
    previous = json.loads(args.build_receipt.read_text(encoding="utf-8-sig"))
    recorded_sources = previous.get("sources", previous.get("source", {}))
    worker_files = previous.get("worker", previous.get("workerFiles", {}))
    if not recorded_sources or not worker_files or "ContextSuite.Worker.exe" not in worker_files:
        raise RuntimeError("Missing retained worker source/binary receipt.")
    for name, expected in recorded_sources.items():
        # Tests may evolve independently; the prepared worker implementation must match.
        normalized = name.replace("\\", "/")
        if normalized.startswith(("src/", "proprietary/src/")) and digest(root / name) != expected:
            raise RuntimeError(f"Retained worker implementation source changed: {name}")
    actual = {path.name: digest(path) for path in worker.parent.iterdir() if path.is_file()}
    if actual != worker_files:
        raise RuntimeError("Retained worker files differ from their build receipt.")
    inputs = list((root / "proprietary/tests/ContextSuite.Pdf.ContractTests").glob("*.cs"))
    inputs += [root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj",
               root / "src/ContextSuite.Application/Infrastructure/WorkerClient.cs",
               root / "src/ContextSuite.Application/Infrastructure/WorkerProcessJob.cs",
               root / "src/ContextSuite.Application/Infrastructure/OfficeSandboxOwner.cs", Path(__file__)]
    inputs += [root / "src/ContextSuite.Application/Infrastructure/OfficeOwnershipJournal.cs",
               root / "src/ContextSuite.Application/Infrastructure/PublicationFiles.cs"]
    sources = {str(path.relative_to(root)): digest(path) for path in inputs}
    fixtures = {str(folder.resolve() / name): digest(folder / name)
                for folder in (args.fixtures, args.large_fixtures)
                for name in ("Word \u00fc.docx", "Excel \u00fc.xlsx", "PowerPoint \u00fc.pptx")}
    scratch = root / ".codex-temp/office-worker" / uuid.uuid4().hex
    scratch.mkdir(parents=True)
    print("Office interruption evidence:", scratch, flush=True)
    result = subprocess.run(["dotnet", "build",
        str(root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"),
        "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
    (scratch / "build.log").write_bytes(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError("Interruption harness build failed before profile creation.")
    managed = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    receipt = {"sources": sources, "worker": str(worker), "workerFiles": actual, "mode": args.mode,
               "previousBuildReceipt": str(args.build_receipt.resolve()),
               "previousBuildReceiptSha256": digest(args.build_receipt), "fixtures": fixtures,
               "managedFiles": {path.name: digest(path) for path in managed.parent.iterdir() if path.is_file()}}
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Harness sources changed during build.")
    (scratch / "build.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    # The private adapter validates the complete pinned Office runtime before each launch.
    # Preserve the owner process until its bounded operations and profile cleanup finish.
    with (scratch / "stdout.log").open("wb") as output, (scratch / "stderr.log").open("wb") as error:
        result = subprocess.run([str(managed), "--office-worker-stop", str(worker),
            str(args.large_fixtures.resolve()), str(args.fixtures.resolve()), str(scratch / "contracts"), args.mode],
            cwd=root, stdout=output, stderr=error)
    print((scratch / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Office interruption check failed; inspect logs and owned profile receipts before retrying: {scratch}")
    report = json.loads((scratch / "contracts/results.json").read_text(encoding="utf-8"))
    modes = ["cancel", "worker-loss", "deadline"] if args.mode == "all" else [args.mode]
    if not report["Passed"] or report["Modes"] != modes or len(report["Checks"]) != 30 * len(modes):
        raise RuntimeError("Missing complete interruption evidence.")
    if any(digest(Path(name)) != expected for name, expected in fixtures.items()):
        raise RuntimeError("An original fixture changed during interruption checks.")


if __name__ == "__main__":
    main()
