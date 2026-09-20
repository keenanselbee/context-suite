"""Compose and verify a scratch HEIF/expanded-audio/Voice payload with existing PDF/Office engines.

Does not reserve a release version, install anything or replace a retained stage.
The final versioned candidate and its acceptance replay remain separate.
"""
import argparse
import hashlib
import importlib.util
import json
import pathlib
import shutil
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]


def load(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def identities(folder):
    return {path.relative_to(folder).as_posix(): digest(path)
            for path in sorted(folder.rglob("*")) if path.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-payload", required=True, type=pathlib.Path)
    parser.add_argument("--heif-distribution", required=True, type=pathlib.Path)
    parser.add_argument("--audio-distribution", required=True, type=pathlib.Path)
    parser.add_argument("--voice-distribution", required=True, type=pathlib.Path)
    parser.add_argument("--audio-manifest", required=True, type=pathlib.Path)
    args = parser.parse_args()
    audio = load("combined_audio", "tools/audio-engine/Stage-AudioPayload.py")
    heif = load("combined_heif", "tools/heif-engine/Stage-HeifPayload.py")
    base = heif.local(args.base_payload)
    bundles = {name: heif.local(path) for name, path in (
        ("Heif", args.heif_distribution), ("Audio", args.audio_distribution), ("Voice", args.voice_distribution))}
    manifest = heif.local(args.audio_manifest)
    if not base.is_dir() or not manifest.is_file() or any(not path.is_dir() for path in bundles.values()):
        raise ValueError("Existing verified local payload, bundles and fixture manifest are required.")
    evidence = ROOT / ".codex-temp/combined-inputs" / uuid.uuid4().hex
    evidence.mkdir(parents=True, exist_ok=False)
    print("Combined input evidence:", evidence, flush=True)
    print("Checking the retained baseline inventory before copying.", flush=True)
    audio.verify_inventory(base)
    before = {"Base": identities(base), "Bundles": {name: identities(path) for name, path in bundles.items()},
              "AudioManifest": digest(manifest)}
    sources = {}
    for folder in ("src", "proprietary/src", "tests/ContextSuite.Core.ContractTests", "tools"):
        for path in (ROOT / folder).rglob("*"):
            if path.is_file() and not {"obj", "bin", "__pycache__"}.intersection(path.relative_to(ROOT).parts) and path.suffix in (
                    ".cs", ".csproj", ".xaml", ".icc", ".py", ".ps1", ".json", ".props", ".targets", ".patch"):
                sources[str(path)] = digest(path)
    sources.update({str(path): digest(path) for path in ROOT.iterdir() if path.suffix in (".props", ".targets") or path.name == "global.json"})
    (evidence / "inputs.json").write_text(json.dumps({"Paths": {"Base": str(base), "Bundles": {name: str(path) for name, path in bundles.items()},
        "AudioManifest": str(manifest)}, "Identities": before, "Sources": sources}, indent=2) + "\n")
    checks = []

    def run(name, command, timeout=300):
        command = list(map(str, command))
        with (evidence / (name + ".log")).open("xb") as log:
            child = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
        (evidence / (name + ".command.json")).write_text(json.dumps({"Command": command, "ExitCode": child.returncode}, indent=2) + "\n")
        if child.returncode:
            raise RuntimeError(name + " failed; see retained log.")
        checks.append(name)
        print("Passed:", name, flush=True)

    def verify_payload(name, path):
        flags = ["-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"]
        if (path / "heif").is_dir():
            flags.append("-AllowHeifCandidate")
        run(name, ["powershell", "-NoProfile", "-File", ROOT / "tools/curated-engine/Test-ProductionPayload.ps1", "-Payload", path, *flags])

    code = 1
    try:
        verify_payload("base-payload", base)
        combined = evidence / "combined"
        # Omit old optional engines and the old inventory only while copying into
        # a new destination. Never delete members or receipts from the source.
        def ignored(directory, names):
            return set(names).intersection({"audio-engine", "heif", "payload-inventory.json"}) if pathlib.Path(directory) == base else set()
        shutil.copytree(base, combined, ignore=ignored)
        heif.stage(bundles["Heif"], combined)
        audio.stage(bundles["Audio"], combined, bundles["Voice"])
        if audio.verify(combined).get("profile") != "expanded":
            raise ValueError("Combined candidate requires the expanded audio profile.")
        for project in ("ContextSuite.Application", "ContextSuite.Worker"):
            run(project + "-build", ["dotnet", "build", ROOT / "src" / project / (project + ".csproj"), "-c", "Release", "--no-restore", "--nologo"])
            for path in (ROOT / "artifacts/managed/bin" / project / "Release/net10.0-windows").iterdir():
                if path.is_file():
                    shutil.copy2(path, combined / path.name)
        verify_payload("combined-payload", combined)
        inventory = [{"path": path.relative_to(combined).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)}
                     for path in sorted(combined.rglob("*")) if path.is_file()]
        (combined / "payload-inventory.json").write_text(json.dumps(inventory, indent=2) + "\n")
        audio.verify_inventory(combined)
        size = sum(item["bytes"] for item in inventory) + (combined / "payload-inventory.json").stat().st_size
        if size > 1_650_000_000:
            raise ValueError("Combined payload exceeds the predeclared 1.65 GB engineering ceiling.")
        # The missing-provider negative control is a separate minimal worker;
        # the full inventoried candidate stays untouched throughout the test.
        no_voice = evidence / "worker-no-voice"
        no_voice.mkdir()
        for path in combined.iterdir():
            if path.is_file() and path.name != "payload-inventory.json":
                shutil.copy2(path, no_voice / path.name)
        shutil.copytree(combined / "audio-engine", no_voice / "audio-engine", ignore=shutil.ignore_patterns("voice"))
        control_hashes = identities(no_voice)
        run("test-host-build", ["dotnet", "build", ROOT / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj",
                                "-c", "Release", "--no-restore", "--nologo"])
        host = ROOT / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
        run("expanded-audio-direct", [host, "--expanded-audio-direct", evidence / "audio-results", combined / "ContextSuite.Worker.exe", manifest], timeout=1800)
        verify_payload("combined-payload-after", combined)
        audio.verify_inventory(combined)
        if identities(no_voice) != control_hashes or before != {"Base": identities(base),
                "Bundles": {name: identities(path) for name, path in bundles.items()}, "AudioManifest": digest(manifest)} or any(
                    digest(pathlib.Path(path)) != value for path, value in sources.items()):
            raise ValueError("Original payload, bundles, test control, manifest or sources changed.")
        sizes = {}
        for item in inventory:
            group = item["path"].split("/")[0] if "/" in item["path"] else "top-level"
            sizes[group] = sizes.get(group, 0) + item["bytes"]
        (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "Payload": str(combined),
            "InventorySha256": digest(combined / "payload-inventory.json"), "FileCount": len(inventory) + 1,
            "TotalBytesIncludingInventory": size, "GroupBytes": sizes,
            "Scope": "Scratch combined inputs only; final versioned candidate, Office fidelity/security and human acceptance remain open."}, indent=2) + "\n")
        code = 0
    finally:
        (evidence / "exit.json").write_text(json.dumps({"ExitCode": code}) + "\n")
    return code


if __name__ == "__main__":
    sys.dont_write_bytecode = True
    raise SystemExit(main())
