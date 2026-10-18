"""Exercise audio review archive boundaries using disposable copies."""
import copy
import importlib.util
import json
import pathlib
import shutil
import sys
import uuid
import zipfile


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("verify_distribution", TOOLS / "Verify-AudioDistribution.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


def main():
    original = verifier.distribution.build_tool.local_path(sys.argv[1])
    accepted = verifier.verify(str(original))
    root = original.parent / ("audio-distribution-tests-" + uuid.uuid4().hex)
    root.mkdir()
    manifest = json.loads((original / "bundle-inventory.json").read_text())
    results = [{"case": "original", "result": accepted}]
    cases = ["missing-license", "extra-private-entry", "changed-runtime", "changed-source", "changed-recipe",
             "changed-notice", "duplicate-entry", "changed-evidence", "changed-notice-inventory"]
    if manifest.get("profile") in ("expanded", "video"):
        cases += ["missing-patch", "changed-patch", "changed-profile", "changed-repair-flags", "changed-effective-source"]
    if manifest.get("profile") == "video":
        cases += ["changed-video-flag"]
    for case in cases:
        directory = root / case
        directory.mkdir()
        current = copy.deepcopy(manifest)
        shutil.copytree(original / "evidence", directory / "evidence")
        shutil.copyfile(original / "source-notice-inventory.json", directory / "source-notice-inventory.json")
        for bundle in current["bundles"]:
            entries = []
            with zipfile.ZipFile(original / bundle["path"]) as archive:
                for entry in archive.infolist():
                    data = archive.read(entry)
                    if case == "missing-license" and entry.filename == "licenses/LAME.COPYING.txt":
                        continue
                    if case == "missing-patch" and entry.filename == "tools/audio-engine/wmapro-channel-matrix.patch":
                        continue
                    change = ((case == "changed-runtime" and entry.filename == "bin/ffmpeg.exe")
                              or (case == "changed-source" and entry.filename.startswith(".codex-temp/") and entry.filename.endswith(".zip"))
                              or (case == "changed-recipe" and entry.filename == "tools/audio-engine/Build-AudioEngine.py")
                              or (case == "changed-notice" and entry.filename == "licenses/Opus.COPYING.txt")
                              or (case == "changed-patch" and entry.filename == "tools/audio-engine/wmapro-channel-matrix.patch"))
                    if change:
                        data = bytes([data[0] ^ 1]) + data[1:]
                    entries.append((entry, data))
            if bundle["path"] == "audio-runtime-review.zip":
                if case == "extra-private-entry":
                    entry = zipfile.ZipInfo("proprietary/synthetic-canary.txt")
                    entry.external_attr = 0o100644 << 16
                    entries.append((entry, b"Authored test canary; no private source.\n"))
                if case == "duplicate-entry":
                    entries.append(entries[0])
            # Recompute the outer inventory so a self-consistent altered bundle
            # must still fail trusted membership/content checks.
            path = directory / bundle["path"]
            with zipfile.ZipFile(path, "x") as archive:
                for entry, data in entries:
                    archive.writestr(entry, data)
            bundle["files"] = [{"path": entry.filename, "bytes": len(data), "sha256": verifier.sha(data)} for entry, data in entries]
            bundle["bytes"] = path.stat().st_size
            bundle["sha256"] = verifier.distribution.build_tool.digest(path)
        if case == "changed-evidence":
            with (directory / "evidence/build.log").open("ab") as stream:
                stream.write(b"changed")
        if case == "changed-notice-inventory":
            with (directory / "source-notice-inventory.json").open("ab") as stream:
                stream.write(b" ")
        if case == "changed-profile":
            current["profile"] = "curated"
        if case in ("changed-repair-flags", "changed-effective-source", "changed-video-flag"):
            record_path = directory / "evidence/build-result.json"
            record = json.loads(record_path.read_text())
            if case == "changed-repair-flags":
                record["wmaProMatrixRepair"] = False
            elif case == "changed-video-flag":
                record["videoAudioInputs"] = False
            else:
                effective_path = directory / "evidence/effective-sources.json"
                effective = json.loads(effective_path.read_text())
                effective["libavcodec/wmaprodec.c"] = "0" * 64
                verifier.distribution.write_json(effective_path, effective)
                record["effectiveSourcesSha256"] = verifier.distribution.build_tool.digest(effective_path)
            verifier.distribution.write_json(record_path, record)
            # Rebind the altered evidence inventory to test selected-profile
            # semantics rather than only its outer file checksums.
            current["evidence"] = verifier.distribution.inventory(directory / "evidence")
        verifier.distribution.write_json(directory / "bundle-inventory.json", current)
        try:
            verifier.verify(str(directory))
        except ValueError as error:
            results.append({"case": case, "refused": str(error)})
        else:
            raise AssertionError("Altered review bundle accepted: " + case)
    if json.loads((original / "bundle-inventory.json").read_text()) != manifest:
        raise AssertionError("Original inventory changed")
    verifier.verify(str(original))
    verifier.distribution.write_json(root / "results.json", results)
    print(f"Passed {len(results)} audio distribution checks: {root}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Test-AudioDistribution.py <review bundle directory>")
    main()
