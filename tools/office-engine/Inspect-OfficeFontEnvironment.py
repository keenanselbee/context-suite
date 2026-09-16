"""Inspect the fixed read-only renderer font query in completed authored isolation cases."""

import argparse
from decimal import Decimal, InvalidOperation
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import uuid

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("office_payload", TOOLS / "Stage-OfficePayload.py")
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)
MAXIMUM_BYTES = 256 * 1024


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Repeated font-query property.")
        result[key] = value
    return result


def read_families(data):
    if not data or len(data) > MAXIMUM_BYTES:
        raise ValueError("Font query exceeds its byte bound.")
    value = json.loads(data.decode("utf-8", errors="strict"), object_pairs_hook=unique)
    if not isinstance(value, dict) or value.get("commandName") != ".uno:CharFontName":
        raise ValueError("Unexpected font query.")
    if set(value) == {"commandName", "commandValues"}:
        values = value["commandValues"]
        if values == "":  # Pinned legacy writer's missing FontList representation.
            return []
        if not isinstance(values, dict):
            raise ValueError("Invalid legacy font list.")
        # The pinned legacy writer uses dotted property-tree paths. For example,
        # Modern No. 20 becomes {"Modern No": {" 20": [...]}}. Preserve the dots
        # when reading that documented writer shape rather than dropping a font.
        names = []
        def visit(branch, prefix="", depth=0):
            if depth > 8 or not branch:
                raise ValueError("Invalid or deeply nested legacy font list.")
            for key, child in branch.items():
                name = prefix + "." + key if depth else key
                if isinstance(child, dict):
                    visit(child, name, depth + 1)
                else:
                    validate_sizes(child)
                    names.append(name)
                    if len(names) > 4096:
                        raise ValueError("Oversized legacy font family list.")
        if values:
            visit(values)
    elif set(value) == {"commandName", "FontNames", "FontSizes"}:
        names = value["FontNames"]
        validate_sizes(value["FontSizes"])
    else:
        raise ValueError("Missing or unknown font-query fields.")
    if not isinstance(names, list) or len(names) > 4096:
        raise ValueError("Invalid or oversized font family list.")
    for name in names:
        if not isinstance(name, str) or not name.strip() or len(name) > 256 or any(ord(char) < 32 or 127 <= ord(char) < 160 for char in name):
            raise ValueError("Invalid font family name.")
        name.encode("utf-8", errors="strict")
    if len(names) != len(set(names)):
        raise ValueError("Repeated font family name.")
    return sorted(names)


def validate_sizes(values):
    if not isinstance(values, list) or not 1 <= len(values) <= 64:
        raise ValueError("Invalid font size list.")
    for value in values:
        if not isinstance(value, str) or not re.fullmatch(r"[0-9]{1,3}(?:\.[0-9]{1,3})?", value):
            raise ValueError("Invalid font size declaration.")
        try:
            if not 0 < Decimal(value) <= 500:
                raise ValueError("Font size declaration exceeds bounds.")
        except InvalidOperation as error:
            raise ValueError("Invalid font size.") from error


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def inspect(stage):
    stage = payload.local_path(stage, scratch_only=True)
    if stage.parent != payload.ROOT / ".codex-temp/office-isolation":
        raise ValueError("Use completed repository isolation evidence.")
    case = stage / "case"
    if json.loads((case / "profile-cleanup.json").read_text()) != {"removed": True}:
        raise ValueError("Native profile cleanup must complete before inspection.")
    rows = []
    inputs = {}
    for family in ("Word", "Excel", "PowerPoint"):
        for variant in ("control", "missing"):
            for mode in ("control", "isolated"):
                name = f"{family}-font-{variant}-{mode}"
                terminal = payload.local_path(case / (name + "-export.json"), scratch_only=True)
                result = json.loads(terminal.read_text())
                if not result["completed"] or result["exitCode"] != 0 or result["timedOut"] or result["outputLimit"] or result["activeAfterCleanup"] != 0:
                    raise ValueError("Font observation requires a completed bounded export.")
                if result["rootAppContainerTokenVerified"] != (mode == "isolated"):
                    raise ValueError("Missing expected process-token evidence.")
                inputs[str(terminal)] = digest(terminal)
                log = payload.local_path(case / (name + ".log"), scratch_only=True)
                if log.stat().st_size > 65536:
                    raise ValueError("Oversized native diagnostic evidence.")
                reported = []
                for line in log.read_text(encoding="utf-8", errors="strict").splitlines():
                    if line.startswith('{"fontEnvironmentBytes":'):
                        record = json.loads(line, object_pairs_hook=unique)
                        if set(record) != {"fontEnvironmentBytes"} or type(record["fontEnvironmentBytes"]) is not int:
                            raise ValueError("Invalid native font-query byte observation.")
                        reported.append(record["fontEnvironmentBytes"])
                inputs[str(log)] = digest(log)
                names = []
                lengths = []
                for moment in ("before", "after"):
                    path = payload.local_path(case / "writable" / name / ("fonts-" + moment + ".json"), scratch_only=True)
                    if path.stat().st_size > MAXIMUM_BYTES:
                        raise ValueError("Oversized font-query evidence.")
                    names.append(read_families(path.read_bytes()))
                    lengths.append(path.stat().st_size)
                    inputs[str(path)] = digest(path)
                if reported != lengths:
                    raise ValueError("Native query observations do not match the retained before/after bytes.")
                rows.append({"Name": name, "Family": family, "Variant": variant, "Isolated": mode == "isolated",
                    "Before": names[0], "After": names[1], "StableAcrossExport": names[0] == names[1],
                    "ArialListed": all("Arial" in values for values in names),
                    "SyntheticFamilyListed": any("ContextSuiteAbsentFont9361" in values for values in names)})
    comparisons = []
    for family in ("Word", "Excel", "PowerPoint"):
        for variant in ("control", "missing"):
            pair = [row for row in rows if row["Family"] == family and row["Variant"] == variant]
            comparisons.append({"Family": family, "Variant": variant, "IsolationSetsEqual": pair[0]["Before"] == pair[1]["Before"] and pair[0]["After"] == pair[1]["After"]})
    if not all(digest(Path(path)) == expected for path, expected in inputs.items()):
        raise ValueError("Inspection inputs changed.")
    result = {"Completed": True, "Rows": rows, "Comparisons": comparisons, "Inputs": inputs,
        "DistinguishesAuthoredAbsentFamily": all(row["ArialListed"] and not row["SyntheticFamilyListed"] for row in rows),
        "Scope": "Renderer family-list observations only; no style, font-program, alias, embedding-rights or glyph-coverage proof."}
    destination = stage / ("font-environment-" + uuid.uuid4().hex + ".json")
    with destination.open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2, ensure_ascii=False)
        output.write("\n")
    print("Font environment evidence:", destination)
    print(json.dumps({"Cases": len(rows), "Counts": sorted({len(row["Before"]) for row in rows}),
        "StableAcrossExport": all(row["StableAcrossExport"] for row in rows),
        "IsolationSetsEqual": all(pair["IsolationSetsEqual"] for pair in comparisons),
        "DistinguishesAuthoredAbsentFamily": result["DistinguishesAuthoredAbsentFamily"]}))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage")
    inspect(parser.parse_args().stage)
