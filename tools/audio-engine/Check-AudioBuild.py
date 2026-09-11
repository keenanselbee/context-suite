"""Review exact native diagnostics and emitted Windows audio hardening metadata."""
import collections
import importlib.util
import json
import pathlib
import re
import subprocess
import sys
import uuid


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_build", TOOLS / "Build-AudioEngine.py")
build_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build_tool)

REVIEWS = {
    ("libavutil/log.c", 201, "C4333"): "Windows uint8 color entry promotes to int; shifting by 16 is defined and zero; plain diagnostic logs are tested.",
    ("libavutil/log.c", 207, "C4333"): "Windows uint8 color entry promotes to int; shifting by 16 is defined and zero; plain diagnostic logs are tested.",
    ("libavutil/log.c", 208, "C4333"): "Windows uint8 color entry promotes to int; shifting by 8 is defined and zero; plain diagnostic logs are tested.",
    ("libavutil/ripemd.c", 139, "C4101"): "Unused local t is already annotated av_unused upstream; no runtime operation is omitted.",
    ("libavutil/ripemd.c", 196, "C4101"): "Unused local t is already annotated av_unused upstream; no runtime operation is omitted.",
    ("libavutil/ripemd.c", 321, "C4101"): "Unused local t is already annotated av_unused upstream; no runtime operation is omitted.",
    ("libavutil/ripemd.c", 393, "C4101"): "Unused local t is already annotated av_unused upstream; no runtime operation is omitted.",
    ("libavcodec/vlc.c", 75, "C4334"): "Table bits are bounded to 30 before allocation; the signed 1 shift is representable before pointer arithmetic widens it.",
    ("libavcodec/vlc.c", 340, "C4334"): "Nonzero lengths are checked against at most 32; unsigned 1 shifts by 0..31 before widening to the 64-bit code accumulator.",
    ("libavcodec/vlc.c", 555, "C4334"): "Nonzero lengths are checked against at most 32; unsigned 1 shifts by 0..31 before widening to the 64-bit code accumulator.",
}
HOST_WARNING = "cl : Command line warning D9024 : unrecognized source file type 'ffbuild/bin2c_host.o', object file assumed"
MESSAGES = {"C4333": "'>>': right shift by too large amount, data loss",
            "C4101": "'t': unreferenced local variable",
            "C4334": "'<<': result of 32-bit shift implicitly converted to 64 bits (was 64-bit shift intended?)"}
HARDENING = ("8664 machine (x64)", "High Entropy Virtual Addresses", "Dynamic base", "NX compatible",
             "Control Flow Guard", "CET compatible", "CF instrumented", "FID table present")
SYSTEM_IMPORTS = {"kernel32.dll", "bcrypt.dll", "psapi.dll", "shell32.dll", "vcruntime140.dll"}
CRT_IMPORTS = {f"api-ms-win-crt-{name}-l1-1-0.dll" for name in
               ("runtime", "stdio", "math", "string", "utility", "heap", "convert", "filesystem", "time", "environment", "locale", "conio")}


def review_diagnostics(log, source):
    prefix = re.escape(source.rstrip("/").replace("\\", "/"))
    pattern = re.compile(prefix + r"/(.+)\((\d+)\): warning (C\d+): (.+)")
    counts = collections.Counter()
    reviewed = []
    for line in log.splitlines():
        if not re.search(r"\b(?:warning|error|fatal)\s+(?:[A-Z]+\d+|:)", line, re.IGNORECASE):
            continue
        if line == HOST_WARNING:
            key = ("host", 0, "D9024")
            explanation = "MSVC explicitly treats the Unix .o suffix as an object; the host utility links and emits final resources."
        else:
            match = pattern.fullmatch(line.replace("\\", "/"))
            if not match:
                raise ValueError("Unreviewed native diagnostic: " + line)
            name, number, code, message = match.groups()
            key = (name, int(number), code)
            if key not in REVIEWS or message != MESSAGES[code]:
                raise ValueError("Unreviewed native diagnostic: " + line)
            explanation = REVIEWS[key]
        counts[key] += 1
        if counts[key] > 1:
            raise ValueError("Duplicate native diagnostic: " + line)
        reviewed.append({"diagnostic": line, "review": explanation})
    return reviewed


def review_pe(text, runtime_names):
    missing = [value for value in HARDENING if value not in text]
    if missing:
        raise ValueError("Missing emitted PE hardening: " + ", ".join(missing))
    for label in ("Security Cookie", "Guard CF function table", "Guard CF function count"):
        value = re.search(r"^\s+([0-9A-Fa-f]+) " + re.escape(label) + r"\s*$", text, re.MULTILINE)
        if not value or not int(value[1], 16):
            raise ValueError("Missing nonzero PE field: " + label)
    imports = {name.lower() for name in re.findall(r"^    ([^\s]+\.dll)\s*$", text, re.MULTILINE | re.IGNORECASE)}
    if not imports or imports - (runtime_names | SYSTEM_IMPORTS | CRT_IMPORTS):
        raise ValueError("Unexpected or empty native import inventory: " + str(sorted(imports)))
    return {"imports": sorted(imports), "requiredHardening": list(HARDENING)}


def main():
    if len(sys.argv) != 2:
        raise ValueError("Usage: python Check-AudioBuild.py <completed FFmpeg workspace>")
    directory = build_tool.local_path(sys.argv[1])
    record = json.loads((directory / "build-result.json").read_text())
    if record["exitCode"] or record["scriptSha256"] != build_tool.digest(TOOLS / "Build-AudioEngine.py"):
        raise ValueError("Use a completed build with the current recipe")
    configure = record["configure"]
    cflags = next(value for value in configure if value.startswith("--extra-cflags="))
    ldflags = next(value for value in configure if value.startswith("--extra-ldflags="))
    if any(value not in cflags.split() for value in ("-GS", "-guard:cf", "-guard:ehcont")):
        raise ValueError("Compiler hardening was not requested")
    if any(value not in ldflags.replace("--extra-ldflags=", "").split() for value in
           ("-guard:cf", "-guard:ehcont", "-CETCOMPAT", "-DYNAMICBASE", "-HIGHENTROPYVA", "-NXCOMPAT")):
        raise ValueError("Linker hardening was not requested")
    configured = dict(re.findall(r"^(CFLAGS|LDFLAGS)=(.*)$",
                                (directory / "build/ffbuild/config.mak").read_text(), re.MULTILINE))
    for key, required in (("CFLAGS", ("-GS", "-guard:cf", "-guard:ehcont")),
                          ("LDFLAGS", ("-guard:cf", "-guard:ehcont", "-CETCOMPAT", "-DYNAMICBASE", "-HIGHENTROPYVA", "-NXCOMPAT"))):
        if any(flag not in configured.get(key, "").split() for flag in required):
            raise ValueError("Generated configuration lost hardening: " + key)
    source = build_tool.short_path(directory / "source")
    diagnostics = review_diagnostics((directory / "build.log").read_text(), source)
    inventory = json.loads((directory / "runtime-files.json").read_text())
    names = {"ffmpeg.exe", "ffprobe.exe", *(pathlib.PurePosixPath(name).name for name in build_tool.DLLS)}
    if (len(inventory) != len(names) or {item["path"] for item in inventory} != names
            or {path.name for path in (directory / "bin").iterdir()} != names):
        raise ValueError("Unexpected runtime inventory")
    visual_studio = pathlib.Path(record["visualStudio"])
    version = (visual_studio / "VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt").read_text().strip()
    dumpbin = visual_studio / "VC/Tools/MSVC" / version / "bin/Hostx64/x64/dumpbin.exe"
    evidence = directory / ("build-review-" + uuid.uuid4().hex)
    evidence.mkdir()
    results = {}
    for item in inventory:
        path = build_tool.local_path(directory / "bin" / item["path"])
        if build_tool.digest(path) != item["sha256"] or path.stat().st_size != item["bytes"]:
            raise ValueError("Runtime inventory mismatch")
        output = subprocess.run([str(dumpbin), "/headers", "/loadconfig", "/dependents", str(path)],
                                capture_output=True, text=True, timeout=20, check=True).stdout
        (evidence / (path.name + ".txt")).write_text(output)
        results[path.name] = review_pe(output, {name.lower() for name in names if name.endswith(".dll")})
    result = {"scope": "Reviewed compiler diagnostics and emitted hardening/import metadata; full runtime acceptance remains required",
              "reviewedDiagnostics": diagnostics, "runtime": inventory, "pe": results,
              "dumpbin": str(dumpbin), "dumpbinSha256": build_tool.digest(dumpbin),
              "checkerSha256": build_tool.digest(pathlib.Path(__file__)), "buildReceiptSha256": build_tool.digest(directory / "build-result.json")}
    (evidence / "result.json").write_text(json.dumps(result, indent=2))
    print(f"Reviewed {len(diagnostics)} diagnostics and seven hardened PE files: {evidence}")


if __name__ == "__main__":
    main()
