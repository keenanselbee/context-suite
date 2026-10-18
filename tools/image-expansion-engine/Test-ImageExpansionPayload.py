"""Exercise exact membership and immutable staging guards for the selected engines."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import zipfile


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("image_expansion_stage", TOOLS / "Stage-ImageExpansionPayload.py")
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


def refused(action, label):
    try:
        action()
    except (ValueError, FileNotFoundError, OSError):
        return label
    raise AssertionError("Expected refusal: " + label)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--avif-build", type=Path, required=True)
    parser.add_argument("--avif-sources", type=Path, required=True)
    parser.add_argument("--svg-build", type=Path, required=True)
    parser.add_argument("--cargo-home", type=Path, required=True)
    arguments = parser.parse_args()
    valid = stage.safe(arguments.payload, payload=True, verify=True)
    stage.verify(valid)
    results = ["selected payload verifies"]
    with tempfile.TemporaryDirectory(prefix="image-expansion-guards-", dir=stage.SCRATCH) as temporary:
        scratch = Path(temporary)
        archive = scratch / "image-expansion.zip"
        selected = stage.selection()
        with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as output:
            for engine, record in selected.items():
                for name in record["files"]:
                    output.write(valid / engine / name, engine + "/" + name)
        extracted = scratch / "extracted"
        extracted.mkdir()
        with zipfile.ZipFile(archive) as source:
            if set(source.namelist()) != {engine + "/" + name for engine, record in selected.items()
                                             for name in record["files"]}:
                raise AssertionError("Archive membership differs from selection")
            source.extractall(extracted)
        stage.verify(extracted)
        inventory = [{"path": (engine + "/" + name).replace("/", "\\"), **pin}
                     for engine, record in selected.items() for name, pin in record["files"].items()]
        (extracted / "payload-inventory.json").write_text(json.dumps(inventory), encoding="utf-8")
        stage.verify(extracted, inventory=True)
        results.append("extracted archive and outer inventory verify")

        copy = scratch / "copy"
        copy.mkdir()
        shutil.copytree(valid / "avif", copy / "avif")
        shutil.copytree(valid / "svg", copy / "svg")
        stage.verify(copy)

        def altered(path, content):
            original = path.read_bytes()
            path.write_bytes(content)
            try:
                results.append(refused(lambda: stage.verify(copy), "changed " + path.name))
            finally:
                path.write_bytes(original)
            stage.verify(copy)

        altered(copy / "avif/ContextSuite.Avif.Native.dll", b"altered binary")
        altered(copy / "svg/ContextSuite.Svg.exe", b"altered binary")
        altered(copy / "avif/provenance/notices/Aom.License.txt", b"altered notice")
        altered(copy / "svg/provenance/notices/Rust.Library.Notices.html", b"altered notice")
        altered(copy / "avif/provenance/source/aom.tar.gz", b"altered source")
        changed_crate = next((copy / "svg/provenance/source/crates").glob("*.crate"))
        altered(changed_crate, b"altered crate")

        missing = copy / "svg/provenance/source-verification.json"
        original = missing.read_bytes()
        missing.unlink()
        results.append(refused(lambda: stage.verify(copy), "missing provenance"))
        missing.write_bytes(original)
        extra = copy / "svg/unreviewed.exe"
        extra.write_bytes(b"unreviewed")
        results.append(refused(lambda: stage.verify(copy), "extra executable"))
        extra.unlink()
        (copy / "avif/empty").mkdir()
        results.append(refused(lambda: stage.verify(copy), "extra empty directory"))
        (copy / "avif/empty").rmdir()
        stage.verify(copy)

        link = copy / "avif/provenance/notices/Unreviewed.txt"
        os.link(copy / "avif/provenance/notices/Aom.License.txt", link)
        results.append(refused(lambda: stage.verify(copy), "hard-linked extra member"))
        link.unlink()
        stage.verify(copy)

        inventory = copy / "payload-inventory.json"
        inventory.write_text(json.dumps([{"path": "avif/ContextSuite.Avif.Native.dll", "bytes": 1,
                                          "sha256": "0" * 64}]), encoding="utf-8")
        results.append(refused(lambda: stage.verify(copy, inventory=True), "false outer inventory"))
        results.append(refused(lambda: stage.stage(copy, arguments.avif_build, arguments.avif_sources,
                                                    arguments.svg_build, arguments.cargo_home),
                               "inventoried stage mutation"))

        fresh = scratch / "fresh"
        fresh.mkdir()
        fresh.joinpath("payload-inventory.json").write_text("[]", encoding="utf-8")
        results.append(refused(lambda: stage.stage(fresh, arguments.avif_build, arguments.avif_sources,
                                                    arguments.svg_build, arguments.cargo_home),
                               "inventoried fresh stage"))
        if (fresh / "avif").exists() or (fresh / "svg").exists():
            raise AssertionError("Inventoried refusal created an engine directory")

        stale_build = scratch / "stale-svg-build"
        stale_build.mkdir()
        for name in ("ContextSuite.Svg.exe", "build-provenance.json", "source-verification.json"):
            shutil.copyfile(arguments.svg_build / name, stale_build / name)
        (stale_build / "ContextSuite.Svg.exe").write_bytes(b"old unsigned renderer")
        empty_stage = scratch / "stale-stage"
        empty_stage.mkdir()
        results.append(refused(lambda: stage.stage(empty_stage, arguments.avif_build,
                                                    arguments.avif_sources, stale_build,
                                                    arguments.cargo_home), "unpinned renderer input"))
        if list(empty_stage.iterdir()):
            raise AssertionError("Unpinned input created an engine directory")
        results.append(refused(lambda: stage.stage(empty_stage, arguments.avif_build,
                                                    arguments.avif_sources, arguments.svg_build,
                                                    scratch / "missing-cargo-home"), "missing crate input"))
        if list(empty_stage.iterdir()):
            raise AssertionError("Missing input created an engine directory")

        unsigned = scratch / "unsigned"
        (unsigned / "avif").mkdir(parents=True)
        (unsigned / "svg").mkdir()
        shutil.copyfile(valid / "avif/ContextSuite.Avif.Native.dll", unsigned / "avif/ContextSuite.Avif.Native.dll")
        shutil.copyfile(valid / "svg/ContextSuite.Svg.exe", unsigned / "svg/ContextSuite.Svg.exe")
        results.append(refused(lambda: stage.verify(unsigned), "unsigned binary-only payload"))

    print(json.dumps({"checks": len(results), "passed": results}, indent=2))


if __name__ == "__main__":
    main()
