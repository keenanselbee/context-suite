"""Verify supplemental Office component notices against the retained source kit.

Archives are read without extracting or executing dependency code. Build records
select a conservative source-notice set; they are not a claim that every nested
source package in that set is linked into the shipped runtime.
"""

import argparse
import hashlib
import html
import io
import json
from pathlib import Path
import re
import tarfile
import zipfile


ROOT = Path(__file__).resolve().parents[2]
RECORD = ROOT / "tools/office-engine/component-notice-review.json"
NOTICE = ROOT / "assets/third-party/office/Office.Dependency.Notices.html"
NOTICE_NAME = re.compile(
    r"(^|[._-])(licen[cs]e|copying|copyright|notice|patents?|authors|gpl|lgpl|mpl|bsd|mit|ftl)([._-]|$)", re.I)
EXCLUDED_SUFFIXES = {".c", ".h", ".cc", ".cpp", ".py", ".pm", ".java", ".pl", ".sh", ".png", ".jpg", ".pdf"}
EXTRA_FILES = {
    "libjpeg-turbo": {"README.ijg"},
    "openssl": {"README.md", "ACKNOWLEDGEMENTS.md"},
    "lpsolve": {"README.txt", "lp_lib.c", "colamd/colamd.c"},
    "sqlite3": {"sqlite3.h"},
}
LATIN1 = {"lcms2-2.19.1/AUTHORS", "pdfium/third_party/freetype/FTL.TXT"}


def pin(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest().upper()}


def source_notices(data, component):
    extras = EXTRA_FILES.get(component, set())
    found_extras = set()
    found = {}

    def selected(name):
        relative = name.partition("/")[2]
        if relative in extras:
            found_extras.add(relative)
            return True
        return bool(NOTICE_NAME.search(Path(name).name)) and Path(name).suffix.lower() not in EXCLUDED_SUFFIXES

    if zipfile.is_zipfile(io.BytesIO(data)):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for member in archive.infolist():
                if not member.is_dir() and selected(member.filename):
                    if member.filename in found:
                        raise ValueError("Duplicate source notice: " + member.filename)
                    found[member.filename] = archive.read(member)
    else:
        with tarfile.open(fileobj=io.BytesIO(data), mode="r:*") as archive:
            for member in archive:
                if not selected(member.name):
                    continue
                if member.issym() or member.islnk():
                    raise ValueError("Linked source notice requires review: " + member.name)
                if member.isfile():
                    if member.name in found:
                        raise ValueError("Duplicate source notice: " + member.name)
                    found[member.name] = archive.extractfile(member).read()
    if found_extras != extras:
        raise ValueError("Missing explicit component notice: " + component)
    result = []
    for name, original in sorted(found.items()):
        if not original:
            continue
        start, end = 0, len(original)
        if name.endswith(("/sqlite3.h", "/lp_lib.c", "/colamd/colamd.c")):
            comments = list(re.finditer(rb"/\*.*?\*/", original, re.S))
            if name.endswith("/colamd/colamd.c"):
                comment = next(c for c in comments if b"Authors:" in c[0] and b"Copyright" in c[0])
            else:
                comment = comments[0]
            start, end = comment.span()
        raw = original[start:end]
        encoding = "iso-8859-1" if name in LATIN1 else "utf-8"
        raw.decode(encoding)  # New encodings require review, never replacement characters.
        metadata = {"component": component, "member": name, "source": pin(original),
                    "start": start, "end": end, "encoding": encoding, **pin(raw)}
        result.append((metadata, raw))
    return result


def collate(notices):
    parts = ['<!doctype html>\n<html lang="en"><head><meta charset="utf-8">',
             '<meta name="viewport" content="width=device-width, initial-scale=1">',
             '<title>Office dependency notices - Context Suite</title>',
             '<style>:root{color-scheme:light dark}body{font:1rem/1.55 system-ui,sans-serif;',
             'max-width:76rem;margin:2rem auto;padding:0 1.5rem}pre{white-space:pre-wrap;',
             'overflow-wrap:anywhere;font:0.9rem/1.5 ui-monospace,monospace}',
             'section{border-top:1px solid GrayText;margin-top:2rem}a{color:LinkText}',
             'a:focus-visible{outline:3px solid Highlight;outline-offset:3px}</style></head><body>',
             '<h1>Office dependency notices</h1>',
             '<p>These source notices supplement the Office runtime licenses and the separate ',
             'Office Python notices. The selection conservatively includes nested source, test and ',
             'build-tool notices; inclusion does not mean every listed subcomponent is shipped. ',
             'Original license alternatives remain unchanged.</p>',
             '<p>Text is displayed as UTF-8 with HTML escaping. The corresponding source archives ',
             'retain original file bytes. Explicitly labeled source comments preserve their full ',
             'selected comment blocks.</p>',
             '<p>COLAMD authors: Stefan I. Larimore and Timothy A. Davis, University of Florida. ',
             'Copyright (c) 1998-2001 by the University of Florida. Used by permission. ',
             'The original availability and notice are retained below.</p>',
             '<nav aria-label="Components"><h2>Components</h2><ul>']
    components = sorted({metadata["component"] for metadata, _ in notices})
    for component in components:
        parts.append(f'<li><a href="#{html.escape(component, quote=True)}">{html.escape(component)}</a></li>')
    parts.append('</ul></nav><main>')
    for component in components:
        parts.append(f'<section id="{html.escape(component, quote=True)}"><h2>{html.escape(component)}</h2>')
        for metadata, raw in notices:
            if metadata["component"] != component:
                continue
            parts.append('<h3>' + html.escape(metadata["member"]) + '</h3>')
            if metadata["start"] != 0 or metadata["end"] != metadata["source"]["bytes"]:
                parts.append('<p>Source comment (byte offsets ' + str(metadata["start"]) +
                             ' to ' + str(metadata["end"]) + ', end exclusive).</p>')
            parts.append('<pre>' + html.escape(raw.decode(metadata["encoding"])) + '</pre>')
        parts.append('</section>')
    parts.append('</main></body></html>\n')
    return '\n'.join(parts).encode('utf-8')


def verify(bundle, source_kit, engine=None):
    record_bytes = RECORD.read_bytes()
    record = json.loads(record_bytes)
    selected = json.loads((ROOT / "tools/office-engine/source-runtime-candidate.json").read_bytes())
    if (record["formatVersion"] != 1 or record["inventorySha256"] != selected["inventorySha256"] or
            record["officeSourceKitManifestSha256"] != selected["sourceKitManifestSha256"]):
        raise ValueError("Component notices describe a different Office selection.")
    manifest_bytes = (source_kit / "source-kit.json").read_bytes()
    if pin(manifest_bytes)["sha256"] != record["officeSourceKitManifestSha256"]:
        raise ValueError("Office source kit differs from the reviewed selection.")
    manifest = json.loads(manifest_bytes)
    dependencies = {item["Name"]: item for item in manifest["Dependencies"]}
    with zipfile.ZipFile(bundle) as archive:
        build_bytes = archive.read("current-build-review.json")
        if pin(build_bytes) != record["buildReview"]:
            raise ValueError("Office component build evidence changed.")
        build = json.loads(build_bytes)
        if (build["inventorySha256"] != record["inventorySha256"] or
                build["sourceKitManifestSha256"] != record["officeSourceKitManifestSha256"] or
                build["components"] != record["components"]):
            raise ValueError("Office source selection and build evidence disagree.")
        expected = {"component-notice-review.json", "Office.Dependency.Notices.html", "current-build-review.json"}
        files = dict(build["evidence"])
        files.update({item["recipe"]: item["recipePin"] for item in record["components"]})
        expected.update("build/" + name for name in files)
        if len(archive.namelist()) != len(expected) or set(archive.namelist()) != expected:
            raise ValueError("Unexpected component notice companion membership.")
        if archive.read("component-notice-review.json") != record_bytes:
            raise ValueError("Component source companion record differs.")
        for name, identity in files.items():
            if pin(archive.read("build/" + name)) != identity:
                raise ValueError("Component build record changed: " + name)
        native = {}
        inventory = archive.read("build/runtime-files.txt")
        if pin(inventory)["sha256"] != record["inventorySha256"]:
            raise ValueError("Build evidence contains another runtime inventory.")
        for line in inventory.decode("utf-8").splitlines():
            name, length, digest = line.split("\t")
            if Path(name).suffix.lower() in {".dll", ".exe", ".pyd", ".jar"}:
                native[name] = {"bytes": int(length), "sha256": digest}
        if native != {name: {key: value[key] for key in ("bytes", "sha256")}
                      for name, value in build["nativeFiles"].items()}:
            raise ValueError("Build evidence does not cover the runtime native file set.")
        consumers = {}
        for name, outputs in build["targets"].items():
            if not outputs or any(output not in native or Path(output).name + ".d" != Path(name).name for output in outputs):
                raise ValueError("Build target has no matching runtime file: " + name)
            for component in re.findall(rb"workdir/(?:UnpackedTarball|ExternalProject)/([^/\s\\]+)", archive.read("build/" + name)):
                consumers.setdefault(component.decode().removesuffix(".done"), set()).add(name)
        selected_components = {item["component"] for item in record["components"]}
        if not set(consumers) <= selected_components:
            raise ValueError("Build dependency has no component notice selection.")
        for item in record["components"]:
            if (set(item["dependencyRecords"]) != consumers.get(item["component"], set()) or
                    not item["dependencyRecords"] and not item["additionalEvidence"]):
                raise ValueError("Component notice has inconsistent dependency evidence.")
        notices = []
        for item in record["components"]:
            name = item["archive"]
            if dependencies.get(name) != item["archivePin"]:
                raise ValueError("Component source archive selection differs: " + name)
            data = (source_kit / "dependencies" / name).read_bytes()
            if pin(data) != {"bytes": item["archivePin"]["Bytes"], "sha256": item["archivePin"]["Sha256"]}:
                raise ValueError("Component source archive changed: " + name)
            notices.extend(source_notices(data, item["component"]))
        if [metadata for metadata, _ in notices] != record["notices"]:
            raise ValueError("Source notice contents or membership changed.")
        text = collate(notices)
        if pin(text) != record["notice"] or text != NOTICE.read_bytes() or text != archive.read("Office.Dependency.Notices.html"):
            raise ValueError("Supplemental notices differ from their source texts.")
    compared = None
    if engine is not None:
        if pin((engine / "runtime-files.txt").read_bytes())["sha256"] != record["inventorySha256"]:
            raise ValueError("Runtime inventory differs from notice selection.")
        for name, identity in build["nativeFiles"].items():
            if pin((engine / "runtime" / name).read_bytes()) != {key: identity[key] for key in ("bytes", "sha256")}:
                raise ValueError("Runtime native file differs: " + name)
        compared = len(build["nativeFiles"])
    return {"Passed": True, "Components": len(record["components"]), "Notices": len(notices),
            "NativeFilesCompared": compared, "NativeRebuilt": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--source-kit", required=True, type=Path)
    parser.add_argument("--engine-directory", type=Path)
    args = parser.parse_args()
    print(json.dumps(verify(args.bundle, args.source_kit, args.engine_directory), indent=2))


if __name__ == "__main__":
    main()
