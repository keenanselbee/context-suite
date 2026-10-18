"""Derive an isolated Office experiment from an exact, retained full runtime.

Only the explicitly reviewed alternative themes and SDK files are excluded.
Production selection is not changed. Failed copies remain for inspection.
"""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat


TOOLS = Path(__file__).resolve().parent
POLICY = TOOLS / "runtime-reduction-policy.json"
spec = importlib.util.spec_from_file_location("office_stage", TOOLS / "Stage-OfficePayload.py")
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


def verify_tree(engine, record):
    engine = stage.local_path(engine, scratch_only=True)
    files, directories = stage.membership(engine, record)
    actual_files, actual_directories = set(), set()

    def failed(error):
        raise error

    for current, folders, names in os.walk(engine, followlinks=False, onerror=failed):
        for name in folders + names:
            path = Path(current) / name
            info = stage.ordinary(path)
            relative = path.relative_to(engine).as_posix()
            (actual_directories if stat.S_ISDIR(info.st_mode) else actual_files).add(relative)
    if actual_files != set(files) or actual_directories != directories:
        raise ValueError("Runtime membership differs from the specified selection.")
    for name, pin in files.items():
        stage.identity(engine / name, pin)
    return files


def derive(source, destination):
    source = stage.local_path(source, scratch_only=True)
    destination = stage.local_path(destination, scratch_only=True)
    if source.is_relative_to(destination) or destination.is_relative_to(source):
        raise ValueError("Source and destination must not overlap.")
    if os.path.lexists(destination):
        raise ValueError("Use a fresh destination; existing experiments are preserved.")
    policy_bytes = POLICY.read_bytes()
    policy = json.loads(policy_bytes)
    if policy["formatVersion"] != 1 or policy["retainedTheme"] != "share/config/images_colibre.zip":
        raise ValueError("Unknown Office reduction policy.")
    base = policy["baseSelection"]
    files = verify_tree(source, base)
    excluded = {}
    for item in policy["excludedFiles"]:
        name = item["path"]
        if (name in excluded or name == policy["retainedTheme"] or
                not (name.startswith("sdk/") or
                     name.startswith("share/config/images_") and name.endswith(".zip"))):
            raise ValueError("Exclusion is outside the bounded theme/SDK policy.")
        pin = {key: item[key] for key in ("bytes", "sha256")}
        if files.get("runtime/" + name) != pin:
            raise ValueError("Excluded file does not match the full runtime: " + name)
        excluded[name] = pin
    if not excluded or "runtime/" + policy["retainedTheme"] not in files:
        raise ValueError("The reduced runtime must retain its fallback icon theme.")
    retained = {name.removeprefix("runtime/"): pin for name, pin in files.items()
                if name.startswith("runtime/") and name.removeprefix("runtime/") not in excluded}
    inventory = "".join(f"{name}\t{pin['bytes']}\t{pin['sha256']}\n"
                        for name, pin in sorted(retained.items())).encode("utf-8")
    record = json.loads(json.dumps(base))
    record["scope"] = "Derived theme/SDK reduction experiment; rendering and production acceptance pending."
    record["inventory"].update(bytes=len(inventory), sha256=hashlib.sha256(inventory).hexdigest().upper(),
                               files=len(retained), runtimeBytes=sum(pin["bytes"] for pin in retained.values()))
    # The source archive remains provenance for unchanged retained binaries.
    # A new runtime archive identity is assigned only after experiment acceptance.
    destination.mkdir(parents=True)
    engine = destination / "office-engine"
    engine.mkdir()
    copy = {name: pin for name, pin in files.items()
            if name != base["inventory"]["path"] and
            (not name.startswith("runtime/") or name.removeprefix("runtime/") in retained)}
    for name in record["emptyDirectories"]:
        (engine / "runtime" / name).mkdir(parents=True, exist_ok=True)
    for name, pin in sorted(copy.items()):
        original = stage.local_path(source / name, scratch_only=True)
        target = stage.local_path(engine / name, scratch_only=True)
        target.parent.mkdir(parents=True, exist_ok=True)
        with original.open("rb") as input_file, target.open("xb") as output_file:
            shutil.copyfileobj(input_file, output_file)
        stage.identity(target, pin)
    with (engine / record["inventory"]["path"]).open("xb") as stream:
        stream.write(inventory)
    verify_tree(engine, record)
    verify_tree(source, base)
    if POLICY.read_bytes() != policy_bytes:
        raise ValueError("Reduction policy changed during derivation.")
    receipt = {
        "formatVersion": 1,
        "policySha256": hashlib.sha256(policy_bytes).hexdigest().upper(),
        "scriptSha256": stage.digest(Path(__file__)),
        "baseInventorySha256": base["inventory"]["sha256"],
        "derivedInventorySha256": record["inventory"]["sha256"],
        "excludedFiles": policy["excludedFiles"],
        "removedBytes": sum(pin["bytes"] for pin in excluded.values()),
        "retainedBinaryBytesUnchanged": True,
        "sourceUnchanged": True,
        "acceptance": "Not yet tested for conversion fidelity or production use.",
    }
    for name, value in (("selection.json", record), ("derivation.json", receipt)):
        with (destination / name).open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(json.dumps(value, indent=2) + "\n")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine-directory", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    receipt = derive(args.engine_directory, args.output)
    print(json.dumps({"output": str(args.output), "removedBytes": receipt["removedBytes"],
                      "inventorySha256": receipt["derivedInventorySha256"]}))


if __name__ == "__main__":
    main()
