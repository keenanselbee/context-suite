"""Recheck an isolated Office runtime after an evaluation, without executing it."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_payload", TOOLS / "Stage-OfficePayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def verify(stage):
    stage = payload.local_path(stage, scratch_only=True)
    if stage.parent != payload.ROOT / ".codex-temp/office-isolation":
        raise ValueError("Use a retained Office isolation stage.")
    receipt_path = payload.local_path(stage / "office-copy.json", scratch_only=True)
    if receipt_path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError("Office copy receipt exceeds the inspection limit.")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    runtime = payload.local_path(stage / "runtime/office", scratch_only=True)
    record = payload.selection()
    if Path(receipt["destination"]).absolute() != runtime or receipt["archiveSha256"] != record["archiveSha256"]:
        raise ValueError("Office copy receipt identifies another runtime or archive.")
    entries = receipt["Files"]
    canonical = "".join(f"{item['Path']}\t{item['Bytes']}\t{item['Sha256'].upper()}\n"
                        for item in sorted(entries, key=lambda item: item["Path"])).encode("utf-8")
    if (len(canonical) != record["inventory"]["bytes"] or
            hashlib.sha256(canonical).hexdigest().upper() != record["inventory"]["sha256"]):
        raise ValueError("Office copy membership differs from the pinned inventory.")
    expected_files = {item["Path"] for item in entries}
    expected_directories = set(record["emptyDirectories"])
    for name in expected_files:
        expected_directories.update(parent.as_posix() for parent in Path(name).parents if parent != Path("."))
    actual_files, actual_directories = set(), set()

    def failed(error):
        raise error

    for current, folders, files in os.walk(runtime, followlinks=False, onerror=failed):
        for name in folders + files:
            path = Path(current) / name
            info = payload.ordinary(path)
            relative = path.relative_to(runtime).as_posix()
            (actual_directories if stat.S_ISDIR(info.st_mode) else actual_files).add(relative)
    if actual_files != expected_files or actual_directories != expected_directories:
        raise ValueError("Office runtime acquired missing or extra entries during evaluation.")
    for item in entries:
        payload.identity(runtime / item["Path"], {"bytes": item["Bytes"], "sha256": item["Sha256"].upper()})
    return {"RuntimeFiles": len(entries), "Bytes": sum(item["Bytes"] for item in entries),
            "InventorySha256": record["inventory"]["sha256"], "ExactMembership": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage")
    args = parser.parse_args()
    print(json.dumps(verify(args.stage)))


if __name__ == "__main__":
    main()
