"""Exercise Office staging and production allowlists in disposable repository copies."""

import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import uuid
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_payload", TOOLS / "Stage-OfficePayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine-directory", required=True)
    parser.add_argument("--base-payload", required=True, help="Retained complete image/audio/PDF candidate; read only.")
    args = parser.parse_args()
    source = payload.local_path(args.engine_directory, scratch_only=True)
    base = payload.local_path(args.base_payload)
    if (base / "office-engine").exists():
        raise ValueError("Choose the retained image/audio/PDF payload without Office.")
    scratch = payload.ROOT / ".codex-temp" / ("office-payload-tests-" + uuid.uuid4().hex)
    scratch.mkdir()
    print("Office payload evidence:", scratch, flush=True)
    results = []
    record = payload.selection()
    input_paths = [Path(__file__).resolve(), TOOLS / "Stage-OfficePayload.py", TOOLS / "payload-candidate.json",
                   TOOLS / "evaluation.json", TOOLS / "Write-OfficeRuntimeInventory.py",
                   payload.ROOT / "proprietary/src/ContextSuite.Private/Office/OfficeRuntimeLease.cs",
                   payload.ROOT / "tools/Build-Production.ps1", payload.ROOT / "tools/curated-engine/Test-ProductionPayload.ps1"]
    input_paths.extend(payload.ROOT / item["path"] for item in record["host"]["sources"])
    source_inputs = {str(path.relative_to(payload.ROOT)): payload.digest(path) for path in input_paths}

    def passed(name):
        results.append(name)
        print("Passed:", name, flush=True)

    def refused(name, operation):
        try:
            operation()
        except (ValueError, FileNotFoundError):
            passed(name)
        else:
            raise AssertionError("Expected refusal: " + name)

    def powershell(name, arguments, expected=None):
        run = subprocess.run(["powershell", "-NoProfile", "-File", *arguments], cwd=payload.ROOT,
                             capture_output=True, text=True, timeout=180)
        (scratch / (name.replace(" ", "-") + ".log")).write_text(run.stdout + run.stderr, encoding="utf-8")
        if (expected is None and run.returncode != 0 or
                expected is not None and (run.returncode == 0 or expected not in run.stdout + run.stderr)):
            raise AssertionError("Production entry-point check failed: " + name)
        passed(name)

    lease = (payload.ROOT / "proprietary/src/ContextSuite.Private/Office/OfficeRuntimeLease.cs").read_text()
    writer = (TOOLS / "Write-OfficeRuntimeInventory.py").read_text()
    for value in (record["host"]["sha256"], record["inventory"]["sha256"], str(record["host"]["bytes"]),
                  str(record["inventory"]["bytes"]), str(record["inventory"]["files"]), str(record["inventory"]["runtimeBytes"])):
        if value not in lease:
            raise AssertionError("Office packaging pin differs from the private runtime lease.")
    if record["inventory"]["sha256"] not in writer or record["archiveSha256"] not in writer:
        raise AssertionError("Office packaging pin differs from the independently prepared inventory.")
    passed("Packaging pins agree with private lease and prepared inventory")

    combined = scratch / "combined"
    shutil.copytree(base, combined)
    base_inputs = {path.relative_to(base).as_posix(): payload.digest(path) for path in base.rglob("*") if path.is_file()}
    for name, digest in base_inputs.items():
        if payload.digest(combined / name) != digest:
            raise AssertionError("Base payload copy differs.")
    # A scratch integration assembly is not a new formal product version. Its
    # inherited base receipt would be misleading after adding Office.
    (combined / "payload-inventory.json").unlink()
    payload.stage(combined, source)
    engine = combined / "office-engine"
    passed("Fresh Office copy and complete verification passed")
    refused("Existing Office stage refused", lambda: payload.stage(combined, source))
    refused("Overlapping source and destination refused", lambda: payload.stage(source, source))
    refused("Development payload refused", lambda: payload.local_path(payload.ROOT / "artifacts/production/Release"))
    refused("Repository root refused", lambda: payload.local_path(payload.ROOT))
    refused("Parent traversal refused", lambda: payload.local_path(scratch / ".." / "other"))
    invalid = scratch / "invalid-input"
    invalid.mkdir()

    changes = [
        ("Changed host", "ContextSuite.OfficeHost.exe", b"changed"),
        ("Changed inventory", "runtime-files.txt", b"changed"),
        ("Missing runtime library", "runtime/program/mergedlo.dll", None),
        ("Changed vendor notice", "runtime/NOTICE", b"changed"),
        ("Same length vendor notice corruption", "runtime/NOTICE", b"x" * (engine / "runtime/NOTICE").stat().st_size),
        ("Extra runtime file", "runtime/unreviewed.dll", b"extra"),
        ("Extra host neighbor", "unreviewed.dll", b"extra"),
    ]
    for name, relative, value in changes:
        path = engine / relative
        backup = scratch / "mutation-backup"
        existed = path.exists()
        if existed:
            path.rename(backup)
        try:
            if value is not None:
                path.write_bytes(value)
            refused(name + " refused", lambda: payload.verify(engine))
            if name == "Changed host":
                refused("Invalid source refused before destination writes", lambda: payload.stage(invalid, engine))
                if any(invalid.iterdir()):
                    raise AssertionError("Invalid source changed destination.")
        finally:
            if path.exists():
                path.unlink()
            if existed:
                backup.rename(path)
    extra = engine / "runtime/unreviewed-directory"
    extra.mkdir()
    try:
        refused("Extra empty directory refused", lambda: payload.verify(engine))
    finally:
        extra.rmdir()
    empty = engine / "runtime/share/uno_packages/cache/uno_packages"
    empty.rmdir()
    try:
        refused("Missing required empty directory refused", lambda: payload.verify(engine))
    finally:
        empty.mkdir()

    wrong_source = json.loads(json.dumps(record))
    wrong_source["host"]["sources"][0]["sha256"] = "0" * 64
    with patch.object(payload, "selection", return_value=wrong_source):
        refused("Host source mismatch refused before writes", lambda: payload.stage(invalid, source))
    if any(invalid.iterdir()):
        raise AssertionError("Host source mismatch changed destination.")

    # Faults affect only the disposable destination. A partial copy must remain
    # inspectable, fail verification and refuse a retry into that same stage.
    for mode in ("interrupted", "corrupted"):
        failed_stage = scratch / (mode + "-copy")
        failed_stage.mkdir()

        def failed_copy(original, output):
            output.write(b"incomplete host")
            if mode == "interrupted":
                raise OSError("Authored copy interruption")

        with patch.object(payload.shutil, "copyfileobj", side_effect=failed_copy):
            try:
                payload.stage(failed_stage, source)
            except OSError as error:
                if mode != "interrupted" or str(error) != "Authored copy interruption":
                    raise
            except ValueError as error:
                if mode != "corrupted" or "Office payload identity differs" not in str(error):
                    raise
            else:
                raise AssertionError("Copy fault was accepted: " + mode)
        partial = failed_stage / "office-engine"
        partial_files = {path.relative_to(partial).as_posix(): payload.digest(path)
                         for path in partial.rglob("*") if path.is_file()}
        if partial_files != {record["host"]["path"]: payload.digest(partial / record["host"]["path"])} or (
                partial / record["host"]["path"]).read_bytes() != b"incomplete host":
            raise AssertionError("Failed copy was removed or later files were written.")
        passed(mode + " copy stops and retains partial destination")
        refused(mode + " copy fails payload verification", lambda: payload.verify(partial))
        refused(mode + " copy cannot be overwritten on retry", lambda: payload.stage(failed_stage, source))
        if partial_files != {path.relative_to(partial).as_posix(): payload.digest(path)
                             for path in partial.rglob("*") if path.is_file()}:
            raise AssertionError("Refused retry changed the partial copy.")

    # A junction within this disposable tree exercises both ancestor and child
    # checks without changing permissions or touching a target outside scratch.
    linked = scratch / "linked-engine"
    command = "New-Item -ItemType Junction -Path '" + str(linked).replace("'", "''") + "' -Target '" + str(engine).replace("'", "''") + "' | Out-Null"
    junction = subprocess.run(["powershell", "-NoProfile", "-Command", command], capture_output=True, text=True, timeout=30)
    if junction.returncode:
        raise AssertionError("Could not create disposable junction: " + junction.stderr)
    try:
        refused("Linked engine root refused", lambda: payload.verify(linked))
        refused("Linked destination ancestor refused", lambda: payload.stage(linked / "new", source))
        child = engine / "runtime/unreviewed-link"
        linked.rename(child)
        try:
            refused("Linked runtime child refused", lambda: payload.verify(engine))
        finally:
            child.rename(linked)
    finally:
        linked.rmdir()

    build = "tools/Build-Production.ps1"
    verifier = "tools/curated-engine/Test-ProductionPayload.ps1"
    powershell("No default Office staging", [build, "-OfficeEngineDirectory", "absent"], "Office candidate requires a new StagingId")
    powershell("Office staging requires PDF validation", [build, "-OfficeEngineDirectory", "absent", "-StagingId", str(uuid.uuid4())],
               "Office candidate requires a new StagingId")
    powershell("Office verification requires PDF validation", [verifier, "-Payload", str(combined), "-AllowOfficeCandidate"],
               "Office candidate requires the PDF candidate")
    powershell("Default allowlist rejects Office", [verifier, "-Payload", str(combined), "-AllowAudioCandidate", "-AllowPdfCandidate"],
               "Unreviewed production file")
    powershell("Combined candidate allowlist passes", [verifier, "-Payload", str(combined), "-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"])
    payload.verify(source)
    if any(payload.digest(base / name) != digest for name, digest in base_inputs.items()) or any(
            payload.digest(payload.ROOT / name) != digest for name, digest in source_inputs.items()):
        raise AssertionError("Test inputs changed during verification.")
    passed("Original Office runtime base payload and test sources unchanged")
    (scratch / "results.json").write_text(json.dumps({"Passed": True, "Checks": results, "Sources": source_inputs,
                                                     "OfficeSelection": record, "OfficeSource": str(source),
                                                     "BasePayload": str(base), "BaseFiles": base_inputs,
                                                     "CombinedPayload": str(combined)}, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {len(results)} Office payload checks. No Office process or Windows profile was created.", flush=True)


if __name__ == "__main__":
    main()
