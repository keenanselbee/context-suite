"""Exercise deployment identity and inventory refusal using disposable audio copies."""
import importlib.util
import json
import pathlib
import shutil
import subprocess
import uuid


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_payload", TOOLS / "Stage-AudioPayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def main():
    import sys
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Test-AudioPayload.py <completed audio production stage>")
    source = payload.local_path(sys.argv[1])
    payload.verify(source)
    payload.verify_inventory(source)
    scratch = payload.ROOT / ".codex-temp" / ("audio-payload-tests-" + uuid.uuid4().hex)
    scratch.mkdir()
    results = ["Complete production payload and inventory accepted"]

    def refused(name, operation):
        try:
            operation()
        except ValueError:
            results.append(name)
        else:
            raise AssertionError("Expected refusal: " + name)

    def altered(name, change):
        target = scratch / name
        shutil.copytree(source / "audio-engine", target / "audio-engine")
        change(target / "audio-engine")
        refused(name, lambda: payload.verify(target))

    altered("changed-encoder", lambda root: (root / "ffmpeg.exe").write_bytes(b"invalid encoder"))
    altered("changed-library", lambda root: (root / "avcodec-63.dll").write_bytes(b"invalid library"))
    altered("missing-probe", lambda root: (root / "ffprobe.exe").unlink())
    altered("missing-license", lambda root: (root / "licenses/FFmpeg.LGPLv2.1.txt").unlink())
    altered("changed-notice", lambda root: (root / "licenses/SourceCopyrightNotices.txt").write_bytes(b"changed notice"))
    altered("extra-file", lambda root: (root / "unreviewed.dll").write_bytes(b"extra runtime"))
    altered("changed-runtime-inventory", lambda root: (root / "runtime-inventory.json").write_text("{}"))
    refused("Development payload refused", lambda: payload.verify(payload.ROOT / "artifacts/production/Release"))
    refused("Existing deployment refused before distribution access", lambda: payload.stage("not-a-bundle", source))

    target = scratch / "inventory"
    target.mkdir()
    test_file = target / "worker.bin"
    test_file.write_bytes(b"original")
    inventory = target / "payload-inventory.json"
    record = {"path": test_file.name, "bytes": 8, "sha256": payload.digest(b"original")}
    inventory.write_text(json.dumps([record]))
    payload.verify_inventory(target)
    results.append("Complete synthetic inventory accepted")
    test_file.write_bytes(b"modified")
    refused("Changed inventoried bytes refused", lambda: payload.verify_inventory(target))
    test_file.write_bytes(b"original")
    inventory.write_text(json.dumps([record, record]))
    refused("Duplicate inventory entries refused", lambda: payload.verify_inventory(target))
    inventory.write_text(json.dumps([record]))
    extra = target / "extra.bin"
    extra.write_bytes(b"extra")
    refused("Uninventoried file refused", lambda: payload.verify_inventory(target))
    extra.unlink()
    test_file.unlink()
    refused("Missing inventoried file refused", lambda: payload.verify_inventory(target))

    # Exercise the real entry points, including the default release allowlist.
    # Invalid requests must fail before any build or runtime execution.
    guards = [
        ("No development audio staging", ["tools/Build-Production.ps1", "-AudioDistributionDirectory", "not-a-bundle"],
         "audio candidate requires a new StagingId"),
        ("Existing production stage refused", ["tools/Build-Production.ps1", "-StagingId", source.name,
                                               "-AudioDistributionDirectory", "not-a-bundle"],
         "Production staging already exists"),
        ("Default release allowlist rejects audio", ["tools/curated-engine/Test-ProductionPayload.ps1", "-Payload", str(source)],
         "Unreviewed production file"),
    ]
    for name, arguments, expected in guards:
        result = subprocess.run(["powershell", "-NoProfile", "-File", *arguments], cwd=payload.ROOT,
                                capture_output=True, text=True, timeout=90)
        (scratch / (name.replace(" ", "-") + ".log")).write_text(result.stdout + result.stderr)
        if result.returncode == 0 or expected not in result.stdout + result.stderr:
            raise AssertionError("Entry-point refusal failed: " + name)
        results.append(name)
    (scratch / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(f"{len(results)} audio payload checks passed: {scratch}")


if __name__ == "__main__":
    main()
