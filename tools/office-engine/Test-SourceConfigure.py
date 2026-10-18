"""Check the real source-kit configure plan without starting configuration."""

import argparse
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("settings", type=Path)
    parser.add_argument("--baseline-options", type=Path, required=True)
    args = parser.parse_args()
    tool = Path(__file__).with_name("Configure-Source.py").resolve()
    spec = importlib.util.spec_from_file_location("configure_source", tool)
    configure = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(configure)
    settings_path = configure.driver.local_path(args.settings)
    settings = json.loads(settings_path.read_bytes())
    evidence = Path(settings["Workspace"]) / "evidence" / ("configure-checks-" + uuid.uuid4().hex)
    configure.driver.local_path(evidence)
    evidence.mkdir()
    paths, plan = configure.prepare(settings_path)
    checks = []
    assert plan["Options"] == args.baseline_options.read_text(encoding="utf-8")
    assert plan["Jobs"] == 2 and plan["WorkspaceLimitBytes"] == 100 * 1024**3
    checks.append("options-match-actual-configured-baseline")
    _, no_python = configure.prepare(settings_path, "none")
    assert no_python["Options"] == plan["Options"] + "--enable-python=no\n"
    assert no_python["Inputs"] == plan["Inputs"] and no_python["Script"] == plan["Script"]
    checks.append("no-python-experiment-changes-only-explicit-configure-option")
    python = Path(sys.executable)
    _, explicit_python = configure.prepare(settings_path, "none", python)
    assert explicit_python["Options"] == no_python["Options"] + "PYTHON_FOR_BUILD=" + python.as_posix() + "\n"
    assert explicit_python["Inputs"] == dict(plan["Inputs"], **{str(python): configure.driver.digest(python)})
    checks.append("build-time-python-is-explicit-and-hash-bound")
    try:
        configure.prepare(settings_path, "bundled", python)
        raise AssertionError("Default bundled configuration accepted an unrelated override.")
    except ValueError:
        checks.append("bundled-mode-refuses-build-python-override")
    try:
        configure.prepare(settings_path, "system")
        raise AssertionError("Unreviewed Python runtime accepted.")
    except ValueError:
        checks.append("refuses-unreviewed-python-mode")
    before = set(evidence.parent.iterdir())
    assert not paths["Build"].exists()
    command = [sys.executable, "-B", str(tool), str(settings_path), "--plan"]
    result = subprocess.run(command, capture_output=True, timeout=60)
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == plan
    assert not paths["Build"].exists() and set(evidence.parent.iterdir()) == before
    checks.append("cli-plan-creates-no-build-or-evidence-directory")
    script = evidence / "planned.sh"
    script.write_text(plan["Script"], encoding="utf-8", newline="\n")
    syntax = subprocess.run([str(paths["Bash"]), "--noprofile", "--norc", "-n", str(script)], capture_output=True, timeout=30)
    assert syntax.returncode == 0, syntax.stderr
    checks.append("actual-bash-accepts-generated-script")
    for label in ("outside-build", "nested-build", "multiline-tool", "unknown-setting"):
        changed = copy.deepcopy(settings)
        if label == "outside-build":
            changed["Build"] = str(paths["Workspace"].parent / "not-authorized-configure")
        elif label == "nested-build":
            changed["Build"] = str(paths["Source"] / "unsafe-build")
        elif label == "multiline-tool":
            changed["Clang"] += "\n--extra-option"
        else:
            changed["ExtraOptions"] = "--disable-security"
        path = evidence / (label + ".json")
        path.write_text(json.dumps(changed), encoding="utf-8")
        try:
            configure.prepare(path)
            raise AssertionError("Unexpected configure admission: " + label)
        except (ValueError, OSError):
            checks.append("refuses-" + label)
    existing = dict(paths, Build=evidence)
    before = set(evidence.iterdir())
    try:
        configure.execute(existing, plan)
        raise AssertionError("Existing configure directory accepted.")
    except ValueError:
        checks.append("refuses-existing-build-before-writes")
    assert before == set(evidence.iterdir())
    result = dict(Passed=True, Checks=checks, ToolSha256=configure.driver.digest(tool),
        SettingsSha256=configure.driver.digest(settings_path),
        Scope="Actual source-kit plan/options and refusal checks; no configure or compilation execution.")
    (evidence / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(dict(Evidence=str(evidence), **result), indent=2))


if __name__ == "__main__":
    main()
