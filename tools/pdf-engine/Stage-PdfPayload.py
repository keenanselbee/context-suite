"""Stage pinned PDF engines and notices for an isolated production candidate."""
import argparse
import hashlib
import io
import json
import pathlib
import stat
import tarfile
import zipfile


TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
DIRECTORIES = {"pdf-engine", "pdf-renderer", "pdf-validator"}


def digest(data):
    return hashlib.sha256(data).hexdigest().upper()


def local_path(value, scratch_only=False):
    path = pathlib.Path(value).absolute()
    roots = [ROOT / ".codex-temp"]
    if not scratch_only:
        roots.append(ROOT / "artifacts/production-staging")
    if ".." in path.parts or not any(path != root and path.is_relative_to(root) for root in roots):
        raise ValueError("Use isolated repository staging and scratch inputs")
    for ancestor in (path, *path.parents):
        if ancestor.exists() and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked PDF payload paths are not allowed")
    return path


def selection():
    record = json.loads((TOOLS / "payload-candidate.json").read_text())
    names = [item["path"] for item in record["files"]]
    if len(names) != len(set(names)) or any(
            name.split("/")[0] not in DIRECTORIES or "\\" in name or ":" in name or
            any(part in ("", ".", "..") for part in name.split("/")) for name in names):
        raise ValueError("Invalid reviewed PDF deployment membership")
    qpdf = json.loads((TOOLS / "evaluation.json").read_text())
    pdfium = json.loads((TOOLS / "pdfium-evaluation.json").read_text())
    if (record["archives"]["qpdf"]["sha256"] != qpdf["archiveSha256"] or
            record["archives"]["qpdf-source"]["sha256"] != qpdf["sourceSha256"] or
            record["archives"]["pdfium"]["sha256"] != pdfium["archiveSha256"]):
        raise ValueError("PDF archive selection differs from the reviewed candidates")
    return record


def verify(payload):
    root = local_path(payload)
    record = selection()
    expected = {item["path"]: item for item in record["files"]}
    actual = set()
    for directory in DIRECTORIES:
        local_path(root / directory)
        for path in (root / directory).rglob("*"):
            local_path(path)
            if path.is_file():
                name = path.relative_to(root).as_posix()
                if name not in expected:
                    raise ValueError("Unreviewed PDF payload file: " + name)
                item = expected[name]
                if path.stat().st_size != item["bytes"] or digest(path.read_bytes()) != item["sha256"]:
                    raise ValueError("PDF payload identity differs: " + name)
                actual.add(name)
    if actual != set(expected):
        raise ValueError("PDF payload is missing reviewed runtime files or notices")
    return record


def stage(payload, qpdf_directory, pdfium_directory, source_archive):
    root = local_path(payload)
    for name in DIRECTORIES:
        local_path(root / name)
        if (root / name).exists():
            raise ValueError("PDF payload already exists; use a new stage")
    qpdf = local_path(qpdf_directory, True)
    pdfium = local_path(pdfium_directory, True)
    source = local_path(source_archive, True)
    record = selection()
    inputs = {"qpdf": qpdf / "upstream.zip", "pdfium": pdfium / "upstream.tgz", "qpdf-source": source}
    archives = {}
    try:
        for name, path in inputs.items():
            local_path(path, True)
            pin = record["archives"][name]
            if path.stat().st_size != pin["bytes"]:
                raise ValueError("PDF input archive size differs: " + name)
            data = path.read_bytes()
            if digest(data) != pin["sha256"]:
                raise ValueError("PDF input archive identity differs: " + name)
            archives[name] = (zipfile.ZipFile(io.BytesIO(data)) if name == "qpdf"
                              else tarfile.open(fileobj=io.BytesIO(data), mode="r:gz"))
        hosts = {}
        for host in record["hosts"]:
            base = qpdf if host["input"] == "qpdf" else pdfium
            path = local_path(base / host["path"], True)
            if path.stat().st_size != host["bytes"]:
                raise ValueError("PDF host size differs")
            data = path.read_bytes()
            if digest(data) != host["sha256"]:
                raise ValueError("PDF host differs from the private adapter's pinned binary")
            for item in host["sources"]:
                if digest((ROOT / item["path"]).read_bytes()) != item["sha256"]:
                    raise ValueError("PDF host source changed; rebuild and review the candidate")
            hosts[host["name"]] = data
        contents = {}
        for item in record["files"]:
            kind = item["input"]
            if kind == "host":
                data = hosts[item["member"]]
            elif kind == "authored":
                data = (TOOLS / item["member"]).read_bytes()
            elif kind == "qpdf":
                entry = archives[kind].getinfo(item["member"])
                if entry.is_dir() or entry.file_size != item["bytes"]:
                    raise ValueError("Invalid selected qpdf ZIP member")
                data = archives[kind].read(entry)
            else:
                entry = archives[kind].getmember(item["member"])
                if not entry.isfile() or entry.size != item["bytes"]:
                    raise ValueError("Invalid selected PDF tar member")
                data = archives[kind].extractfile(entry).read()
            if len(data) != item["bytes"] or digest(data) != item["sha256"]:
                raise ValueError("PDF source member differs: " + item["path"])
            contents[item["path"]] = data
        # No writes until every archive, host, source and notice has been checked.
        for name in sorted(DIRECTORIES):
            (root / name).mkdir()
        for name, data in contents.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as stream:
                stream.write(data)
    finally:
        for archive in archives.values():
            archive.close()
    verify(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", required=True)
    parser.add_argument("--qpdf-directory")
    parser.add_argument("--pdfium-directory")
    parser.add_argument("--qpdf-source")
    args = parser.parse_args()
    inputs = [args.qpdf_directory, args.pdfium_directory, args.qpdf_source]
    if any(inputs):
        if not all(inputs):
            raise ValueError("Supply both prepared PDF engines and the pinned qpdf source archive")
        stage(args.payload, *inputs)
    else:
        verify(args.payload)
    print("Verified complete pinned PDF candidate runtime and notices; release approval remains separate.")


if __name__ == "__main__":
    main()
