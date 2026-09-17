"""Verify HEIF review-bundle tampering is rejected without executing runtime files."""
import importlib.util
import json
from pathlib import Path
import shutil
import stat
import sys
import uuid
import zipfile


spec = importlib.util.spec_from_file_location("distribution", Path(__file__).with_name("Review-HeifDistribution.py"))
distribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(distribution)


def main():
    original = distribution.local(sys.argv[1])
    distribution.verify(original)
    before = {path.name: distribution.sha(path.read_bytes()) for path in original.iterdir() if path.is_file()}
    evidence = distribution.SCRATCH / "heif-distribution-tests" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    checks = ["Original source/runtime bundle passes"]
    mutations = [
        ("binary", "heif-runtime-review.zip", "bin/heif.dll", "change"),
        ("missing-license", "heif-runtime-review.zip", "licenses/libheif.COPYING.txt", "remove"),
        ("patent", "heif-runtime-review.zip", "licenses/AOM.PATENTS.txt", "change"),
        ("product-notice", "heif-runtime-review.zip", "THIRD-PARTY-NOTICES.txt", "change"),
        ("patch", "heif-runtime-review.zip", "SOURCE-CHANGES.patch", "change"),
        ("source", "heif-source-review.zip", ".codex-temp/inputs/libheif.tar.gz", "change"),
        ("recipe", "heif-source-review.zip", "tools/heif-engine/Build-HeifEngine.py", "change"),
        ("aom-artifact", "heif-source-review.zip", ".codex-temp/aom/lib/CORE_RL_aom_.lib", "change"),
        ("extra-canary", "heif-source-review.zip", "proprietary/synthetic-canary.txt", "extra"),
        ("traversal", "heif-runtime-review.zip", "../escape.txt", "extra"),
        ("duplicate", "heif-runtime-review.zip", "THIRD-PARTY-NOTICES.txt", "duplicate"),
    ]
    for label, archive_name, entry_name, action in mutations:
        target = evidence / label
        shutil.copytree(original, target)
        content = distribution.zip_contents((target / archive_name).read_bytes())
        if action == "change":
            content[entry_name] += b"\nauthored mismatch"
        elif action == "remove":
            del content[entry_name]
        elif action == "extra":
            content[entry_name] = b"synthetic test data, not private source"
        with zipfile.ZipFile(target / archive_name, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in content.items():
                entry = zipfile.ZipInfo(name)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | 0o644) << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, data)
            if action == "duplicate":
                archive.writestr(entry_name, content[entry_name])
        # Recompute the editable inventory; fixed identities and derived notices
        # must still reject the changed material.
        manifest = json.loads((target / "bundle-inventory.json").read_text())
        manifest["bundles"][archive_name] = {name: {"bytes": len(data), "sha256": distribution.sha(data)}
                                            for name, data in sorted(content.items())}
        (target / "bundle-inventory.json").write_text(json.dumps(manifest))
        try:
            distribution.verify(target)
        except (ValueError, KeyError, zipfile.BadZipFile) as error:
            checks.append(label + " rejected: " + str(error))
        else:
            raise AssertionError("Accepted tampered bundle: " + label)
    distribution.verify(original)
    if before != {path.name: distribution.sha(path.read_bytes()) for path in original.iterdir() if path.is_file()}:
        raise ValueError("Original review material changed")
    checks.append("Original bundle unchanged and passes after all refusals")
    if (evidence / "escape.txt").exists():
        raise ValueError("Verification wrote an archive entry")
    checks.append("Traversal entry never extracted")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks}, indent=2) + "\n")
    print("Passed", len(checks), "HEIF distribution checks:", evidence, flush=True)


if __name__ == "__main__":
    main()
