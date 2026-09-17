"""Verify the private in-memory bridge against retained independent decoder fixtures.

This does not stage an engine, enable application capabilities or prove admission.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import struct
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


class Info(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint32) for name in (
        "abi", "width", "height", "bits", "primaries", "transfer", "matrix", "fullRange",
        "itemType", "flags", "iccBytes", "exifBytes", "xmpBytes")]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge", required=True, type=Path)
    parser.add_argument("--fixture-evidence", required=True, action="append", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    bridge = args.bridge.resolve(strict=True)
    bridge.relative_to(root / ".codex-temp")
    evidence = root / ".codex-temp/heif-bridge-tests" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    print("Bridge evidence:", evidence, flush=True)
    inputs = {str(path): digest(path) for path in (
        bridge, bridge.parent / "heif.dll", bridge.parent / "libde265.dll", Path(__file__).resolve())}
    inputs.update({str(path): digest(path) for path in (root / "proprietary/src/ContextSuite.Heif.Native").iterdir() if path.is_file()})
    directories = [path.resolve(strict=True) for path in args.fixture_evidence]
    for folder in directories:
        folder.relative_to(root / ".codex-temp/heif-fixtures")
        recorded = json.loads((folder / "inputs.json").read_text())
        recorded.update(json.loads((folder / "results.json").read_text())["Fixtures"])
        if any(digest(Path(name)) != expected for name, expected in recorded.items()):
            raise ValueError("Fixture evidence inputs changed: " + str(folder))
        inputs[str(folder / "results.json")] = digest(folder / "results.json")
    (evidence / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")
    dll_directory = os.add_dll_directory(str(bridge.parent))
    native = ctypes.CDLL(str(bridge))
    callback_type = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_void_p)
    native.cs_heif_decode.argtypes = [ctypes.c_void_p, ctypes.c_uint64, callback_type, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
    native.cs_heif_decode.restype = ctypes.c_int32
    native.cs_heif_info.argtypes = [ctypes.c_void_p, ctypes.POINTER(Info), ctypes.c_uint32]
    native.cs_heif_info.restype = ctypes.c_int32
    native.cs_heif_copy.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint64]
    native.cs_heif_copy.restype = ctypes.c_int32
    native.cs_heif_release.argtypes = [ctypes.c_void_p]
    native.cs_heif_release.restype = None
    checks = 0

    def check(condition, message):
        nonlocal checks
        if not condition:
            raise ValueError(message)
        checks += 1

    def copy(result, block, size):
        buffer = ctypes.create_string_buffer(size)
        check(native.cs_heif_copy(result, block, buffer, size) == 0, "Copy block")
        return bytes(buffer.raw)

    observations = []
    null_callback = callback_type()
    for folder in directories:
        baseline = json.loads((folder / "results.json").read_text())
        check(baseline["Passed"] and baseline["InputsUnchanged"], "Passing baseline required")
        for row in baseline["Decoded"]:
            source = folder / "fixtures" / row["file"]
            data = ctypes.create_string_buffer(source.read_bytes())
            result = ctypes.c_void_p()
            check(native.cs_heif_decode(data, len(data) - 1, null_callback, None, ctypes.byref(result)) == 0 and result.value, "Decode " + source.name)
            try:
                info = Info()
                check(native.cs_heif_info(result, ctypes.byref(info), ctypes.sizeof(info)) == 0, "ABI info")
                facts = row["facts"]
                check((info.abi, info.width, info.height, info.bits) == (1, facts["width"], facts["height"], facts["sourceLumaBits"]), "Geometry and precision")
                check(all(getattr(info, key) == value for key, value in facts["sourceNclx"].items()), "Source NCLX")
                check(info.itemType == int.from_bytes(b"hvc1" if source.suffix == ".heic" else b"av01", "big"), "Primary codec item")
                check(bool(info.flags & 1) == bool(facts["alpha"]) and bool(info.flags & 16), "Alpha/NCLX flags")
                check(bool(info.flags & 64) == bool(facts["thumbnails"]) and bool(info.flags & 128) == (facts["topLevelImages"] > 1), "Thumbnail/collection flags")
                check(not (info.flags & (2 | 4 | 8 | 256 | 512)), "Unexpected capability flags")
                # x265 fixtures also carry a clean aperture for encoder padding.
                check(bool(info.flags & 32) == (source.suffix == ".heic" or int(source.stem.split("-")[1]) != 1),
                      "Container geometry flag " + source.name)
                pixels = folder / (source.name + "-decoded") / "pixels.rgba"
                check(digest(pixels) == row["expectedPixelsSha256"], "Exact independently authored pixel oracle")
                samples = list(pixels.read_bytes()) if info.bits == 8 else [x[0] for x in struct.iter_unpack("<H", pixels.read_bytes())]
                maximum = (1 << info.bits) - 1
                expected = b"".join(struct.pack("<H", (sample * 65535 + maximum // 2) // maximum) for sample in samples)
                actual = copy(result, 0, info.width * info.height * 8)
                check(actual == expected, "Exact Q16 normalization " + source.name)
                profile = folder / (source.name + "-decoded") / "profile.icc"
                check(copy(result, 1, info.iccBytes) == (profile.read_bytes() if profile.exists() else b""), "Exact ICC")
                metadata = [(folder / (source.name + "-decoded") / f"metadata-{index}.bin").read_bytes() for index in range(facts["metadataCount"])]
                exif = next((b[4 + int.from_bytes(b[:4], "big"):] for b in metadata if b[:4] == b"\0\0\0\0"), b"")
                xmp = next((b for b in metadata if b.startswith(b"<x:xmpmeta")), b"")
                check(copy(result, 2, info.exifBytes) == exif and copy(result, 3, info.xmpBytes) == xmp, "Exact EXIF/XMP")
                sentinel = ctypes.create_string_buffer(b"untouched")
                check(native.cs_heif_copy(result, 0, sentinel, 9) == 1 and sentinel.value == b"untouched", "Reject wrong buffer length without writing")
                check(native.cs_heif_info(result, ctypes.byref(info), 0) == 1 and native.cs_heif_copy(result, 4, None, 0) == 1, "Reject ABI/block mismatch")
                observations.append({"file": str(source), "bits": info.bits, "q16Sha256": hashlib.sha256(actual).hexdigest().upper(), "flags": info.flags})
            finally:
                native.cs_heif_release(result)
        for extension in ("heic", "avif"):
            for name in ("truncated", "oversized"):
                data = ctypes.create_string_buffer((folder / f"{name}.{extension}").read_bytes())
                result = ctypes.c_void_p(123)
                check(native.cs_heif_decode(data, len(data) - 1, null_callback, None, ctypes.byref(result)) != 0 and not result.value, "Refused input leaves no result")
        valid = ctypes.create_string_buffer((folder / "fixtures/orientation-1.heic").read_bytes())
        for after in (1, 3):
            calls = 0

            def cancel(_):
                nonlocal calls
                calls += 1
                return int(calls >= after)

            callback = callback_type(cancel)
            result = ctypes.c_void_p(123)
            check(native.cs_heif_decode(valid, len(valid) - 1, callback, None, ctypes.byref(result)) == 5 and not result.value, "Cancellation leaves no result")
        result = ctypes.c_void_p()
        check(native.cs_heif_decode(valid, len(valid) - 1, null_callback, None, ctypes.byref(result)) == 0, "Decode after cancellation")
        native.cs_heif_release(result)
    check(native.cs_heif_info(None, None, 0) == 1 and native.cs_heif_copy(None, 0, None, 0) == 1, "Null result boundaries")
    native.cs_heif_release(None)
    check(all(digest(Path(name)) == value for name, value in inputs.items()), "Bridge/evidence inputs unchanged")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "Decoded": observations}, indent=2) + "\n")
    dll_directory.close()
    print(f"{checks} bridge checks passed for {len(observations)} authored inputs.", flush=True)


if __name__ == "__main__":
    main()
