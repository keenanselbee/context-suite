"""Compare Windows directory boundaries with and without the Office host manifest."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid
import winreg


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    root = Path(__file__).resolve().parents[2]
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\FileSystem") as key:
        enabled = winreg.QueryValueEx(key, "LongPathsEnabled")[0]
    if enabled != 1:
        raise RuntimeError("This comparison requires the existing LongPathsEnabled=1 policy; the test never changes it.")
    scratch = root / ".codex-temp/office-path" / uuid.uuid4().hex
    source = scratch / "source"
    source.mkdir(parents=True)
    inputs = [root / "tools/office-engine/PathProbe/Probe.cpp",
              root / "proprietary/src/ContextSuite.OfficeHost.Native/app.manifest", Path(__file__)]
    sources = {str(path.relative_to(root)): digest(path) for path in inputs}
    (source / "Probe.cpp").write_bytes(inputs[0].read_bytes())
    (source / "app.manifest").write_bytes(inputs[1].read_bytes())
    (source / "CMakeLists.txt").write_text("""cmake_minimum_required(VERSION 3.24)
project(OfficePathBoundary LANGUAGES CXX)
foreach(name Legacy Aware)
  add_executable(${name} Probe.cpp)
  target_compile_features(${name} PRIVATE cxx_std_20)
  target_compile_options(${name} PRIVATE /W4 /WX /utf-8 /Brepro)
  set_target_properties(${name} PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
endforeach()
target_link_options(Aware PRIVATE "/MANIFESTINPUT:${CMAKE_CURRENT_SOURCE_DIR}/app.manifest")
""", encoding="utf-8")
    vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    visual_studio = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-requires",
        "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip()
    cmake = Path(visual_studio) / "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
    commands = [[str(cmake), "-S", str(source), "-B", str(scratch / "build"), "-G", "Visual Studio 18 2026", "-A", "x64",
                 "-DCMAKE_SYSTEM_VERSION=10.0.26100.0"],
                [str(cmake), "--build", str(scratch / "build"), "--config", "Release", "--", "/m",
                 "/p:ImportDirectoryBuildProps=false", "/p:ImportDirectoryBuildTargets=false", "/verbosity:minimal"]]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (scratch / f"build-{index}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Path probe build failed: {scratch}")
    reports = {}
    binaries = {}
    for name in ("Legacy", "Aware"):
        executable = scratch / f"build/bin/Release/{name}.exe"
        binaries[name] = digest(executable)
        result = subprocess.run([str(executable), str(scratch / name)], cwd=root, capture_output=True)
        (scratch / f"{name}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Path probe failed: {scratch}")
        cells = json.loads(result.stdout)
        if [cell["length"] for cell in cells] != [247, 248, 249, 260, 261, 320] or any(
            not cell["prefixed"] or cell["plain"] != (name == "Aware" or cell["length"] < 248) for cell in cells):
            raise RuntimeError(f"Unexpected native boundary observations: {scratch}")
        if any((scratch / name).iterdir()):
            raise RuntimeError("Probe left a test directory behind.")
        reports[name] = cells
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Probe sources changed during verification.")
    (scratch / "results.json").write_text(json.dumps({"Passed": True, "Checks": 24, "LongPathsEnabled": enabled,
        "Sources": sources, "Binaries": binaries, "Reports": reports}, indent=2) + "\n", encoding="utf-8")
    print("Office path boundary checks passed: 24. Evidence:", scratch)


if __name__ == "__main__":
    main()
