"""Exercise generated HEIC/AVIF fixtures with a separate decode-only engine.

This research harness never registers an engine or modifies a production stage.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def expected_pixels(orientation, alpha, colored):
    width, height = (48, 64) if orientation >= 5 else (64, 48)
    result = bytearray()
    for y in range(height):
        for x in range(width):
            sx, sy = {
                1: (x, y), 2: (63 - x, y), 3: (63 - x, 47 - y),
                4: (x, 47 - y), 5: (y, x), 6: (y, 47 - x),
                7: (63 - y, 47 - x), 8: (63 - y, x),
            }[orientation]
            gray = (sx * 7 + sy * 11 + ((sx // 8) ^ (sy // 8)) * 19) & 255
            opacity = ((sx // 8) % 4) * 85 if alpha else 255
            if colored:
                cb = 96 + ((sx // 8) % 4) * 16 - 128
                cr = 104 + ((sy // 8) % 4) * 16 - 128
                # Independent full-range BT.709 matrix, retaining sRGB transfer.
                red = gray + 2 * (1 - 0.2126) * cr
                blue = gray + 2 * (1 - 0.0722) * cb
                green = (gray - 0.2126 * red - 0.0722 * blue) / (1 - 0.2126 - 0.0722)
                result.extend(max(0, min(255, round(value))) for value in (red, green, blue))
                result.append(opacity)
            else:
                result.extend((gray, gray, gray, opacity))
    return width, height, bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--decoder-sdk", required=True, type=Path)
    parser.add_argument("--encoder-sdk", required=True, type=Path)
    parser.add_argument("--cmake", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    scratch = root / ".codex-temp"
    decoder = args.decoder_sdk.resolve(strict=True)
    encoder = args.encoder_sdk.resolve(strict=True)
    for sdk in (decoder, encoder):
        sdk.relative_to(scratch)
    if decoder == encoder:
        raise ValueError("Use separate decoder and fixture-encoder SDKs.")
    cmake = args.cmake.resolve(strict=True)
    evidence = scratch / "heif-fixtures" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    (evidence / "Directory.Build.props").write_text("<Project />\n")
    print("HEIF fixture evidence:", evidence, flush=True)
    sources = [Path(__file__).resolve()]
    for directory in ("Fixtures", "Probe"):
        sources += list((root / "tools/heif-engine" / directory).glob("*"))
    inputs = {str(path): digest(path) for path in sources if path.is_file()}
    inputs.update({str(path): digest(path) for sdk in (decoder, encoder)
                   for path in sdk.rglob("*") if path.is_file()})
    (evidence / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")

    def run(name, command, expected_success=True):
        child = subprocess.run([str(value) for value in command], cwd=evidence,
                               capture_output=True, timeout=180)
        (evidence / (name + ".stdout.log")).write_bytes(child.stdout)
        (evidence / (name + ".stderr.log")).write_bytes(child.stderr)
        (evidence / (name + ".command.json")).write_text(json.dumps({
            "command": [str(value) for value in command], "exit": child.returncode}, indent=2))
        if (child.returncode == 0) != expected_success:
            raise RuntimeError("Unexpected result for " + name + "; inspect retained logs.")
        return child.stdout

    executables = {}
    for name, sdk in (("Fixtures", encoder), ("Probe", decoder)):
        build = evidence / (name + "-build")
        run(name + "-configure", [cmake, "-S", root / "tools/heif-engine" / name,
            "-B", build, "-G", "Visual Studio 18 2026", "-A", "x64",
            "-DCMAKE_PREFIX_PATH=" + str(sdk)])
        run(name + "-build", [cmake, "--build", build, "--config", "Release", "--parallel", "4"])
        for library in (sdk / "bin/heif.dll", decoder / "bin/libde265.dll"):
            shutil.copy2(library, build / "Release" / library.name)
        executables[name] = build / "Release" / ("ContextSuite.Heif." + name + ".exe")
    fixtures = evidence / "fixtures"
    run("generate", [executables["Fixtures"], fixtures])
    originals = {str(path): digest(path) for path in fixtures.iterdir()}
    if len(originals) != 24:
        raise ValueError("Expected twenty-four authored fixtures.")
    results = []
    exif = bytes.fromhex("49492a0008000000010012010300010000000100000000000000")
    xmp = ('<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
           '<rdf:Description xmlns:dc="http://purl.org/dc/elements/1.1/" dc:description="Context Suite generated fixture"/></rdf:RDF></x:xmpmeta>').encode()
    for source in sorted(fixtures.iterdir()):
        orientation = int(source.stem.split("-")[1])
        alpha = "-alpha" in source.stem
        metadata = "-metadata" in source.stem
        output = evidence / (source.name + "-decoded")
        info = json.loads(run(source.name, [executables["Probe"], source, output]))
        width, height, pixels = expected_pixels(orientation, alpha, "-color" in source.stem)
        actual = (output / "pixels.rgba").read_bytes()
        if (info["width"], info["height"]) != (width, height) or len(actual) != len(pixels):
            raise ValueError("Wrong oriented geometry: " + source.name)
        differences = [abs(a - b) for a, b in zip(actual, pixels)]
        if max(differences) > 1 or any(differences[3::4]):
            raise ValueError("Unexpected decoded pixels: " + source.name + ", max difference " + str(max(differences)))
        if info["alpha"] != int(alpha) or info["sourceLumaBits"] != 8 or info["topLevelImages"] != 1:
            raise ValueError("Wrong source declarations: " + source.name)
        blocks = [(output / ("metadata-" + str(i) + ".bin")).read_bytes() for i in range(info["metadataCount"])]
        if metadata:
            if len(blocks) != 2 or xmp not in blocks or not any(
                    len(block) >= 4 and block[4 + int.from_bytes(block[:4], "big"):] == exif for block in blocks):
                raise ValueError("Fixture metadata differs: " + source.name)
        elif blocks:
            raise ValueError("Unexpected fixture metadata: " + source.name)
        results.append({"file": source.name, "facts": info, "maximumPixelDifference": max(differences),
                        "expectedPixelsSha256": hashlib.sha256(pixels).hexdigest().upper(),
                        "actualPixelsSha256": digest(output / "pixels.rgba")})
    for extension in ("heic", "avif"):
        content = (fixtures / ("orientation-1." + extension)).read_bytes()
        mutations = {"truncated": content[:24]}
        large = bytearray(content)
        position = large.index(b"ispe")
        struct.pack_into(">I", large, position + 8, 40000001)
        mutations["oversized"] = large
        for name, data in mutations.items():
            source = evidence / (name + "." + extension)
            source.write_bytes(data)
            run(source.name, [executables["Probe"], source, evidence / (source.name + "-decoded")], False)
            if (evidence / (source.name + "-decoded")).exists():
                raise ValueError("Refused input created output: " + source.name)
    if any(digest(Path(name)) != value for group in (inputs, originals) for name, value in group.items()):
        raise ValueError("Source, SDK or fixture changed during verification.")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Fixtures": originals,
        "Decoded": results, "Refusals": 4, "InputsUnchanged": True}, indent=2) + "\n")
    print("Twenty-four authored decodes and four hostile-input refusals passed.", flush=True)


if __name__ == "__main__":
    main()
