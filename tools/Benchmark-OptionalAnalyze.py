"""Measure optional audio/PDF Analyze on retained generated fixtures and an exact candidate."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import statistics
import subprocess
import time
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--production-stage", type=Path, required=True)
    parser.add_argument("--audio-review-id", type=uuid.UUID, required=True)
    parser.add_argument("--pdf-performance-id", type=uuid.UUID, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    candidate = args.production_stage.resolve(strict=True)
    if candidate.parent != root / "artifacts/production-staging" or uuid.UUID(candidate.name).hex != candidate.name:
        parser.error("Use an isolated versioned production candidate.")
    audio = root / ".codex-temp/audio-review" / args.audio_review_id.hex
    pdf = root / ".codex-temp/pdf-performance" / args.pdf_performance_id.hex
    reports = [audio / "exit.json", audio / "pack/results.json", pdf / "exit.json", pdf / "runs/results.json"]
    a, p = read(reports[1]), read(reports[3])
    for report in (reports[0], reports[2]):
        if read(report) != {"ExitCode": 0, "InputsUnchanged": True}:
            raise ValueError("Use completed, unchanged generated fixture evidence.")
    if not a["AutomatedPassed"] or not p["Passed"]:
        raise ValueError("Fixture preparation did not pass.")
    music = next(item for item in a["Sources"] if item["Name"] == "music")
    selected = [("wave", Path(music["Path"]), music["Sha256"], "wave", "pcm_s24le", 44100)]
    expected = {"Flac": ("flac", "flac", 44100), "Mp3": ("mp3", "mp3", 44100),
                "M4a": ("m4a", "aac", 44100), "Vorbis": ("ogg", "vorbis", 44100), "Opus": ("ogg", "opus", 48000)}
    for target, facts in expected.items():
        item = next(item for item in a["Comparisons"] if item["Clip"] == "music" and item["Target"] == target)
        selected.append((target.lower(), Path(item["Encoded"]), item["EncodedSha256"], *facts))
    pdf_sources = [(Path(path), facts) for path, facts in p["Sources"].items() if Path(path).suffix == ".pdf"]
    if len(pdf_sources) != 1:
        raise ValueError("Expected one authored ten-page PDF fixture.")
    selected.append(("pdf", pdf_sources[0][0], pdf_sources[0][1]["Sha256"], "pdf", None, 0))
    for name, path, expected_hash, *_ in selected:
        path.resolve(strict=True).relative_to((pdf if name == "pdf" else audio).resolve())
        if digest(path) != expected_hash:
            raise ValueError("Generated fixture differs from its retained receipt.")
    stage = root / ".codex-temp/optional-analyze-performance" / uuid.uuid4().hex
    fixtures = stage / "fixtures"; fixtures.mkdir(parents=True)
    print("Optional Analyze evidence:", stage, flush=True)

    def run(label, command):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        if result.returncode:
            raise RuntimeError("Preparation/verification failed: " + label)
        return result.stdout

    inventory = ["python", "-B", str(root / "tools/audio-engine/Stage-AudioPayload.py"), "--payload", str(candidate), "--inventory"]
    run("payload", ["powershell", "-NoProfile", "-File", str(root / "tools/curated-engine/Test-ProductionPayload.ps1"),
                    "-Payload", str(candidate), "-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"])
    run("inventory-before", inventory)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared", "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json", "tools/Require-Private.targets") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    run("build", ["dotnet", "build", str(root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj"),
                  "-c", "Release", "--no-restore", "--verbosity", "quiet"])
    hardware = run("machine", ["powershell", "-NoProfile", "-Command",
        "$cpu = Get-CimInstance Win32_Processor; $system = Get-CimInstance Win32_ComputerSystem; "
        "[ordered]@{Cpu = @($cpu.Name); PhysicalMemoryBytes = $system.TotalPhysicalMemory; "
        "LogicalProcessors = $system.NumberOfLogicalProcessors} | ConvertTo-Json"])
    (stage / "machine.json").write_text(json.dumps(json.loads(hardware), indent=2))
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in candidate.rglob("*") if path.is_file()})
    originals = {str(path): {"Sha256": digest(path), "WrittenNs": path.stat().st_mtime_ns} for path in reports + [item[1] for item in selected]}
    cases = []
    for name, source, expected_hash, format_id, codec, rate in selected:
        destination = fixtures / (name + source.suffix); shutil.copy2(source, destination)
        if digest(destination) != expected_hash:
            raise ValueError("Copied fixture identity differs.")
        originals[str(destination)] = {"Sha256": expected_hash, "WrittenNs": destination.stat().st_mtime_ns}
        cases.append((name, {"Path": str(destination), "Worker": str(candidate / "ContextSuite.Worker.exe"),
                             "Format": format_id, "Codec": codec, "Rate": rate}))
    inputs = {"Sources": sources, "Binaries": binaries, "Originals": originals, "ProductionStage": str(candidate),
              "AudioPreparation": str(audio), "PdfPreparation": str(pdf)}
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2))
    if any(digest(root / name) != expected_hash for name, expected_hash in sources.items()):
        raise RuntimeError("Source drift before measurement.")
    summaries = []
    for name, specification in cases:
        runs = []
        for session in range(5):
            request = stage / (name + "-" + str(session) + ".request.json")
            request.write_text(json.dumps(specification))
            started = time.perf_counter()
            result = subprocess.run([str(host), "--optional-analyze-performance", str(request)],
                                    cwd=root, capture_output=True, timeout=120)
            elapsed = time.perf_counter() - started
            (stage / (name + "-" + str(session) + ".stdout.log")).write_bytes(result.stdout)
            (stage / (name + "-" + str(session) + ".stderr.log")).write_bytes(result.stderr)
            if result.returncode:
                raise RuntimeError("Optional Analyze session failed: " + name + " / " + str(session))
            run_result = json.loads(result.stdout)
            if not run_result["Passed"] or len(run_result["Samples"]) != 6:
                raise RuntimeError("Incomplete optional Analyze session.")
            run_result["WholeProcessSeconds"] = elapsed
            (stage / (name + "-" + str(session) + ".json")).write_text(json.dumps(run_result, indent=2))
            runs.append(run_result)
        first = statistics.median(run_result["Samples"][0]["Seconds"] for run_result in runs)
        repeats = sorted(sample["Seconds"] for run_result in runs for sample in run_result["Samples"][1:])
        p95 = repeats[math.ceil(len(repeats) * .95) - 1]
        summaries.append({"Case": name, "FreshSessions": 5, "FirstCallMedianSeconds": first,
            "RepeatedSamples": len(repeats), "RepeatedMedianSeconds": statistics.median(repeats),
            "RepeatedP95Seconds": p95, "Passed": first <= 2 and p95 <= 1})
        (stage / "summaries.json").write_text(json.dumps(summaries, indent=2))
        print(f"{name}: first median {first:.3f} s; repeated p95 {p95:.3f} s", flush=True)
    unchanged = all(digest(root / name) == value for name, value in sources.items()) and all(
        digest(Path(name)) == value for name, value in binaries.items()) and all(
        digest(Path(name)) == value["Sha256"] and Path(name).stat().st_mtime_ns == value["WrittenNs"] for name, value in originals.items())
    run("inventory-after", inventory)
    passed = unchanged and len(summaries) == 7 and all(item["Passed"] for item in summaries)
    (stage / "results.json").write_text(json.dumps({"Passed": passed, "InputsUnchanged": unchanged,
        "FirstCallMedianLimitSeconds": 2, "RepeatedP95LimitSeconds": 1, "Summaries": summaries,
        "Scope": "35 fresh application/worker sessions and 210 Analyze commands. No human, cold-storage or final expanded-candidate acceptance."}, indent=2))
    if not passed:
        raise RuntimeError("Optional Analyze budget failed; retain all samples and original limits.")
    print("All seven optional Analyze cases meet their declared limits.", flush=True)


if __name__ == "__main__":
    main()
