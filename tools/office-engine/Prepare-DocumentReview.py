"""Copy hash-bound authored document evidence into a local human-review worksheet."""

import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import uuid

ROOT = Path(__file__).resolve().parents[2]
GROUPS = {f"D{number:02}": title for number, title in enumerate((
    "Ordinary Word", "Word font styles", "Word revisions", "Word multilingual text and image",
    "Excel print layout", "Excel calculation and dates", "Excel charts and capacity",
    "PowerPoint slide order", "PowerPoint layout", "Approved real Word report",
    "Approved real Excel workbook", "Approved real PowerPoint deck"), 1)}
GROUPS["PDF"] = "PDF appearance controls"
STATUSES = {"pass": "Recorded automated checks passed", "fail": "Known automated failure",
            "refused": "Output withheld as required", "control": "Historical rendering control"}


def read_local(path):
    path = Path(path).absolute()
    if ".." in path.parts or not path.is_relative_to(ROOT / ".codex-temp"):
        raise ValueError("Use repository-local review evidence")
    for item in (path, *path.parents):
        if item == ROOT:
            break
        if item.is_symlink() or item.is_junction():
            raise ValueError("Linked review evidence is refused")
    if not path.is_file() or path.stat().st_size > 32 * 1024 * 1024:
        raise ValueError("Missing or oversized review evidence")
    return path, path.read_bytes()


def prepare(manifest_path):
    manifest_path, raw = read_local(manifest_path)
    manifest = json.loads(raw)
    cases = manifest["Cases"]
    if not 1 <= len(cases) <= 200 or set(manifest["Remaining"]) != set(GROUPS):
        raise ValueError("Declare every acceptance group and a bounded set of cases")
    loaded = {}
    identifiers = set()
    for case in cases:
        identifier = case["Id"]
        if not re.fullmatch(r"[a-z0-9-]{1,80}", identifier) or identifier in identifiers:
            raise ValueError("Invalid or duplicate review case")
        identifiers.add(identifier)
        if case["Group"] not in GROUPS or case["Status"] not in STATUSES or not case["Instructions"].strip():
            raise ValueError("Every case needs a group, recorded status and review instructions")
        if not case["Files"] or not any(item["Kind"] == "evidence" for item in case["Files"]):
            raise ValueError("Every case needs its evidence report")
        for item in case["Files"]:
            path, data = read_local(item["Path"])
            if item["Kind"] not in ("source", "output", "reference", "withheld", "evidence"):
                raise ValueError("Unknown review artifact kind")
            if path.suffix.lower() not in (".docx", ".xlsx", ".pptx", ".pdf", ".json"):
                raise ValueError("Only authored Office, PDF and JSON evidence is supported")
            if hashlib.sha256(data).hexdigest().upper() != item["Sha256"]:
                raise ValueError("Review artifact identity changed: " + str(path))
            if item["Kind"] == "evidence":
                report = json.loads(data)
                if report.get("Passed") is False and case["Status"] == "pass":
                    raise ValueError("Failed evidence cannot be labeled passed")
            loaded[str(path)] = data
    if sum(len(data) for data in loaded.values()) > 256 * 1024 * 1024:
        raise ValueError("Review pack exceeds the authored-evidence budget")

    pack = ROOT / ".codex-temp/document-review" / uuid.uuid4().hex
    pack.mkdir(parents=True)
    (pack / "evidence").mkdir()
    (pack / "input-manifest.json").write_bytes(raw)
    cards = {group: [] for group in GROUPS}
    artifacts = []
    for case in cases:
        folder = pack / case["Id"]
        folder.mkdir()
        links = []
        for index, item in enumerate(case["Files"]):
            source = Path(item["Path"]).absolute()
            name = f"{index + 1}-{item['Kind']}{source.suffix.lower()}"
            target = pack / "evidence" / (item["Sha256"] + ".json") if item["Kind"] == "evidence" else folder / name
            relative = target.relative_to(pack).as_posix()
            if not target.exists():
                target.write_bytes(loaded[str(source)])
                artifacts.append({"Path": relative, "Sha256": item["Sha256"]})
            links.append(f'<li><a href="{relative}" target="_blank" rel="noopener">{html.escape(item["Label"])}</a></li>')
        cards[case["Group"]].append(f'''<details data-case="{case['Id']}" data-automated="{case['Status']}">
<summary>{html.escape(case['Title'])} <span class="status">{STATUSES[case['Status']]}</span></summary>
<p>{html.escape(case['Instructions'])}</p><ul>{''.join(links)}</ul>
<label>Human observation<select name="review"><option>Not reviewed</option><option>No additional issue seen</option><option>Issue seen</option></select></label>
<label>Notes<textarea name="notes" rows="3"></textarea></label></details>''')
    sections = []
    for group, title in GROUPS.items():
        remaining = html.escape(manifest["Remaining"][group])
        sections.append(f'<section id="{group}"><h2>{group}: {title}</h2><p>{remaining}</p>' +
                        (''.join(cards[group]) or '<p><strong>No review files prepared for this group yet.</strong></p>') + '</section>')
    record = {"ManifestSha256": hashlib.sha256(raw).hexdigest().upper(), "CaseCount": len(cases),
              "Artifacts": artifacts, "HumanAcceptance": "Not reviewed", "FinalCandidateReplay": False}
    identity = json.dumps(record).replace("<", "\\u003c")
    page = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Context Suite document review</title><style>
:root{color-scheme:light dark;font:16px/1.55 system-ui,sans-serif}body{max-width:1000px;margin:auto;padding:24px}
a{color:light-dark(#0647a5,#9bc8ff)}section{border-top:2px solid #888;margin-top:32px}details{border:1px solid #888;padding:16px;margin:12px 0}
summary{cursor:pointer;font-weight:600}.status{display:block;font-weight:400}label{display:block;margin:16px 0 8px}
input,textarea,select,button{font:inherit;padding:8px}input,textarea{width:100%;box-sizing:border-box}textarea{resize:vertical}
.notice{border:2px solid #888;padding:16px}nav{display:flex;flex-wrap:wrap;gap:16px}button{margin-top:24px}
</style><h1>Document review</h1><p class="notice"><strong>Human acceptance is open.</strong>
These retained authored tests come from several historical workers. This is not the final candidate replay.
Known failures remain failures regardless of the notes entered here.</p>
<p>Open each PDF in your intended viewer and compare every page with its source and the stated checks.
Historical rendering controls are not Microsoft Office reference exports. JSON links contain the recorded automated evidence.
Keep withheld or known-failed PDFs separate from acceptable output. Nothing opens, saves or uploads automatically.</p>
<p>Record the viewer/version, operating system, monitor/scaling and any source application used.
Download notes before closing. Screen-reader, theme, DPI and installed Explorer acceptance are separate checks.</p>
<label>Review environment<input id="setup"></label><nav>''' + ''.join(f'<a href="#{key}">{key}</a>' for key in GROUPS) + '</nav>' + ''.join(sections) + '''
<button id="download">Download review notes</button><p id="saved" role="status"></p><script>
document.getElementById('download').addEventListener('click',()=>{
const cases=[...document.querySelectorAll('[data-case]')].map(row=>({id:row.dataset.case,automated:row.dataset.automated,
review:row.querySelector('[name=review]').value,notes:row.querySelector('[name=notes]').value}));
const content={scope:'Historical authored document review; not final candidate acceptance',pack:''' + identity + ''',
setup:document.getElementById('setup').value,cases};
const url=URL.createObjectURL(new Blob([JSON.stringify(content,null,2)],{type:'application/json'}));
const link=document.createElement('a');link.href=url;link.download='context-suite-document-review.json';link.click();
setTimeout(()=>URL.revokeObjectURL(url),1000);document.getElementById('saved').textContent='Review notes download requested.';
});</script></html>'''
    (pack / "review.html").write_text(page, encoding="utf-8")
    for source, data in loaded.items():
        if read_local(source)[1] != data:
            raise ValueError("Source evidence changed while preparing review")
    for artifact in artifacts:
        if hashlib.sha256((pack / artifact["Path"]).read_bytes()).hexdigest().upper() != artifact["Sha256"]:
            raise ValueError("Copied review artifact changed")
    (pack / "prepared.json").write_text(json.dumps(record, indent=2) + "\n")
    print("Document review:", pack / "review.html", flush=True)
    return pack


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    prepare(parser.parse_args().manifest)
