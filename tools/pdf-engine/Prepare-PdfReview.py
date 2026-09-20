"""Export four authored PDF appearance controls using an unchanged inventoried worker."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    spec = importlib.util.spec_from_file_location("pdf_review_paths", root / "tools/heif-engine/Stage-HeifPayload.py")
    paths = importlib.util.module_from_spec(spec); spec.loader.exec_module(paths)
    candidate = paths.local(args.payload)
    for name in ("ContextSuite.Worker.exe", "payload-inventory.json", "pdf-engine", "pdf-renderer", "pdf-validator"):
        if not (candidate / name).exists():
            parser.error("Use an inventoried combined payload containing the worker and PDF components.")
    stage = root / ".codex-temp/pdf-review" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("PDF review evidence:", stage, flush=True)

    def run(label, command):
        command = [str(value) for value in command]
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=600)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        (stage / (label + ".command.json")).write_text(json.dumps({"Command": command, "ExitCode": result.returncode}, indent=2))
        if result.returncode:
            raise RuntimeError("PDF review preparation failed: " + label)
        return result.stdout

    flags = ["-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"]
    if (candidate / "heif").is_dir(): flags.append("-AllowHeifCandidate")
    run("payload", ["powershell", "-NoProfile", "-File", root / "tools/curated-engine/Test-ProductionPayload.ps1",
                    "-Payload", candidate, *flags])
    inventory = [sys.executable, "-B", root / "tools/audio-engine/Stage-AudioPayload.py", "--payload", candidate, "--inventory"]
    run("inventory-before", inventory)
    files = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "tests/ContextSuite.Core.ContractTests"):
        files.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    files.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json",
        "tools/Require-Private.targets", "tools/curated-engine/Test-ProductionPayload.ps1", "tools/audio-engine/Stage-AudioPayload.py",
        "tools/pdf-engine/Stage-PdfPayload.py", "tools/heif-engine/Stage-HeifPayload.py", "tools/heif-engine/payload-candidate.json") if (root / name).is_file())
    identities = {str(path): digest(path) for path in files}
    identities.update({str(path): digest(path) for path in candidate.rglob("*") if path.is_file()})
    run("contracts-build", ["dotnet", "build", root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj",
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"])
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    identities.update({str(path): digest(path) for path in host.parent.iterdir() if path.is_file()})
    (stage / "inputs.json").write_text(json.dumps({"Identities": identities, "Payload": str(candidate),
        "InventorySha256": digest(candidate / "payload-inventory.json")}, indent=2))
    if any(digest(Path(path)) != expected for path, expected in identities.items()):
        raise RuntimeError("PDF review input drift")
    print("Starting four PDF-to-PNG commands in one application batch.", flush=True)
    run("prepare", [host, "--pdf-review", stage / "runs", candidate / "ContextSuite.Worker.exe"])
    print((stage / "prepare.stdout.log").read_text(errors="replace"), end="", flush=True)
    unchanged = all(digest(Path(path)) == expected for path, expected in identities.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": 0, "InputsUnchanged": unchanged}))
    if not unchanged:
        raise RuntimeError("PDF review changed an input or candidate")
    run("inventory-after", inventory)
    result = json.loads((stage / "runs/results.json").read_text())
    if not result["Passed"] or len(result["Observations"]) != 4:
        raise RuntimeError("PDF appearance controls are incomplete")
    (stage / "verified.json").write_text(json.dumps({"Passed": True, "InputsUnchanged": True,
        "Worker": str(candidate / "ContextSuite.Worker.exe"), "InventorySha256": digest(candidate / "payload-inventory.json"),
        "ResultSha256": digest(stage / "runs/results.json"), "HumanAcceptance": "Not reviewed"}, indent=2) + "\n")
    print("Verified PDF review:", stage / "runs/results.json", flush=True)


if __name__ == "__main__":
    main()
