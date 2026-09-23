"""Consolidate existing historical worksheets without changing their evidence.

The resulting collection is explicitly a draft, never final-candidate or human
acceptance. This command copies local files; it opens no viewer or media player.
"""

import argparse
import hashlib
import html
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import shutil
import stat
from urllib.parse import unquote, urlsplit
import uuid


ROOT = Path(__file__).resolve().parents[1]
PACKS = {
    "audio": ("H01: original audio", "15 generated comparisons"),
    "expanded-audio": ("H01: expanded audio", "10 AIFF/WMA comparisons"),
    "images": ("H02: HEIC and AVIF", "16 image outputs"),
    "documents": ("H03: documents", "62 historical review entries; recorded failures retained"),
}
EXTENSIONS = {".txt", ".json", ".html", ".wav", ".flac", ".m4a", ".mp3", ".opus", ".ogg",
              ".aif", ".pcm", ".wma", ".f32", ".jpg", ".png", ".avif", ".heic", ".rgba16",
              ".docx", ".pdf", ".xlsx", ".pptx"}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def local(value):
    path = Path(value).absolute()
    if ".." in path.parts or not path.is_relative_to(ROOT / ".codex-temp"):
        raise ValueError("Use repository scratch review inputs.")
    for ancestor in (path, *path.parents):
        if os.path.lexists(ancestor) and ancestor.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Linked review paths are refused.")
    return path


def inventory(directory):
    directory = local(directory)
    result = {}
    pending = [directory]
    while pending:
        for path in sorted(pending.pop().iterdir()):
            local(path)
            if path.is_dir():
                pending.append(path)
            elif path.is_file() and path.suffix.lower() in EXTENSIONS and path.stat().st_size <= 64 * 1024**2:
                result[path.relative_to(directory).as_posix()] = dict(Bytes=path.stat().st_size, Sha256=digest(path))
            else:
                raise ValueError("Unreviewed or oversized worksheet artifact: " + str(path))
    if not result or len(result) > 1000 or "review.html" not in result:
        raise ValueError("Missing or oversized worksheet collection.")
    return dict(sorted(result.items()))


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.references = []

    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if name in ("href", "src") and value:
                self.references.append((name, value))


def worksheet_links(directory):
    parser = Links()
    parser.feed((directory / "review.html").read_text(encoding="utf-8"))
    checked = 0
    for attribute, value in parser.references:
        url = urlsplit(value)
        if url.scheme in ("http", "https") and attribute == "href":
            continue
        if url.scheme or url.netloc:
            raise ValueError("Worksheet loads nonlocal content: " + value)
        if not url.path:
            continue
        target = (directory / unquote(url.path)).resolve()
        if not target.is_relative_to(directory.resolve()) or not target.is_file():
            raise ValueError("Worksheet link leaves its pack or is missing: " + value)
        checked += 1
    return checked


def prepare(manifest_path):
    manifest_path = local(manifest_path)
    manifest_bytes = manifest_path.read_bytes()
    manifest = json.loads(manifest_bytes)
    if manifest.get("Scope") != "HistoricalDraft" or set(manifest.get("Packs", {})) != set(PACKS):
        raise ValueError("Declare the four historical packs explicitly; no candidate acceptance is inferred.")
    loaded = {}
    total = 0
    for name, entry in manifest["Packs"].items():
        directory = local(entry["Directory"])
        files = inventory(directory)
        if files != entry["Files"]:
            raise ValueError("Worksheet identities changed: " + name)
        total += sum(item["Bytes"] for item in files.values())
        loaded[name] = (directory, files, worksheet_links(directory))
    if total > 512 * 1024**2:
        raise ValueError("Review collection exceeds its 512-MiB budget.")
    document_manifest = json.loads((loaded["documents"][0] / "input-manifest.json").read_bytes())
    cases = document_manifest["Cases"]
    if len(cases) != 62 or any(case["Status"] not in {"pass", "fail", "control", "refused"} for case in cases):
        raise ValueError("Historical document selection differs; review the collection contract first.")
    for name, count in (("audio", 15), ("expanded-audio", 10), ("images", 16)):
        report = json.loads((loaded[name][0] / "results.json").read_bytes())
        if report.get("AutomatedPassed") is not True or len(report.get("Comparisons", [])) != count:
            raise ValueError("Missing prepared media worksheet result: " + name)
    stage = local(ROOT / ".codex-temp/owner-review-draft" / uuid.uuid4().hex)
    stage.mkdir(parents=True)
    (stage / "input-manifest.json").write_bytes(manifest_bytes)
    cards = []
    for name, (directory, files, links) in loaded.items():
        destination = stage / "packs" / name
        destination.mkdir(parents=True)
        for relative, identity in files.items():
            source = local(directory / relative)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open("rb") as original, target.open("xb") as output:
                shutil.copyfileobj(original, output)
            if digest(target) != identity["Sha256"]:
                raise ValueError("Worksheet copy differs.")
        if inventory(destination) != files or worksheet_links(destination) != links or inventory(directory) != files:
            raise ValueError("Worksheet copy or original changed during preparation.")
        title, detail = PACKS[name]
        cards.append('<li><a href="packs/' + name + '/review.html">' + html.escape(title) + '</a><p>' + html.escape(detail) + '</p></li>')
    page = '''<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1"><title>Context Suite review draft</title>
<style>:root{color-scheme:light dark;font:16px/1.5 system-ui,sans-serif}body{max-width:850px;margin:auto;padding:24px}li{margin:20px 0}a{color:light-dark(#0647a5,#9bc8ff)}.notice{padding:16px;border:2px solid currentColor}p{margin:8px 0}h1{font-size:2rem}</style>
<h1>Context Suite review draft</h1>
<p class="notice"><strong>Historical evidence — not the final candidate.</strong>
These copied worksheets retain their original automated results, including known failures.
Human review is unverified. Final candidate outputs must be regenerated and checked before acceptance.</p>
<h2>Prepared comparisons</h2><ul>''' + "".join(cards) + '''</ul>
<h2>How to use the worksheets</h2>
<p>Open one worksheet, follow its instructions, and compare every source/reference with its output.
Record the viewer or player, display or audio device, and any issues. Download that worksheet's notes
before closing it. Nothing plays automatically or uploads. A human observation never clears an automated failure.</p>
<h2>Still needed</h2><ul>
<li>Nine audio comparisons from three approved real recordings.</li>
<li>Approved real Word, Excel and PowerPoint documents and independent references.</li>
<li>Repaired-chart application acceptance and the outstanding PowerPoint font-size decision.</li>
<li>Eight interactive workflow scenarios using disposable copies and TEST-ONLY access.</li>
<li>Actual light/dark, scaling, monitor and screen-reader observations.</li>
<li>A fresh combined candidate, exact automated/performance replay and refreshed review artifacts.</li>
</ul><p>No installation, Explorer registration, live licensing or publishing is part of this collection.</p>
<p><a href="manifest.json">Copied artifact identities and retained status</a></p></html>'''
    (stage / "index.html").write_text(page, encoding="utf-8", newline="\n")
    result = dict(Scope="HistoricalDraft", HumanAcceptance="Not performed", FinalCandidateAcceptance=False,
        PreparerSha256=digest(Path(__file__)),
        InputManifestSha256=hashlib.sha256(manifest_bytes).hexdigest().upper(),
        Packs={name: dict(Original=str(directory), Files=files, LocalLinks=links) for name, (directory, files, links) in loaded.items()},
        DocumentStatuses={status: sum(case["Status"] == status for case in cases) for status in ("pass", "fail", "control", "refused")},
        Files=sum(len(files) for _, files, _ in loaded.values()), Bytes=total,
        Notes="Every worksheet and artifact is an unchanged copy. Source reports retain their original paths and identities as provenance. No media operation or human review was performed.")
    (stage / "manifest.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return stage, result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    stage, result = prepare(args.manifest)
    print(json.dumps(dict(Directory=str(stage), Files=result["Files"], Bytes=result["Bytes"],
                         DocumentStatuses=result["DocumentStatuses"], Scope=result["Scope"]), indent=2))


if __name__ == "__main__":
    main()
