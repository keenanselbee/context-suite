"""Build an isolated pinned HEIF decoder and private bridge; never install or stage the app."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import tarfile
import time
import uuid


ROOT = Path(__file__).resolve().parents[2]
SCRATCH = ROOT / ".codex-temp"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def local(value):
    path = Path(value).absolute()
    if ".." in path.parts or not path.is_relative_to(SCRATCH) or path == SCRATCH:
        raise ValueError("Engine inputs must stay inside repository scratch")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked build paths are not allowed")
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", required=True, help="Directory containing pinned libheif.tar.gz and libde265.tar.gz")
    parser.add_argument("--aom", required=True, help="Selected pinned AOM headers, static library, resource and license")
    parser.add_argument("--cmake", required=True)
    parser.add_argument("--libraries-only", action="store_true",
                        help="Rebuild the LGPL libraries without the private bridge checkout")
    args = parser.parse_args()
    if os.name != "nt":
        raise ValueError("Use Windows x64 with Visual Studio 18")
    recipe = Path(__file__).parent
    manifest = recipe / "source-inputs.json"
    pins = json.loads(manifest.read_text())
    sources, aom = local(args.sources), local(args.aom)
    cmake = Path(args.cmake).resolve(strict=True)
    inputs = {str(path): digest(path) for path in (Path(__file__).resolve(), manifest,
              recipe / "libheif-de265-push-errors.patch", cmake)}
    for pin in pins["archives"]:
        path = sources / (pin["name"] + ".tar.gz")
        if path.stat().st_size != pin["bytes"] or digest(path) != pin["sha256"]:
            raise ValueError("Archive identity differs: " + pin["name"])
        inputs[str(path)] = pin["sha256"]
    for relative, expected in pins["aom"]["files"].items():
        path = local(aom / relative)
        if digest(path) != expected:
            raise ValueError("AOM input identity differs: " + relative)
        inputs[str(path)] = expected
    bridge_source = ROOT / "proprietary/src/ContextSuite.Heif.Native"
    if not args.libraries_only:
        for path in bridge_source.iterdir():
            if path.is_file():
                inputs[str(path)] = digest(path)
    workspace = SCRATCH / "heif-engine" / uuid.uuid4().hex
    workspace.mkdir(parents=True)
    print("HEIF engine evidence:", workspace, flush=True)
    (workspace / "Directory.Build.props").write_text("<Project />\n")
    (workspace / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")
    sdk = workspace / "sdk"
    inventory = {}
    for pin in pins["archives"]:
        prefix = pin["name"] + "-" + pin["version"] + "/"
        total = 0
        with tarfile.open(sources / (pin["name"] + ".tar.gz"), "r:gz") as archive:
            for entry in archive:
                if entry.isdir():
                    continue
                if not entry.isfile() or not entry.name.startswith(prefix):
                    raise ValueError("Unexpected or linked source entry")
                relative = entry.name[len(prefix):]
                if "\\" in relative or ":" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
                    raise ValueError("Unsafe source entry")
                total += entry.size
                if entry.size > 32 * 1024 * 1024 or total > 128 * 1024 * 1024 or len(inventory) >= 20000:
                    raise ValueError("Source archive exceeds bounds")
                target = workspace / "source" / pin["name"] / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(entry) as stream, target.open("xb") as output:
                    shutil.copyfileobj(stream, output)
                inventory[target.relative_to(workspace).as_posix()] = digest(target)
    (workspace / "original-sources.json").write_text(json.dumps(inventory, indent=2) + "\n")

    def run(name, command, cwd=workspace):
        command = [str(value) for value in command]
        started = time.monotonic()
        child = subprocess.run(command, cwd=cwd, capture_output=True, timeout=600)
        (workspace / (name + ".stdout.log")).write_bytes(child.stdout)
        (workspace / (name + ".stderr.log")).write_bytes(child.stderr)
        (workspace / (name + ".command.json")).write_text(json.dumps({"command": command,
            "cwd": str(cwd), "exit": child.returncode, "seconds": time.monotonic() - started}, indent=2) + "\n")
        if child.returncode:
            print(child.stdout.decode(errors="replace"), child.stderr.decode(errors="replace"), flush=True)
            raise RuntimeError("Failed " + name + "; retained evidence: " + str(workspace))

    patch = recipe / "libheif-de265-push-errors.patch"
    heif_source = workspace / "source/libheif"
    run("patch-check", ["git", "apply", "--check", patch], heif_source)
    run("patch", ["git", "apply", patch], heif_source)
    changed = {key: digest(workspace / key) for key in inventory if digest(workspace / key) != inventory[key]}
    if set(changed) != {"source/libheif/libheif/plugins/decoder_libde265.cc"}:
        raise ValueError("Patch affected unexpected source membership")
    (workspace / "patched-sources.json").write_text(json.dumps(changed, indent=2) + "\n")
    common = ["-G", "Visual Studio 18 2026", "-A", "x64", "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded",
              "-DCMAKE_INSTALL_PREFIX=" + str(sdk)]
    run("de265-configure", [cmake, "-S", workspace / "source/libde265", "-B", workspace / "de265-build", *common,
        "-DBUILD_SHARED_LIBS=ON", "-DENABLE_DECODER=OFF", "-DENABLE_ENCODER=OFF", "-DENABLE_SDL=OFF"])
    run("de265-build", [cmake, "--build", workspace / "de265-build", "--config", "Release", "--parallel", "4"])
    run("de265-sdk", [cmake, "--install", workspace / "de265-build", "--config", "Release"])
    disabled = ["ENABLE_PLUGIN_LOADING", "WITH_LIBDE265_PLUGIN", "WITH_AOM_ENCODER", "WITH_AOM_DECODER_PLUGIN",
        "WITH_X265", "WITH_X264", "WITH_OpenH264_DECODER", "WITH_DAV1D", "WITH_KVAZAAR", "WITH_UVG266",
        "WITH_VVDEC", "WITH_VVENC", "WITH_SvtEnc", "WITH_RAV1E", "WITH_JPEG_DECODER", "WITH_JPEG_ENCODER",
        "WITH_OpenJPEG_DECODER", "WITH_OpenJPEG_ENCODER", "WITH_FFMPEG_DECODER", "WITH_OPENJPH_ENCODER",
        "WITH_UNCOMPRESSED_CODEC", "WITH_LIBSHARPYUV", "WITH_EXAMPLES", "WITH_GDK_PIXBUF", "BUILD_DOCUMENTATION",
        "BUILD_TESTING", "WITH_HEADER_COMPRESSION"]
    run("heif-configure", [cmake, "-S", heif_source, "-B", workspace / "heif-build", *common,
        "-DBUILD_SHARED_LIBS=ON", "-DWITH_LIBDE265=ON", "-DWITH_AOM_DECODER=ON",
        *["-D" + option + "=OFF" for option in disabled], "-DCMAKE_PREFIX_PATH=" + str(sdk),
        "-DAOM_INCLUDE_DIR=" + str(aom / "include/aom"), "-DAOM_LIBRARY=" + str(aom / "lib/CORE_RL_aom_.lib")])
    run("heif-build", [cmake, "--build", workspace / "heif-build", "--config", "Release", "--parallel", "4"])
    run("heif-sdk", [cmake, "--install", workspace / "heif-build", "--config", "Release"])
    if not args.libraries_only:
        run("bridge-configure", [cmake, "-S", bridge_source, "-B", workspace / "bridge-build", *common,
            "-DCMAKE_PREFIX_PATH=" + str(sdk)])
        run("bridge-build", [cmake, "--build", workspace / "bridge-build", "--config", "Release", "--parallel", "4"])
    payload = workspace / "payload"
    payload.mkdir()
    if not args.libraries_only:
        shutil.copy2(workspace / "bridge-build/Release/ContextSuite.Heif.Native.dll", payload)
    for name in ("heif.dll", "libde265.dll"):
        shutil.copy2(sdk / "bin" / name, payload)
    if any(digest(Path(path)) != expected for path, expected in inputs.items()):
        raise ValueError("Build inputs changed")
    (workspace / "results.json").write_text(json.dumps({"Passed": True, "BuildOnly": True,
        "LibrariesOnly": args.libraries_only,
        "Payload": {path.name: digest(path) for path in payload.iterdir()},
        "InputsSha256": digest(workspace / "inputs.json"),
        "OriginalSourcesSha256": digest(workspace / "original-sources.json"),
        "PatchedSourcesSha256": digest(workspace / "patched-sources.json")}, indent=2) + "\n")
    print("Built isolated decoder payload:", payload, flush=True)


if __name__ == "__main__":
    main()
