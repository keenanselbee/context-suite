"""Stage or verify the pinned AVIF encoder and SVG renderer with their source kits."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess


ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools"
SELECTION = Path(__file__).with_name("production.json")
SELECTION_SHA256 = "C085A1DA073A7535D7F95CA67AC4CB84EA59854F5D99310D74285076FC98CBFC"
SCRATCH = ROOT / ".codex-temp"
STAGING = ROOT / "artifacts/production-staging"
RELEASE = ROOT / "artifacts/release-candidates"
REQUIRED_PINS = {
    "avif": {
        "ContextSuite.Avif.Native.dll": "D31E4A28AE6C9CD73E6CF96A86805BFC22915329D600A73D420E991239C6B77B",
        "provenance/build-results.json": "82489CFE11A2FB347B5C3C964F5E37081F3E6D6AA54CA5D4BA44085FDE264C85",
        "provenance/build-inputs.json": "8F3BC1E3D8C9CC3E454EEB12C1743A5DF2DD45CEE2ED7D3962DC99036A4508D7",
        "provenance/source/libavif.tar.gz": "2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D",
        "provenance/source/aom.tar.gz": "A8E348AD4F009B284BA9A4B5609E9D422A8403B074D38DF6E172F71D92994FD2",
    },
    "svg": {
        "ContextSuite.Svg.exe": "1348469816E870C68857423BBBEAB045FAB45BA848E085079A866A358004B090",
        "provenance/build-provenance.json": "98489523FACD950B72351C2757D4CFE903C4B213C979980C00F150328BFE433E",
        "provenance/source-verification.json": "3CE96FA0045F61933F374A54D883018200034C33993F722E39E7E049D025CC07",
        "provenance/notices/Rust.Library.Notices.html": "68129500B616D5838629E68F55FF3AED5E096DACF60CE9EB41BBE599A563AFA6",
    },
}


def safe(path, *, payload=False, verify=False):
    path = Path(path)
    if not path.is_absolute() or ".." in path.parts:
        raise ValueError("Use absolute paths without traversal")
    path = path.absolute()
    if payload:
        roots = (SCRATCH, STAGING, RELEASE) if verify else (SCRATCH, STAGING)
        if not any(path != root and path.is_relative_to(root) for root in roots):
            raise ValueError("Image expansion payload must be isolated repository scratch or staging")
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            info = ancestor.lstat()
            if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Linked image expansion path: " + str(ancestor))
            if not (stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode)):
                raise ValueError("Unusual image expansion path: " + str(ancestor))
    return path


def digest(path):
    path = safe(path)
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def checked(path, pin):
    path = safe(path)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_size != pin["bytes"] or digest(path) != pin["sha256"]:
        raise ValueError("Image expansion file differs from the reviewed pin: " + str(path))


def item(path):
    path = safe(path)
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        raise ValueError("Expected a singly owned ordinary file: " + str(path))
    return {"bytes": info.st_size, "sha256": digest(path)}


def relative(name):
    if not isinstance(name, str) or not name or "\\" in name or ":" in name or name.startswith("/") or any(
            part in ("", ".", "..") for part in name.split("/")):
        raise ValueError("Unsafe image expansion member: " + str(name))
    return name


def selection():
    if digest(SELECTION) != SELECTION_SHA256:
        raise ValueError("Image expansion production selection changed without review")
    data = json.loads(SELECTION.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 1 or set(data.get("engines", {})) != {"avif", "svg"}:
        raise ValueError("Incomplete image expansion production selection")
    for engine, record in data["engines"].items():
        files = record.get("files")
        if not isinstance(files, dict) or len(files) != {"avif": 20, "svg": 195}[engine] or \
                len(files) != len({name.casefold() for name in files}):
            raise ValueError("Invalid image expansion file selection: " + engine)
        for name, pin in files.items():
            relative(name)
            if not isinstance(pin.get("bytes"), int) or pin["bytes"] <= 0 or not isinstance(pin.get("sha256"), str) or len(pin["sha256"]) != 64:
                raise ValueError("Invalid image expansion file pin: " + name)
        for name, expected in REQUIRED_PINS[engine].items():
            if files.get(name, {}).get("sha256") != expected:
                raise ValueError("Changed runtime or source provenance anchor: " + engine + "/" + name)
    return data["engines"]


def membership(root, files):
    expected = set(files)
    directories = {parent.as_posix() for name in expected for parent in Path(name).parents if parent != Path(".")}
    actual_files, actual_directories = set(), set()

    def failed(error):
        raise error

    for current, folders, names in os.walk(root, followlinks=False, onerror=failed):
        for name in folders + names:
            path = safe(Path(current) / name)
            key = path.relative_to(root).as_posix()
            if path.is_dir():
                if key not in directories:
                    raise ValueError("Unexpected image expansion directory: " + key)
                actual_directories.add(key)
            else:
                if key not in expected:
                    raise ValueError("Unexpected image expansion file: " + key)
                checked(path, files[key])
                actual_files.add(key)
    if actual_files != expected or actual_directories != directories:
        raise ValueError("Image expansion payload is missing reviewed files or directories")


def verify(payload, inventory=False):
    root = safe(payload, payload=True, verify=True)
    records = selection()
    for engine, record in records.items():
        directory = safe(root / engine, payload=True, verify=True)
        if not directory.is_dir():
            raise ValueError("Missing image expansion engine: " + engine)
        membership(directory, record["files"])
    if inventory and os.path.lexists(root / "payload-inventory.json"):
        path = safe(root / "payload-inventory.json", payload=True, verify=True)
        if not path.is_file() or path.stat().st_size > 32 * 1024 * 1024:
            raise ValueError("Invalid production inventory")
        entries = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(entries, list):
            raise ValueError("Invalid production inventory entries")
        expected = {engine + "/" + name: pin for engine, record in records.items() for name, pin in record["files"].items()}
        actual = {}
        for entry in entries:
            name = entry["path"].replace("\\", "/")
            if name.startswith(("avif/", "svg/")):
                if name in actual or name not in expected:
                    raise ValueError("Unexpected or duplicate image expansion inventory entry")
                actual[name] = {"bytes": entry["bytes"], "sha256": entry["sha256"]}
        if actual != expected:
            raise ValueError("Production inventory differs from image expansion selection")
    return records


def sources(avif_build, avif_sources, svg_build, cargo_home):
    records = selection()
    build = safe(avif_build)
    source = safe(avif_sources)
    svg = safe(svg_build)
    cargo = safe(cargo_home)
    for path in (build, source, svg, cargo):
        if not path.is_dir() or not path.is_relative_to(SCRATCH):
            raise ValueError("Engine builds and prepared sources must be repository scratch directories")

    for name, path in {
            "ContextSuite.Avif.Native.dll": build / "bridge-build/Release/ContextSuite.Avif.Native.dll",
            "provenance/build-results.json": build / "results.json",
            "provenance/build-inputs.json": build / "inputs.json",
    }.items():
        checked(path, records["avif"]["files"][name])
    for name, path in {
            "ContextSuite.Svg.exe": svg / "ContextSuite.Svg.exe",
            "provenance/build-provenance.json": svg / "build-provenance.json",
            "provenance/source-verification.json": svg / "source-verification.json",
    }.items():
        checked(path, records["svg"]["files"][name])

    avif_result = json.loads((build / "results.json").read_text(encoding="utf-8"))
    if avif_result.get("passed") is not True or avif_result.get("abi") != 1:
        raise ValueError("AVIF build lacks a passing ABI receipt")
    avif_binary = build / "bridge-build/Release/ContextSuite.Avif.Native.dll"
    if avif_result.get("runtimeFiles") != {avif_binary.name: records["avif"]["files"][avif_binary.name]}:
        raise ValueError("AVIF build result does not identify the selected runtime")
    avif_inputs = json.loads((build / "inputs.json").read_text(encoding="utf-8"))
    private_bridge = ROOT / "proprietary/src/ContextSuite.Avif.Native"
    bridge_inputs = {key.removeprefix("bridge/"): value for key, value in avif_inputs.items()
                     if key.startswith("bridge/")}
    if set(bridge_inputs) != {"CMakeLists.txt", "Encoder.cpp", "Encoder.h"} or \
            {name: item(private_bridge / name)["sha256"] for name in bridge_inputs} != bridge_inputs:
        raise ValueError("Private AVIF bridge source differs from the selected build receipt")
    for name in ("Build-AvifEngine.py", "Prepare-AvifSource.py"):
        if avif_inputs.get("recipe/" + name) != digest(TOOLS / "avif-engine" / name):
            raise ValueError("AVIF public build recipe differs from the build receipt: " + name)
    for name, expected in avif_result.get("notices", {}).items():
        if not name.startswith("notice/") or digest(ROOT / "assets/third-party/avif" / name.removeprefix("notice/")) != expected:
            raise ValueError("AVIF notice differs from the build receipt: " + name)
    svg_binary = svg / "ContextSuite.Svg.exe"
    svg_provenance = json.loads((svg / "build-provenance.json").read_text(encoding="utf-8"))
    if svg_provenance.get("Sha256") != records["svg"]["files"][svg_binary.name]["sha256"] or \
            svg_provenance.get("DependencyCount") != 63 or svg_provenance.get("Target") != "x86_64-pc-windows-msvc":
        raise ValueError("SVG build provenance does not identify the selected runtime")
    rust_version = subprocess.run(["rustc", "--version"], capture_output=True, text=True, timeout=10, check=True).stdout.strip()
    if rust_version != svg_provenance.get("RustVersion"):
        raise ValueError("Rust standard-library notices must match the SVG build toolchain")
    rust_sysroot = Path(subprocess.run(["rustc", "--print", "sysroot"], capture_output=True,
                                    text=True, timeout=10, check=True).stdout.strip())
    verification = json.loads((svg / "source-verification.json").read_text(encoding="utf-8"))
    if verification.get("DependencyCount") != 63 or verification.get("LockSha256", "").upper() != svg_provenance.get("LockSha256"):
        raise ValueError("SVG source verification differs from build provenance")
    if digest(svg / "source-verification.json") != svg_provenance.get("SourceVerificationSha256"):
        raise ValueError("SVG source verification receipt changed")
    if digest(TOOLS / "svg-engine/Build-SvgEngine.ps1") != svg_provenance.get("BuildScriptSha256") or \
            digest(TOOLS / "svg-engine/Verify-CargoSources.py") != svg_provenance.get("SourceVerifierSha256"):
        raise ValueError("SVG public build recipe differs from the build receipt")
    private_svg = ROOT / "proprietary/src/ContextSuite.Svg.Native"
    svg_sources = svg_provenance.get("SourceSha256", {})
    if set(svg_sources) != {"Cargo.toml", "Cargo.lock", "src/main.rs"} or \
            {name: item(private_svg / name)["sha256"] for name in svg_sources} != svg_sources:
        raise ValueError("Private SVG source differs from the selected build provenance")
    inventory = json.loads((ROOT / "assets/third-party/svg/dependency-inventory.json").read_text(encoding="utf-8"))
    if len(inventory) != 63 or digest(ROOT / "assets/third-party/svg/dependency-inventory.json") != svg_provenance.get("InventorySha256"):
        raise ValueError("SVG dependency inventory differs from build provenance")
    crate_index = {(entry["name"], entry["version"]): entry for entry in inventory}
    if len(crate_index) != 63 or {(entry["Name"], entry["Version"]): entry["ArchiveSha256"] for entry in verification["Dependencies"]} != \
            {key: value["crateChecksum"] for key, value in crate_index.items()}:
        raise ValueError("SVG verified crates differ from the notice inventory")
    cache = cargo / "registry/cache"
    mapping = {
        "avif/ContextSuite.Avif.Native.dll": avif_binary,
        "avif/provenance/build-results.json": build / "results.json",
        "avif/provenance/build-inputs.json": build / "inputs.json",
        "svg/ContextSuite.Svg.exe": svg_binary,
        "svg/provenance/build-provenance.json": svg / "build-provenance.json",
        "svg/provenance/source-verification.json": svg / "source-verification.json",
        "svg/provenance/dependency-inventory.json": ROOT / "assets/third-party/svg/dependency-inventory.json",
    }
    for name, pin in (("libavif", "2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D"),
                      ("aom", "A8E348AD4F009B284BA9A4B5609E9D422A8403B074D38DF6E172F71D92994FD2")):
        archive = source / (name + ".tar.gz")
        if digest(archive) != pin:
            raise ValueError("AVIF official source archive changed: " + name)
        mapping["avif/provenance/source/" + archive.name] = archive
    for entry in inventory:
        filename = f"{entry['name']}-{entry['version']}.crate"
        archives = list(cache.glob("index.crates.io-*/" + filename))
        if len(archives) != 1 or digest(archives[0]) != entry["crateChecksum"].upper():
            raise ValueError("Missing or changed locked crate archive: " + filename)
        mapping["svg/provenance/source/crates/" + filename] = archives[0]
        for license_file in entry["licenseFiles"]:
            notice = ROOT / "assets/third-party/svg" / license_file["file"]
            if digest(notice) != license_file["sha256"].upper():
                raise ValueError("Changed SVG crate license: " + license_file["file"])
            mapping["svg/provenance/" + license_file["file"]] = notice
    for name in ("README.md", "Build-AvifEngine.py", "Prepare-AvifSource.py"):
        mapping["avif/provenance/recipe/" + name] = TOOLS / "avif-engine" / name
    mapping["avif/provenance/recipe/KIT.md"] = TOOLS / "image-expansion-engine/AVIF-KIT.md"
    for name in ("README.md", "Build-SvgEngine.ps1", "Verify-CargoSources.py"):
        mapping["svg/provenance/recipe/" + name] = TOOLS / "svg-engine" / name
    mapping["svg/provenance/recipe/KIT.md"] = TOOLS / "image-expansion-engine/SVG-KIT.md"
    for notice in (ROOT / "assets/third-party/avif").glob("*.txt"):
        mapping["avif/provenance/notices/" + notice.name] = notice
    mapping["svg/provenance/recipe/DEPENDENCIES.md"] = ROOT / "assets/third-party/svg/README.md"
    mapping["svg/provenance/notices/Rust.Library.Notices.html"] = rust_sysroot / "share/doc/rust/COPYRIGHT-library.html"
    for name, path in mapping.items():
        engine, relative_name = name.split("/", 1)
        if relative_name not in records[engine]["files"]:
            raise ValueError("Selected source or notice lacks a deployment pin: " + name)
        checked(path, records[engine]["files"][relative_name])
    if set(mapping) != {engine + "/" + name for engine, record in records.items() for name in record["files"]}:
        raise ValueError("Selection includes an unprovided image expansion member")
    return mapping


def stage(payload, avif_build, avif_sources, svg_build, cargo_home):
    root = safe(payload, payload=True)
    if not root.is_dir() or any(os.path.lexists(root / name) for name in ("avif", "svg", "payload-inventory.json")):
        raise ValueError("Image expansion needs a fresh uninventoried isolated payload")
    for value in (avif_build, avif_sources, svg_build, cargo_home):
        source = safe(value)
        if source.is_relative_to(root) or root.is_relative_to(source):
            raise ValueError("Image expansion inputs and payload must not overlap")
    mapping = sources(avif_build, avif_sources, svg_build, cargo_home)
    for engine in ("avif", "svg"):
        (root / engine).mkdir()
    for name, source in sorted(mapping.items()):
        target = safe(root / name, payload=True)
        target.parent.mkdir(parents=True, exist_ok=True)
        with source.open("rb") as input_file, target.open("xb") as output_file:
            shutil.copyfileobj(input_file, output_file, length=1024 * 1024)
    verify(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--avif-build", type=Path)
    parser.add_argument("--avif-sources", type=Path)
    parser.add_argument("--svg-build", type=Path)
    parser.add_argument("--cargo-home", type=Path)
    parser.add_argument("--inventory", action="store_true")
    args = parser.parse_args()
    inputs = (args.avif_build, args.avif_sources, args.svg_build, args.cargo_home)
    if any(inputs) and not all(inputs):
        raise ValueError("Supply all four AVIF/SVG distribution inputs together")
    if all(inputs):
        stage(args.payload, *inputs)
    records = verify(args.payload, inventory=args.inventory)
    print("Verified " + str(sum(len(record["files"]) for record in records.values())) +
          " pinned AVIF/SVG runtime, source, recipe and notice files; isolated candidate only.")


if __name__ == "__main__":
    main()
