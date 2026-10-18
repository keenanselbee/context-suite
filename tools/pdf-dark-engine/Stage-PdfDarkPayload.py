"""Stage or verify the pinned PDF dark-mode host beside a complete PDF candidate."""
import argparse
import hashlib
import importlib.util
import json
import pathlib
import stat


TOOLS = pathlib.Path(__file__).absolute().parent
ROOT = TOOLS.parents[1]
PDF_TOOLS = ROOT / "tools/pdf-engine"
HOST_NAME = "ContextSuite.PdfDark.exe"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def local_path(value, build=False, verification=False):
    path = pathlib.Path(value).absolute()
    roots = [ROOT / ".codex-temp"] if build else [ROOT / ".codex-temp", ROOT / "artifacts/production-staging"]
    if verification and not build:
        roots.append(ROOT / "artifacts/release-candidates")
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use isolated repository-local PDF dark-mode inputs and payloads")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked PDF dark-mode paths are not allowed")
    return path


def checked_relative(value, prefix=None):
    if not isinstance(value, str) or "\\" in value or ":" in value or any(
            part in ("", ".", "..") for part in value.split("/")):
        raise ValueError("Invalid PDF dark-mode pin path")
    if prefix and not value.startswith(prefix):
        raise ValueError("PDF dark-mode source is outside its reviewed directory")
    return pathlib.Path(value)


def pins():
    record = json.loads((TOOLS / "payload-candidate.json").read_text(encoding="utf-8"))
    host = record["host"]
    if record["schema"] != 1 or host["path"] != "bin/Release/" + HOST_NAME or not host["sources"]:
        raise ValueError("Invalid PDF dark-mode host selection")
    for item in [host, *host["sources"]]:
        if len(item["sha256"]) != 64 or any(c not in "0123456789abcdefABCDEF" for c in item["sha256"]):
            raise ValueError("Invalid PDF dark-mode SHA-256 pin")
    if not isinstance(host["bytes"], int) or host["bytes"] <= 0:
        raise ValueError("Invalid PDF dark-mode host size pin")
    sources = [checked_relative(item["path"], "proprietary/src/ContextSuite.PdfDark.Native/")
               for item in host["sources"]]
    if len(sources) != len(set(sources)):
        raise ValueError("Duplicate PDF dark-mode source pin")
    pdf_selection = json.loads((PDF_TOOLS / "payload-candidate.json").read_text(encoding="utf-8"))
    dependencies = [item for item in pdf_selection["files"]
                    if item["path"].startswith("pdf-engine/") and item["path"] != "pdf-engine/qpdf.exe"]
    names = [pathlib.Path(item["path"]).name for item in dependencies]
    if not dependencies or len(names) != len(set(names)) or HOST_NAME in names:
        raise ValueError("Invalid selected qpdf runtime or notice membership")
    return host, dependencies


def verify_pdf(payload):
    # Reuse the existing exact PDF runtime, renderer, validator and notice verifier.
    spec = importlib.util.spec_from_file_location("context_suite_pdf_stage", PDF_TOOLS / "Stage-PdfPayload.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.verify(payload)


def expected_files(host, dependencies):
    expected = {HOST_NAME: (host["bytes"], host["sha256"])}
    for item in dependencies:
        expected[pathlib.Path(item["path"]).name] = (item["bytes"], item["sha256"])
    return expected


def verify(payload):
    root = local_path(payload, verification=True)
    verify_pdf(root)
    host, dependencies = pins()
    folder = local_path(root / "pdf-dark", verification=True)
    if not folder.is_dir():
        raise ValueError("PDF dark-mode candidate is missing")
    expected = expected_files(host, dependencies)
    actual = {}
    for path in folder.rglob("*"):
        local_path(path, verification=True)
        if not path.is_file() or path.parent != folder:
            raise ValueError("Unreviewed PDF dark-mode payload entry")
        actual[path.name] = (path.stat().st_size, digest(path))
    if actual != expected:
        raise ValueError("PDF dark-mode payload is missing, changed or contains extra files")
    return expected


def stage(payload, build_directory):
    root = local_path(payload)
    build = local_path(build_directory, build=True)
    folder = local_path(root / "pdf-dark")
    if not root.is_dir() or folder.exists():
        raise ValueError("Use an existing new stage without a PDF dark-mode payload")
    verify_pdf(root)
    host, dependencies = pins()
    executable = local_path(build / checked_relative(host["path"]), build=True)
    if not executable.is_file() or executable.stat().st_size != host["bytes"] or digest(executable) != host["sha256"]:
        raise ValueError("PDF dark-mode host differs from the reviewed build")
    for item in host["sources"]:
        source = ROOT / checked_relative(item["path"], "proprietary/src/ContextSuite.PdfDark.Native/")
        for ancestor in (source, *source.parents):
            if ancestor == ROOT:
                break
            if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError("Linked PDF dark-mode native source is not allowed")
        if not source.is_file() or digest(source) != item["sha256"]:
            raise ValueError("PDF dark-mode native source changed since pinning")
    contents = {HOST_NAME: executable.read_bytes()}
    for item in dependencies:
        source = local_path(root / checked_relative(item["path"]))
        if not source.is_file() or source.stat().st_size != item["bytes"] or digest(source) != item["sha256"]:
            raise ValueError("Pinned PDF runtime or notice changed")
        contents[pathlib.Path(item["path"]).name] = source.read_bytes()
    # Every source and the complete existing PDF candidate are verified before writing.
    folder.mkdir()
    for name, content in contents.items():
        with (folder / name).open("xb") as stream:
            stream.write(content)
    verify(root)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--build-directory")
    args = parser.parse_args()
    if args.build_directory:
        stage(args.payload, args.build_directory)
    else:
        verify(args.payload)
    print("Verified complete pinned PDF dark-mode runtime and notices; release approval remains separate.")


if __name__ == "__main__":
    main()
