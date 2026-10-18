"""Create an offline, two-pane readability gallery from existing PDF review runs.

The input directories are outputs of Review-PdfDarkDocument.py. This tool only
creates a fresh HTML gallery under the repository's .codex-temp directory; it
does not alter the PDFs or their rendered page images.
"""

import argparse
import html
import json
import os
import pathlib
from urllib.parse import quote


def relative_url(target, gallery):
    relative = pathlib.Path(os.path.relpath(target, gallery)).as_posix()
    return quote(relative, safe="/.-_")


def image_url(review, page, kind, gallery):
    path = review / f"page-{page:02}-{kind}.png"
    if not path.is_file():
        raise ValueError(f"Missing review image: {path}")
    return relative_url(path, gallery)


def review_pages(review):
    report = review / "report.json"
    if not report.is_file():
        raise ValueError(f"Missing review report: {report}")
    data = json.loads(report.read_text(encoding="utf-8"))
    if data.get("exitCode") != 0 or not data.get("structuralChecksPassed"):
        raise ValueError(f"Review did not pass its structural checks: {report}")
    return {entry["page"] for entry in data["pages"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", required=True, help="Current Review-PdfDarkDocument.py output")
    parser.add_argument("--previous", help="Optional earlier review output")
    parser.add_argument("--scratch", required=True, help="Fresh gallery directory under .codex-temp")
    parser.add_argument("--pages", default="14,19,37,44", help="Comma-separated one-based page numbers")
    parser.add_argument("--render-scale", type=int, choices=(2, 3, 4), default=4,
                        help="PDF render pixels per point; 2 reuses existing review images")
    args = parser.parse_args()

    repository = pathlib.Path(__file__).resolve().parents[2]
    scratch_base = (repository / ".codex-temp").resolve()
    gallery = pathlib.Path(args.scratch).resolve()
    if gallery.exists() or not gallery.is_relative_to(scratch_base) or gallery == scratch_base:
        parser.error("--scratch must name a fresh directory inside this repository's .codex-temp")
    current = pathlib.Path(args.current).resolve(strict=True)
    previous = pathlib.Path(args.previous).resolve(strict=True) if args.previous else None
    if not current.is_dir() or (previous and not previous.is_dir()):
        parser.error("Review inputs must be directories")
    try:
        pages = list(dict.fromkeys(int(item.strip()) for item in args.pages.split(",")))
    except ValueError:
        parser.error("--pages must contain comma-separated positive integers")
    if not pages or any(page < 1 for page in pages):
        parser.error("--pages must contain comma-separated positive integers")

    current_pages = review_pages(current)
    previous_pages = review_pages(previous) if previous else set()
    if any(page not in current_pages or (previous and page not in previous_pages) for page in pages):
        parser.error("Every selected page must exist in each supplied review report")
    for filename in ("index.html", "reading-copy.pdf", "report.json"):
        if not (current / filename).is_file():
            parser.error(f"Current review is missing {filename}")

    rendered = {}
    if args.render_scale != 2:
        import pymupdf
        report = json.loads((current / "report.json").read_text(encoding="utf-8"))
        sources = {"improved": current / "reading-copy.pdf",
                   "original": pathlib.Path(report["originals"][0]["path"]),
                   "reference": pathlib.Path(report["originals"][1]["path"])}
        if previous:
            sources["previous"] = previous / "reading-copy.pdf"
        for path in sources.values():
            if not path.is_file():
                parser.error(f"Missing PDF for high-resolution review: {path}")
        gallery.mkdir(parents=True)
        for kind, path in sources.items():
            with pymupdf.open(path) as document:
                for page in pages:
                    destination = gallery / f"page-{page:02}-{kind}.png"
                    document[page - 1].get_pixmap(matrix=pymupdf.Matrix(args.render_scale, args.render_scale),
                                                alpha=False).save(destination)
                    rendered[page, kind] = relative_url(destination, gallery)

    cards = []
    for page in pages:
        versions = {
            "improved": image_url(current, page, "converted", gallery),
            "reference": image_url(current, page, "reference", gallery),
            "original": image_url(current, page, "original", gallery),
        }
        if previous:
            versions["previous"] = image_url(previous, page, "converted", gallery)
        if rendered:
            versions = {kind: rendered[page, kind] for kind in versions}
        image_data = " ".join(
            f'data-{kind}="{html.escape(url, quote=True)}"'
            for kind, url in versions.items()
        )
        cards.append(f'''<section class="page" id="page-{page}" aria-labelledby="heading-{page}">
  <h2 id="heading-{page}">Page {page}</h2>
  <div class="pair">
    <figure><figcaption>Left: <span class="left-name">Improved</span></figcaption>
      <div class="viewport" tabindex="0" aria-label="Page {page}, left image; scroll to pan">
        <img class="left-image" {image_data} src="{html.escape(versions['improved'], quote=True)}" alt="Page {page}, improved PDF render" loading="lazy">
      </div></figure>
    <figure><figcaption>Right: <span class="right-name">Reference</span></figcaption>
      <div class="viewport" tabindex="0" aria-label="Page {page}, right image; scroll to pan">
        <img class="right-image" {image_data} src="{html.escape(versions['reference'], quote=True)}" alt="Page {page}, reference PDF render" loading="lazy">
      </div></figure>
  </div>
</section>''')

    previous_option = '<option value="previous">Previous</option>' if previous else ""
    current_review = relative_url(current / "index.html", gallery)
    current_pdf = relative_url(current / "reading-copy.pdf", gallery)
    current_report = relative_url(current / "report.json", gallery)
    previous_link = (f' · <a href="{html.escape(relative_url(previous / "index.html", gallery), quote=True)}">Previous all-page review</a>'
                     if previous and (previous / "index.html").is_file() else "")
    page_links = " · ".join(f'<a href="#page-{page}">Page {page}</a>' for page in pages)
    page_count = len(current_pages)
    markup = f'''<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="icon" href="data:,">
<title>PDF dark mode readability comparison</title>
<style>
  :root {{ color-scheme: dark; font: 16px/1.5 system-ui, sans-serif; background: #15181d; color: #f0eee8; }}
  body {{ margin: 0 auto; padding: 24px; max-width: 1800px; }}
  h1 {{ margin: 0 0 8px; font-size: clamp(1.5rem, 3vw, 2rem); }}
  h2 {{ font-size: 1.2rem; margin: 0 0 12px; }}
  p {{ max-width: 80ch; }}
  a {{ color: #a9d1ff; }}
  a:hover {{ color: #d3e7ff; }}
  :focus-visible {{ outline: 3px solid #f9c663; outline-offset: 3px; }}
  .status {{ border-left: 4px solid #f9c663; padding: 8px 12px; background: #29271f; }}
  .controls {{ display: flex; flex-wrap: wrap; align-items: end; gap: 12px 24px; padding: 16px; background: #252b34; border-radius: 8px; margin: 20px 0; }}
  .controls label {{ display: grid; gap: 4px; font-weight: 600; }}
  select {{ font: inherit; color: #f0eee8; background: #171b21; border: 1px solid #8692a0; border-radius: 4px; min-height: 42px; padding: 4px 10px; }}
  .hint {{ width: 100%; margin: 0; color: #d7dde5; }}
  nav {{ margin: 16px 0 24px; }}
  .page {{ margin: 32px 0 48px; scroll-margin-top: 12px; }}
  .pair {{ display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }}
  figure {{ margin: 0; min-width: 0; }}
  figcaption {{ font-weight: 600; margin-bottom: 6px; }}
  .viewport {{ height: min(72vh, 850px); overflow: auto; background: #0c0e12; border: 1px solid #697582; border-radius: 3px; scrollbar-color: #9aaac0 #252b34; cursor: grab; touch-action: pan-x pan-y; }}
  .viewport.dragging {{ cursor: grabbing; }}
  .viewport img {{ display: block; max-width: none; height: auto; user-select: none; -webkit-user-drag: none; }}
  .viewport img.fit {{ width: 100%; }}
  @media (max-width: 850px) {{ body {{ padding: 16px; }} .pair {{ grid-template-columns: 1fr; }} .viewport {{ height: min(65vh, 700px); }} }}
  .viewport.fit-pane {{ height: auto; }}
  body.single .pair {{ grid-template-columns: 1fr; }}
  body.single .pair figure:nth-child(2) {{ display: none; }}
</style>
</head>
<body>
<h1>PDF dark mode readability comparison</h1>
<p class="status" role="status">Source-only review. This improved conversion is not packaged or installed.</p>
<p>Compare two versions of the same page at reading size, then enlarge and pan both panes together to inspect small labels, arrows, and diagram contrast.</p>
<p><a href="{html.escape(current_review, quote=True)}">All-page review ({page_count} pages)</a> ·
<a href="{html.escape(current_pdf, quote=True)}">Open improved PDF</a> ·
<a href="{html.escape(current_report, quote=True)}">Structural review report</a>{previous_link}</p>
<div class="controls" role="group" aria-label="Comparison controls">
  <label for="layout">View
    <select id="layout"><option value="compare">Two-pane comparison</option><option value="single">Single page</option></select>
  </label>
  <label for="left-version">Left version
    <select id="left-version"><option value="improved" selected>Improved</option><option value="reference">Reference</option><option value="original">Original</option>{previous_option}</select>
  </label>
  <label for="right-version">Right version
    <select id="right-version"><option value="reference" selected>Reference</option><option value="improved">Improved</option><option value="original">Original</option>{previous_option}</select>
  </label>
  <label for="zoom">Zoom, both panes
    <select id="zoom"><option value="fit" selected>Fit to pane</option><option value="100">100% reading size</option><option value="150">150%</option><option value="200">200%</option></select>
  </label>
  <p class="hint">Use scrollbars, touch, arrow keys while a pane is focused, or drag inside a pane to pan. The paired pane follows.</p>
</div>
<nav aria-label="Selected pages">{page_links}</nav>
{''.join(cards)}
<script>
(() => {{
  const labels = {{ improved: 'Improved', reference: 'Reference', original: 'Original', previous: 'Previous' }};
  const left = document.getElementById('left-version');
  const right = document.getElementById('right-version');
  const zoom = document.getElementById('zoom');
  document.getElementById('layout').addEventListener('change', event => {{
    document.body.classList.toggle('single', event.target.value === 'single');
  }});

  function sizeImage(image) {{
    image.parentElement.classList.toggle('fit-pane', zoom.value === 'fit');
    if (zoom.value === 'fit') {{
      image.classList.add('fit');
      image.style.width = '';
    }} else if (image.naturalWidth) {{
      image.classList.remove('fit');
      image.style.width = (image.naturalWidth * Number(zoom.value) / {args.render_scale * 100}) + 'px';
    }}
  }}

  function updateSide(side, version) {{
    document.querySelectorAll('.' + side + '-image').forEach(image => {{
      image.src = image.dataset[version];
      image.alt = 'Page ' + image.closest('.page').querySelector('h2').textContent.split(' ')[1] + ', ' + labels[version].toLowerCase() + ' PDF render';
      image.closest('figure').querySelector('.' + side + '-name').textContent = labels[version];
      sizeImage(image);
    }});
  }}
  left.addEventListener('change', () => updateSide('left', left.value));
  right.addEventListener('change', () => updateSide('right', right.value));
  zoom.addEventListener('change', () => {{
    document.querySelectorAll('.viewport img').forEach(sizeImage);
  }});
  document.querySelectorAll('.viewport img').forEach(image => {{
    image.addEventListener('load', () => sizeImage(image));
    sizeImage(image);
  }});

  document.querySelectorAll('.pair').forEach(pair => {{
    const panes = [...pair.querySelectorAll('.viewport')];
    panes.forEach((pane, index) => {{
      const other = panes[1 - index];
      pane.addEventListener('scroll', () => {{
        const x = pane.scrollLeft / Math.max(1, pane.scrollWidth - pane.clientWidth);
        const y = pane.scrollTop / Math.max(1, pane.scrollHeight - pane.clientHeight);
        const targetX = x * Math.max(0, other.scrollWidth - other.clientWidth);
        const targetY = y * Math.max(0, other.scrollHeight - other.clientHeight);
        if (Math.abs(other.scrollLeft - targetX) > 1) other.scrollLeft = targetX;
        if (Math.abs(other.scrollTop - targetY) > 1) other.scrollTop = targetY;
      }});
      let start = null;
      pane.addEventListener('pointerdown', event => {{
        if (event.pointerType !== 'mouse' || event.button !== 0) return;
        start = {{ x: event.clientX, y: event.clientY, left: pane.scrollLeft, top: pane.scrollTop }};
        pane.setPointerCapture(event.pointerId);
        pane.classList.add('dragging');
      }});
      pane.addEventListener('pointermove', event => {{
        if (!start) return;
        pane.scrollLeft = start.left + start.x - event.clientX;
        pane.scrollTop = start.top + start.y - event.clientY;
      }});
      const endDrag = () => {{ start = null; pane.classList.remove('dragging'); }};
      pane.addEventListener('pointerup', endDrag);
      pane.addEventListener('pointercancel', endDrag);
      pane.addEventListener('lostpointercapture', endDrag);
    }});
  }});
}})();
</script>
</body>
</html>
'''
    gallery.mkdir(parents=True, exist_ok=bool(rendered))
    destination = gallery / "index.html"
    destination.write_text(markup, encoding="utf-8")
    print(json.dumps({"gallery": str(destination), "pages": pages, "versions": 4 if previous else 3}))


if __name__ == "__main__":
    main()
