"""Verify local audio review archives against runtime, source and notice identities."""
import hashlib
import importlib.util
import io
import json
import pathlib
import stat
import sys
import tarfile
import zipfile


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("distribution", TOOLS / "Prepare-AudioDistribution.py")
distribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(distribution)
ROOT_LICENSES = {
    "FFmpeg.LGPLv2.1.txt": ("ffmpeg", "COPYING.LGPLv2.1"), "FFmpeg.LICENSE.md": ("ffmpeg", "LICENSE.md"),
    "Opus.COPYING.txt": ("opus", "COPYING"), "Ogg.COPYING.txt": ("ogg", "COPYING"),
    "Vorbis.COPYING.txt": ("vorbis", "COPYING"), "LAME.COPYING.txt": ("lame-stable", "COPYING"),
    "zlib.LICENSE.txt": ("zlib", "LICENSE"),
}


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def verify(value):
    directory = distribution.build_tool.local_path(value)
    manifest_path = directory / "bundle-inventory.json"
    if manifest_path.stat().st_size > 1024 * 1024:
        raise ValueError("Oversized bundle inventory")
    manifest = json.loads(manifest_path.read_text())
    candidate = json.loads((TOOLS / "curated-candidate.json").read_text())
    pins = {item["id"]: item for item in json.loads((TOOLS / "source-inputs.json").read_text())["archives"]
            if item["id"] not in ("supplier-recipe", "lame")}
    if manifest["candidate"]["files"] != candidate["files"] or manifest["inputs"] != list(pins.values()):
        raise ValueError("Bundle candidate or source identities differ")
    evidence_expected = {"build-inputs.json", "build-result.json", "source-inventory.json", "dependency-files.json", "runtime-files.json",
                         "configure.log", "build.log", "ffmpeg-version.log", "ffprobe-version.log"}
    evidence_expected |= {name + "/" + file for name in distribution.DEPENDENCIES
                          for file in ("dependency-build.json", "source-inventory.json", "tests.log")}
    evidence = manifest["evidence"]
    if len(evidence) != len(evidence_expected) or {item["path"] for item in evidence} != evidence_expected:
        raise ValueError("Unexpected build evidence membership")
    for item in evidence:
        path = distribution.build_tool.local_path(directory / "evidence" / item["path"])
        if path.stat().st_size != item["bytes"] or distribution.build_tool.digest(path) != item["sha256"]:
            raise ValueError("Build evidence identity mismatch")
    source_expected = {".codex-temp/audio-source-retained/" + item["file"] for item in pins.values()}
    source_expected |= {"tools/audio-engine/" + name for name in distribution.RECIPE_FILES} | {"BUILD.md"}
    runtime_expected = {"bin/" + item["path"] for item in candidate["files"]}
    runtime_expected |= {"licenses/" + name for name in ROOT_LICENSES}
    runtime_expected |= {"licenses/FFmpeg.IJG.txt", "licenses/SourceCopyrightNotices.txt", "THIRD-PARTY-NOTICES.txt", "runtime-inventory.json"}
    expected = {"audio-source-review.zip": source_expected, "audio-runtime-review.zip": runtime_expected}
    if len(manifest["bundles"]) != 2 or {item["path"] for item in manifest["bundles"]} != set(expected):
        raise ValueError("Unexpected review archive membership")
    contents = {}
    for bundle in manifest["bundles"]:
        path = distribution.build_tool.local_path(directory / bundle["path"])
        if (path.stat().st_size != bundle["bytes"] or path.stat().st_size > 100 * 1024 * 1024
                or distribution.build_tool.digest(path) != bundle["sha256"]):
            raise ValueError("Review archive identity mismatch")
        files = bundle["files"]
        if len(files) != len(expected[path.name]) or {item["path"] for item in files} != expected[path.name]:
            raise ValueError("Unexpected review file membership")
        with zipfile.ZipFile(path) as archive:
            if archive.namelist() != [item["path"] for item in files]:
                raise ValueError("Review ZIP membership differs")
            for item in files:
                entry = archive.getinfo(item["path"])
                if (entry.file_size != item["bytes"] or entry.file_size > 32 * 1024 * 1024
                        or stat.S_IFMT(entry.external_attr >> 16) != stat.S_IFREG):
                    raise ValueError("Invalid review ZIP entry")
                data = archive.read(entry)
                if sha(data) != item["sha256"]:
                    raise ValueError("Review ZIP content mismatch")
                contents[(path.name, item["path"])] = data
    source_name, runtime_name = "audio-source-review.zip", "audio-runtime-review.zip"
    sources = {}
    for identity, pin in pins.items():
        data = contents[(source_name, ".codex-temp/audio-source-retained/" + pin["file"])]
        if len(data) != pin["bytes"] or sha(data) != pin["sha256"]:
            raise ValueError("Original source archive changed")
        sources[identity] = data
    for name in distribution.RECIPE_FILES:
        if sha(contents[(source_name, "tools/audio-engine/" + name)]) != distribution.build_tool.digest(TOOLS / name):
            raise ValueError("Source kit recipe differs from the current reviewed tools")
    if contents[(source_name, "BUILD.md")] != (TOOLS / "distribution/BUILD.md").read_bytes():
        raise ValueError("Source kit build instructions differ")
    for item in candidate["files"]:
        data = contents[(runtime_name, "bin/" + item["path"])]
        if len(data) != item["bytes"] or sha(data) != item["sha256"]:
            raise ValueError("Runtime binary differs from the curated pin")
    if contents[(runtime_name, "THIRD-PARTY-NOTICES.txt")] != (TOOLS / "distribution/THIRD-PARTY-NOTICES.txt").read_bytes():
        raise ValueError("Runtime notice differs")
    if json.loads(contents[(runtime_name, "runtime-inventory.json")])["files"] != candidate["files"]:
        raise ValueError("Runtime inventory differs")

    archives = {}
    try:
        for identity, data in sources.items():
            archives[identity] = (tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") if pins[identity]["file"].endswith(".tar.gz")
                                  else zipfile.ZipFile(io.BytesIO(data)))

        def source_file(identity, relative):
            archive = archives[identity]
            name = pins[identity]["prefix"] + relative
            if isinstance(archive, zipfile.ZipFile):
                if archive.getinfo(name).file_size > 4 * 1024 * 1024:
                    raise ValueError("Oversized source notice input")
                return archive.read(name)
            member = archive.getmember(name)
            if not member.isfile() or member.size > 4 * 1024 * 1024:
                raise ValueError("Invalid source notice input")
            with archive.extractfile(member) as stream:
                return stream.read()

        for name, (identity, relative) in ROOT_LICENSES.items():
            if contents[(runtime_name, "licenses/" + name)] != source_file(identity, relative):
                raise ValueError("Root license differs from the original archive: " + name)
        ijg = source_file("ffmpeg", "libavcodec/jrevdct.c")
        if contents[(runtime_name, "licenses/FFmpeg.IJG.txt")] != ijg[:ijg.index(b"*/") + 2]:
            raise ValueError("IJG notice differs from the source")
        notice_path = directory / "source-notice-inventory.json"
        if notice_path.stat().st_size > 4 * 1024 * 1024 or distribution.build_tool.digest(notice_path) != manifest["sourceNoticeInventorySha256"]:
            raise ValueError("Source notice inventory identity mismatch")
        notices = json.loads(notice_path.read_text())
        if not 1 <= len(notices) <= 5000 or len({item["path"] for item in notices}) != len(notices):
            raise ValueError("Invalid source notice membership")
        collected = []
        for item in notices:
            parts = item["path"].split("/")
            if parts[0] == "ffmpeg":
                identity, relative = "ffmpeg", "/".join(parts[1:])
            else:
                identity, relative = parts[1], "/".join(parts[2:])
            data = source_file(identity, relative)
            if sha(data) != item["sha256"]:
                raise ValueError("Inventoried notice source differs")
            if item["noticeFound"]:
                offset = item["noticeOffset"]
                if not isinstance(offset, int) or not 0 <= offset < min(len(data), 65536):
                    raise ValueError("Invalid source notice offset")
                # Find the exact recorded prefix without accepting a changed notice.
                ends = [position + 2 for position in range(offset, min(len(data) - 1, offset + 65536)) if data[position:position + 2] == b"*/"]
                ends += [position + 1 for position in range(offset, min(len(data), offset + 65536)) if data[position:position + 1] == b"\n"]
                match = next((data[offset:end] for end in sorted(set(ends)) if sha(data[offset:end]) == item["noticeSha256"]), None)
                if match is None:
                    raise ValueError("Source comment notice differs")
                collected.append(item["path"] + "\n" + "=" * len(item["path"]) + "\n" + match.decode("utf-8") + "\n")
        text = "Source-comment notices from recorded compiler inputs; full file terms remain in source archives.\n\n" + "\n".join(collected)
        if contents[(runtime_name, "licenses/SourceCopyrightNotices.txt")] != text.encode("utf-8"):
            raise ValueError("Collected source notices differ")
    finally:
        for archive in archives.values():
            archive.close()
    return {"status": "Local review archive integrity verified; release approval remains separate", "sourceArchives": len(pins),
            "runtimeFiles": len(candidate["files"]), "sourceInputs": len(notices), "collectedNotices": len(collected)}


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Verify-AudioDistribution.py <review bundle directory>")
    print(json.dumps(verify(sys.argv[1])))
