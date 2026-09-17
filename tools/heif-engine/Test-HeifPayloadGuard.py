"""Verify the HEIF staging guard without copying another complete Office runtime."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import uuid
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("heif_payload", Path(__file__).with_name("Stage-HeifPayload.py"))
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distribution", required=True, type=Path)
    parser.add_argument("--retained-payload", required=True, type=Path)
    args = parser.parse_args()
    retained = payload.local(args.retained_payload)
    evidence = payload.ROOT / ".codex-temp/heif-payload-guard" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    inputs = {str(path): hashlib.sha256(path.read_bytes()).hexdigest().upper()
              for path in (Path(__file__), Path(__file__).with_name("Stage-HeifPayload.py"), retained / "payload-inventory.json")}
    checks = []
    for root in (retained, evidence / "authored-inventory"):
        if root != retained:
            root.mkdir(); (root / "payload-inventory.json").write_bytes(b"authored inventory marker")
        inventory = (root / "payload-inventory.json").read_bytes()
        if (root / "heif").exists(): raise ValueError("Choose a retained payload without HEIF")
        with patch.object(payload.distribution, "verify", side_effect=AssertionError("Guard did not run before distribution access")):
            try:
                payload.stage(args.distribution, root)
            except ValueError as error:
                if "inventoried payload cannot be changed" not in str(error): raise
            else: raise AssertionError("Inventoried payload was writable")
        checks.append("Inventoried payload refused before distribution access: " + str(root))
        if (root / "heif").exists() or (root / "payload-inventory.json").read_bytes() != inventory:
            raise AssertionError("Refused staging changed its destination")
        checks.append("No decoder directory created and inventory unchanged: " + str(root))
    fresh = evidence / "fresh"; fresh.mkdir()
    payload.stage(args.distribution, fresh)
    payload.verify(fresh)
    checks.append("Fresh un-inventoried stage still admits all fourteen verified members")
    if any(hashlib.sha256(Path(path).read_bytes()).hexdigest().upper() != expected for path, expected in inputs.items()):
        raise AssertionError("Guard test inputs changed")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "Inputs": inputs}, indent=2))
    print("Passed", len(checks), "HEIF staging guard checks:", evidence, flush=True)


if __name__ == "__main__":
    main()
