"""Prepare a local audio runtime/source review bundle without production adoption."""
import argparse
import hashlib
import importlib.util
import json
import pathlib
import re
import shutil
import subprocess
import sys
import uuid
import zipfile


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_build", TOOLS / "Build-AudioEngine.py")
build_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build_tool)
RECIPE_FILES = ["Build-AudioEngine.py", "Build-AudioDependency.ps1", "Prepare-AudioSources.ps1",
                "Read-SourceTar.py", "Check-AudioConfiguration.py", "Check-AudioBuild.py", "Rebuild-AudioSource.ps1",
                "source-inputs.json", "opus/CMakeLists.txt", "opus/Version.c", "ogg-vorbis/CMakeLists.txt",
                "lame/CMakeLists.txt", "lame/Probe.c", "make/CMakeLists.txt", "make/Smoke.py",
                "nasm/CMakeLists.txt", "nasm/MsvcCompatibility.h", "nasm/Probe.asm", "nasm/Probe.c", "zlib/CMakeLists.txt"]
DEPENDENCIES = {"opus": "Opus", "ogg_vorbis": "OggVorbis", "lame": "LameStable", "make": "Make", "nasm": "Nasm", "zlib": "Zlib"}


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8", newline="\n")


def copy_verified(source, target, expected):
    target.parent.mkdir(parents=True, exist_ok=True)
    with source.open("rb") as incoming, target.open("xb") as outgoing:
        shutil.copyfileobj(incoming, outgoing)
    if build_tool.digest(source) != expected or build_tool.digest(target) != expected:
        raise ValueError("Copy identity mismatch: " + str(source))


def inventory(root):
    return [{"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size, "sha256": build_tool.digest(path)}
            for path in sorted(root.rglob("*")) if path.is_file()]


def create_archive(root, path):
    files = inventory(root)
    with zipfile.ZipFile(path, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for item in files:
            entry = zipfile.ZipInfo(item["path"], (2026, 9, 11, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_STORED if item["path"].endswith((".zip", ".tar.gz")) else zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, (root / item["path"]).read_bytes())
    with zipfile.ZipFile(path) as archive:
        if archive.namelist() != [item["path"] for item in files] or archive.testzip() is not None:
            raise ValueError("Archive membership or CRC mismatch")
        for item in files:
            with archive.open(item["path"]) as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest().upper() != item["sha256"]:
                    raise ValueError("Archive content mismatch")
    return {"path": path.name, "bytes": path.stat().st_size, "sha256": build_tool.digest(path), "files": files}


def collect_source_notices(candidate, dependencies, destination):
    files = {}
    source = candidate / "source"
    prefix = build_tool.short_path(source) + "/"
    original = {item["path"]: item for item in json.loads((candidate / "source-inventory.json").read_text())}
    selected = set()
    for directory in ("fftools", "libavcodec", "libavfilter", "libavformat", "libavutil", "libswresample"):
        for dependency in (candidate / "build" / directory).rglob("*.d"):
            for value in re.findall(re.escape(prefix) + r"([^\s\\]+)", dependency.read_text()):
                selected.add(pathlib.PurePosixPath(value.lstrip("/")).as_posix())
            relative = dependency.relative_to(candidate / "build")
            for suffix in (".c", ".asm"):
                path = relative.with_suffix(suffix).as_posix()
                if path in original:
                    selected.add(path)
    for name in sorted(selected):
        if name not in original:
            raise ValueError("FFmpeg dependency path is not inventoried: " + name)
        path = source / name
        if build_tool.digest(path) != original[name]["sha256"]:
            raise ValueError("FFmpeg notice source changed")
        files["ffmpeg/" + name] = path
    targets = {"opus": ["opus"], "ogg_vorbis": ["ogg", "vorbis", "vorbisenc"], "lame": ["mp3lame"], "zlib": ["zlibstatic"]}
    for name, names in targets.items():
        directory = dependencies[name][0]
        retained = json.loads((directory / "source-inventory.json").read_text(encoding="utf-8-sig"))
        by_path = {str(directory / "source" / item["path"]).casefold(): item for item in retained}
        count = 0
        for target in names:
            projects = list((directory / "build").rglob(target + ".vcxproj"))
            if len(projects) != 1:
                raise ValueError("Expected one compiled dependency project: " + target)
            logs = list(projects[0].with_suffix(".dir").rglob("CL.read.1.tlog"))
            if len(logs) != 1:
                raise ValueError("Expected one compiled input log: " + target)
            for line in logs[0].read_text(encoding="utf-16").splitlines():
                for value in line.lstrip("^").split("|"):
                    item = by_path.get(value.casefold())
                    if item:
                        path = directory / "source" / item["path"]
                        if build_tool.digest(path) != item["sha256"]:
                            raise ValueError("Dependency notice source changed")
                        files[name + "/" + item["path"].replace("\\", "/")] = path
                        count += 1
        if not count:
            raise ValueError("No compiled source inputs found: " + name)
    comments = re.compile(rb"\A(?:\xef\xbb\xbf)?(?:\s*(?:/\*.*?\*/|//[^\r\n]*(?:\r?\n|$)|;[^\r\n]*(?:\r?\n|$)))+", re.DOTALL)
    notices, report = [], []
    for name, path in sorted(files.items()):
        content = path.read_bytes()
        match = comments.match(content)
        header = match.group() if match else b""
        found = bool(re.search(rb"copyright|license|permission|warranty", header, re.IGNORECASE))
        if not found:
            # Some headers place their notice after an include guard.
            for block in re.finditer(rb"/\*.*?\*/", content[:65536], re.DOTALL):
                if re.search(rb"copyright|license|permission|warranty", block.group(), re.IGNORECASE):
                    header, found = block.group(), True
                    break
        if len(header) > 65536:
            raise ValueError("Oversized leading source notice")
        if found:
            notices.append(name + "\n" + "=" * len(name) + "\n" + header.decode("utf-8") + "\n")
        report.append({"path": name, "sha256": build_tool.digest(path), "noticeFound": found,
                       "noticeOffset": content.find(header) if found else None,
                       "noticeSha256": hashlib.sha256(header).hexdigest().upper() if found else None})
    destination.write_text("Source-comment notices from recorded compiler inputs; full file terms remain in source archives.\n\n" +
                           "\n".join(notices), encoding="utf-8", newline="\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", required=True)
    parser.add_argument("--sources", required=True)
    args = parser.parse_args()
    candidate, sources = (build_tool.local_path(value) for value in (args.candidate, args.sources))
    pin = json.loads((TOOLS / "curated-candidate.json").read_text())
    runtime = json.loads((candidate / "runtime-files.json").read_text())
    if runtime != pin["files"]:
        raise ValueError("Use the exact reviewed curated runtime")
    subprocess.run([sys.executable, "-B", str(TOOLS / "Check-AudioBuild.py"), str(candidate)], check=True)
    record = json.loads((candidate / "build-result.json").read_text())
    pins = {item["id"]: item for item in json.loads((TOOLS / "source-inputs.json").read_text())["archives"]}
    dependencies = {}
    for name, kind in DEPENDENCIES.items():
        entry = record["dependencies"][name]
        path = build_tool.local_path(entry["directory"])
        if build_tool.digest(path / "dependency-build.json") != entry["manifestSha256"]:
            raise ValueError("Dependency build identity changed")
        dependencies[name] = build_tool.verify_build(str(path), kind, pins)
    inputs = [item for name, item in pins.items() if name not in ("supplier-recipe", "lame")]
    if len(inputs) != 8:
        raise ValueError("Review changed source membership")
    for item in inputs:
        path = build_tool.local_path(sources / item["file"])
        if path.stat().st_size != item["bytes"] or build_tool.digest(path) != item["sha256"]:
            raise ValueError("Retained source identity mismatch")
    with zipfile.ZipFile(sources / pins["ffmpeg"]["file"]) as archive:
        source_count = 0
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            if not entry.filename.startswith(pins["ffmpeg"]["prefix"]):
                raise ValueError("Unexpected FFmpeg source prefix")
            name = entry.filename[len(pins["ffmpeg"]["prefix"]):]
            with archive.open(entry) as stream:
                expected = hashlib.file_digest(stream, "sha256").hexdigest().upper()
            if build_tool.digest(build_tool.local_path(candidate / "source" / name)) != expected:
                raise ValueError("FFmpeg source differs from the retained archive: " + name)
            source_count += 1
        if sum(path.is_file() for path in (candidate / "source").rglob("*")) != source_count:
            raise ValueError("FFmpeg source membership changed")
    output = build_tool.SCRATCH / ("audio-distribution-" + uuid.uuid4().hex)
    output.mkdir()
    print("Audio distribution review: " + str(output), flush=True)
    kit, binary = output / "source-kit", output / "runtime"
    for item in inputs:
        copy_verified(sources / item["file"], kit / ".codex-temp/audio-source-retained" / item["file"], item["sha256"])
    for name in RECIPE_FILES:
        path = TOOLS / name
        copy_verified(path, kit / "tools/audio-engine" / name, build_tool.digest(path))
    copy_verified(TOOLS / "distribution/BUILD.md", kit / "BUILD.md", build_tool.digest(TOOLS / "distribution/BUILD.md"))
    for item in runtime:
        copy_verified(candidate / "bin" / item["path"], binary / "bin" / item["path"], item["sha256"])
    licenses = [(candidate / "source/COPYING.LGPLv2.1", "FFmpeg.LGPLv2.1.txt"),
                (candidate / "source/LICENSE.md", "FFmpeg.LICENSE.md"),
                (dependencies["opus"][0] / "source/opus/COPYING", "Opus.COPYING.txt"),
                (dependencies["ogg_vorbis"][0] / "source/ogg/COPYING", "Ogg.COPYING.txt"),
                (dependencies["ogg_vorbis"][0] / "source/vorbis/COPYING", "Vorbis.COPYING.txt"),
                (dependencies["lame"][0] / "source/lame-stable/COPYING", "LAME.COPYING.txt"),
                (dependencies["zlib"][0] / "source/zlib/LICENSE", "zlib.LICENSE.txt")]
    for path, name in licenses:
        copy_verified(path, binary / "licenses" / name, build_tool.digest(path))
    ijg = (candidate / "source/libavcodec/jrevdct.c").read_bytes()
    header = ijg[:ijg.index(b"*/") + 2]
    (binary / "licenses/FFmpeg.IJG.txt").write_bytes(header)
    copy_verified(TOOLS / "distribution/THIRD-PARTY-NOTICES.txt", binary / "THIRD-PARTY-NOTICES.txt",
                  build_tool.digest(TOOLS / "distribution/THIRD-PARTY-NOTICES.txt"))
    notices = collect_source_notices(candidate, dependencies, binary / "licenses/SourceCopyrightNotices.txt")
    write_json(output / "source-notice-inventory.json", notices)
    write_json(binary / "runtime-inventory.json", {"status": "Local review only; no release approval", "files": runtime})
    evidence = output / "evidence"
    evidence.mkdir()
    for name in ("build-inputs.json", "build-result.json", "source-inventory.json", "dependency-files.json", "runtime-files.json",
                 "configure.log", "build.log", "ffmpeg-version.log", "ffprobe-version.log"):
        copy_verified(candidate / name, evidence / name, build_tool.digest(candidate / name))
    for name, (directory, _) in dependencies.items():
        for file in ("dependency-build.json", "source-inventory.json", "tests.log"):
            copy_verified(directory / file, evidence / name / file, build_tool.digest(directory / file))
    bundles = [create_archive(kit, output / "audio-source-review.zip"), create_archive(binary, output / "audio-runtime-review.zip")]
    write_json(output / "bundle-inventory.json", {"status": "Local source/runtime review; not production adoption or redistribution clearance",
               "candidate": pin, "inputs": inputs, "bundles": bundles, "evidence": inventory(evidence),
               "sourceNoticeInventorySha256": build_tool.digest(output / "source-notice-inventory.json"),
               "scriptSha256": build_tool.digest(pathlib.Path(__file__))})
    print(f"Retained {len(inputs)} source archives and {len(runtime)} runtime files; inventoried {len(notices)} compiler source inputs.")
    print("Verified local review archives: " + str(output))


if __name__ == "__main__":
    main()
