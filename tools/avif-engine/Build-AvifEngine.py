"""Rebuild the pinned static AVIF encoder and private x64 bridge in fresh scratch."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import time

from importlib.machinery import SourceFileLoader


ROOT = Path(__file__).resolve().parents[2]
BRIDGE = ROOT / "proprietary/src/ContextSuite.Avif.Native"
PREPARE = SourceFileLoader("avif_source", str(Path(__file__).with_name("Prepare-AvifSource.py"))).load_module()
NOTICES = {
    "Libavif.License.txt": "libavif/LICENSE",
    "Aom.License.txt": "aom/LICENSE",
    "Aom.Patents.txt": "aom/PATENTS",
    "X86inc.License.txt": "aom/third_party/x86inc/LICENSE",
    "Vector.License.txt": "aom/third_party/vector/LICENSE",
    "Highway.License.txt": "aom/third_party/highway/LICENSE-BSD3",
    "Libyuv.License.txt": "aom/third_party/libyuv/LICENSE",
    "Fastfeat.License.txt": "aom/third_party/fastfeat/LICENSE",
    "Libwebm.License.txt": "aom/third_party/libwebm/LICENSE.TXT",
    "Libwebm.Patents.txt": "aom/third_party/libwebm/PATENTS.TXT",
    "Googletest.License.txt": "aom/third_party/googletest/src/LICENSE",
}


def local(path):
    path = path.absolute()
    scratch = ROOT / ".codex-temp"
    if path == scratch or not path.is_relative_to(scratch):
        raise ValueError("Sources and output must be under repository .codex-temp")
    for part in (path, *path.parents):
        if part.exists() and part.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked source/build path: " + str(part))
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", type=Path, required=True, help="Prepared pinned source directory")
    parser.add_argument("--output", type=Path, required=True, help="Fresh isolated build directory")
    parser.add_argument("--cmake", type=Path, required=True)
    parser.add_argument("--nasm", type=Path, required=True)
    parser.add_argument("--perl", type=Path, required=True)
    args = parser.parse_args()
    if os.name != "nt":
        raise ValueError("Build requires Windows x64 and Visual Studio 18 2026")
    sources, output = local(args.sources), local(args.output)
    if output.exists():
        raise ValueError("Output must be a fresh directory")
    tools = {name: path.resolve(strict=True) for name, path in
             (("cmake", args.cmake), ("nasm", args.nasm), ("perl", args.perl))}
    for name, _, pin, length in PREPARE.PINS:
        archive = sources / (name + ".tar.gz")
        if archive.stat().st_size != length or PREPARE.digest(archive) != pin:
            raise ValueError("Official source archive identity differs: " + name)
    inventory = json.loads((sources / "source-inventory.json").read_text())
    if len(inventory) > 20000 or not inventory:
        raise ValueError("Invalid source inventory")
    actual = {}
    for folder in (sources / "aom", sources / "libavif"):
        for path in folder.rglob("*"):
            if path.is_file():
                local(path)
                actual[path.relative_to(sources).as_posix()] = PREPARE.digest(path)
            elif not path.is_dir():
                raise ValueError("Unexpected source entry")
    if actual != inventory:
        raise ValueError("Prepared source differs from the source inventory")
    notice_inputs = {}
    for name, relative in NOTICES.items():
        source = sources / relative
        notice = ROOT / "assets/third-party/avif" / name
        if source.read_bytes() != notice.read_bytes():
            raise ValueError("Review notice differs from pinned source: " + name)
        notice_inputs["notice/" + name] = PREPARE.digest(notice)
    if not BRIDGE.is_dir():
        raise ValueError("Private bridge checkout is required")
    inputs = {"source/" + key: value for key, value in inventory.items()}
    for path in BRIDGE.iterdir():
        if path.is_file():
            inputs["bridge/" + path.name] = PREPARE.digest(path)
    inputs["recipe/Build-AvifEngine.py"] = PREPARE.digest(Path(__file__))
    inputs["recipe/Prepare-AvifSource.py"] = PREPARE.digest(Path(PREPARE.__file__))
    inputs.update({"tool/" + name: PREPARE.digest(path) for name, path in tools.items()})
    inputs.update(notice_inputs)
    output.mkdir(parents=True)
    (output / "Directory.Build.props").write_text("<Project />\n")
    (output / "inputs.json").write_text(json.dumps(inputs, indent=2, sort_keys=True) + "\n")
    print("AVIF build evidence:", output, flush=True)
    environment = os.environ.copy()
    environment["NASM_PATH"] = str(tools["nasm"].parent)
    steps = []

    def run(name, arguments):
        command = [str(tools["cmake"]), *(str(value) for value in arguments)]
        started = time.monotonic()
        with (output / (name + ".log")).open("xb") as log:
            child = subprocess.run(command, cwd=output, env=environment, stdout=log,
                                   stderr=subprocess.STDOUT, timeout=1200)
        steps.append({"name": name, "command": command, "exit": child.returncode,
                      "seconds": time.monotonic() - started})
        (output / "progress.json").write_text(json.dumps(steps, indent=2) + "\n")
        print(name + ": " + str(child.returncode), flush=True)
        if child.returncode:
            raise RuntimeError("Inspect retained log: " + str(output / (name + ".log")))

    common = ["-G", "Visual Studio 18 2026", "-A", "x64",
              "-DCMAKE_POLICY_DEFAULT_CMP0091=NEW", "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded",
              "-DBUILD_SHARED_LIBS=OFF"]
    run("aom-configure", ["-S", sources / "aom", "-B", output / "aom-build", *common,
        "-DCONFIG_AV1_ENCODER=1", "-DCONFIG_AV1_DECODER=0", "-DENABLE_APPS=OFF",
        "-DENABLE_EXAMPLES=OFF", "-DENABLE_TESTS=OFF", "-DENABLE_TESTDATA=OFF",
        "-DENABLE_DOCS=OFF", "-DENABLE_TOOLS=OFF", "-DENABLE_NASM=ON",
        "-DCMAKE_ASM_NASM_COMPILER=" + str(tools["nasm"]),
        "-DPERL_EXECUTABLE=" + str(tools["perl"])])
    run("aom-build", ["--build", output / "aom-build", "--config", "Release", "--parallel", "4"])
    aom_lib = output / "aom-build/Release/aom.lib"
    run("avif-configure", ["-S", sources / "libavif", "-B", output / "avif-build", *common,
        "-DAVIF_CODEC_AOM=SYSTEM", "-DAVIF_CODEC_AOM_ENCODE=ON", "-DAVIF_CODEC_AOM_DECODE=OFF",
        "-DAVIF_CODEC_DAV1D=OFF", "-DAVIF_CODEC_LIBGAV1=OFF", "-DAVIF_CODEC_RAV1E=OFF",
        "-DAVIF_CODEC_SVT=OFF", "-DAVIF_CODEC_AVM=OFF", "-DAVIF_BUILD_APPS=OFF",
        "-DAVIF_BUILD_EXAMPLES=OFF", "-DAVIF_BUILD_TESTS=OFF", "-DAVIF_LIBYUV=OFF",
        "-DAVIF_LIBSHARPYUV=OFF", "-DAVIF_LIBXML2=OFF",
        "-DAOM_INCLUDE_DIR=" + str(sources / "aom"), "-DAOM_LIBRARY=" + str(aom_lib)])
    run("avif-build", ["--build", output / "avif-build", "--config", "Release", "--parallel", "4"])
    avif_lib = output / "avif-build/Release/avif.lib"
    run("bridge-configure", ["-S", BRIDGE, "-B", output / "bridge-build", *common,
        "-DAVIF_SOURCE_DIR=" + str(sources / "libavif"),
        "-DAVIF_LIBRARY=" + str(avif_lib), "-DAOM_LIBRARY=" + str(aom_lib)])
    run("bridge-build", ["--build", output / "bridge-build", "--config", "Release", "--parallel", "4"])
    for key, expected in inputs.items():
        if key.startswith("source/"):
            path = sources / key.removeprefix("source/")
        elif key.startswith("bridge/"):
            path = BRIDGE / key.removeprefix("bridge/")
        elif key.startswith("recipe/"):
            path = Path(__file__).parent / key.removeprefix("recipe/")
        elif key.startswith("notice/"):
            path = ROOT / "assets/third-party/avif" / key.removeprefix("notice/")
        else:
            path = tools[key.removeprefix("tool/")]
        if PREPARE.digest(path) != expected:
            raise ValueError("Build input changed: " + key)
    dll = output / "bridge-build/Release/ContextSuite.Avif.Native.dll"
    files = (aom_lib, avif_lib, dll)
    outputs = {path.relative_to(output).as_posix(): {"bytes": path.stat().st_size,
               "sha256": PREPARE.digest(path)} for path in files}
    (output / "results.json").write_text(json.dumps({"passed": True, "abi": 1,
        "runtimeFiles": {dll.name: outputs[dll.relative_to(output).as_posix()]},
        "outputs": outputs, "notices": notice_inputs, "steps": steps}, indent=2) + "\n")
    print(json.dumps(outputs, indent=2), flush=True)


if __name__ == "__main__":
    main()
