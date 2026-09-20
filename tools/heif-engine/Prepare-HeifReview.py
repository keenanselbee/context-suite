"""Prepare the finite sixteen-output HEIC/AVIF human review pack."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("payload", "encoder-sdk", "decoder-sdk", "cmake", "photograph"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    spec = importlib.util.spec_from_file_location("heif_payload", Path(__file__).with_name("Stage-HeifPayload.py"))
    deployment = importlib.util.module_from_spec(spec); spec.loader.exec_module(deployment)
    candidate = deployment.local(args.payload); deployment.verify(candidate)
    encoder, decoder, photograph, cmake = (path.resolve(strict=True) for path in (args.encoder_sdk, args.decoder_sdk, args.photograph, args.cmake))
    for folder in (encoder, decoder): folder.relative_to(root / ".codex-temp")
    photograph.relative_to(root / ".codex-temp/heif-review-inputs")
    if digest(photograph) != "E25CA1FF2F0C0CB5FDFD5F9B0A0BB21AC4C3DE3C84A67F35B09A85D3306249DB":
        raise ValueError("Use the pinned local Kodak Image 3 photograph")
    provenance = json.loads((photograph.parent / "provenance.json").read_text())
    for name, item in provenance["Files"].items():
        if Path(name).name != name or digest(photograph.parent / name) != item["Sha256"]:
            raise ValueError("Retained photograph provenance changed")
    for name in ("ContextSuite.Worker.exe", "payload-inventory.json", "heif"):
        if not (candidate / name).exists(): raise ValueError("Use a complete inventoried HEIF candidate")
    stage = root / ".codex-temp/heif-review" / uuid.uuid4().hex; stage.mkdir(parents=True)
    pack = stage / "pack"
    print("HEIF review evidence:", stage, flush=True)

    def run(label, command, timeout=600):
        command = [str(value) for value in command]
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=timeout)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        (stage / (label + ".command.json")).write_text(json.dumps({"Command": command, "ExitCode": result.returncode}, indent=2))
        if result.returncode: raise RuntimeError("HEIF review preparation failed: " + label)
        return result.stdout

    inventory = [sys.executable, "-B", root / "tools/audio-engine/Stage-AudioPayload.py", "--payload", candidate, "--inventory"]
    run("payload", ["powershell", "-NoProfile", "-File", root / "tools/curated-engine/Test-ProductionPayload.ps1", "-Payload", candidate,
                    "-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate", "-AllowHeifCandidate"])
    run("inventory-before", inventory)
    paths = {Path(__file__).resolve(), Path(__file__).with_name("Write-HeifReviewPage.py")}
    paths.update((root / "tools/heif-engine/Review").iterdir())
    paths.update(path for path in (root / "tools/heif-engine").iterdir() if path.suffix in (".py", ".json"))
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props",
        "global.json", "tools/Require-Private.targets", "tools/curated-engine/Test-ProductionPayload.ps1", "tools/audio-engine/Stage-AudioPayload.py",
        "tools/office-engine/Stage-OfficePayload.py") if (root / name).is_file())
    identities = {str(path): digest(path) for path in paths}
    identities.update({str(path): digest(path) for folder in (encoder, photograph.parent, candidate) for path in folder.rglob("*") if path.is_file()})
    identities.update({str(path): digest(path) for path in (decoder / "bin/libde265.dll", cmake)})
    (stage / "initial-identities.json").write_text(json.dumps(identities, indent=2))
    run("contracts-build", ["dotnet", "build", root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj", "-c", "Release", "--no-restore", "--verbosity", "quiet"])
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    run("reference-prepare", [host, "--heif-review-sources", pack, photograph])
    shutil.copytree(photograph.parent, pack / "provenance")
    run("encoder-configure", [cmake, "-S", root / "tools/heif-engine/Review", "-B", stage / "encoder-build", "-G", "Visual Studio 18 2026",
        "-A", "x64", "-DCMAKE_PREFIX_PATH=" + str(encoder)])
    run("encoder-build", [cmake, "--build", stage / "encoder-build", "--config", "Release", "--parallel", "4"])
    binary = stage / "encoder-build/Release"
    shutil.copy2(encoder / "bin/heif.dll", binary / "heif.dll"); shutil.copy2(decoder / "bin/libde265.dll", binary / "libde265.dll")
    run("fixture-encode", [binary / "ContextSuite.Heif.Review.exe", pack / "sources"])
    identities.update({str(path): digest(path) for folder in (pack, binary) for path in folder.rglob("*") if path.is_file()})
    identities.update({str(path): digest(path) for path in host.parent.iterdir() if path.is_file()})
    (stage / "inputs.json").write_text(json.dumps({"Identities": identities, "Candidate": str(candidate),
        "InventorySha256": digest(candidate / "payload-inventory.json")}, indent=2))
    if any(digest(Path(path)) != expected for path, expected in identities.items()): raise RuntimeError("Review input drift")
    print("Starting sixteen direct HEIF conversions.", flush=True)
    run("prepare", [host, "--heif-review", pack, candidate / "ContextSuite.Worker.exe"])
    print((stage / "prepare.stdout.log").read_text(errors="replace"), end="", flush=True)
    unchanged = all(digest(Path(path)) == expected for path, expected in identities.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": 0, "InputsUnchanged": unchanged}))
    if not unchanged: raise RuntimeError("Review changed an input or candidate")
    run("inventory-after", inventory)
    run("worksheet", [sys.executable, "-B", Path(__file__).with_name("Write-HeifReviewPage.py"), pack])
    print("Open the image review page:", pack / "review.html", flush=True)


if __name__ == "__main__":
    main()
