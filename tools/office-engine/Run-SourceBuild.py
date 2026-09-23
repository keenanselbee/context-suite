"""Run the fixed Office source-build targets in a configured, owned workspace.

This does not install a toolchain, configure a source tree, run native tests or
adopt a runtime. Use --plan to validate inputs without creating files/processes.
"""

import argparse
from contextlib import contextmanager
import datetime
import hashlib
import json
import msvcrt
import os
from pathlib import Path
import re
import shlex
import stat
import subprocess
import time
import uuid


TARGETS = (
    "CppunitTest_embeddedobj_contextsuite",
    "Executable_soffice_bin", "Executable_soffice_exe", "Executable_soffice_com",
    "Package_setup", "Package_setup_ure", "Package_instsetoo_native_setup",
    "Package_instsetoo_native_setup_ure", "Rdb_services", "Package_extras_cfgusr",
    "Package_extras_autotextuser", "Package_extras_database",
    "Package_extras_databasebiblio", "Package_extras_gallmytheme",
    "Package_readlicense_oo_license", "Package_readlicense_oo_files",
    "Package_readlicense_oo_readmes",
)
WORKSPACE_LIMIT = 100 * 1024**3
ENVIRONMENT_KEYS = {
    "PATH", "TMPDIR", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
    "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE",
    "LC_ALL", "VSCMD_SKIP_SENDTELEMETRY", "CMAKE_BUILD_PARALLEL_LEVEL",
    "DOTNET_GENERATE_ASPNET_CERTIFICATE", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH",
}


def local_path(value):
    path = Path(value).absolute()
    if ".." in path.parts or not re.fullmatch(r"[A-Za-z]:\\", path.anchor):
        raise ValueError("Use a canonical local drive path: " + str(value))
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor):
            info = ancestor.lstat()
            if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Linked build paths are not allowed: " + str(ancestor))
    return path


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def footprint(root):
    total = 0
    pending = [root]
    while pending:
        try:
            entries = os.scandir(pending.pop())
        except FileNotFoundError:
            continue
        with entries:
            for entry in entries:
                try:
                    info = entry.stat(follow_symlinks=False)
                except FileNotFoundError:
                    continue
                if info.st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                    continue
                if stat.S_ISDIR(info.st_mode):
                    pending.append(entry.path)
                else:
                    total += info.st_size
    return total


@contextmanager
def build_lock(build):
    path = local_path(build / "contextsuite-source-build.lock")
    with path.open("a+b") as stream:
        if not stream.seek(0, os.SEEK_END):
            stream.write(b"1")
            stream.flush()
        stream.seek(0)
        try:
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            raise ValueError("This configured build is already owned by another build driver.") from error
        try:
            yield
        finally:
            stream.seek(0)
            msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)


def preparation(args):
    workspace = local_path(args.workspace)
    build = local_path(args.build)
    bash = local_path(args.bash)
    make = local_path(args.make)
    environment_file = local_path(args.environment)
    if workspace == Path(workspace.anchor) or not workspace.is_dir() or build == workspace or not build.is_relative_to(workspace):
        raise ValueError("The configured build must be inside the owned workspace.")
    for path in (bash, make, environment_file, build / "config_host.mk"):
        local_path(path)
        if not path.is_file():
            raise ValueError("Required build input is missing: " + str(path))
    config = (build / "config_host.mk").read_text(encoding="utf-8")
    if "export PARALLELISM?=2\n" not in config or not re.search(r"^export SHA256SUM=.+$", config, re.M):
        raise ValueError("Configuration must retain two jobs and dependency hashing.")
    source_match = re.search(r"^export SRC_ROOT=(.+)$", config, re.M)
    if not source_match:
        raise ValueError("Configured source root is missing.")
    source = local_path(source_match.group(1))
    if source == workspace or not source.is_relative_to(workspace):
        raise ValueError("Configured sources must be inside the owned workspace.")
    python_recipe = source / "external/python3/ExternalProject_python3.mk"
    local_path(python_recipe)
    recipe = python_recipe.read_text(encoding="utf-8")
    if not all(value in recipe for value in ("/MP1", "/maxcpucount:1", "/p:MultiProcessorCompilation=false")):
        raise ValueError("The Python dependency must retain its one-worker recipe.")
    environment = json.loads(environment_file.read_text(encoding="utf-8"))
    if not isinstance(environment, dict) or set(environment) != ENVIRONMENT_KEYS:
        raise ValueError("Build environment keys differ from the reviewed set.")
    if any(not isinstance(value, str) or any(character in value for character in "\r\n\0") for value in environment.values()):
        raise ValueError("Build environment values must be single-line strings.")
    expected = {
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "VSCMD_SKIP_SENDTELEMETRY": "1", "CMAKE_BUILD_PARALLEL_LEVEL": "2",
        "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
        "LC_ALL": "C.UTF-8",
    }
    if any(environment[name] != value for name, value in expected.items()):
        raise ValueError("Build environment changes a reviewed limit or side-effect setting.")
    for name in ("DOTNET_CLI_HOME", "NUGET_PACKAGES"):
        path = local_path(environment[name])
        if not path.is_relative_to(workspace) or path == workspace:
            raise ValueError(name + " must remain inside the owned workspace.")
    temporary = local_path(workspace / "temp")
    cygwin_temp = "/cygdrive/" + temporary.drive[0].lower() + temporary.as_posix()[2:]
    if environment["TMPDIR"] not in (temporary.as_posix(), cygwin_temp):
        raise ValueError("TMPDIR must be the owned workspace's temp directory.")
    script = "set -eu\nunset MAKEFLAGS MFLAGS GNUMAKEFLAGS\nexport PARALLELISM=2\n"
    for name, value in sorted(environment.items()):
        script += "export " + name + "=" + shlex.quote(value) + "\n"
    script += "cd -- " + shlex.quote(build.as_posix()) + "\n"
    script += "for target in " + " ".join(TARGETS) + "; do\n"
    script += "    " + shlex.quote(make.as_posix()) + ' -j2 PARALLELISM=2 gb_SUPPRESS_TESTS=1 "$target"\n'
    script += "done\n"
    inputs = {str(path): digest(path) for path in
              (Path(__file__).resolve(), bash, make, environment_file, build / "config_host.mk", python_recipe)}
    return workspace, build, bash, script, inputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--build", required=True)
    parser.add_argument("--bash", required=True)
    parser.add_argument("--make", required=True)
    parser.add_argument("--environment", required=True)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--plan", action="store_true")
    mode.add_argument("--execute", action="store_true")
    args = parser.parse_args()
    workspace, build, bash, script, inputs = preparation(args)
    plan = dict(Workspace=str(workspace), Build=str(build), Bash=str(bash), Jobs=2,
                WorkspaceLimitBytes=WORKSPACE_LIMIT, Targets=TARGETS, Inputs=inputs,
                Script=script, NativeTestsExecuted=False)
    if args.plan:
        print(json.dumps(plan, indent=2))
        return
    with build_lock(build):
        execute(workspace, build, bash, script, inputs, plan)


def execute(workspace, build, bash, script, inputs, plan):
    initial = footprint(workspace)
    if initial >= WORKSPACE_LIMIT:
        raise ValueError("The owned workspace is already at its 100-GiB limit.")
    stage = workspace / "evidence" / ("source-build-" + uuid.uuid4().hex)
    local_path(stage)
    stage.mkdir(parents=True)
    script_path = stage / "build.sh"
    script_path.write_text(script, encoding="utf-8", newline="\n")
    plan["InitialWorkspaceBytes"] = initial
    (stage / "plan.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    environment = dict(os.environ, TEMP=str(workspace / "temp"), TMP=str(workspace / "temp"))
    (workspace / "temp").mkdir(exist_ok=True)
    for name in ("ProgramFiles(x86)", "CommonProgramFiles(x86)"):
        if name.upper() in environment:
            environment[name] = environment.pop(name.upper())
    command = [str(bash), "--noprofile", "--norc", str(script_path)]
    started = time.monotonic()
    sizes = []
    exceeded = False
    with (stage / "run.log").open("xb") as log:
        process = subprocess.Popen(command, cwd=build, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW | subprocess.BELOW_NORMAL_PRIORITY_CLASS)
        (stage / "launch.json").write_text(json.dumps(dict(Command=command, Pid=process.pid,
            StartedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat()), indent=2), encoding="utf-8")
        print("Office source-build evidence:", stage, "PID:", process.pid, flush=True)
        try:
            while process.poll() is None:
                size = footprint(workspace)
                sizes.append(dict(Seconds=round(time.monotonic() - started), Bytes=size))
                (stage / "footprint.json").write_text(json.dumps(sizes, indent=2), encoding="utf-8")
                if size > WORKSPACE_LIMIT and process.poll() is None:
                    exceeded = True
                    subprocess.run(["taskkill.exe", "/PID", str(process.pid), "/T", "/F"], check=True, capture_output=True)
                    break
                try:
                    process.wait(timeout=45)
                except subprocess.TimeoutExpired:
                    pass
        except BaseException:
            if process.poll() is None:
                subprocess.run(["taskkill.exe", "/PID", str(process.pid), "/T", "/F"], check=True, capture_output=True)
            (stage / "interrupted.json").write_text(json.dumps(dict(ExitCode=process.wait(),
                Seconds=time.monotonic() - started, Scope="Build monitor interrupted; owned process tree stopped.")), encoding="utf-8")
            raise
        code = process.wait()
    result = dict(ExitCode=code, Seconds=time.monotonic() - started, BudgetExceeded=exceeded,
                  InputsUnchanged=all(digest(Path(path)) == value for path, value in inputs.items()),
                  NativeTestsExecuted=False, Scope="Compilation only; native/host replay and adoption remain separate.")
    (stage / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    if code or exceeded or not result["InputsUnchanged"]:
        raise SystemExit(code or 1)


if __name__ == "__main__":
    main()
