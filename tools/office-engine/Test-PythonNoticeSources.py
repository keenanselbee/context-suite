"""Verify Office's supplemental Python notices against retained source archives.

Reads archives in memory without extracting or executing dependency code. The
optional engine check compares all packaged pip/setuptools files with their
sources; the existing Office payload verifier owns complete engine membership.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile
import zipfile


ROOT = Path(__file__).resolve().parents[2]
RECORD = ROOT / "tools/office-engine/python-notice-review.json"
NOTICE = ROOT / "assets/third-party/office/Office.Python.Notices.txt"
PREAMBLE = (
    "Context Suite - supplemental Office Python notices\n"
    "==================================================\n\n"
    "The selected Office runtime includes pip 25.0.1, setuptools 79.0.1 and\n"
    "their vendored libraries. These notices supplement the runtime's existing\n"
    "Python and LibreOffice licenses. Source notice contents below are unchanged.\n"
    "Some upstream components include multiple license alternatives; retaining\n"
    "their original texts does not change those alternatives.\n\n"
)


def pin(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest().upper()}


def source_contents(python_source, pip_source):
    wheels = {}
    with tarfile.open(fileobj=io.BytesIO(python_source), mode="r:xz") as archive:
        for name in (
            "Lib/ensurepip/_bundled/pip-25.0.1-py3-none-any.whl",
            "Lib/test/wheeldata/setuptools-79.0.1-py3-none-any.whl",
        ):
            wheels[name] = archive.extractfile("Python-3.12.14/" + name).read()
    with tarfile.open(fileobj=io.BytesIO(pip_source), mode="r:gz") as archive:
        pip_files = {member.name.removeprefix("pip-25.0.1/"): archive.extractfile(member).read()
                     for member in archive if member.isfile()}
    notices = {}
    for name, data in pip_files.items():
        if any(word in Path(name).name.lower() for word in ("license", "copying", "notice", "authors")):
            notices["pip-25.0.1/" + name] = data
    with zipfile.ZipFile(io.BytesIO(wheels["Lib/test/wheeldata/setuptools-79.0.1-py3-none-any.whl"])) as archive:
        setuptools_files = {name: archive.read(name) for name in archive.namelist() if not name.endswith("/")}
    for name, data in setuptools_files.items():
        if any(word in Path(name).name.lower() for word in ("license", "copying", "notice", "authors")):
            notices["setuptools-79.0.1-wheel/" + name] = data
    with zipfile.ZipFile(io.BytesIO(wheels["Lib/ensurepip/_bundled/pip-25.0.1-py3-none-any.whl"])) as archive:
        pip_wheel = {name: archive.read(name) for name in archive.namelist() if not name.endswith("/")}
    # The supplemental sdist must describe the bundled wheel, including vendored
    # source. No unrelated newer license version is substituted.
    for name, data in pip_wheel.items():
        if name.startswith("pip/") and pip_files.get("src/" + name) != data:
            raise ValueError("pip wheel/source archive mismatch: " + name)
    for name in ("LICENSE.txt", "AUTHORS.txt"):
        if pip_wheel["pip-25.0.1.dist-info/" + name] != pip_files[name]:
            raise ValueError("pip wheel/source notice mismatch: " + name)
    return notices, wheels, pip_wheel, setuptools_files


def collate(notices):
    result = bytearray(PREAMBLE.encode("utf-8"))
    for name, data in sorted(notices.items()):
        result.extend(("\n--- " + name + " ---\n\n").encode("utf-8"))
        data.decode("utf-8")  # Refuse unreadable notices, preserve original bytes.
        result.extend(data)
        result.extend(b"\n")
    return bytes(result)


def verify(bundle, engine=None):
    record_bytes = RECORD.read_bytes()
    record = json.loads(record_bytes)
    selected = json.loads((ROOT / "tools/office-engine/source-runtime-candidate.json").read_bytes())
    if (record["formatVersion"] != 1 or record["inventorySha256"] != selected["inventorySha256"] or
            record["officeSourceKitManifestSha256"] != selected["sourceKitManifestSha256"]):
        raise ValueError("Supplemental notices describe a different Office runtime.")
    with zipfile.ZipFile(bundle) as archive:
        expected = {"python-notice-review.json", "Office.Python.Notices.txt", *record["archives"]}
        if len(archive.namelist()) != len(expected) or set(archive.namelist()) != expected:
            raise ValueError("Unexpected Python source companion membership.")
        if archive.read("python-notice-review.json") != record_bytes:
            raise ValueError("Python source companion record differs.")
        sources = {name: archive.read(name) for name in record["archives"]}
        for name, data in sources.items():
            if pin(data) != {key: record["archives"][name][key] for key in ("bytes", "sha256")}:
                raise ValueError("Changed Python source archive: " + name)
        notices, wheels, pip_files, setuptools_files = source_contents(
            sources["Python-3.12.14.tar.xz"], sources["pip-25.0.1.tar.gz"])
        if {name: pin(data) for name, data in notices.items()} != record["notices"]:
            raise ValueError("Source notice membership or contents changed.")
        if {name: pin(data) for name, data in wheels.items()} != record["wheels"]:
            raise ValueError("Python's bundled wheel identities changed.")
        text = collate(notices)
        if pin(text) != record["notice"] or text != NOTICE.read_bytes() or text != archive.read("Office.Python.Notices.txt"):
            raise ValueError("Supplemental notice text differs from original sources.")
    matched = None
    if engine is not None:
        inventory = (engine / "runtime-files.txt").read_bytes()
        if pin(inventory)["sha256"] != record["inventorySha256"]:
            raise ValueError("Engine inventory differs from supplemental notice selection.")
        prefix = "program/python-core-3.12.14/lib/"
        names = {}
        for line in inventory.decode("utf-8").splitlines():
            name, length, digest = line.split("\t")
            if name.startswith(prefix) and name.removeprefix(prefix).split("/")[0] in record["packageDirectories"]:
                names[name.removeprefix(prefix)] = {"bytes": int(length), "sha256": digest}
        if len(names) != record["runtimeFiles"]:
            raise ValueError("Python package file count differs.")
        for name, expected_pin in names.items():
            source_name = name.replace("/Lorem_ipsum.txt", "/Lorem ipsum.txt")
            original = (pip_files if name.startswith("pip/") else setuptools_files)[source_name]
            if name == "setuptools/_vendor/jaraco/text/__init__.py":
                original = original.replace(b"joinpath('Lorem ipsum.txt')", b"joinpath('Lorem_ipsum.txt')")
            if pin(original) != expected_pin or (engine / "runtime" / prefix / name).read_bytes() != original:
                raise ValueError("Runtime Python source mismatch: " + name)
        matched = len(names)
    return {"Passed": True, "Notices": len(notices), "RuntimeFilesCompared": matched,
            "SourceArchives": len(sources), "NativeRebuilt": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--engine-directory", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.bundle, args.engine_directory), indent=2))


if __name__ == "__main__":
    main()
