"""Exercise the build driver with harmless Make targets, never a compiler."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bash", type=Path, required=True)
    parser.add_argument("--make", type=Path, required=True)
    args = parser.parse_args()
    tool = Path(__file__).with_name("Run-SourceBuild.py").resolve()
    repo = tool.parents[2]
    stage = repo / ".codex-temp/office-source-build-driver" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    spec = importlib.util.spec_from_file_location("office_build_driver", tool)
    driver = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(driver)
    bash = driver.local_path(args.bash)
    make = driver.local_path(args.make)
    checks = []
    fixture = stage / "workspace with spaces"
    build = fixture / "build"
    source = fixture / "source"
    build.mkdir(parents=True)
    (source / "external/python3").mkdir(parents=True)
    config = "export PARALLELISM?=2\nexport SHA256SUM=sha256sum\nexport SRC_ROOT=" + source.as_posix() + "\n"
    (build / "config_host.mk").write_text(config, encoding="utf-8")
    (source / "external/python3/ExternalProject_python3.mk").write_text(
        "/MP1 /maxcpucount:1 /p:MultiProcessorCompilation=false\n", encoding="utf-8")
    for name, relative in driver.PACKAGE_DEFINITIONS.items():
        path = source / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("$(eval $(call gb_Package_Package," + name + ",authored-fixture))\n", encoding="utf-8")
    environment = {
        "PATH": "/usr/bin:/bin", "TMPDIR": fixture.as_posix() + "/temp",
        "DOTNET_CLI_HOME": (fixture / "dotnet-home").as_posix(),
        "NUGET_PACKAGES": (fixture / "nuget").as_posix(),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "LC_ALL": "C.UTF-8", "VSCMD_SKIP_SENDTELEMETRY": "1",
        "CMAKE_BUILD_PARALLEL_LEVEL": "2", "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
        "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
    }
    environment_file = stage / "environment.json"
    environment_file.write_text(json.dumps(environment), encoding="utf-8")
    command = [sys.executable, "-B", str(tool), "--workspace", str(fixture), "--build", str(build),
               "--bash", str(bash), "--make", str(make), "--environment", str(environment_file)]

    def run(label, mode, success):
        result = subprocess.run(command + [mode], cwd=repo, capture_output=True, timeout=60,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        (stage / (label + ".log")).write_bytes(result.stdout + result.stderr)
        assert (result.returncode == 0) == success, (label, result.stdout, result.stderr)
        checks.append(label)
        return result

    before = sorted(str(p.relative_to(fixture)) for p in fixture.rglob("*"))
    plan = json.loads(run("valid-plan", "--plan", True).stdout)
    assert plan["Jobs"] == 2 and plan["WorkspaceLimitBytes"] == 100 * 1024**3
    assert plan["Targets"] == list(driver.TARGETS) and not plan["NativeTestsExecuted"]
    assert len(plan["Targets"]) == 15 and "Package_setup" not in plan["Targets"] and "Package_setup_ure" not in plan["Targets"]
    assert "unset MAKEFLAGS MFLAGS GNUMAKEFLAGS" in plan["Script"]
    assert before == sorted(str(p.relative_to(fixture)) for p in fixture.rglob("*"))
    checks.append("plan-creates-no-build-files")
    setup = source / "instsetoo_native/Package_setup.mk"
    setup_original = setup.read_bytes()
    setup.write_bytes(setup_original.replace(b"instsetoo_native_setup", b"setup"))
    run("reject-package-filename-as-target", "--plan", False)
    setup.write_bytes(setup_original)
    setup.rename(setup.with_suffix(".missing"))
    run("reject-missing-package-definition", "--plan", False)
    setup.with_suffix(".missing").rename(setup)
    assert str(setup) in plan["Inputs"]
    checks.append("package-definitions-bound-to-plan")
    for label, key, value in [
        ("reject-extra-jobs", "CMAKE_BUILD_PARALLEL_LEVEL", "4"),
        ("reject-external-temp", "TMPDIR", "C:/Windows/Temp"),
        ("reject-external-cache", "NUGET_PACKAGES", "C:/outside-owned-workspace"),
        ("reject-multiline-environment", "PATH", "/usr/bin\nfalse"),
    ]:
        changed = dict(environment, **{key: value})
        environment_file.write_text(json.dumps(changed), encoding="utf-8")
        run(label, "--plan", False)
    environment_file.write_text(json.dumps(environment), encoding="utf-8")
    (build / "config_host.mk").write_text(config.replace("PARALLELISM?=2", "PARALLELISM?=4"), encoding="utf-8")
    run("reject-configured-extra-jobs", "--plan", False)
    (build / "config_host.mk").write_text(config.replace("SHA256SUM=sha256sum", "SHA256SUM="), encoding="utf-8")
    run("reject-missing-dependency-hashing", "--plan", False)
    (build / "config_host.mk").write_text(config, encoding="utf-8")
    recipe = source / "external/python3/ExternalProject_python3.mk"
    bounded = recipe.read_bytes()
    recipe.write_bytes(bounded.replace(b"/MP1", b"/MP"))
    run("reject-unbounded-python-compiler", "--plan", False)
    recipe.write_bytes(bounded)
    with driver.build_lock(build):
        run("reject-concurrent-build-driver", "--execute", False)
    assert not (fixture / "evidence").exists()
    checks.append("locked-build-creates-no-run")

    # Real Make executes only echo or false. No native project or compiler runs.
    makefile = build / "Makefile"
    makefile.write_text("ifneq ($(PARALLELISM),2)\n$(error Unexpected parallelism)\nendif\n" +
        ".PHONY: " + " ".join(driver.TARGETS) + "\n" +
        "\n".join(name + ":\n\t@echo CONTEXT_TARGET:" + name for name in driver.TARGETS) + "\n", encoding="utf-8")
    saved_environment = {name: os.environ.get(name) for name in ("PARALLELISM", "MAKEFLAGS", "MFLAGS", "GNUMAKEFLAGS")}
    try:
        os.environ.update(PARALLELISM="99", MAKEFLAGS="--invalid-inherited-flag", MFLAGS="--invalid-inherited-flag", GNUMAKEFLAGS="--invalid-inherited-flag")
        run("execute-sequential-targets", "--execute", True)
    finally:
        for name, value in saved_environment.items():
            if value is None:
                os.environ.pop(name, None)
            else:
                os.environ[name] = value
    checks.append("inherited-make-overrides-cleared")
    completed = list((fixture / "evidence").glob("source-build-*/result.json"))
    assert len(completed) == 1
    result = json.loads(completed[0].read_bytes())
    assert result["ExitCode"] == 0 and result["InputsUnchanged"] and not result["BudgetExceeded"]
    log = (completed[0].parent / "run.log").read_text()
    observed = [line.partition(":")[2] for line in log.splitlines() if line.startswith("CONTEXT_TARGET:")]
    assert observed == list(driver.TARGETS)
    checks.append("all-targets-exact-order")
    makefile.write_text(".PHONY: " + " ".join(driver.TARGETS) + "\n" +
        driver.TARGETS[0] + ":\n\t@echo CONTEXT_EXPECTED_FAILURE\n\t@false\n" +
        "\n".join(name + ":\n\t@echo CONTEXT_UNEXPECTED_NEXT" for name in driver.TARGETS[1:]) + "\n", encoding="utf-8")
    run("execute-stops-on-first-error", "--execute", False)
    completed = list((fixture / "evidence").glob("source-build-*/result.json"))
    failed = [path for path in completed if json.loads(path.read_bytes())["ExitCode"] != 0]
    assert len(failed) == 1 and "CONTEXT_UNEXPECTED_NEXT" not in (failed[0].parent / "run.log").read_text()
    checks.append("failed-receipt-retained-no-later-targets")
    result = dict(Passed=True, Checks=checks, ToolSha256=hashlib.sha256(tool.read_bytes()).hexdigest().upper(),
        BashSha256=driver.digest(bash), MakeSha256=driver.digest(make), Evidence=str(stage),
        Scope="Build-driver boundaries and real harmless Make commands. No compiler, native engine, profile, installation or resource-limit stress test.")
    (stage / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
