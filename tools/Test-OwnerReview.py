"""Verify draft collection identity and refusal guards without media execution."""

import argparse
import copy
import importlib.util
import json
from pathlib import Path
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    tool = Path(__file__).with_name("Prepare-OwnerReview.py").resolve()
    spec = importlib.util.spec_from_file_location("owner_review", tool)
    review = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(review)
    source = review.local(args.manifest)
    inputs = json.loads(source.read_bytes())
    evidence = review.ROOT / ".codex-temp/owner-review-checks" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    checks = []
    stage, result = review.prepare(source)
    for name, item in result["Packs"].items():
        assert review.inventory(stage / "packs" / name) == item["Files"] == inputs["Packs"][name]["Files"]
        assert review.worksheet_links(stage / "packs" / name) == item["LocalLinks"]
    checks.append("all-copied-artifacts-and-local-links-match")
    assert result["DocumentStatuses"] == {"pass": 50, "fail": 5, "control": 5, "refused": 2}
    checks.append("historical-failures-controls-and-refusals-preserved")
    assert result["HumanAcceptance"] == "Not performed" and not result["FinalCandidateAcceptance"]
    assert "not the final candidate" in (stage / "index.html").read_text(encoding="utf-8")
    checks.append("human-and-final-candidate-acceptance-unset")
    collection_root = stage.parent
    for label in ("changed-artifact", "outside-path", "omitted-family", "false-final-scope"):
        changed = copy.deepcopy(inputs)
        if label == "changed-artifact":
            changed["Packs"]["audio"]["Files"]["results.json"]["Sha256"] = "0" * 64
        elif label == "outside-path":
            changed["Packs"]["audio"]["Directory"] = str(review.ROOT / "tools")
        elif label == "omitted-family":
            del changed["Packs"]["documents"]
        else:
            changed["Scope"] = "FinalCandidate"
        path = evidence / (label + ".json")
        path.write_text(json.dumps(changed), encoding="utf-8")
        before = set(collection_root.iterdir())
        try:
            review.prepare(path)
            raise AssertionError("Invalid owner-review input accepted: " + label)
        except ValueError:
            checks.append("refuses-" + label)
        assert set(collection_root.iterdir()) == before
    checks.append("rejected-inputs-create-no-collection")
    result = dict(Passed=True, Checks=checks, Collection=str(stage),
        ToolSha256=review.digest(tool), InputManifestSha256=review.digest(source),
        Scope="Historical draft assembly/identity checks only; no media processing, UI operation or human acceptance.")
    (evidence / "results.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(Evidence=str(evidence), **result), indent=2))


if __name__ == "__main__":
    main()
