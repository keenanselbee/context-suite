"""Check actual Form calls separately from unused inherited XObject resources."""
import argparse
import hashlib
import importlib.util
import io
import json
import pathlib

import pymupdf
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, DecodedStreamObject, DictionaryObject, NameObject, NumberObject
from reportlab.pdfgen.canvas import Canvas

spec = importlib.util.spec_from_file_location('named_colors', pathlib.Path(__file__).with_name('Test-PdfDarkNamedColors.py'))
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)


def fixture(nested=False, cycle=False, missing=False, own=False):
    raw = io.BytesIO()
    canvas = Canvas(raw, pagesize=(240, 180), invariant=1)
    canvas.drawString(20, 150, 'INHERITED RESOURCES')
    canvas.save()
    writer = PdfWriter(clone_from=PdfReader(io.BytesIO(raw.getvalue())))
    page = writer.pages[0]
    resources = page['/Resources']
    def form(content):
        stream = DecodedStreamObject()
        stream.set_data(content)
        stream.update({NameObject('/Type'): NameObject('/XObject'),
                       NameObject('/Subtype'): NameObject('/Form'),
                       NameObject('/BBox'): ArrayObject([NumberObject(v) for v in (0, 0, 120, 100)])})
        return writer._add_object(stream)
    child = form(b'0 g BT /F1 12 Tf 10 25 Td (CHILD) Tj ET')
    code = b'0 g BT /F1 12 Tf 10 60 Td (FORM) Tj ET'
    if nested:
        code += b' /Child Do'
    if cycle:
        code += b' /Form Do'
    if missing:
        code += b' /Missing Do'
    parent = form(code)
    objects = DictionaryObject({NameObject('/Form'): parent, NameObject('/Child'): child})
    resources[NameObject('/XObject')] = objects
    if own:
        parent.get_object()[NameObject('/Resources')] = DictionaryObject({
            NameObject('/Font'): resources['/Font'],
            NameObject('/XObject'): DictionaryObject({NameObject('/Child'): child})})
    content = DecodedStreamObject()
    content.set_data(page.get_contents().get_data() + b' q 1 0 0 1 20 40 cm /Form Do Q')
    page[NameObject('/Contents')] = writer._add_object(content)
    out = io.BytesIO()
    writer.write(out)
    return out.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--engine', required=True)
    parser.add_argument('--scratch', required=True)
    args = parser.parse_args()
    root = pathlib.Path(args.scratch).resolve()
    repository = pathlib.Path(__file__).resolve().parents[2]
    if root.exists() or not root.is_relative_to(repository / '.codex-temp'):
        raise ValueError('Use a fresh repository-local scratch directory')
    root.mkdir(parents=True)
    engine = pathlib.Path(args.engine).resolve(strict=True)
    rows = []
    for label, options in [('unused-self', {}), ('nested-child', {'nested': True}),
                           ('own-unused-self', {'own': True}), ('own-nested-child', {'own': True, 'nested': True}),
                           ('actual-cycle', {'cycle': True}), ('missing-call', {'missing': True})]:
        source = fixture(**options)
        (root / (label + '-source.pdf')).write_bytes(source)
        for mode in ('--inspect', '--convert'):
            code, facts, output = helpers.invoke(engine, root, source, mode)
            if options.get('cycle') or options.get('missing'):
                assert code in (3, 4, 5) and not output, (label, mode, code, facts)
            else:
                assert code == 0, (label, mode, code, facts)
                if mode == '--convert':
                    (root / (label + '-dark.pdf')).write_bytes(output)
                    with pymupdf.open(stream=source, filetype='pdf') as before, pymupdf.open(stream=output, filetype='pdf') as after:
                        assert before[0].get_text() == after[0].get_text(), label
                        left = [(s['font'], s['size'], s['chars']) for s in before[0].get_texttrace()]
                        right = [(s['font'], s['size'], s['chars']) for s in after[0].get_texttrace()]
                        assert left == right, label
                        after[0].get_pixmap().save(root / (label + '.png'))
                    repeat, detail, _ = helpers.invoke(engine, root, output, '--inspect')
                    assert repeat == 0 and detail['changedPages'] == 0, (label, repeat, detail)
            rows.append(dict(case=label, mode=mode, passed=True))
    (root / 'report.json').write_text(json.dumps(dict(engineSha256=hashlib.sha256(engine.read_bytes()).hexdigest(), results=rows), indent=2), encoding='utf-8')
    print(f'Passed {len(rows)} inherited-resource checks.')


if __name__ == '__main__':
    main()
