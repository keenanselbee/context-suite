"""Finite native SVG protocol and admission checks. Pass a freshly built executable."""

import base64
import json
import struct
import subprocess
import sys
import zlib


SVG_NS = 'xmlns="http://www.w3.org/2000/svg"'


def svg(body, width=4, height=4):
    return f'<svg {SVG_NS} width="{width}" height="{height}">{body}</svg>'.encode()


def run(exe, source, *args):
    process = subprocess.run([exe, *args], input=source, capture_output=True, timeout=15)
    assert len(process.stderr) <= 65536, "stderr bound"
    return process


def inspect(exe, source):
    process = run(exe, source, "inspect")
    assert process.returncode == 0, (process.returncode, process.stderr)
    return json.loads(process.stdout)


def render(exe, source, width, height):
    process = run(exe, source, "render", str(width), str(height))
    assert process.returncode == 0, (process.returncode, process.stderr)
    assert len(process.stdout) >= 4
    length = struct.unpack_from("<I", process.stdout)[0]
    assert length <= 32768
    header = json.loads(process.stdout[4:4 + length])
    pixels = process.stdout[4 + length:]
    assert len(pixels) == width * height * 4
    return header, pixels


def pixel(pixels, width, x=0, y=0):
    return pixels[(y * width + x) * 4:(y * width + x + 1) * 4]


def refuse(exe, source, expected):
    for args in [("inspect",), ("render", "4", "4")]:
        process = run(exe, source, *args)
        assert process.returncode == expected, (args, process.returncode, process.stderr)
        assert not process.stdout


def png_chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def png(red=255, extra=b""):
    return (b"\x89PNG\r\n\x1a\n" +
            png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0)) +
            extra + png_chunk(b"IDAT", zlib.compress(bytes([0, red, 0, 0, 255]))) +
            png_chunk(b"IEND", b""))


def main(exe):
    count = 0
    red = svg('<rect width="4" height="4" fill="red"/>')
    probe = inspect(exe, red)
    assert probe["Abi"] == 1 and probe["IntrinsicWidth"] == 4 and probe["IntrinsicHeight"] == 4
    assert probe["Width"] == 4 and probe["Height"] == 4
    assert probe["MaximumSide"] == 4096 and probe["MaximumPixels"] == 16000000
    assert len(probe["FontFingerprint"]) == 64 and len(probe["FontNotices"]) <= 32
    header, pixels = render(exe, red, 8, 8)
    assert header["FontFingerprint"] == probe["FontFingerprint"]
    assert pixel(pixels, 8, 7, 7) == b"\xff\x00\x00\xff"
    count += 1

    transparent = svg('<rect width="4" height="4" fill="red" opacity="0.5"/>')
    _, pixels = render(exe, transparent, 4, 4)
    assert pixel(pixels, 4)[:3] == b"\xff\x00\x00" and pixel(pixels, 4)[3] in (127, 128)
    count += 1

    css = svg('<style>rect { fill: rgb(255,0,0); }</style><rect width="4" height="4"/>')
    assert pixel(render(exe, css, 4, 4)[1], 4) == b"\xff\x00\x00\xff"
    inline = svg('<rect width="4" height="4" style="fill:red"/>')
    assert pixel(render(exe, inline, 4, 4)[1], 4) == b"\xff\x00\x00\xff"
    count += 2

    use = svg('<defs><rect id="shape" width="4" height="4" fill="red"/></defs><use href="#shape"/>')
    assert pixel(render(exe, use, 4, 4)[1], 4) == b"\xff\x00\x00\xff"
    count += 1
    xlink_use = svg('<defs><rect id="shape" width="4" height="4" fill="red"/></defs><use xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="#shape"/>')
    assert pixel(render(exe, xlink_use, 4, 4)[1], 4) == b"\xff\x00\x00\xff"
    count += 1

    gradient = svg('<defs><linearGradient id="g"><stop stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient></defs><rect width="4" height="4" fill="url(#g)"/>')
    left = pixel(render(exe, gradient, 4, 4)[1], 4)
    assert left[0] > left[2] and left[3] == 255
    count += 1

    radial = svg('<defs><radialGradient id="g" cx="0.5" cy="0.5" r="0.5" fx="0.25" fy="0.25" fr="0"><stop stop-color="red"/><stop offset="1" stop-color="blue"/></radialGradient></defs><rect width="4" height="4" fill="url(#g)"/>')
    assert pixel(render(exe, radial, 4, 4)[1], 4, 0, 0)[3] == 255
    count += 1
    focused = svg('<defs><radialGradient id="g" cx="0.5" cy="0.5" r="0.5" fx="0.2" fy="0.5"><stop stop-color="red"/><stop offset="1" stop-color="blue"/></radialGradient></defs><rect width="10" height="10" fill="url(#g)"/>', 10, 10)
    centered = svg('<defs><radialGradient id="g" cx="0.5" cy="0.5" r="0.5" fx="0.5" fy="0.5"><stop stop-color="red"/><stop offset="1" stop-color="blue"/></radialGradient></defs><rect width="10" height="10" fill="url(#g)"/>', 10, 10)
    focus_pixels = render(exe, focused, 10, 10)[1]
    center_pixels = render(exe, centered, 10, 10)[1]
    assert pixel(focus_pixels, 10, 2, 5)[0] > pixel(center_pixels, 10, 2, 5)[0] + 50
    assert pixel(focus_pixels, 10, 9, 5)[2] > 200
    count += 1

    clip = svg('<defs><clipPath id="c"><rect width="2" height="4"/></clipPath></defs><rect width="4" height="4" fill="red" clip-path="url(#c)"/>')
    pixels = render(exe, clip, 4, 4)[1]
    assert pixel(pixels, 4, 0, 0)[3] == 255 and pixel(pixels, 4, 3, 0) == b"\x00\x00\x00\x00"
    count += 1

    mask = svg('<defs><mask id="m"><rect width="2" height="4" fill="white"/></mask></defs><rect width="4" height="4" fill="red" mask="url(#m)"/>')
    pixels = render(exe, mask, 4, 4)[1]
    assert pixel(pixels, 4, 0, 0)[3] > 0 and pixel(pixels, 4, 3, 0)[3] == 0
    count += 1

    image = base64.b64encode(png()).decode()
    embedded = svg(f'<image width="4" height="4" href="data:image/png;base64,{image}"/>')
    assert pixel(render(exe, embedded, 4, 4)[1], 4)[0] > 200
    count += 1
    reused_image = svg(f'<defs><image id="i" width="4" height="4" href="data:image/png;base64,{image}"/></defs><use href="#i"/>')
    assert pixel(render(exe, reused_image, 4, 4)[1], 4)[0] > 200
    count += 1
    repeated_image = svg(f'<defs><image id="i" width="1" height="1" href="data:image/png;base64,{image}"/></defs><use href="#i" x="0"/><use href="#i" x="2"/>', 4, 1)
    repeated_pixels = render(exe, repeated_image, 4, 1)[1]
    assert [pixel(repeated_pixels, 4, x) for x in range(4)] == [
        b"\xff\x00\x00\xff", b"\x00\x00\x00\x00",
        b"\xff\x00\x00\xff", b"\x00\x00\x00\x00",
    ]
    count += 1
    masked_image = svg(f'<defs><mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="4" height="4"><image width="2" height="4" href="data:image/png;base64,{image}"/></mask></defs><rect width="4" height="4" fill="blue" mask="url(#m)"/>')
    masked_pixels = render(exe, masked_image, 4, 4)[1]
    assert 40 <= pixel(masked_pixels, 4, 0, 1)[3] <= 70
    assert pixel(masked_pixels, 4, 0, 1)[:3] == b"\x00\x00\xff"
    assert pixel(masked_pixels, 4, 3, 1) == b"\x00\x00\x00\x00"
    count += 1
    patterned_image = svg(f'<defs><pattern id="p" patternUnits="userSpaceOnUse" width="2" height="2"><image width="1" height="1" href="data:image/png;base64,{image}"/></pattern></defs><rect width="4" height="4" fill="url(#p)"/>')
    pattern_pixels = render(exe, patterned_image, 4, 4)[1]
    assert [pixel(pattern_pixels, 4, x) for x in range(4)] == [
        b"\xff\x00\x00\xff", b"\x00\x00\x00\x00",
        b"\xff\x00\x00\xff", b"\x00\x00\x00\x00",
    ]
    count += 1
    clipped_image = svg(f'<defs><clipPath id="c"><rect width="2" height="4"/></clipPath></defs><image width="4" height="4" href="data:image/png;base64,{image}" clip-path="url(#c)"/>')
    clipped_pixels = render(exe, clipped_image, 4, 4)[1]
    assert pixel(clipped_pixels, 4, 0) == b"\xff\x00\x00\xff"
    assert pixel(clipped_pixels, 4, 3) == b"\x00\x00\x00\x00"
    count += 1

    font = svg('<text x="0" y="16" font-family="Nonexistent Family" font-size="16">A</text>', 30, 20)
    header, pixels = render(exe, font, 30, 20)
    assert any("Nonexistent Family" in notice and "will use" in notice for notice in header["FontNotices"])
    assert any(pixels[i + 3] for i in range(0, len(pixels), 4))
    count += 1
    known_font = svg('<text x="0" y="16" font-family="Arial" font-size="16">A</text>', 30, 20)
    assert not inspect(exe, known_font)["FontNotices"]
    count += 1

    many_fonts = svg(''.join(f'<text x="0" y="{20 * (i + 1)}" font-family="Absent{i}" font-size="12">A</text>' for i in range(33)), 100, 700)
    refuse(exe, many_fonts, 4)
    count += 1

    huge = svg('<rect width="8000" height="6000" fill="red"/>', 8000, 6000)
    assert inspect(exe, huge)["Width"] == 8000
    assert render(exe, huge, 4, 3)[0]["MaximumSide"] == 4096
    count += 1

    filtered = svg('<filter id="f"><feGaussianBlur stdDeviation="1"/></filter><rect width="4" height="4" fill="red" filter="url(#f)"/>')
    assert inspect(exe, filtered)["MaximumSide"] == 1024
    assert render(exe, filtered, 4, 4)[0]["MaximumPixels"] == 1000000
    count += 1

    refusal_cases = [
        (b"<bad/>", 2),
        (b"\xff", 2),
        (b'<!DOCTYPE svg [<!ENTITY x "foo">]>' + red, 3),
        (svg('<script/>'), 3),
        (svg('<animate attributeName="x"/>'), 3),
        (svg('<image href="file:///c:/secret.png"/>'), 3),
        (svg('<rect width="4" height="4" fill="url(http://example.com/x)"/>'), 3),
        (svg('<style>@import url(x);</style>'), 3),
        (svg('<rect width="4" height="4" onclick="x"/>'), 3),
        (svg('<use href="#missing"/>'), 3),
        (svg('<use id="loop" href="#loop"/>'), 3),
        (svg('<defs><linearGradient id="a" href="#b"/><linearGradient id="b" href="#a"/></defs>'), 3),
        (svg('<defs><pattern id="p" width="1" height="1"><rect width="1" height="1" fill="url(#p)"/></pattern></defs>'), 3),
        (svg('<rect width="4" height="4" fill="url(#missing)"/>'), 3),
        (svg('<path d="M 0 0 L" fill="red"/>'), 2),
        (svg('<polyline points="0,0 1"/>'), 2),
        (svg('<rect width="4" height="4" transform="spin(3)"/>'), 2),
        (svg('<rect width="4" height="4" transform="scale(1e400)"/>'), 2),
        (svg('<rect width="4" height="4" fill="wut"/>'), 3),
        (svg(f'<defs><clipPath id="c"><image width="4" height="4" href="data:image/png;base64,{image}"/></clipPath></defs><rect width="4" height="4" fill="red" clip-path="url(#c)"/>'), 3),
        (svg('<filter id="f" filterUnits="userSpaceOnUse"><feGaussianBlur stdDeviation="1"/></filter>'), 3),
        (svg('<rect width="4" height="4" style="animation: spin 1s"/>'), 3),
        (svg(f'<image href="data:image/png;base64,{base64.b64encode(png(extra=png_chunk(b"iCCP", b"bad"))).decode()}"/>'), 3),
        (svg(f'<image href="data:image/png;base64,{base64.b64encode(b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 16, 6, 0, 0, 0)) + png_chunk(b"IDAT", zlib.compress(b"\x00" + b"\xff\xff\x00\x00\x00\x00\xff\xff")) + png_chunk(b"IEND", b"")).decode()}"/>'), 3),
        (red + b" " * 1_048_576, 4),
    ]
    for source, code in refusal_cases:
        refuse(exe, source, code)
        count += 1
    assert run(exe, red, "render", "0", "4").returncode == 4
    assert run(exe, red, "render", "4", "2").returncode == 2
    assert run(exe, red, "render", "4097", "4097").returncode == 4
    count += 3
    print(f"{count} native SVG checks passed")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: Test-SvgEngine.py <fresh ContextSuite.Svg.exe>")
    main(sys.argv[1])
