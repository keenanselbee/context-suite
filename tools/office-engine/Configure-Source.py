"""Configure prepared Office sources using explicit local toolchain paths.

No tools are installed and no compilation target or runtime is launched. --plan
is read-only. --execute requires a fresh build directory and retains its receipt.
Keep this file beside Run-SourceBuild.py in a standalone source kit.
"""

import argparse
import datetime
import importlib.util
import json
import os
from pathlib import Path
import shlex
import subprocess
import time
import uuid


driver_path = Path(__file__).with_name("Run-SourceBuild.py")
spec = importlib.util.spec_from_file_location("source_build", driver_path)
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


def prepare(settings_path, python_runtime="bundled", build_python=None):
    if python_runtime not in ("bundled", "none"):
        raise ValueError("Choose bundled Python or the separate no-Python build experiment.")
    if build_python is not None and python_runtime != "none":
        raise ValueError("An explicit build Python belongs only to the no-Python experiment.")
    settings_path = driver.local_path(settings_path)
    settings = json.loads(settings_path.read_bytes())
    required = {"Workspace", "Source", "Build", "Kit", "Bash", "Environment", "Jdk", "Ant", "Clang", "PkgConfig"}
    if set(settings) != required:
        raise ValueError("Declare exactly the documented configure paths.")
    paths = {key: driver.local_path(value) for key, value in settings.items()}
    workspace = paths["Workspace"]
    if workspace == Path(workspace.anchor) or not workspace.is_dir():
        raise ValueError("Use an existing owned workspace, not a drive root.")
    for key in ("Source", "Build", "Kit"):
        if paths[key] == workspace or not paths[key].is_relative_to(workspace):
            raise ValueError(key + " must remain inside the owned workspace.")
    build = paths["Build"]
    for key in ("Source", "Kit"):
        if build.is_relative_to(paths[key]) or paths[key].is_relative_to(build):
            raise ValueError("Keep build, source and kit directories separate.")
    for key in ("Bash", "Clang", "PkgConfig", "Environment"):
        if not paths[key].is_file():
            raise ValueError("Missing configure input: " + key)
    for path in (paths["Jdk"] / "bin/java.exe", paths["Ant"] / "bin/ant.bat"):
        driver.local_path(path)
        if not path.is_file():
            raise ValueError("Missing toolchain input: " + str(path))
    if any(any(character in value for character in "\r\n\0") for value in settings.values()):
        raise ValueError("Multiline paths are not supported.")
    kit = paths["Kit"]
    manifest_path = kit / "source-kit.json"
    manifest = json.loads(manifest_path.read_bytes())
    inputs = {str(path): driver.digest(path) for path in
              (Path(__file__).resolve(), driver_path.resolve(), settings_path, manifest_path,
               paths["Bash"], paths["Clang"], paths["PkgConfig"], paths["Environment"],
               paths["Jdk"] / "bin/java.exe", paths["Ant"] / "bin/ant.bat")}
    for name, identity in manifest["Files"].items():
        relative = Path(name)
        if relative.is_absolute() or ".." in relative.parts or ":" in name or "\\" in name:
            raise ValueError("Invalid source-kit path.")
        path = driver.local_path(kit / relative)
        if path.stat().st_size != identity["Bytes"] or driver.digest(path) != identity["Sha256"]:
            raise ValueError("Source-kit identity differs: " + name)
    source = paths["Source"]
    receipt_path = source.with_name(source.name + "-preparation.json")
    receipt = json.loads(receipt_path.read_bytes())
    if (receipt.get("Prepared") is not True or receipt.get("Recipe") != "utf8" or
            driver.local_path(receipt["Destination"]) != source or
            receipt["SourceArchiveSha256"] != manifest["Files"][manifest["SourceArchive"]]["Sha256"]):
        raise ValueError("Source preparation receipt does not match the canonical kit.")
    inputs[str(receipt_path)] = driver.digest(receipt_path)
    for item in manifest["Modifications"] + manifest["Additions"]:
        path = driver.local_path(source / item["Path"])
        expected = (manifest["CanonicalConfigureSha256"] if item["Path"] == "configure.ac"
                    else item.get("CurrentSha256", item.get("Sha256")))
        if driver.digest(path) != expected:
            raise ValueError("Prepared source differs: " + item["Path"])
        inputs[str(path)] = expected
    environment = json.loads(paths["Environment"].read_bytes())
    if set(environment) != driver.ENVIRONMENT_KEYS or any(
            not isinstance(value, str) or any(char in value for char in "\r\n\0") for value in environment.values()):
        raise ValueError("Use the reviewed single-line build environment.")
    expected = {"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
                "VSCMD_SKIP_SENDTELEMETRY": "1", "CMAKE_BUILD_PARALLEL_LEVEL": "2",
                "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
                "LC_ALL": "C.UTF-8"}
    if any(environment[name] != value for name, value in expected.items()):
        raise ValueError("Configure environment changes a reviewed setting.")
    for name in ("DOTNET_CLI_HOME", "NUGET_PACKAGES"):
        path = driver.local_path(environment[name])
        if path == workspace or not path.is_relative_to(workspace):
            raise ValueError("Keep configure caches inside the workspace.")
    temporary = workspace / "temp"
    cygwin_temp = "/cygdrive/" + temporary.drive[0].lower() + temporary.as_posix()[2:]
    if environment["TMPDIR"] not in (temporary.as_posix(), cygwin_temp):
        raise ValueError("Use the owned workspace temp directory.")
    options = "\n".join((
        "--host=x86_64-pc-cygwin", "--with-visual-studio=2026",
        "--with-jdk-home=" + paths["Jdk"].as_posix(), "--with-ant-home=" + paths["Ant"].as_posix(),
        "--with-external-tar=" + (kit / "dependencies").as_posix(), "--with-parallelism=2",
        "--without-junit", "--without-help", "--without-myspell-dicts",
        "--disable-online-update", "--disable-extension-update",
        "--with-vendor=Context Suite isolated chart evaluation", "PKG_CONFIG=" + paths["PkgConfig"].as_posix(),
        "LO_CLANG_CC=" + paths["Clang"].as_posix() + " --target=x86_64-pc-windows-msvc -m64",
        "LO_CLANG_CXX=" + paths["Clang"].as_posix() + " --target=x86_64-pc-windows-msvc -m64",
        "LO_CLANG_SHOWINCLUDES_PREFIX=Note: including file:", ""))
    if python_runtime == "none":
        options += "--enable-python=no\n"
        if build_python is not None:
            python_path = driver.local_path(build_python)
            if not python_path.is_file() or any(char in str(python_path) for char in "\r\n\0"):
                raise ValueError("Use an existing local build-time Python executable.")
            inputs[str(python_path)] = driver.digest(python_path)
            options += "PYTHON_FOR_BUILD=" + python_path.as_posix() + "\n"
    script = "set -eu\nunset MAKEFLAGS MFLAGS GNUMAKEFLAGS\n"
    for name, value in sorted(environment.items()):
        script += "export " + name + "=" + shlex.quote(value) + "\n"
    script += "cd -- " + shlex.quote(build.as_posix()) + "\n"
    script += "exec /usr/bin/perl " + shlex.quote((source / "autogen.sh").as_posix()) + "\n"
    inputs[str(source / "autogen.sh")] = driver.digest(source / "autogen.sh")
    return paths, dict(Options=options, Script=script, Inputs=inputs, Jobs=2,
                       WorkspaceLimitBytes=driver.WORKSPACE_LIMIT, Scope="Configure only; no build or adoption.")


def execute(paths, plan):
    workspace, build = paths["Workspace"], paths["Build"]
    if build.exists():
        raise ValueError("Use a fresh build directory; existing configurations are preserved.")
    if driver.footprint(workspace) >= driver.WORKSPACE_LIMIT:
        raise ValueError("Workspace exceeds its 100-GiB limit.")
    stage = driver.local_path(workspace / "evidence" / ("source-configure-" + uuid.uuid4().hex))
    stage.mkdir(parents=True)
    build.mkdir(parents=True)
    (build / "autogen.input").write_text(plan["Options"], encoding="utf-8", newline="\n")
    script = stage / "configure.sh"
    script.write_text(plan["Script"], encoding="utf-8", newline="\n")
    (stage / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    environment = dict(os.environ, TEMP=str(workspace / "temp"), TMP=str(workspace / "temp"))
    for name in ("ProgramFiles(x86)", "CommonProgramFiles(x86)"):
        if name.upper() in environment:
            environment[name] = environment.pop(name.upper())
    command = [str(paths["Bash"]), "--noprofile", "--norc", str(script)]
    started = time.monotonic()
    with (stage / "run.log").open("xb") as log:
        process = subprocess.Popen(command, cwd=build, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW | subprocess.BELOW_NORMAL_PRIORITY_CLASS)
        (stage / "launch.json").write_text(json.dumps(dict(Command=command, Pid=process.pid,
            StartedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())), encoding="utf-8")
        print("Office configure evidence:", stage, "PID:", process.pid, flush=True)
        try:
            while process.poll() is None:
                size = driver.footprint(workspace)
                (stage / "footprint.json").write_text(json.dumps(dict(Bytes=size,
                    Seconds=time.monotonic() - started)), encoding="utf-8")
                if size > driver.WORKSPACE_LIMIT or time.monotonic() - started > 600:
                    raise TimeoutError("Configure exceeded its workspace or ten-minute limit.")
                try:
                    process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    pass
            code = process.wait()
        except BaseException:
            if process.poll() is None:
                subprocess.run(["taskkill.exe", "/PID", str(process.pid), "/T", "/F"], check=True, capture_output=True)
            (stage / "interrupted.json").write_text(json.dumps(dict(ExitCode=process.wait(),
                Seconds=time.monotonic() - started)), encoding="utf-8")
            raise
    result = dict(ExitCode=code, Seconds=time.monotonic() - started, Build=str(build),
        InputsUnchanged=all(driver.digest(Path(path)) == digest for path, digest in plan["Inputs"].items()),
        Scope="Configuration only; native build, execution and adoption remain separate.")
    (stage / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(dict(Evidence=str(stage), **result), indent=2))
    if code or not result["InputsUnchanged"]:
        raise SystemExit(code or 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("settings", type=Path)
    parser.add_argument("--python-runtime", choices=("bundled", "none"), default="bundled",
                        help="Separate source-build experiment; never removes files from an existing runtime.")
    parser.add_argument("--build-python", type=Path,
                        help="Build-time Python executable for the no-Python experiment on Windows.")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--plan", action="store_true")
    mode.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    paths, plan = prepare(args.settings, args.python_runtime, args.build_python)
    if args.plan:
        print(json.dumps(plan, indent=2))
    else:
        execute(paths, plan)


if __name__ == "__main__":
    main()
