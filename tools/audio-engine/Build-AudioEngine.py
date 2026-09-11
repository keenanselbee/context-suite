"""Build a restricted FFmpeg candidate from verified local sources/dependencies."""
import argparse
import ctypes
import hashlib
import io
import json
import os
import pathlib
import shlex
import shutil
import stat
import subprocess
import sys
import uuid
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRATCH = ROOT / ".codex-temp"
DLLS = {"libavcodec/avcodec-63.dll": "libavcodec/avcodec.dll",
        "libavfilter/avfilter-12.dll": "libavfilter/avfilter.dll",
        "libavformat/avformat-63.dll": "libavformat/avformat.dll",
        "libavutil/avutil-61.dll": "libavutil/avutil.dll",
        "libswresample/swresample-7.dll": "libswresample/swresample.dll"}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def local_path(value):
    path = pathlib.Path(value).absolute()
    if ".." in path.parts or not path.is_relative_to(SCRATCH) or path == SCRATCH:
        raise ValueError("Build inputs must remain inside repository scratch")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked build paths are not allowed")
    return path


def short_path(path):
    function = ctypes.WinDLL("kernel32", use_last_error=True).GetShortPathNameW
    function.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32]
    function.restype = ctypes.c_uint32
    buffer = ctypes.create_unicode_buffer(32768)
    length = function(str(path), buffer, len(buffer))
    if not length or length >= len(buffer) or any(value in buffer.value for value in " %!&|^"):
        raise ValueError("A shell-safe existing Windows short path is required")
    return buffer.value.replace("\\", "/")


def verify_build(value, dependency, pins):
    directory = local_path(value)
    record = json.loads((directory / "dependency-build.json").read_text(encoding="utf-8-sig"))
    if record["dependency"] != dependency:
        raise ValueError("Wrong dependency build: " + dependency)
    expected_inputs = {"Opus": {"opus"}, "OggVorbis": {"ogg", "vorbis"},
                       "LameStable": {"lame-stable"}, "Make": {"make"}, "Nasm": {"nasm"}, "Zlib": {"zlib"}}
    if {item["id"] for item in record["inputs"]} != expected_inputs[dependency]:
        raise ValueError("Dependency input membership differs")
    for item in record["inputs"]:
        pin = pins[item["id"]]
        if any(item[key] != pin[key] for key in ("revision", "sha256", "bytes")):
            raise ValueError("Dependency source identity differs")
    inventory = directory / "source-inventory.json"
    if digest(inventory) != record["sourceInventorySha256"]:
        raise ValueError("Source inventory identity differs")
    source_files = json.loads(inventory.read_text(encoding="utf-8-sig"))
    if len(source_files) != record["sourceFiles"]:
        raise ValueError("Dependency source count differs")
    for item in source_files:
        if digest(local_path(directory / "source" / item["path"])) != item["sha256"]:
            raise ValueError("Dependency header/source changed")
    for item in record["artifacts"]:
        if digest(local_path(directory / "build" / item["path"])) != item["sha256"]:
            raise ValueError("Dependency artifact changed")
    return directory, record


def main():
    script_sha256 = digest(pathlib.Path(__file__))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", required=True)
    for dependency in ("opus", "ogg-vorbis", "lame", "make", "nasm", "zlib"):
        parser.add_argument("--" + dependency, required=True)
    parser.add_argument("--configure-only", action="store_true")
    args = parser.parse_args()
    if os.name != "nt" or sys.version_info < (3, 14):
        raise ValueError("Use Windows x64 with Python 3.14 or later")
    pins = {item["id"]: item for item in json.loads(pathlib.Path(__file__).with_name("source-inputs.json").read_text())["archives"]}
    builds = {}
    for name, selection in (("opus", "Opus"), ("ogg_vorbis", "OggVorbis"), ("lame", "LameStable"), ("make", "Make"), ("nasm", "Nasm"), ("zlib", "Zlib")):
        builds[name] = verify_build(getattr(args, name), selection, pins)
    source_archive = local_path(args.sources) / pins["ffmpeg"]["file"]
    if source_archive.stat().st_size != pins["ffmpeg"]["bytes"]:
        raise ValueError("FFmpeg archive size differs")
    archive_bytes = source_archive.read_bytes()
    if hashlib.sha256(archive_bytes).hexdigest().upper() != pins["ffmpeg"]["sha256"]:
        raise ValueError("FFmpeg archive identity differs")
    workspace = SCRATCH / ("audio-ffmpeg-" + uuid.uuid4().hex)
    workspace.mkdir()
    print("FFmpeg candidate evidence: " + str(workspace), flush=True)
    source = workspace / "source"
    source.mkdir()
    inventory = []
    total = 0
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            if stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError("Linked FFmpeg source entries are not supported")
            if not entry.filename.startswith(pins["ffmpeg"]["prefix"]):
                raise ValueError("Unexpected FFmpeg archive prefix")
            relative = entry.filename[len(pins["ffmpeg"]["prefix"]):]
            if "\\" in relative or ":" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
                raise ValueError("Unsafe FFmpeg source path")
            total += entry.file_size
            if entry.file_size > 32 * 1024 * 1024 or total > 256 * 1024 * 1024 or len(inventory) >= 30000:
                raise ValueError("Oversized FFmpeg source tree")
            # The fresh root and strict relative segments establish containment;
            # do not repeat ancestor filesystem probes for every pinned entry.
            target = source / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as stream, target.open("xb") as output:
                shutil.copyfileobj(stream, output)
            inventory.append({"path": relative, "bytes": target.stat().st_size, "sha256": digest(target)})
    (workspace / "source-inventory.json").write_text(json.dumps(inventory, indent=2))
    print(f"Retained {len(inventory)} FFmpeg source files.", flush=True)
    sdk = workspace / "dependencies"
    (sdk / "include").mkdir(parents=True)
    (sdk / "lib").mkdir()
    for name, source_relative, target_relative in (
        ("opus", "source/opus/include", "include/opus"),
        ("ogg_vorbis", "source/ogg/include/ogg", "include/ogg"),
        ("ogg_vorbis", "source/vorbis/include/vorbis", "include/vorbis"),
    ):
        origin = builds[name][0] / source_relative
        destination = sdk / target_relative
        destination.mkdir()
        for header in origin.glob("*.h"):
            shutil.copyfile(header, destination / header.name)
    ogg = builds["ogg_vorbis"][0]
    template = (ogg / "source/ogg/include/ogg/config_types.h.in").read_text()
    values = {"INCLUDE_INTTYPES_H": "1", "INCLUDE_STDINT_H": "1", "INCLUDE_SYS_TYPES_H": "1"}
    for width in (16, 32, 64):
        values["SIZE" + str(width)] = "int" + str(width) + "_t"
        values["USIZE" + str(width)] = "uint" + str(width) + "_t"
    for key, value in values.items():
        template = template.replace("@" + key + "@", value)
    generated = ogg / "build/ogg/include/ogg/config_types.h"
    if generated.read_text() != template:
        raise ValueError("Generated Ogg header differs from the reviewed fixed-width configuration")
    shutil.copyfile(generated, sdk / "include/ogg/config_types.h")
    (sdk / "include/lame").mkdir()
    shutil.copyfile(builds["lame"][0] / "source/lame-stable/include/lame.h", sdk / "include/lame/lame.h")
    shutil.copyfile(builds["zlib"][0] / "source/zlib/zlib.h", sdk / "include/zlib.h")
    shutil.copyfile(builds["zlib"][0] / "build/zlib/zconf.h", sdk / "include/zconf.h")
    for name, relative, target in (
        ("opus", "opus/Release/opus.lib", "opus.lib"),
        ("ogg_vorbis", "ogg/Release/ogg.lib", "ogg.lib"),
        ("ogg_vorbis", "vorbis/lib/Release/vorbis.lib", "vorbis.lib"),
        ("ogg_vorbis", "vorbis/lib/Release/vorbisenc.lib", "vorbisenc.lib"),
        ("lame", "Release/mp3lame.lib", "mp3lame.lib"),
        ("zlib", "zlib/Release/zs.lib", "zlib.lib"),
    ):
        shutil.copyfile(builds[name][0] / "build" / relative, sdk / "lib" / target)
    sdk_inventory = [{"path": path.relative_to(sdk).as_posix(), "bytes": path.stat().st_size, "sha256": digest(path)}
                     for path in sdk.rglob("*") if path.is_file()]
    (workspace / "dependency-files.json").write_text(json.dumps(sdk_inventory, indent=2))
    build = workspace / "build"
    build.mkdir()
    temporary = workspace / "temporary"
    temporary.mkdir()
    sdk_path = short_path(sdk)
    # This resolver supports only the exact queries and three packages required
    # by the pinned configure script. It is not a general pkg-config replacement.
    resolver = workspace / "resolve-dependencies.sh"
    resolver.write_text("#!/bin/sh\nset -eu\ncase \"$*\" in\n"
        "  --version) printf '%s\\n' 'ContextSuite pinned dependency resolver 1' ;;\n"
        "  '--exists --print-errors opus'|'--exists --print-errors vorbis'|'--exists --print-errors vorbisenc') ;;\n"
        "  '--cflags opus') printf '%s\\n' '-I" + sdk_path + "/include/opus' ;;\n"
        "  '--cflags vorbis'|'--cflags vorbisenc') printf '%s\\n' '-I" + sdk_path + "/include' ;;\n"
        "  '--libs opus') printf '%s\\n' '-lopus' ;;\n"
        "  '--libs vorbis') printf '%s\\n' '-lvorbis -logg' ;;\n"
        "  '--libs vorbisenc') printf '%s\\n' '-lvorbisenc -lvorbis -logg' ;;\n"
        "  '--variable=includedir opus') printf '%s\\n' '" + sdk_path + "/include/opus' ;;\n"
        "  '--variable=includedir vorbis'|'--variable=includedir vorbisenc') printf '%s\\n' '" + sdk_path + "/include' ;;\n"
        "  *) printf '%s\\n' \"Unsupported dependency query: $*\" >&2; exit 1 ;;\nesac\n", newline="\n")
    nasm = short_path(builds["nasm"][0] / "build/Release/nasm.exe")
    make = short_path(builds["make"][0] / "build/Release/gnumake.exe")
    bash = pathlib.Path(os.environ["ProgramFiles"]) / "Git/bin/bash.exe"
    source_path = short_path(source)
    make_command = [make, "-f", source_path + "/Makefile", "REVISION=9.0.1", "SRC_PATH=" + source_path,
        "SRC_LINK=" + source_path, "SHELL=" + short_path(bash),
        "LD=sh " + source_path + "/compat/windows/mslink",
        "WINDRES=sh " + source_path + "/compat/windows/mswindres", "-j2", "ffmpeg.exe", "ffprobe.exe"]
    configure = ["sh", short_path(source / "configure"), "--toolchain=msvc", "--arch=x86_64", "--target-os=win32",
        "--extra-version=contextsuite-g9b0578816c6f",
        "--disable-autodetect", "--disable-everything", "--disable-programs", "--enable-ffmpeg", "--enable-ffprobe",
        "--enable-shared", "--disable-static", "--disable-debug", "--disable-stripping", "--disable-doc",
        "--disable-network", "--disable-avdevice", "--disable-swscale", "--enable-avcodec", "--enable-avformat",
        "--enable-avfilter", "--enable-avutil", "--enable-swresample", "--enable-libmp3lame", "--enable-libopus", "--enable-libvorbis", "--enable-zlib",
        "--enable-decoder=pcm_u8,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le,flac,aac,mp3float,vorbis,opus,png,mjpeg",
        "--enable-encoder=pcm_u8,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le,flac,aac,libmp3lame,libvorbis,libopus",
        "--enable-demuxer=wav,flac,mp3,mov,ogg", "--enable-muxer=wav,flac,mp3,ipod,ogg,opus,pcm_f64le",
        "--enable-parser=aac,flac,mpegaudio,vorbis,opus,png,mjpeg", "--enable-protocol=file,pipe,fd",
        "--enable-filter=aresample", "--x86asmexe=" + nasm, "--pkg-config=sh " + short_path(resolver),
        "--extra-cflags=-MD -I" + sdk_path + "/include", "--extra-ldflags=-libpath:" + sdk_path + "/lib"]
    shell_script = workspace / "build.sh"
    checker = pathlib.Path(__file__).with_name("Check-AudioConfiguration.py")
    if (source / "RELEASE").read_text().strip() != "9.0.1" or (source / "VERSION").exists():
        raise ValueError("Review changed FFmpeg release/version inputs")
    shell_script.write_text("#!/bin/sh\nset -eu\nexport revision=9.0.1\n" +
        "export TMP=" + shlex.quote(short_path(temporary)) + " TEMP=" + shlex.quote(short_path(temporary)) +
        " TMPDIR=" + shlex.quote(short_path(temporary)) + "\ncd " + shlex.quote(short_path(build)) + "\n" +
        shlex.join(configure) + " > ../configure.log 2>&1\n" +
        shlex.join([sys.executable.replace("\\", "/"), short_path(checker), short_path(build)]) + " > ../configuration-check.log 2>&1\n" +
        ("" if args.configure_only else shlex.join(make_command) + " > ../build.log 2>&1\n"), newline="\n")
    vswhere = pathlib.Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    visual_studio = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires",
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip()
    vcvars = pathlib.Path(visual_studio) / "VC/Auxiliary/Build/vcvars64.bat"
    command_file = workspace / "build.cmd"
    if any(char in str(vcvars) + str(bash) + str(workspace) for char in '%!&|^"'):
        raise ValueError("Unsupported command launcher path")
    command_file.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0 > "' + str(workspace / "vsenv.log") + '" 2>&1\n'
        'if errorlevel 1 exit /b 1\n"' + str(bash) + '" --noprofile --norc "' + short_path(shell_script) + '"\nexit /b %errorlevel%\n')
    record = {"status": "Configure-only attempt" if args.configure_only else "Unaccepted candidate build attempt",
              "source": pins["ffmpeg"], "dependencies": {name: {"directory": str(value[0]), "manifestSha256": digest(value[0] / "dependency-build.json")} for name, value in builds.items()},
              "configure": configure, "make": make_command, "scriptSha256": script_sha256,
              "checkerSha256": digest(checker), "visualStudio": visual_studio}
    (workspace / "build-inputs.json").write_text(json.dumps(record, indent=2))
    print("Running native configuration" + ("." if args.configure_only else " and build."), flush=True)
    result = subprocess.run([os.environ["COMSPEC"], "/d", "/c", str(command_file)])
    for item in inventory:
        if digest(source / item["path"]) != item["sha256"]:
            raise ValueError("FFmpeg upstream source changed during build")
    if sum(path.is_file() for path in source.rglob("*")) != len(inventory):
        raise ValueError("FFmpeg source membership changed")
    for item in sdk_inventory:
        if digest(sdk / item["path"]) != item["sha256"]:
            raise ValueError("Staged dependency input changed during build")
    record["exitCode"] = result.returncode
    (workspace / "build-result.json").write_text(json.dumps(record, indent=2))
    if result.returncode:
        raise RuntimeError("Native build failed; inspect " + str(workspace))
    if not args.configure_only:
        binary = workspace / "bin"
        binary.mkdir()
        if {path.relative_to(build).as_posix() for path in build.rglob("*.dll")} != set(DLLS) | set(DLLS.values()):
            raise ValueError("Review the runtime file membership")
        for canonical, alias in DLLS.items():
            if digest(build / canonical) != digest(build / alias):
                raise ValueError("Generated DLL alias differs")
        artifacts = [build / "ffmpeg.exe", build / "ffprobe.exe", *(build / name for name in DLLS)]
        outputs = []
        for path in artifacts:
            shutil.copyfile(path, binary / path.name)
            outputs.append({"path": path.name, "bytes": path.stat().st_size, "sha256": digest(path)})
        (workspace / "runtime-files.json").write_text(json.dumps(outputs, indent=2))
        environment = dict(os.environ)
        environment.pop("FFREPORT", None)
        environment["AV_LOG_FORCE_NOCOLOR"] = "1"
        for name in ("ffmpeg", "ffprobe"):
            version = subprocess.run([str(binary / (name + ".exe")), "-version"], cwd=binary, env=environment,
                                     capture_output=True, text=True, timeout=20)
            (workspace / (name + "-version.log")).write_text(version.stdout + version.stderr)
            if version.returncode or not version.stdout.startswith(name + " version 9.0.1-contextsuite-g9b0578816c6f "):
                raise ValueError("Runtime version or loading check failed: " + name)
    print("Native step completed; component/runtime acceptance remains pending: " + str(workspace))


if __name__ == "__main__":
    main()
