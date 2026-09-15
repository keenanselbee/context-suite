"""Copy the pinned evaluation runtime into one fresh, owned isolation case."""
import hashlib
import json
import pathlib
import re
import shutil
import stat
import sys


def regular_path(path):
    for item in (path, *path.parents):
        if item.exists() and item.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Reparse paths are not permitted for evaluation payloads")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def prepare(prepared, destination):
    root = pathlib.Path(__file__).resolve().parents[2]
    regular_path(prepared)
    regular_path(destination)
    prepared = prepared.resolve(strict=True)
    destination = destination.absolute()
    prepared.relative_to(root / ".codex-temp/office-engine")
    relative = destination.relative_to(root / ".codex-temp/office-isolation")
    if len(relative.parts) != 3 or not re.fullmatch("[0-9a-f]{32}", relative.parts[0]) or relative.parts[1:] != ("runtime", "office"):
        raise ValueError("Use the new isolation case's runtime/office directory")
    if destination.parent.exists():
        raise ValueError("Never reuse an isolation runtime boundary")
    pin = json.loads((root / "tools/office-engine/evaluation.json").read_text(encoding="utf-8"))
    if digest(prepared / "upstream.msi") != pin["archiveSha256"]:
        raise ValueError("Pinned Office archive changed")
    inventory = json.loads((prepared / "inventory.json").read_text(encoding="utf-8"))["Files"]
    paths = {}
    folded = set()
    for entry in inventory:
        path = pathlib.PurePosixPath(entry["Path"])
        if path.is_absolute() or any(part in ("", ".", "..") or ":" in part or "\\" in part for part in path.parts):
            raise ValueError("Noncanonical inventory path")
        if str(path) != entry["Path"] or str(path).casefold() in folded:
            raise ValueError("Ambiguous inventory path")
        paths[str(path)] = entry
        folded.add(str(path).casefold())
    payload = prepared / "unpacked"
    actual = set()
    for path in payload.rglob("*"):
        regular_path(path)
        if path.is_file():
            actual.add(path.relative_to(payload).as_posix())
    excluded = sorted(actual - paths.keys())
    if paths.keys() - actual or any(not re.fullmatch(r"program/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\.dmp", name) for name in excluded):
        raise ValueError("Office payload membership changed")
    # Earlier passive evaluations can leave engine crash dumps beside soffice.
    # Preserve them in the source; copy only pinned inventory members.
    destination.parent.mkdir()
    destination.mkdir()
    for index, (name, entry) in enumerate(paths.items(), 1):
        source = payload / name
        if source.stat().st_size != entry["Bytes"] or digest(source) != entry["Sha256"]:
            raise ValueError("Office source payload changed: " + name)
        output = destination / name
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, output)
        if output.stat().st_size != entry["Bytes"] or digest(output) != entry["Sha256"]:
            raise ValueError("Office runtime copy differs: " + name)
        if index % 4000 == 0:
            print(f"Verified {index}/{len(paths)} isolated runtime files", flush=True)
    receipt = {"prepared": str(prepared), "destination": str(destination), "accessBoundary": str(destination.parent), "archiveSha256": pin["archiveSha256"],
               "inventorySha256": digest(prepared / "inventory.json"), "files": len(paths),
               "bytes": sum(entry["Bytes"] for entry in inventory), "excludedSourceDiagnostics": excluded, "Files": inventory}
    (destination.parent.parent / "office-copy.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(f"Verified isolated Office copy: {len(paths)} files", flush=True)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: Prepare-OfficeIsolation.py <prepared Office directory> <new case/runtime/office>")
    prepare(pathlib.Path(sys.argv[1]).absolute(), pathlib.Path(sys.argv[2]).absolute())
