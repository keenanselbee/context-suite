"""Rebuild only the LGPL libraries from a verified extracted local HEIF source kit."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import uuid


spec = importlib.util.spec_from_file_location("distribution", Path(__file__).with_name("Review-HeifDistribution.py"))
distribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(distribution)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--cmake", required=True, type=Path)
    args = parser.parse_args()
    bundle = distribution.local(args.bundle)
    distribution.verify(bundle)
    contents = distribution.zip_contents(distribution.read(bundle / "heif-source-review.zip"))
    # A short fresh root keeps nested native build paths within Windows tool limits.
    root = distribution.SCRATCH / ("hr-" + uuid.uuid4().hex[:12])
    root.mkdir()
    print("HEIF source-kit rebuild:", root, flush=True)
    for name, data in contents.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as stream:
            stream.write(data)
    command = [sys.executable, "-B", str(root / "tools/heif-engine/Build-HeifEngine.py"), "--libraries-only",
        "--sources", str(root / ".codex-temp/inputs"), "--aom", str(root / ".codex-temp/aom"),
        "--cmake", str(args.cmake.resolve(strict=True))]
    child = subprocess.run(command, cwd=root, capture_output=True, timeout=900)
    (root / "rebuild.stdout.log").write_bytes(child.stdout)
    (root / "rebuild.stderr.log").write_bytes(child.stderr)
    (root / "rebuild.command.json").write_text(json.dumps({"command": command, "exit": child.returncode}, indent=2))
    print(child.stdout.decode(errors="replace"), child.stderr.decode(errors="replace"), flush=True)
    if child.returncode:
        raise RuntimeError("Source kit rebuild failed; inspect retained logs")
    for name, data in contents.items():
        if (root / name).read_bytes() != data:
            raise ValueError("Extracted source-kit input changed")
    results = list((root / ".codex-temp/heif-engine").glob("*/results.json"))
    if len(results) != 1:
        raise ValueError("Expected one source-kit build result")
    result = json.loads(results[0].read_text())
    if not result["Passed"] or not result["LibrariesOnly"] or set(result["Payload"]) != {"heif.dll", "libde265.dll"}:
        raise ValueError("Unexpected source-kit build result")
    if (root / "proprietary").exists():
        raise ValueError("Source kit must not require a private checkout")
    (root / "verified.json").write_text(json.dumps({"Passed": True, "SourceKitUnchanged": True,
        "PrivateCheckoutAbsent": True, "LibraryBuildResult": str(results[0]),
        "ResultSha256": distribution.sha(results[0].read_bytes())}, indent=2) + "\n")
    print("Verified library-only source-kit rebuild:", root, flush=True)


if __name__ == "__main__":
    main()
