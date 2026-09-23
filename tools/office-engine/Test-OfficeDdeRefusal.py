"""Exercise the shared DDE fidelity guard and actual worker without a native Office runtime."""
import hashlib
import json
import pathlib
import subprocess
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]


def identities(directory):
    return {str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted(directory.rglob("*")) if path.is_file()}


def main():
    evidence = ROOT / ".codex-temp" / "office-dde-refusal" / uuid.uuid4().hex
    evidence.mkdir(parents=True, exist_ok=False)
    print(evidence, flush=True)
    worker = evidence / "worker-build"
    host = evidence / "test-build"
    code = 1
    try:
        for label, project, output in (
                ("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj", worker),
                ("host", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj", host)):
            with (evidence / (label + "-build.log")).open("wb") as log:
                subprocess.run(["dotnet", "build", str(ROOT / project), "-c", "Release", "--no-restore",
                                "--verbosity", "minimal", "--output", str(output)], cwd=ROOT, stdout=log,
                               stderr=subprocess.STDOUT, check=True, timeout=180)
        before = {"Worker": identities(worker), "TestHost": identities(host)}
        (evidence / "binary-identities.json").write_text(json.dumps(before, indent=2) + "\n")
        with (evidence / "contracts.log").open("wb") as log:
            result = subprocess.run([str(host / "ContextSuite.Core.ContractTests.exe"), "--office-dde-refusal",
                                     str(worker / "ContextSuite.Worker.exe"), str(evidence / "run")],
                                    cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=120)
        if before != {"Worker": identities(worker), "TestHost": identities(host)}:
            raise RuntimeError("Test binaries changed during execution.")
        code = result.returncode
    finally:
        (evidence / "exit.json").write_text(json.dumps({"ExitCode": code}) + "\n")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
