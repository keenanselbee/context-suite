"""Stage or verify the pinned audio runtime in an isolated production candidate."""
import argparse
import hashlib
import importlib.util
import io
import json
import pathlib
import stat
import zipfile


TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def local_path(value):
    path = pathlib.Path(value).absolute()
    roots = (ROOT / "artifacts/production-staging", ROOT / ".codex-temp")
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use a fresh isolated production stage or repository scratch")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked payload paths are not allowed")
    return path


def selection(profile="curated"):
    if profile not in ("curated", "expanded"):
        raise ValueError("Unknown audio payload profile")
    record = json.loads((TOOLS / (("expanded-" if profile == "expanded" else "") + "payload-candidate.json")).read_text())
    candidate = json.loads((TOOLS / (profile + "-candidate.json")).read_text())
    native = [{key: item[key] for key in ("path", "bytes", "sha256")} for item in record["files"]
              if item["path"] in {entry["path"] for entry in candidate["files"]}]
    if native != candidate["files"]:
        raise ValueError("Audio deployment differs from the current curated binary identities")
    names = [item["path"] for item in record["files"]]
    if len(names) != 18 or len(set(names)) != 18 or any(
            name.startswith("/") or "\\" in name or ":" in name or ".." in name.split("/") for name in names):
        raise ValueError("Invalid reviewed audio deployment membership")
    return record


def voice_provider():
    spec = importlib.util.spec_from_file_location("voice_distribution", TOOLS / "WindowsVoiceDistribution.py")
    provider = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(provider)
    return provider


def verify(payload):
    audio = local_path(payload) / "audio-engine"
    local_path(audio)
    probe = local_path(audio / "ffprobe.exe")
    if not probe.is_file() or probe.stat().st_size > 1024 * 1024:
        raise ValueError("Audio probe is missing or oversized")
    probe_hash = digest(probe.read_bytes())
    records = [selection(profile) for profile in ("curated", "expanded")]
    matches = [item for item in records if any(entry["path"] == "ffprobe.exe" and entry["sha256"] == probe_hash for entry in item["files"])]
    if len(matches) != 1:
        raise ValueError("Audio probe identity is not a reviewed profile")
    record = matches[0]
    voice = local_path(audio / "voice")
    if voice.exists():
        if record.get("profile") != "expanded":
            raise ValueError("Windows Voice requires the expanded audio profile")
        record = dict(record, files=record["files"] + voice_provider().selection()["files"])
    expected = {item["path"]: item for item in record["files"]}
    actual = set()
    for path in audio.rglob("*"):
        local_path(path)
        if path.is_file():
            relative = path.relative_to(audio).as_posix()
            if relative not in expected:
                raise ValueError("Unreviewed audio payload file: " + relative)
            item = expected[relative]
            if path.stat().st_size != item["bytes"] or digest(path.read_bytes()) != item["sha256"]:
                raise ValueError("Audio payload identity differs: " + relative)
            actual.add(relative)
    if actual != set(expected):
        raise ValueError("Audio payload is missing reviewed binaries or notices")
    return record


def stage(distribution, payload, voice_distribution=None):
    target = local_path(payload) / "audio-engine"
    local_path(target)
    if target.exists():
        raise ValueError("Audio payload already exists; use a new stage")
    spec = importlib.util.spec_from_file_location("audio_distribution_verifier", TOOLS / "Verify-AudioDistribution.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    reviewed = verifier.verify(distribution)
    source = verifier.distribution.build_tool.local_path(distribution) / "audio-runtime-review.zip"
    record = selection(reviewed["profile"])
    voice_contents = {}
    if voice_distribution is not None:
        if reviewed["profile"] != "expanded":
            raise ValueError("Windows Voice requires the expanded audio profile")
        voice_contents = voice_provider().verify(voice_distribution)
    if source.stat().st_size != record["archiveBytes"]:
        raise ValueError("Audio runtime archive size differs")
    data = source.read_bytes()
    if digest(data) != record["archiveSha256"]:
        raise ValueError("Audio runtime archive identity differs")
    # Extract only explicit members from the already verified in-memory archive.
    # Strip bin/ so the worker finds the executable and its adjacent libraries.
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        contents = {}
        for item in record["files"]:
            value = archive.read(item["archivePath"])
            if len(value) != item["bytes"] or digest(value) != item["sha256"]:
                raise ValueError("Reviewed archive member differs")
            contents[item["path"]] = value
        contents.update(voice_contents)
        target.mkdir()
        for relative, value in contents.items():
            output = target / relative
            output.parent.mkdir(exist_ok=True)
            with output.open("xb") as stream:
                stream.write(value)
    verify(payload)


def verify_inventory(payload):
    root = local_path(payload)
    inventory = root / "payload-inventory.json"
    # The complete Office candidate has over 19,000 pinned runtime files.
    # Keep parsing bounded while admitting the combined production inventory.
    if inventory.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Oversized production inventory")
    records = json.loads(inventory.read_text())
    if not isinstance(records, list) or len(records) > 32768:
        raise ValueError("Invalid production inventory record count")
    expected = {item["path"].replace("\\", "/"): item for item in records}
    if len(expected) != len(records):
        raise ValueError("Duplicate production inventory entries")
    actual = set()
    for path in root.rglob("*"):
        local_path(path)
        if path.is_file() and path != inventory:
            relative = path.relative_to(root).as_posix()
            if relative not in expected:
                raise ValueError("Uninventoried production file: " + relative)
            item = expected[relative]
            with path.open("rb") as stream:
                actual_hash = hashlib.file_digest(stream, "sha256").hexdigest().upper()
            if path.stat().st_size != item["bytes"] or actual_hash != item["sha256"]:
                raise ValueError("Production inventory identity differs: " + relative)
            actual.add(relative)
    if actual != set(expected):
        raise ValueError("Production inventory files are missing")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--distribution")
    parser.add_argument("--voice-distribution")
    parser.add_argument("--inventory", action="store_true")
    args = parser.parse_args()
    if args.voice_distribution and not args.distribution:
        parser.error("--voice-distribution requires --distribution")
    if args.distribution:
        stage(args.distribution, args.payload, args.voice_distribution)
    record = verify(args.payload)
    if args.inventory:
        verify_inventory(args.payload)
    print(f"Verified {len(record['files'])} pinned audio payload files; isolated candidate only, not release clearance.")


if __name__ == "__main__":
    main()
