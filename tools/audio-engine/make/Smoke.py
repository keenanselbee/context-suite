"""Check native make with Git Bash, parallel prerequisites and recursive make."""
import ctypes
import hashlib
import json
import os
import pathlib
import subprocess
import sys


def short_path(path):
    function = ctypes.WinDLL("kernel32", use_last_error=True).GetShortPathNameW
    function.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32]
    function.restype = ctypes.c_uint32
    buffer = ctypes.create_unicode_buffer(32768)
    length = function(str(path), buffer, len(buffer))
    if not length or length >= len(buffer) or " " in buffer.value:
        raise ValueError("A whitespace-free existing short path is required")
    return buffer.value.replace("\\", "/")


def main():
    executable, directory = (pathlib.Path(value).resolve() for value in sys.argv[1:])
    scratch = pathlib.Path(__file__).resolve().parents[3] / ".codex-temp"
    if not executable.is_relative_to(scratch) or not directory.is_relative_to(scratch):
        raise ValueError("Keep make tests inside repository scratch")
    bash = pathlib.Path(os.environ["ProgramFiles"]) / "Git/bin/bash.exe"
    if not bash.is_file():
        raise ValueError("Existing Git Bash is required")
    shell = short_path(bash)
    directory.mkdir(exist_ok=False)
    (directory / "input.txt").write_text("generated input\n")
    (directory / "config.mak").write_text("EXPECTED := alpha beta\n")
    (directory / "Makefile").write_text(r"""include config.mak
.SHELLFLAGS := --noprofile --norc -c
.PHONY: all recurse fail
all: joined recurse escaped
a b: %: input.txt
	@printf '%s\n' '$@' > '$@'
joined: a b
	@cat a b > '$@'
	@test '$(EXPECTED)' = 'alpha beta'
recurse:
	@$(MAKE) --no-print-directory -f Child.mk
escaped:
	@printf '%s\n' 'C:\fixture\file.c' | awk '{gsub(/\\/, "/"); print}' > escaped
fail:
	@exit 7
""")
    (directory / "Child.mk").write_text("child:\n\t@printf '%s\\n' 'recursive child' > child\n")
    environment = {key: value for key, value in os.environ.items()
                   if key.upper() not in ("MAKEFLAGS", "GNUMAKEFLAGS", "MFLAGS", "MAKELEVEL", "MAKEFILES")}
    # Git's shell and Unix commands are already installed; changes affect only children.
    environment["PATH"] = str(bash.parents[1] / "usr/bin") + os.pathsep + environment["PATH"]
    temporary = directory / "temporary"
    temporary.mkdir()
    for name in ("TMP", "TEMP", "TMPDIR"):
        environment[name] = short_path(temporary)
    command = [short_path(executable), "--no-print-directory", "-j2", "SHELL=" + shell]
    outputs = []
    for arguments, expected_exit in (([], 0), ([], 0), (["fail"], 2)):
        result = subprocess.run(command + arguments, cwd=directory, env=environment,
                                capture_output=True, text=True, timeout=20)
        outputs.append({"arguments": arguments, "exit": result.returncode, "stdout": result.stdout, "stderr": result.stderr})
        if result.returncode != expected_exit:
            raise ValueError(json.dumps(outputs))
        if not arguments:
            if (directory / "joined").read_bytes() != b"a\nb\n" or (directory / "child").read_bytes() != b"recursive child\n":
                raise ValueError("Parallel prerequisite or recursive output differs")
            if (directory / "escaped").read_bytes() != b"C:/fixture/file.c\n":
                raise ValueError("Shell backslash/quote transport differs")
            current = [(directory / name).stat().st_mtime_ns for name in ("a", "b", "joined", "child")]
            if len(outputs) == 1:
                timestamps = current
            elif current != timestamps:
                raise ValueError("A completed target unexpectedly rebuilt")
    report = {"scope": "Authored build-tool smoke checks; not the upstream full make suite",
              "shell": str(bash), "shellSha256": hashlib.sha256(bash.read_bytes()).hexdigest(), "runs": outputs}
    (directory / "result.json").write_text(json.dumps(report, indent=2))
    print("Parallel prerequisites, shell quoting, recursive make, incremental no-op and failure propagation passed.")


if __name__ == "__main__":
    main()
