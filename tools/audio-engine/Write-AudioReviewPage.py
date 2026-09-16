"""Write a local-only listening worksheet from a verified generated review pack."""

import hashlib
import html
import json
from pathlib import Path
from urllib.parse import quote


def write_page(pack):
    report = json.loads((pack / "results.json").read_text())
    if not report["AutomatedPassed"] or len(report["Sources"]) != 3 or len(report["Comparisons"]) != 15:
        raise ValueError("A complete automated review pack is required.")
    def media(path, expected):
        path = Path(path).resolve(strict=True)
        relative = path.relative_to(pack.resolve()).as_posix()
        with path.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest().upper() != expected:
                raise ValueError("Review media differs from its verified output.")
        return quote(relative)
    descriptions = {
        "music": "Generated melody, bass and percussion. Listen for smeared attacks, noisy tails and changes in stereo placement.",
        "transients": "Deliberate soft noise impacts every half-second. Placement switches every three seconds; high tones appear from 3–6 seconds, then the final noise fades.",
        "speech": "Local Windows synthetic speech. Compare consonants, S sounds, pauses, and the first and last words. The reference voice is synthetic."
    }
    recipes = {"Flac": "FLAC · level 8", "Mp3": "MP3 · VBR quality 2", "M4a": "AAC-LC · 192 kb/s",
               "Vorbis": "Vorbis · quality 5", "Opus": "Opus · 160 kb/s VBR, 48 kHz"}
    sections = []
    for source in report["Sources"]:
        name = source["Name"]
        reference = media(source["Path"], source["Sha256"])
        rows = []
        for item in report["Comparisons"]:
            if item["Clip"] != name:
                continue
            encoded = media(item["Encoded"], item["EncodedSha256"])
            decoded = media(item["DecodedWave"], item["DecodedSha256"])
            key = name + "-" + item["Target"]
            notes = " ".join(item["Plan"]["Notices"])
            rows.append(f'''<article data-case="{html.escape(key)}">
<h3>{html.escape(recipes[item["Target"]])}</h3>
<p><a href="{encoded}" target="_blank" rel="noopener">Open encoded file</a> · {item["SampleRate"]:,} Hz · {item["Channels"]} channel(s)</p>
<label for="audio-{key}">Decoded WAV comparison</label>
<audio id="audio-{key}" controls preload="none" src="{decoded}"></audio>
<p class="hint">{html.escape("Exact PCM round-trip verified." if item["ExactPcm"] else notes)}</p>
<div class="ratings"><label>Listening<select name="listening"><option>Not reviewed</option><option>No issue heard</option><option>Issue heard</option></select></label>
<label>Encoded-file playback<select name="playback"><option>Not tested</option><option>Opened and played</option><option>Problem</option></select></label></div>
<label>Notes and timestamps<textarea name="notes" rows="2"></textarea></label></article>''')
        sections.append(f'''<section><h2>{html.escape(name.title())}</h2><p>{html.escape(descriptions[name])}</p>
<p>{source["SampleRate"]:,} Hz · {source["Channels"]} channel(s) · {source["SampleBits"]}-bit PCM · {source["Seconds"]:.2f} seconds</p>
<div class="reference"><label for="reference-{name}">Original WAV reference</label>
<audio id="reference-{name}" controls preload="none" src="{reference}"></audio></div>
<div class="grid">{"".join(rows)}</div></section>''')
    document = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Context Suite audio review</title><style>
:root{color-scheme:light dark;font:16px/1.5 system-ui,sans-serif}body{max-width:1100px;margin:auto;padding:24px}
h1{font-size:2rem}h2{border-bottom:2px solid #888;padding-bottom:8px}h3{margin-top:0;font-size:1.05rem}
a{color:light-dark(#0647a5,#9bc8ff)}.notice,.reference,article{border:1px solid #888;border-radius:8px;padding:16px;margin:16px 0}
.notice{background:light-dark(#f1f6ff,#243244)}.reference{background:light-dark(#edf6ed,#253525)}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,290px),1fr));gap:16px}article{margin:0;min-width:0}
audio{display:block;width:100%;margin:8px 0}label{display:block}input,select,textarea,button{font:inherit;padding:8px}
select,textarea{box-sizing:border-box;width:100%}textarea{resize:vertical}.ratings{display:grid;gap:8px;margin:12px 0}
.hint{font-size:.9rem}button{cursor:pointer;margin:12px 0}section{margin-top:36px}#saved{display:inline;margin-left:12px}
</style><h1>Context Suite audio review</h1>
<p class="notice"><strong>Automated checks passed. Human listening and player compatibility are still unverified.</strong>
These generated clips are a first review set; representative real recordings and intended devices still need review.</p>
<ol><li>Use a comfortable volume and keep the same volume for each comparison.</li>
<li>Play the original reference, then a decoded WAV. Listen to the start, middle and end.</li>
<li>For any issue, note the clip, codec, timestamp and what differs from the reference.</li>
<li>Check encoded files in your usual player separately. The links open another tab; if your browser plays or downloads the file, open the pack's encoded file in your player instead.</li>
<li>Download your notes before closing this page. Nothing is uploaded or saved automatically.</li></ol>
<label>Player and headphones/speakers used<input id="setup" placeholder="For example: VLC, wired headphones" style="width:100%;box-sizing:border-box"></label>
''' + "".join(sections) + '''
<button id="download">Download review notes</button><p id="saved" role="status"></p>
<script>
document.addEventListener('play',e=>{if(e.target.tagName==='AUDIO')document.querySelectorAll('audio').forEach(a=>{if(a!==e.target)a.pause()})},true);
document.getElementById('download').addEventListener('click',()=>{
const results=[...document.querySelectorAll('[data-case]')].map(row=>({case:row.dataset.case,listening:row.querySelector('[name=listening]').value,
playback:row.querySelector('[name=playback]').value,notes:row.querySelector('[name=notes]').value}));
const content={scope:'Generated Context Suite audio clips; not complete launch acceptance',setup:document.getElementById('setup').value,results};
const url=URL.createObjectURL(new Blob([JSON.stringify(content,null,2)],{type:'application/json'}));
const link=document.createElement('a');link.href=url;link.download='context-suite-audio-review.json';link.click();
setTimeout(()=>URL.revokeObjectURL(url),1000);document.getElementById('saved').textContent='Review notes download requested.';
});
</script></html>'''
    (pack / "review.html").write_text(document, encoding="utf-8")
    (pack / "READ-ME.txt").write_text(
        "Open review.html in a browser. Start with the original WAV, then compare each decoded WAV at the same volume.\n"
        "Check encoded files in your usual player separately. Record issue timestamps and download the review notes.\n"
        "Nothing is played automatically or uploaded. Notes are not saved until you download them.\n"
        "These clips are generated tests, not complete real-recording, player/device or launch acceptance.\n", encoding="utf-8")


if __name__ == "__main__":
    import sys
    write_page(Path(sys.argv[1]).resolve(strict=True))
