"""Check baseline/expanded component gates using copies of real build headers."""
import importlib.util
import json
import pathlib
import subprocess
import sys
import uuid


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_build", TOOLS / "Build-AudioEngine.py")
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)


def main():
    if len(sys.argv) != 3:
        raise ValueError("Use Test-AudioConfiguration.py <baseline build directory> <expanded build directory>")
    baseline, expanded = (build.local_path(value) for value in sys.argv[1:])
    evidence = build.SCRATCH / ("audio-config-guards-" + uuid.uuid4().hex)
    evidence.mkdir()
    checker = TOOLS / "Check-AudioConfiguration.py"
    cases = [
        ("baseline", baseline, False, None, None, True),
        ("expanded", expanded, True, None, None, True),
        ("expanded-needs-opt-in", expanded, False, None, None, False),
        ("baseline-is-not-expanded", baseline, True, None, None, False),
        ("missing-wma-voice", expanded, True, "CONFIG_WMAVOICE_DECODER", "0", False),
        ("missing-aiff", expanded, True, "CONFIG_AIFF_DEMUXER", "0", False),
        ("missing-asf", expanded, True, "CONFIG_ASF_DEMUXER", "0", False),
        ("missing-big-endian-float", expanded, True, "CONFIG_PCM_F64BE_DECODER", "0", False),
        ("additional-output", expanded, True, "CONFIG_WMAV2_ENCODER", "1", False),
        ("additional-protocol", expanded, True, "CONFIG_HTTP_PROTOCOL", "1", False),
        ("additional-video", expanded, True, "CONFIG_H264_DECODER", "1", False),
        ("network", expanded, True, "CONFIG_NETWORK", "1", False),
        ("gpl", expanded, True, "CONFIG_GPL", "1", False),
        ("nonfree", expanded, True, "CONFIG_NONFREE", "1", False),
        ("wrong-architecture", expanded, True, "ARCH_X86_64", "0", False),
    ]
    hashes = {str(path / name): build.digest(path / name)
              for path in (baseline, expanded) for name in ("config.h", "config_components.h")}
    results = []
    for name, origin, opt_in, macro, value, accepted in cases:
        directory = evidence / name
        directory.mkdir()
        mutations = 0
        for header in ("config.h", "config_components.h"):
            text = (origin / header).read_text()
            if macro:
                previous = f"#define {macro} {1 - int(value)}\n"
                mutations += text.count(previous)
                text = text.replace(previous, f"#define {macro} {value}\n")
            (directory / header).write_text(text)
        if macro and mutations != 1:
            raise ValueError("Fixture did not change exactly one macro: " + name)
        result = subprocess.run([sys.executable, "-B", str(checker), str(directory),
                                 *(["--expanded-inputs"] if opt_in else [])],
                                capture_output=True, text=True, timeout=20)
        (directory / "check.log").write_text(result.stdout + result.stderr)
        if (result.returncode == 0) != accepted or (directory / "configuration.json").exists() != accepted:
            raise ValueError("Unexpected gate result: " + name)
        results.append({"case": name, "accepted": accepted, "exitCode": result.returncode})
    if any(build.digest(pathlib.Path(path)) != digest for path, digest in hashes.items()):
        raise ValueError("Original generated headers changed")
    (evidence / "result.json").write_text(json.dumps({"checks": len(results), "results": results,
        "originalHeaders": hashes, "checkerSha256": build.digest(checker),
        "testSha256": build.digest(pathlib.Path(__file__))}, indent=2))
    print(f"{len(results)} configuration guards passed: {evidence}")


if __name__ == "__main__":
    main()
