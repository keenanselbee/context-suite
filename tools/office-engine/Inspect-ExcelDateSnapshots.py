"""Inspect authored CLI/native workbook copies; no engine or formula is executed."""

import importlib.util
import io
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from decimal import Decimal


TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("date_evidence", TOOLS / "Inspect-ExcelDates.py")
dates = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dates)
NS = dates.NS


def workbook(data):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        if len(entries) > 128 or len({entry.filename for entry in entries}) != len(entries) or sum(entry.file_size for entry in entries) > 1024 * 1024:
            raise ValueError("Unexpected authored workbook extent")
        parts = {name: ET.fromstring(archive.read(name)) for name in
                 ("xl/workbook.xml", "xl/worksheets/sheet1.xml", "xl/styles.xml")}
    cells = {cell.get("r"): cell for cell in parts["xl/worksheets/sheet1.xml"].findall(".//" + NS + "c")}
    styles = parts["xl/styles.xml"]
    formats = {item.get("numFmtId"): item.get("formatCode") for item in styles.findall("./" + NS + "numFmts/" + NS + "numFmt")}
    xfs = styles.findall("./" + NS + "cellXfs/" + NS + "xf")
    properties = parts["xl/workbook.xml"].find(NS + "workbookPr")
    return parts, cells, formats, xfs, properties


def main():
    root = Path(sys.argv[1]).absolute()
    report = json.loads(dates.read(root / "office-evaluation.json"))
    names = {f"Excel dates {variant}.xlsx" for variant in ("1900-default", "1900-explicit", "1904")}
    cases = {(name, profile) for name in names for profile in ("calc-always", "calc-never")}
    native = report["Mode"] == "ExcelNativeDateSnapshots"
    if report["Mode"] not in ("ExcelDateSnapshots", "ExcelNativeDateSnapshots") or len(report["Results"]) != 6 or {(item["Source"], item["ProfileStyle"]) for item in report["Results"]} != cases:
        raise ValueError("Expected six complete workbook snapshot cases")
    checked = []
    values = ["1", "59", "60", "61", "40729", "40729.5", "1.5"]
    for item in report["Results"]:
        name, profile = item["Source"], item["ProfileStyle"]
        folder = root / (name[:-5] + "-" + profile)
        original = dates.read(root / "fixtures" / name)
        snapshot = dates.read(folder / name)
        if not item["Completed"] or dates.sha(original) != item["SourceSha256"] or dates.sha(snapshot) != item["SnapshotSha256"]:
            raise ValueError("Changed source or workbook copy")
        original_parts, original_cells, original_formats, original_xfs, original_properties = workbook(original)
        parts, cells, formats, xfs, properties = workbook(snapshot)
        declaration = "1" if "1904" in name else "0" if "explicit" in name else None
        if (original_properties.get("date1904") if original_properties is not None else None) != declaration:
            raise ValueError("Changed authored date declaration")
        if original_formats != {"164": "yyyy-mm-dd", "165": "yyyy-mm-dd hh:mm:ss", "166": "[h]:mm:ss"} or [xf.get("numFmtId") for xf in original_xfs] != ["0", "164", "165", "166"]:
            raise ValueError("Changed authored number formats")
        observed = []
        for index, value in enumerate(values, 2):
            address = "B" + str(index)
            original_cell, cell = original_cells[address], cells[address]
            cached = "40729" if index < 6 else value
            style = "2" if index == 7 else "3" if index == 8 else "1"
            if original_cell.findtext(NS + "f") != "0+" + value or original_cell.findtext(NS + "v") != cached or original_cell.get("s") != style:
                raise ValueError("Changed authored formula/cache/style")
            expected = cached if profile == "calc-never" else value
            actual = cell.findtext(NS + "v")
            if actual is None or cell.get("t") not in (None, "n") or not Decimal(actual).is_finite():
                raise ValueError("Missing numeric exported cache")
            xf = xfs[int(cell.get("s", "0"))]
            observed.append({"Cell": address, "ExpectedCache": expected, "ExportedCache": actual,
                             "Matches": Decimal(expected) == Decimal(actual), "ExportedFormula": cell.findtext(NS + "f"),
                             "CellFormat": dict(xf.attrib), "CustomFormat": formats.get(xf.get("numFmtId"))})
        preflight = item["DatePreflight"]
        if preflight["FormatId"] != "xlsx" or preflight["Refusal"] is not None or not preflight["StoredDates"]["Complete"] or preflight["StoredDates"]["EarlyDateCells"] != 0 or preflight["StoredDates"]["DateFormulaCells"] != 6:
            raise ValueError("Source preflight observation changed")
        conversion = json.loads(dates.read(folder / "conversion.json"))
        if conversion["JobActiveProcessesAfterCleanup"] != 0 or conversion["Profile"] != item["Profile"]:
            raise ValueError("Incomplete conversion cleanup")
        profile_path = Path(item["Profile"]).absolute()
        if profile_path.parent != dates.ROOT / ".codex-temp" or not re.fullmatch(r"office-profile-[0-9a-f]{32}u", profile_path.name) or profile_path.is_symlink() or profile_path.is_junction():
            raise ValueError("Unexpected snapshot profile path")
        settings_path = profile_path / "settings-verification.json"
        if settings_path.is_symlink() or settings_path.is_junction() or settings_path.stat().st_size > 4096:
            raise ValueError("Invalid profile verification receipt")
        settings = json.loads(settings_path.read_bytes())
        if not settings["Verified"] or settings["CalculationMode"] != (0 if profile == "calc-always" else 1):
            raise ValueError("Explicit calculation policy was not verified")
        sheet = parts["xl/worksheets/sheet1.xml"]
        pdf = inspect_native(folder, item) if native else None
        checked.append({"Source": name, "ProfileStyle": profile, "SourceSha256": item["SourceSha256"],
                        "SnapshotSha256": item["SnapshotSha256"], "ExportedDateProperties": dict(properties.attrib) if properties is not None else {},
                        "InheritedColumns": [dict(column.attrib) for column in sheet.findall(".//" + NS + "col") if column.get("style") is not None],
                        "InheritedRows": [dict(row.attrib) for row in sheet.findall(".//" + NS + "row") if row.get("s") is not None],
                        "StoredDates": item["StoredDates"], "Cells": observed, "PdfComparison": pdf})
    result = {"Mode": report["Mode"], "ExpectedCachesMatch": all(cell["Matches"] for item in checked for cell in item["Cells"]),
              "Results": checked, "Scope": "Authored same-document before/copy/after native exports." if native else "Separate command-line workbook copies only."}
    result["Scope"] += " No repaired rendering, production guard, arbitrary-document isolation or complete formatting interpretation."
    if native:
        result["PdfUnchanged"] = all(item["PdfComparison"]["Unchanged"] for item in checked)
        result["DateFidelityPassed"] = all(item["PdfComparison"]["DatesMatch"] for item in checked)
    with (root / "independent-date-snapshots.json").open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2)
    print(json.dumps({"ExpectedCachesMatch": result["ExpectedCachesMatch"], "Cases": [
        {"Source": item["Source"], "ProfileStyle": item["ProfileStyle"], "StoredDates": item["StoredDates"]} for item in checked]}, indent=2))


def inspect_native(folder, item):
    texts, renders, pixels, observations = [], [], [], []
    for label in ("before", "after"):
        evidence = item[label.title()]
        if dates.sha(dates.read(folder / (label + ".pdf"))) != evidence["Sha256"]:
            raise ValueError("Changed native PDF")
        text = json.loads(dates.read(folder / label / "extracted-text.json"))
        render = json.loads(dates.read(folder / label / "render.json"))
        if text != evidence["Text"] or render != evidence["Render"] or len(text) != 1 or len(render["pages"]) != 1:
            raise ValueError("Changed native text/render evidence")
        page = render["pages"][0]
        if abs(page["widthPoints"] - 612) > 0.1 or abs(page["heightPoints"] - 792) > 0.1 or not (1 <= page["width"] <= 4096 and 1 <= page["height"] <= 4096) or page["stride"] != page["width"] * 4:
            raise ValueError("Changed authored page geometry")
        raw = dates.read(folder / label / "page-1.bgra")
        if len(raw) != page["stride"] * page["height"]:
            raise ValueError("Invalid native rendered buffer")
        matches = re.findall(r"R(0[1-7])\s+(.*?)(?=R0[1-7]|$)", text[0], re.S)
        if len(matches) != 7 or len({row for row, _ in matches}) != 7 or "OUTSIDE" in text[0] or "HIDDEN" in text[0]:
            raise ValueError("Changed authored date text")
        values = [1, 59, 60, 61, 40729, 40729.5, 1.5]
        parsed = []
        for row, value in sorted(matches):
            index = int(row) - 1
            serial = 40729 if item["ProfileStyle"] == "calc-never" and index < 4 else values[index]
            expected = dates.expected(serial, "1904" in item["Source"], "2" if index == 5 else "3" if index == 6 else "1")
            actual = re.sub(r"\s+", " ", value).strip()
            parsed.append({"Row": index + 1, "ExpectedExcelDisplay": expected, "Observed": actual, "Matches": expected == actual})
        if parsed != item[label.title() + "Dates"]:
            raise ValueError("Changed date display observations")
        texts.append(text); renders.append(page); pixels.append(raw); observations.append(parsed)
    unchanged = texts[0] == texts[1] and pixels[0] == pixels[1] and all(renders[0][key] == renders[1][key] for key in ("width", "height", "stride", "widthPoints", "heightPoints"))
    if not unchanged or not item["PdfUnchanged"]:
        raise ValueError("Native copy-save changed PDF output")
    expected_early = 3 if item["ProfileStyle"] == "calc-always" and "1904" not in item["Source"] else 0
    stored = item["StoredDates"]
    if not item["StoredDatesMatch"] or not stored["Complete"] or stored["EarlyDateCells"] != expected_early or stored["DateFormulaCells"] != 6:
        raise ValueError("Changed native snapshot inspection")
    return {"Unchanged": unchanged, "PixelSha256": dates.sha(pixels[0]), "DatesMatch": all(row["Matches"] for row in observations[0]), "Dates": observations[0]}


if __name__ == "__main__":
    main()
