"""Write the pinned candidate inventory from retained Office copy evidence.

This only writes a new inventory file inside repository scratch. It does not
copy/stage a runtime, launch Office, create a Windows profile or enable a menu.
"""

import argparse
import hashlib
import json
from pathlib import Path


INVENTORY_SHA256 = "70DAF53038F8B4E8B5FDF74B6877393616094762C0E0BDC5245D657C16DBCD2D"
ARCHIVE_SHA256 = "F9877032FD908BEB9C0DDF06DF4AF5C2E85F419C42E14876C4CCE5AAE5FB2660"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("copy_receipt", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    scratch = Path(__file__).resolve().parents[2] / ".codex-temp"
    output = args.output.resolve()
    if not output.is_relative_to(scratch.resolve()) or output.exists():
        parser.error("Use a new inventory file inside this repository's .codex-temp directory.")
    receipt = json.loads(args.copy_receipt.read_text(encoding="utf-8"))
    if receipt["archiveSha256"] != ARCHIVE_SHA256:
        raise ValueError("Office copy evidence identifies a different upstream archive.")
    files = receipt["Files"]
    data = "".join(f"{item['Path']}\t{item['Bytes']}\t{item['Sha256'].upper()}\n"
                   for item in sorted(files, key=lambda item: item["Path"])).encode("utf-8")
    if len(files) != 19332 or sum(item["Bytes"] for item in files) != 1517294910 or \
            hashlib.sha256(data).hexdigest().upper() != INVENTORY_SHA256:
        raise ValueError("Office copy inventory differs from the pinned candidate.")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as stream:
        stream.write(data)
    print(json.dumps({"inventory": str(output), "sha256": INVENTORY_SHA256,
                      "scope": "Candidate inventory only; payload verification is a separate runtime lease."}))


if __name__ == "__main__":
    main()
