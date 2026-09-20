"""Write a local image review worksheet bound to sixteen verified outputs."""

import hashlib
import html
import json
from pathlib import Path
from urllib.parse import quote


def write_page(pack):
    report_path = pack / "results.json"; report = json.loads(report_path.read_text())
    cases = {scene + "-" + codec + "-" + target for scene in ("photograph", "orientation", "transparency", "gradient")
             for codec in ("heic", "avif") for target in ("png", "jpeg")}
    rows = report["Comparisons"]
    if not report["AutomatedPassed"] or len(rows) != 16 or {item["Case"] for item in rows} != cases:
        raise ValueError("A complete sixteen-output image pack is required")

    def media(path, expected):
        path = Path(path).resolve(strict=True); relative = path.relative_to(pack.resolve()).as_posix()
        if hashlib.sha256(path.read_bytes()).hexdigest().upper() != expected: raise ValueError("Review media identity changed")
        return quote(relative)

    cards = []
    for row in rows:
        source = media(row["Input"], row["InputSha256"]); reference = media(row["Reference"], row["ReferenceSha256"])
        output = media(row["Output"], row["OutputSha256"]); name = html.escape(row["Case"])
        cards.append(f'''<section data-case="{name}"><h2>{name.replace('-', ' / ')}</h2>
<p>{row["Width"]} × {row["Height"]} · {row["BitDepth"]}-bit output · Background: {html.escape(row["Matte"])}</p>
<p><a href="{source}" target="_blank" rel="noopener">Original HEIF input</a> · <a href="{reference}" target="_blank" rel="noopener">Full-size reference</a> · <a href="{output}" target="_blank" rel="noopener">Full-size output</a></p>
<div class="pair"><figure><figcaption>Expected appearance</figcaption><img src="{reference}" alt="Reference for {name}" loading="lazy"></figure>
<figure><figcaption>Converted output</figcaption><img src="{output}" alt="Output for {name}" loading="lazy"></figure></div>
<label>Visual comparison<select name="review"><option>Not reviewed</option><option>No issue seen</option><option>Issue seen</option></select></label>
<label>Notes: color, direction, transparency, banding or fine detail<textarea name="notes" rows="2"></textarea></label></section>''')
    identity = json.dumps({"reportSha256": hashlib.sha256(report_path.read_bytes()).hexdigest().upper(),
        "comparisons": [{key: item[key] for key in ("Case", "InputSha256", "ReferenceSha256", "OutputSha256")} for item in rows]}).replace("<", "\\u003c")
    document = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Context Suite image review</title><style>
:root{color-scheme:light dark;font:16px/1.5 system-ui,sans-serif}body{max-width:1500px;margin:auto;padding:24px}
section{border-top:2px solid #888;margin-top:32px;padding-top:12px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}
figure{margin:0;min-width:0}figcaption{font-weight:600;margin-bottom:8px}img{display:block;width:100%;height:auto;
background:repeating-conic-gradient(#aaa 0% 25%,#eee 0% 50%) 0/20px 20px}a{color:light-dark(#0647a5,#9bc8ff)}
label{display:block;margin-top:12px}input,select,textarea,button{font:inherit;padding:8px}input,textarea{box-sizing:border-box;width:100%}
textarea{resize:vertical}.notice{border:1px solid #888;padding:16px}button{margin-top:24px;cursor:pointer}
@media(max-width:700px){.pair{grid-template-columns:1fr}}
</style><h1>Context Suite image review</h1>
<p class="notice"><strong>Automated preparation passed. Human visual acceptance is still open.</strong>
Compare all sixteen outputs at full size. Side-by-side previews may hide fine detail when scaled down.</p>
<ul><li>Photograph: compare color, texture and edges. JPEG is lossy; PNG should retain source precision.</li>
<li>Orientation: the stored upward arrow rotates clockwise and should point right. Compare colored corners and dimensions.</li>
<li>Transparency: PNG should show the checkerboard through the alpha ramp; JPEG should match the explicitly selected white background.</li>
<li>Gradient: inspect dark and bright ramps for banding. The PNG output is 16-bit; browser/display precision may limit what you can see.</li></ul>
<p>Use your intended image viewer for full-size inspection. Record its version, monitor, scaling and color/HDR settings.
Download notes before closing; nothing is uploaded or automatically saved.</p>
<p>Photograph: Kodak Image 3, hats, Don Cochran, Bahamas. Source/provenance files are retained with this local pack; these are not shipping assets.</p>
<label>Viewer, display and settings<input id="setup"></label>''' + "".join(cards) + '''
<button id="download">Download review notes</button><p id="saved" role="status"></p><script>
document.getElementById('download').addEventListener('click',()=>{
const results=[...document.querySelectorAll('[data-case]')].map(row=>({case:row.dataset.case,review:row.querySelector('[name=review]').value,notes:row.querySelector('[name=notes]').value}));
const content={scope:'Sixteen generated HEIF comparisons; not complete launch acceptance',pack:''' + identity + ''',setup:document.getElementById('setup').value,results};
const url=URL.createObjectURL(new Blob([JSON.stringify(content,null,2)],{type:'application/json'}));
const link=document.createElement('a');link.href=url;link.download='context-suite-image-review.json';link.click();
setTimeout(()=>URL.revokeObjectURL(url),1000);document.getElementById('saved').textContent='Review notes download requested.';
});</script></html>'''
    (pack / "review.html").write_text(document, encoding="utf-8")


if __name__ == "__main__":
    import sys
    write_page(Path(sys.argv[1]).resolve(strict=True))
