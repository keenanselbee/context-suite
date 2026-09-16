"""Opt-in disposable Office export, independent validation and application copy publication."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--create-disposable-profiles", action="store_true")
    parser.add_argument("--cleanup-only", action="store_true", help="Exercise one obstructed profile cleanup and retry without launching Office.")
    parser.add_argument("--direct", action="store_true", help="Exercise mixed image/Office PDF commands and Office retry through the application view model.")
    parser.add_argument("--font-review", action="store_true", help="Exercise real missing-font reports, per-file review, refusal and cleanup.")
    parser.add_argument("--word-font-styles", action="store_true", help="Export fifteen authored style/theme controls and verify source selections, review signals and cleanup.")
    parser.add_argument("--powerpoint-slides", action="store_true", help="Export three authored slide-order, hidden-slide and speaker-note cases through the application.")
    parser.add_argument("--pdf-validator", type=Path, help="Combined-image validator required for the direct mixed-command test.")
    parser.add_argument("--office-engine", type=Path)
    parser.add_argument("--pdf-engine", type=Path)
    parser.add_argument("--pdf-renderer", type=Path)
    parser.add_argument("--retained-worker", type=Path, help="Reuse a scratch worker after matching its top-level files to the fresh build; readers verify their full pinned runtimes.")
    parser.add_argument("--fixtures", type=Path)
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable Office profile authorization is required.")
    if sum((args.direct, args.cleanup_only, args.font_review, args.word_font_styles, args.powerpoint_slides)) > 1:
        parser.error("Choose only one execution scenario.")
    if (args.word_font_styles or args.powerpoint_slides) and args.fixtures is not None:
        parser.error("The selected mode creates its own authored fixtures; omit --fixtures.")
    if not (args.word_font_styles or args.powerpoint_slides) and args.fixtures is None:
        parser.error("--fixtures is required for the selected scenario.")
    if args.direct and (args.cleanup_only or args.pdf_validator is None and args.retained_worker is None):
        parser.error("Direct mode requires a combined-image validator and cannot use cleanup-only mode.")
    if args.retained_worker is None and not all((args.office_engine, args.pdf_engine, args.pdf_renderer)):
        parser.error("Choose all three engine directories or a retained scratch worker.")
    if args.retained_worker is not None and any((args.office_engine, args.pdf_engine, args.pdf_renderer, args.pdf_validator)):
        parser.error("A retained worker uses its own engine directories; omit separate engines.")
    root = Path(__file__).resolve().parents[2]
    fixtures = args.fixtures.resolve(strict=True) if args.fixtures else None
    if fixtures is not None:
        fixtures.relative_to(root / ".codex-temp")
    originals = {str(path): digest(path) for path in fixtures.iterdir() if path.is_file()} if fixtures else {}
    stage = root / ".codex-temp/office-execution" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office execution evidence:", stage, flush=True)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "tests/ContextSuite.Core.ContractTests"):
        paths.update(path for path in (root / directory).rglob("*") if path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "Version.props", "global.json", "tools/Require-Private.targets")
                 if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    fixture_source = root / "tools/office-engine/Probe/WordFontStyleFixtures.cs"
    if args.word_font_styles:
        sources[str(fixture_source.relative_to(root))] = digest(fixture_source)
    if args.powerpoint_slides:
        for name in ("OfficeFixtures.cs", "PowerPointSlideFixtures.cs"):
            path = root / "tools/office-engine/Probe" / name
            sources[str(path.relative_to(root))] = digest(path)
    for label, project in (("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                           ("contracts", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj")):
        build = subprocess.run(["dotnet", "build", str(root / project), "-c", "Release", "--no-restore", "--verbosity", "quiet"], cwd=root, capture_output=True)
        (stage / (label + "-build.log")).write_bytes(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError("Build failed before profile creation: " + label)
    built = root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows"
    if args.retained_worker is not None:
        worker = args.retained_worker.resolve(strict=True)
        worker.relative_to(root / ".codex-temp/office-execution")
        if worker.name != "ContextSuite.Worker.exe":
            raise RuntimeError("Unexpected retained worker name.")
        worker_root = worker.parent
        actual = {path.name: digest(path) for path in worker_root.iterdir() if path.is_file()}
        expected = {path.name: digest(path) for path in built.iterdir() if path.is_file()}
        if actual != expected:
            raise RuntimeError("Retained worker differs from the current build; prepare a fresh worker.")
    else:
        worker_root = stage / "worker"
        worker_root.mkdir()
        for path in built.iterdir():
            if path.is_file():
                shutil.copy2(path, worker_root / path.name)
    engines = {}
    if args.direct and args.retained_worker is not None and not (worker_root / "pdf-validator/ContextSuite.ImagePdfValidator.exe").is_file():
        raise RuntimeError("Direct mode needs a retained combined-image validator.")
    selected_engines = [("office-engine", args.office_engine), ("pdf-engine", args.pdf_engine), ("pdf-renderer", args.pdf_renderer)]
    if args.pdf_validator is not None:
        selected_engines.append(("pdf-validator", args.pdf_validator))
    for name, folder in selected_engines:
        if args.retained_worker is not None:
            continue
        folder = folder.resolve(strict=True)
        folder.relative_to(root)
        print("Copying isolated", name, flush=True)
        shutil.copytree(folder, worker_root / name)
        for path in folder.rglob("*"):
            if path.is_file():
                expected = digest(path)
                if digest(worker_root / name / path.relative_to(folder)) != expected:
                    raise RuntimeError("Scratch engine copy differs from selected source.")
                engines[str(path)] = expected
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in worker_root.rglob("*") if path.is_file()})
    receipt = {"Sources": sources, "Fixtures": originals, "Engines": engines, "Binaries": binaries}
    (stage / "inputs.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Source drift before execution.")
    print("Starting three disposable PowerPoint slide exports." if args.powerpoint_slides else
          "Starting fifteen disposable Word font-style exports." if args.word_font_styles else
          "Starting thirteen disposable font-review exports." if args.font_review else "Starting one disposable profile cleanup/retry check." if args.cleanup_only else
          "Starting five disposable direct-command exports." if args.direct else "Starting six disposable Office exports and application publication checks.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        arguments = [str(host), "--office-powerpoint-slides" if args.powerpoint_slides else "--office-word-font-styles",
                     str(worker_root / "ContextSuite.Worker.exe"), str(stage / "contracts")] if (args.word_font_styles or args.powerpoint_slides) else [
            str(host), "--office-font-execution" if args.font_review else "--office-execution-cleanup" if args.cleanup_only else "--office-direct-execution" if args.direct else "--office-execution",
            str(worker_root / "ContextSuite.Worker.exe"), str(fixtures), str(stage / "contracts")]
        started = time.perf_counter()
        run = subprocess.run(arguments, cwd=root, stdout=output, stderr=error)
        elapsed = time.perf_counter() - started
    (stage / "timing.json").write_text(json.dumps({"ApplicationSeconds": elapsed,
        "Scope": "Application harness wall time, including authored fixtures and checks; excludes worker staging and final input hashing."}))
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for group in (originals, engines, binaries) for name, expected in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}), encoding="utf-8")
    print((stage / "stdout.log").read_text(encoding="utf-8", errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Office execution failed; inspect retained context journals and profile cleanup before retrying: " + str(stage))
    report = json.loads((stage / "contracts/results.json").read_text(encoding="utf-8"))
    if not report.get("Passed") or len(report.get("Profiles", [])) != (3 if args.powerpoint_slides else 15 if args.word_font_styles else 13 if args.font_review else 1 if args.cleanup_only else 5 if args.direct else 6) or not all(profile["Removed"] and profile["ContextRetired"] for profile in report["Profiles"]):
        raise RuntimeError("Missing complete execution and cleanup evidence.")


if __name__ == "__main__":
    main()
