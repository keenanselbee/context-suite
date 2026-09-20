"""Check document worksheet identity, failure labels and local-only artifact admission."""

import argparse
import copy
import hashlib
from html.parser import HTMLParser
import importlib.util
import json
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("document_review", Path(__file__).with_name("Prepare-DocumentReview.py"))
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.cases = []
        self.script = ""
        self.in_script = False

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "a": self.links.append(attrs["href"])
        if tag == "img": self.links.append(attrs["src"])
        if "data-case" in attrs: self.cases.append((attrs["data-case"], attrs["data-automated"]))
        if tag == "script": self.in_script = True

    def handle_endtag(self, tag):
        if tag == "script": self.in_script = False

    def handle_data(self, data):
        if self.in_script: self.script += data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pack", type=Path)
    args = parser.parse_args()
    pack = review.read_local(args.pack / "prepared.json")[0].parent
    record = json.loads((pack / "prepared.json").read_text())
    original = (pack / "input-manifest.json").read_bytes()
    manifest = json.loads(original)
    if hashlib.sha256(original).hexdigest().upper() != record["ManifestSha256"]:
        raise ValueError("Changed pack manifest")
    for item in record["Artifacts"]:
        if hashlib.sha256(review.read_local(pack / item["Path"])[1]).hexdigest().upper() != item["Sha256"]:
            raise ValueError("Changed copied artifact")
    page = Page(); document = (pack / "review.html").read_text(encoding="utf-8"); page.feed(document)
    if page.cases != [(case["Id"], case["Status"]) for group in review.GROUPS for case in manifest["Cases"] if case["Group"] == group]:
        raise ValueError("Worksheet omitted a case or changed its recorded status")
    if record["HumanAcceptance"] != "Not reviewed" or record["FinalCandidateReplay"] is not False:
        raise ValueError("Preparation cannot claim acceptance")
    if document.count('<option>Not reviewed</option>') != record["CaseCount"]:
        raise ValueError("Human observations must start unset")
    for link in page.links:
        if not link.startswith("#") and not (pack / link).is_file():
            raise ValueError("Broken worksheet artifact link")
    stage = ROOT / ".codex-temp/document-review-checks" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    script = stage / "download-notes.js"; script.write_text(page.script)
    subprocess.run(["node", "--check", str(script)], check=True, cwd=ROOT)
    checks = ["all copied identities match the selected manifest", "all cases and known-failure labels are retained",
              "all human observations remain unset", "all artifact links resolve", "download script parses"]
    source = copy.deepcopy(manifest); source["Cases"] = [source["Cases"][0]]
    def refuse(name, changed, expected):
        path = stage / (name + ".json"); path.write_text(json.dumps(changed))
        before = set((ROOT / ".codex-temp/document-review").iterdir())
        try:
            review.prepare(path)
        except ValueError as error:
            if expected not in str(error): raise
        else:
            raise AssertionError(name + " was accepted")
        if before != set((ROOT / ".codex-temp/document-review").iterdir()):
            raise AssertionError("Rejected input created a review pack")
        checks.append(name)
    changed = copy.deepcopy(source); changed["Cases"][0]["Files"][0]["Sha256"] = "0" * 64
    refuse("changed-artifact", changed, "identity changed")
    changed = copy.deepcopy(source); changed["Cases"][0]["Files"][0]["Path"] = str(ROOT / "README.md")
    refuse("outside-scratch", changed, "repository-local")
    changed = copy.deepcopy(source); changed["Cases"].append(copy.deepcopy(changed["Cases"][0]))
    refuse("duplicate-case", changed, "duplicate")
    changed = copy.deepcopy(source); del changed["Remaining"]["D12"]
    refuse("omitted-acceptance-group", changed, "every acceptance group")
    failed = stage / "failed-inspection.json"; failed.write_text('{"Passed":false}')
    changed = copy.deepcopy(source)
    item = next(item for item in changed["Cases"][0]["Files"] if item["Kind"] == "evidence")
    item["Path"] = str(failed); item["Sha256"] = hashlib.sha256(failed.read_bytes()).hexdigest().upper()
    refuse("false-pass-label", changed, "Failed evidence")
    (stage / "results.json").write_text(json.dumps({"Passed": True, "Checks": checks, "Pack": str(pack),
        "ManifestSha256": record["ManifestSha256"], "Scope": "Identity, markup and admission checks; visual/browser/download interaction unverified."}, indent=2) + "\n")
    print(f"Passed {len(checks)} document review checks: {stage}")


if __name__ == "__main__":
    main()
