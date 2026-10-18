"""Verify or copy the complete pinned Office runtime into an isolated candidate.

No engine is executed, no Windows profile is created and no installed payload is
changed. Build-time verification does not replace the worker's runtime leases.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import xml.etree.ElementTree as ET


TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
SOURCE_RECORD = TOOLS / "source-runtime-candidate.json"
REDUCTION_POLICY = TOOLS / "runtime-reduction-policy.json"
DERIVATION_TOOL = TOOLS / "Derive-ReducedRuntime.py"
PACKAGED_SOURCE_RECORD = "ContextSuite.OfficeSource.json"
NOTICE_RECORD = TOOLS / "python-notice-review.json"
NOTICE_FILE = ROOT / "assets/third-party/office/Office.Python.Notices.txt"
PACKAGED_NOTICE = "Office.Python.Notices.txt"
COMPONENT_NOTICE_RECORD = TOOLS / "component-notice-review.json"
COMPONENT_NOTICE_FILE = ROOT / "assets/third-party/office/Office.Dependency.Notices.html"
PACKAGED_COMPONENT_NOTICE = "Office.Dependency.Notices.html"
SERVICES_NAMESPACE = "{http://openoffice.org/2010/uno-components}"


def python_runtime(record):
    mode = record.get("pythonRuntime", "bundled")
    if mode not in ("bundled", "none"):
        raise ValueError("Unsupported Office Python runtime mode: " + str(mode))
    return mode


def python_runtime_name(name):
    parts = [part.casefold() for part in name.split("/")]
    base = parts[-1]
    return (any(part.startswith(("python-core-", "pyuno", "pythonloader")) for part in parts) or
            base.endswith(".pyd") or
            re.fullmatch(r"pythonw?(?:\d+(?:\.\d+)*)?\.exe", base) is not None or
            re.fullmatch(r"python\d+[^/]*\.dll", base) is not None or
            any(part in {"pip", "setuptools", "pkg_resources", "_distutils_hack"} for part in parts))


def verify_python_free_services(engine, files):
    registries = sorted(name for name in files if name.startswith("runtime/") and
                        Path(name).name == "services.rdb")
    if not registries:
        raise ValueError("Python-free Office runtime has no pinned services registry.")
    for name in registries:
        root = ET.parse(engine / name).getroot()
        if root.tag != SERVICES_NAMESPACE + "components":
            raise ValueError("Unexpected Office services registry XML: " + name)
        for component in root:
            if component.tag != SERVICES_NAMESPACE + "component":
                raise ValueError("Unexpected Office services registry component: " + name)
            loader, uri = component.get("loader", ""), component.get("uri", "")
            if not loader or not uri or any("python" in value.casefold() or "pyuno" in value.casefold()
                                             for element in component.iter() for value in element.attrib.values()):
                raise ValueError("Python loader or component remains in Office services registry: " + name)


def ordinary(path):
    info = path.lstat()
    if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
        raise ValueError("Linked Office payload paths are not allowed: " + str(path))
    if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
        raise ValueError("Office payload entries must be ordinary files or directories.")
    return info


def local_path(value, scratch_only=False, verification=False):
    path = Path(value).absolute()
    roots = [ROOT / ".codex-temp"]
    if not scratch_only:
        roots.append(ROOT / "artifacts/production-staging")
        if verification:
            roots.append(ROOT / "artifacts/release-candidates")
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use isolated repository staging and scratch inputs.")
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            ordinary(ancestor)
    return path


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def identity(path, pin):
    info = ordinary(path)
    if not stat.S_ISREG(info.st_mode) or info.st_size != pin["bytes"] or digest(path) != pin["sha256"]:
        raise ValueError("Office payload identity differs: " + str(path))


def selection():
    record = json.loads((TOOLS / "payload-candidate.json").read_text(encoding="utf-8"))
    upstream = json.loads(SOURCE_RECORD.read_text(encoding="utf-8"))
    python_runtime(upstream)
    if (upstream["formatVersion"] != 1 or record["archiveSha256"] != upstream["archiveSha256"] or
            record["inventory"]["sha256"] != upstream["inventorySha256"] or
            upstream["host"] != {key: record["host"][key] for key in ("path", "bytes", "sha256")} or
            upstream["runtime"] != {"files": record["inventory"]["files"], "bytes": record["inventory"]["runtimeBytes"]}):
        raise ValueError("Office archive selection differs from the reviewed candidate.")
    policy = json.loads(REDUCTION_POLICY.read_bytes())
    base = policy["baseSelection"]
    derivation = upstream.get("runtimeDerivation")
    if derivation is None:
        if record["inventory"] != base["inventory"]:
            raise ValueError("A derived Office runtime requires its exact reduction recipe.")
    else:
        expected = {
            "formatVersion": 1,
            "baseArchiveSha256": base["archiveSha256"],
            "baseInventorySha256": base["inventory"]["sha256"],
            "policySha256": digest(REDUCTION_POLICY),
            "toolSha256": digest(DERIVATION_TOOL),
            "excludedFiles": policy["excludedFiles"],
            "retainedBinaryBytesUnchanged": True,
        }
        if derivation != expected:
            raise ValueError("Office runtime derivation differs from its reviewed recipe.")
        original = policy["baseSourceRecord"]
        for key in original:
            if key not in ("archiveSha256", "inventorySha256", "runtime", "purpose", "derivation") and upstream.get(key) != original[key]:
                raise ValueError("Office reduction changed its original build/source identity: " + key)
        for key in ("host", "hostDependencies", "emptyDirectories"):
            if record[key] != base[key]:
                raise ValueError("Office reduction changed a retained host dependency.")
        removed_bytes = sum(item["bytes"] for item in policy["excludedFiles"])
        if (record["inventory"]["files"] != base["inventory"]["files"] - len(policy["excludedFiles"]) or
                record["inventory"]["runtimeBytes"] != base["inventory"]["runtimeBytes"] - removed_bytes):
            raise ValueError("Office reduction totals differ from the exact exclusions.")
    return record


def verify_source_record(payload):
    ordinary(SOURCE_RECORD)
    identity(local_path(payload, verification=True) / PACKAGED_SOURCE_RECORD,
             {"bytes": SOURCE_RECORD.stat().st_size, "sha256": digest(SOURCE_RECORD)})


def supplemental_notice():
    ordinary(NOTICE_RECORD)
    record = json.loads(NOTICE_RECORD.read_bytes())
    if record["formatVersion"] != 1 or record["inventorySha256"] != selection()["inventory"]["sha256"]:
        raise ValueError("Supplemental Python notices differ from the selected Office runtime.")
    identity(NOTICE_FILE, record["notice"])
    return record["notice"]


def component_notice():
    ordinary(COMPONENT_NOTICE_RECORD)
    record = json.loads(COMPONENT_NOTICE_RECORD.read_bytes())
    selected = json.loads(SOURCE_RECORD.read_bytes())
    mode = python_runtime(selected)
    valid_v1 = (record.get("formatVersion") == 1 and mode == "bundled" and
                record.get("officeSourceKitManifestSha256") == selected["sourceKitManifestSha256"])
    valid_v2 = (record.get("formatVersion") == 2 and mode == "none" and
                record.get("sourceKitManifestSha256") == selected["sourceKitManifestSha256"] and
                record.get("pythonRuntime") == "none" and
                record.get("sourceCorrespondenceAccepted") is True and
                record.get("packagingAccepted") is True)
    if (record["inventorySha256"] != selection()["inventory"]["sha256"] or
            not (valid_v1 or valid_v2)):
        raise ValueError("Supplemental component notices differ from the selected Office runtime/source kit.")
    identity(COMPONENT_NOTICE_FILE, record["notice"])
    return record["notice"]


def verify_notices(payload):
    root = local_path(payload, verification=True)
    if python_runtime(json.loads(SOURCE_RECORD.read_bytes())) == "bundled":
        identity(root / PACKAGED_NOTICE, supplemental_notice())
    elif os.path.lexists(root / PACKAGED_NOTICE):
        raise ValueError("Python-free Office payload contains stale Python notices.")
    identity(root / PACKAGED_COMPONENT_NOTICE, component_notice())


def membership(engine, record):
    inventory = engine / record["inventory"]["path"]
    identity(inventory, record["inventory"])
    data = inventory.read_bytes()
    # Parse only the exact pinned bytes, including when a file changes after hashing.
    if hashlib.sha256(data).hexdigest().upper() != record["inventory"]["sha256"]:
        raise ValueError("Office inventory changed while reading.")
    files = {record["host"]["path"]: record["host"], record["inventory"]["path"]: record["inventory"]}
    for dependency in record["hostDependencies"]:
        name = dependency["path"]
        if Path(name).name != name or ":" in name or "\\" in name or name in files:
            raise ValueError("Invalid pinned Office host dependency path.")
        files[name] = dependency
    directories = {"runtime", *("runtime/" + name for name in record["emptyDirectories"])}
    names, total = [], 0
    for line in data.decode("utf-8").splitlines():
        name, length, sha256 = line.split("\t")
        if ("\\" in name or ":" in name or any(part in ("", ".", "..") for part in name.split("/"))):
            raise ValueError("Invalid Office inventory path.")
        names.append(name)
        total += int(length)
        relative = "runtime/" + name
        files[relative] = {"bytes": int(length), "sha256": sha256}
        directories.update(parent.as_posix() for parent in Path(relative).parents if parent != Path("."))
    if (names != sorted(names) or len(names) != len(set(name.casefold() for name in names)) or
            len(names) != record["inventory"]["files"] or total != record["inventory"]["runtimeBytes"]):
        raise ValueError("Office inventory membership differs from the pinned candidate.")
    if python_runtime(json.loads(SOURCE_RECORD.read_bytes())) == "none":
        for name in names:
            if python_runtime_name(name):
                raise ValueError("Python runtime file remains in Office inventory: " + name)
    return files, directories


def verify(engine):
    engine = local_path(engine, verification=True)
    record = selection()
    identity(engine / record["host"]["path"], record["host"])
    files, directories = membership(engine, record)
    actual_files, actual_directories = set(), set()

    def failed(error):
        raise error

    # Check links before descending, including empty or otherwise unlisted folders.
    for current, folders, names in os.walk(engine, followlinks=False, onerror=failed):
        for name in folders + names:
            path = Path(current) / name
            info = ordinary(path)
            relative = path.relative_to(engine).as_posix()
            if stat.S_ISDIR(info.st_mode):
                if relative not in directories:
                    raise ValueError("Unreviewed Office payload directory: " + relative)
                actual_directories.add(relative)
            else:
                if relative not in files:
                    raise ValueError("Unreviewed Office payload file: " + relative)
                actual_files.add(relative)
    if actual_files != set(files) or actual_directories != directories:
        raise ValueError("Office payload is missing reviewed runtime files, notices or directories.")
    for name, pin in files.items():
        identity(engine / name, pin)
    if python_runtime(json.loads(SOURCE_RECORD.read_bytes())) == "none":
        verify_python_free_services(engine, files)
    return files, directories


def stage(payload, engine_directory):
    root = local_path(payload)
    if not root.is_dir():
        raise ValueError("Create a new isolated payload directory before staging Office.")
    destination = local_path(root / "office-engine")
    if os.path.lexists(destination):
        raise ValueError("Office payload already exists; use a new stage.")
    source_target = local_path(root / PACKAGED_SOURCE_RECORD)
    if os.path.lexists(source_target):
        raise ValueError("Office source record already exists; use a new stage.")
    mode = python_runtime(json.loads(SOURCE_RECORD.read_bytes()))
    if mode == "none" and os.path.lexists(root / PACKAGED_NOTICE):
        raise ValueError("Python-free Office payload contains stale Python notices.")
    notice_sources = [(PACKAGED_COMPONENT_NOTICE, COMPONENT_NOTICE_FILE, component_notice())]
    if mode == "bundled":
        notice_sources.insert(0, (PACKAGED_NOTICE, NOTICE_FILE, supplemental_notice()))
    notices = []
    for name, source_file, notice_pin in notice_sources:
        target = local_path(root / name)
        if os.path.lexists(target):
            raise ValueError("Office supplemental notices already exist; use a new stage.")
        data = source_file.read_bytes()
        if len(data) != notice_pin["bytes"] or hashlib.sha256(data).hexdigest().upper() != notice_pin["sha256"]:
            raise ValueError("Office supplemental notices changed while reading.")
        notices.append((target, data))
    source = local_path(engine_directory, scratch_only=True)
    if destination.is_relative_to(source) or source.is_relative_to(destination):
        raise ValueError("Office source and destination must not overlap.")
    files, directories = verify(source)
    ordinary(SOURCE_RECORD)
    source_record = SOURCE_RECORD.read_bytes()
    source_pin = {"bytes": len(source_record), "sha256": hashlib.sha256(source_record).hexdigest().upper()}
    for item in selection()["host"]["sources"]:
        path = ROOT / item["path"]
        ordinary(path)
        if digest(path) != item["sha256"]:
            raise ValueError("Office host source changed; rebuild and review the candidate.")
    # No writes before full source verification. Leave any partial failed stage
    # for inspection; never merge, overwrite or automatically delete a payload.
    destination.mkdir()
    for name in sorted(directories, key=lambda value: (value.count("/"), value)):
        (destination / name).mkdir()
    for number, (name, pin) in enumerate(files.items(), 1):
        source_file = local_path(source / name, scratch_only=True)
        target = local_path(destination / name)
        with source_file.open("rb") as original, target.open("xb") as output:
            shutil.copyfileobj(original, output)
        identity(target, pin)
        if number % 4000 == 0:
            print(f"Copied and verified {number:,} Office payload files.", flush=True)
    verify(destination)
    with source_target.open("xb") as output:
        output.write(source_record)
    identity(source_target, source_pin)
    verify_source_record(root)
    for target, data in notices:
        with target.open("xb") as output:
            output.write(data)
    verify_notices(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--engine-directory", help="Complete pinned Office engine in repository scratch.")
    args = parser.parse_args()
    if args.engine_directory:
        stage(args.payload, args.engine_directory)
    else:
        root = local_path(args.payload, verification=True)
        verify(root / "office-engine")
        verify_source_record(root)
        verify_notices(root)
    print(f"Verified {selection()['inventory']['files']:,} Office runtime files, host dependencies, source record, supplemental notices and exact directories; release approval remains separate.")


if __name__ == "__main__":
    main()
