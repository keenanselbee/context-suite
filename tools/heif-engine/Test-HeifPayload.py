"""Verify HEIF staging, copy failures and a fresh combined scratch worker."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("heif_payload", Path(__file__).with_name("Stage-HeifPayload.py"))
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distribution", required=True, type=Path)
    parser.add_argument("--base-payload", required=True, type=Path)
    args = parser.parse_args()
    root = payload.ROOT
    bundle = payload.distribution.local(args.distribution)
    base = payload.local(args.base_payload)
    if not base.is_dir() or not (base / "payload-inventory.json").is_file():
        raise ValueError("A complete retained base payload and inventory are required")
    if (base / "heif").exists():
        raise ValueError("Use the retained base without HEIF")
    evidence = root / ".codex-temp/heif-payload-tests" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    print("HEIF payload evidence:", evidence, flush=True)
    checks = []
    sources = {Path(__file__).resolve(), Path(__file__).with_name("Stage-HeifPayload.py"),
               root / "tools/Build-Production.ps1", root / "tools/curated-engine/Test-ProductionPayload.ps1",
               root / "tools/audio-engine/Stage-AudioPayload.py"}
    sources.update(path for path in payload.TOOLS.iterdir() if path.is_file() and path.suffix in (".py", ".json", ".patch"))
    for folder in ("src", "proprietary/src"):
        sources.update(path for path in (root / folder).rglob("*") if path.is_file() and path.suffix in (".cs", ".csproj", ".icc", ".xaml"))
    sources.update(path for path in root.iterdir() if path.suffix in (".props", ".targets") or path.name == "global.json")
    inputs = {str(path): digest(path) for path in sources}
    inputs.update({str(path): digest(path) for path in bundle.iterdir() if path.is_file()})
    base_files = {path.relative_to(base).as_posix(): digest(path) for path in base.rglob("*") if path.is_file()}
    (evidence / "inputs.json").write_text(json.dumps({"Sources": inputs, "Base": str(base), "BaseFiles": base_files}, indent=2))

    def passed(name):
        checks.append(name); print("Passed:", name, flush=True)

    def refused(name, action):
        try:
            action()
        except (ValueError, FileNotFoundError):
            passed(name)
        else:
            raise AssertionError("Expected refusal: " + name)

    def run(name, command, expected=None):
        command = list(map(str, command))
        child = subprocess.run(command, cwd=root, capture_output=True, timeout=180)
        text = (child.stdout + child.stderr).decode(errors="replace")
        (evidence / (name + ".log")).write_text(text)
        (evidence / (name + ".command.json")).write_text(json.dumps({"command": command, "exit": child.returncode}, indent=2))
        if expected is None and child.returncode != 0 or expected is not None and (child.returncode == 0 or expected not in text):
            raise AssertionError("Failed " + name + ": " + text[-2000:])
        passed(name)

    valid = evidence / "valid"; valid.mkdir()
    payload.stage(bundle, valid)
    record = payload.verify(valid)
    passed("Complete reviewed runtime stages and verifies")
    lease = (root / "proprietary/src/ContextSuite.Private/Images/HeifCodec.cs").read_text()
    if any(item["sha256"] not in lease for item in record["files"] if item["path"].endswith(".dll")):
        raise AssertionError("Packaging differs from private runtime pins")
    passed("Packaging pins match the actual worker loader")
    refused("Existing destination refused", lambda: payload.stage(bundle, valid))
    refused("External destination refused before writes", lambda: payload.stage(bundle, root.parent / "uncreated-heif-test"))
    refused("Overlapping distribution destination refused", lambda: payload.stage(bundle, bundle))
    for item in record["files"]:
        for mode in ("missing", "changed"):
            mutated = evidence / (mode + "-" + str(len(checks)))
            shutil.copytree(valid, mutated)
            target = mutated / "heif" / item["path"]
            if mode == "missing": target.unlink()
            else: target.write_bytes(b"authored corruption")
            refused(mode + " " + item["path"], lambda: payload.verify(mutated))
    for name, directory in (("unexpected.dll", False), ("extra.txt", False), ("empty", True)):
        mutated = evidence / ("extra-" + name); shutil.copytree(valid, mutated)
        target = mutated / "heif" / name
        if directory: target.mkdir()
        else: target.write_bytes(b"authored extra member")
        refused("Extra member " + name, lambda: payload.verify(mutated))
    linked = evidence / "hardlink"; shutil.copytree(valid, linked)
    target = linked / "heif/ContextSuite.Heif.Native.dll"
    target.unlink(); os.link(valid / "heif/ContextSuite.Heif.Native.dll", target)
    try:
        refused("Hard-linked engine refused", lambda: payload.verify(linked))
    finally:
        target.unlink()
    junction = evidence / "junction"
    script = evidence / "junction.ps1"
    script.write_text("param([string]$Link,[string]$Target)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n")
    run("Create disposable junction", ["powershell", "-NoProfile", "-File", script, "-Link", junction, "-Target", valid])
    refused("Linked ancestor refused", lambda: payload.verify(junction))
    child = valid / "heif/linked"; junction.rename(child)
    try:
        refused("Linked child refused before traversal", lambda: payload.verify(valid))
    finally:
        child.rename(junction); junction.rmdir()

    # Inject write faults only into the first file in a new disposable stage.
    original_open = Path.open
    for mode in ("interrupted", "corrupted"):
        partial = evidence / mode; partial.mkdir()
        first = partial / "heif" / record["files"][0]["path"]

        class FaultWriter:
            def __enter__(self):
                self.stream = original_open(first, "xb")
                return self
            def __exit__(self, *args):
                self.stream.close()
            def write(self, value):
                self.stream.write(b"partial")
                if mode == "interrupted": raise OSError("Authored HEIF copy interruption")
                return len(value)

        def fault_open(path, *args, **kwargs):
            return FaultWriter() if path == first and args == ("xb",) else original_open(path, *args, **kwargs)

        with patch.object(Path, "open", fault_open):
            try:
                payload.stage(bundle, partial)
            except OSError as error:
                if mode != "interrupted" or str(error) != "Authored HEIF copy interruption": raise
            except ValueError as error:
                if mode != "corrupted" or "HEIF payload size" not in str(error): raise
            else: raise AssertionError("Copy failure accepted")
        if list((partial / "heif").rglob("*")) != [first] or first.read_bytes() != b"partial":
            raise AssertionError("Partial file disappeared or later files were written")
        passed(mode + " copy stops and retains partial file")
        refused(mode + " copy fails verification", lambda: payload.verify(partial))
        refused(mode + " retry cannot overwrite partial evidence", lambda: payload.stage(bundle, partial))
        if first.read_bytes() != b"partial": raise AssertionError("Retry changed partial file")

    run("Build requires isolated staging", ["powershell", "-NoProfile", "-File", root / "tools/Build-Production.ps1",
        "-HeifDistributionDirectory", bundle], "HEIF candidate requires a new StagingId")
    combined = evidence / "combined"
    print("Preparing combined scratch payload with current application and worker.", flush=True)
    shutil.copytree(base, combined)
    for name, expected in base_files.items():
        if digest(combined / name) != expected: raise AssertionError("Base copy differs")
    (combined / "payload-inventory.json").unlink()
    payload.stage(bundle, combined)
    for project in ("ContextSuite.Application", "ContextSuite.Worker"):
        run(project + "-build", ["dotnet", "build", root / "src" / project / (project + ".csproj"), "-c", "Release", "--nologo"])
        for path in (root / "artifacts/managed/bin" / project / "Release/net10.0-windows").iterdir():
            if path.is_file(): shutil.copy2(path, combined / path.name)
    verifier = ["powershell", "-NoProfile", "-File", root / "tools/curated-engine/Test-ProductionPayload.ps1", "-Payload", combined]
    for folder, flag in (("audio-engine", "-AllowAudioCandidate"), ("pdf-engine", "-AllowPdfCandidate"), ("office-engine", "-AllowOfficeCandidate")):
        if (combined / folder).exists(): verifier.append(flag)
    run("Default allowlist refuses HEIF", verifier, "Unreviewed production file")
    run("Combined candidate allowlist passes", [*verifier, "-AllowHeifCandidate"])
    inventory = [{"path": path.relative_to(combined).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)}
                 for path in sorted(combined.rglob("*")) if path.is_file()]
    (combined / "payload-inventory.json").write_text(json.dumps(inventory, indent=2))
    run("Combined inventory verifies", ["python", "-B", root / "tools/audio-engine/Stage-AudioPayload.py", "--payload", combined, "--inventory"])
    if any(digest(Path(path)) != expected for path, expected in inputs.items()) or any(digest(base / name) != expected for name, expected in base_files.items()):
        raise AssertionError("Original input, source or base payload changed")
    passed("Original stage distribution and source identities unchanged")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "HeifSelection": record,
        "CombinedPayload": str(combined), "InventorySha256": digest(combined / "payload-inventory.json"),
        "Files": len(inventory), "Bytes": sum(row["bytes"] for row in inventory)}, indent=2))
    print("Passed", len(checks), "HEIF packaging checks; no product version reserved or installed.", flush=True)


if __name__ == "__main__":
    main()
