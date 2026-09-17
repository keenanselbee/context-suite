"""Verify authored missing-color variants without weakening HDR admission."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import uuid

spec = importlib.util.spec_from_file_location("bridge", Path(__file__).with_name("Test-HeifBridge.py"))
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


def mutate(data, remove=False, transfer=None):
    """Change only one known authored NCLX; retain every byte offset/extent."""
    changed = bytearray(data)
    if data.count(b"nclx") != 1:
        raise ValueError("Expected one authored NCLX")
    offset = data.index(b"nclx")
    if data[offset - 4:offset] != b"colr":
        raise ValueError("Unexpected color property")
    if remove:
        changed[offset - 4:offset] = b"free"
        # The payload becomes an ignored nonessential property. Clear only its
        # association's essential bit, retaining item/property identities.
        start = data.index(b"ipco") + 4
        index = 1
        while start != offset - 8:
            size = struct.unpack_from(">I", data, start)[0]
            if size < 8 or start + size > offset - 8:
                raise ValueError("Unexpected property layout")
            start += size; index += 1
        start = data.index(b"ipma") + 4
        version, flags = data[start], int.from_bytes(data[start + 1:start + 4], "big")
        entries = struct.unpack_from(">I", data, start + 4)[0]
        start += 8
        found = 0
        for _ in range(entries):
            start += 2 if version < 1 else 4
            count = data[start]; start += 1
            size = 2 if flags & 1 else 1
            for _ in range(count):
                value = int.from_bytes(data[start:start + size], "big")
                mask = (1 << (size * 8 - 1)) - 1
                if value & mask == index:
                    changed[start:start + size] = (value & mask).to_bytes(size, "big")
                    found += 1
                start += size
        if found != 1:
            raise ValueError("Expected one color association")
    else:
        struct.pack_into(">H", changed, offset + 6, transfer)
        if transfer == 2:
            struct.pack_into(">H", changed, offset + 4, 2)
    return bytes(changed)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge", type=Path, required=True)
    parser.add_argument("--fixture-evidence", type=Path, action="append", required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    library = args.bridge.resolve(strict=True)
    library.relative_to(root / ".codex-temp")
    output = root / ".codex-temp/heif-color" / uuid.uuid4().hex
    output.mkdir(parents=True); (output / "fixtures").mkdir()
    print("HEIF color evidence:", output, flush=True)
    inputs = {str(path): bridge.digest(path) for path in (Path(__file__).resolve(),
        Path(__file__).with_name("Test-HeifBridge.py"), library, library.parent / "heif.dll", library.parent / "libde265.dll")}
    lease = os.add_dll_directory(str(library.parent))
    native = ctypes.CDLL(str(library))
    native.cs_heif_decode.argtypes = [ctypes.c_void_p, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
    native.cs_heif_info.argtypes = [ctypes.c_void_p, ctypes.POINTER(bridge.Info), ctypes.c_uint32]
    native.cs_heif_copy.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint64]
    native.cs_heif_release.argtypes = [ctypes.c_void_p]
    checks = []; rows = []

    def check(value, message):
        if not value:
            (output / "failure.json").write_text(json.dumps({"Failed": message, "Checks": checks, "Rows": rows}, indent=2))
            raise AssertionError(message)
        checks.append(message)

    for evidence in args.fixture_evidence:
        evidence = evidence.resolve(strict=True); evidence.relative_to(root / ".codex-temp")
        baseline = json.loads((evidence / "results.json").read_text())
        if not baseline["Passed"]:
            raise ValueError("Passing independent fixture evidence required")
        recorded = json.loads((evidence / "inputs.json").read_text()) | baseline["Fixtures"]
        if any(bridge.digest(Path(path)) != expected for path, expected in recorded.items()):
            raise ValueError("Authored fixture evidence changed")
        inputs.update(recorded); inputs[str(evidence / "results.json")] = bridge.digest(evidence / "results.json")
        depth = baseline["BitDepth"]
        for codec in ("heic", "avif"):
            for variant, mode in (("icc-srgb", "remove"), ("icc-adobe", "remove"),
                ("p3", "remove"), ("bt709", "remove"), ("icc-adobe-bt709", "remove"),
                ("", "remove"), ("", "unspecified"), ("pq", "remove"), ("hlg", "remove"),
                ("pq", "hidden"), ("hlg", "hidden")):
                name = "orientation-1-color" + ("-" + variant if variant else "") + "." + codec
                source = evidence / "fixtures" / name
                data = mutate(source.read_bytes(), remove=mode == "remove", transfer=2 if mode != "remove" else None)
                path = output / "fixtures" / (str(depth) + "-" + mode + "-" + name)
                path.write_bytes(data)
                buffer = ctypes.create_string_buffer(data); handle = ctypes.c_void_p()
                status = native.cs_heif_decode(buffer, len(data), None, None, ctypes.byref(handle))
                row = {"file": str(path), "sha256": bridge.digest(path), "hdr": variant in ("pq", "hlg"), "status": status}
                rows.append(row)
                try:
                    if mode == "hidden":
                        check(status == 3 and not handle, "Hidden bitstream HDR is refused " + path.name)
                        continue
                    check(status == 0 and handle, "Valid native color decode " + path.name)
                    info = bridge.Info()
                    check(native.cs_heif_info(handle, ctypes.byref(info), ctypes.sizeof(info)) == 0 and info.abi == 3,
                        "Color-aware ABI " + path.name)
                    row["info"] = {name: getattr(info, name) for name, _ in info._fields_}
                    check((info.effectivePrimaries, info.effectiveTransfer, info.effectiveMatrix, info.effectiveFullRange) ==
                        (12 if variant == "p3" else 1,
                         16 if variant == "pq" else 18 if variant == "hlg" else 1 if variant in ("bt709", "icc-adobe-bt709") else 13,
                         1, 1), "Effective color " + path.name)
                    check(bool(info.flags & 2048) == (mode == "unspecified"), "Explicit default marker " + path.name)
                    raw = (evidence / (name + "-decoded") / "pixels.rgba").read_bytes()
                    oracle = next(item for item in baseline["Decoded"] if item["file"] == name)
                    check(bridge.digest(evidence / (name + "-decoded") / "pixels.rgba") == oracle["actualPixelsSha256"] and
                        oracle["maximumPixelDifference"] <= 1, "Independent authored sample baseline")
                    samples = list(raw) if depth == 8 else [x[0] for x in struct.iter_unpack("<H", raw)]
                    maximum = (1 << depth) - 1
                    expected = b"".join(struct.pack("<H", (value * 65535 + maximum // 2) // maximum) for value in samples)
                    pixels = ctypes.create_string_buffer(len(expected))
                    check(native.cs_heif_copy(handle, 0, pixels, len(expected)) == 0 and pixels.raw == expected,
                        "Exact normalized samples " + path.name)
                    row["expectedPixelsSha256"] = hashlib.sha256(expected).hexdigest().upper()
                    (path.with_suffix(path.suffix + ".rgba")).write_bytes(expected)
                    if info.iccBytes:
                        icc = ctypes.create_string_buffer(info.iccBytes)
                        check(native.cs_heif_copy(handle, 1, icc, len(icc)) == 0 and
                            icc.raw == (evidence / (name + "-decoded") / "profile.icc").read_bytes(), "Original ICC retained " + path.name)
                        (path.with_suffix(path.suffix + ".icc")).write_bytes(icc.raw)
                        row["expectedIccSha256"] = hashlib.sha256(icc.raw).hexdigest().upper()
                finally:
                    if handle: native.cs_heif_release(handle)
    check(all(bridge.digest(Path(path)) == expected for path, expected in inputs.items()), "All inputs unchanged")
    (output / "inputs.json").write_text(json.dumps(inputs, indent=2))
    (output / "results.json").write_text(json.dumps({"Passed": True, "Checks": len(checks), "Rows": rows}, indent=2))
    print("Passed", len(checks), "native color checks", flush=True)


if __name__ == "__main__":
    main()
