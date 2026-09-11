"""Require the exact reviewed component selection before compiling FFmpeg."""
import hashlib
import json
import pathlib
import re
import sys


EXPECTED = {
    "DECODER": "AAC FLAC MJPEG MP3FLOAT OPUS PCM_F32LE PCM_F64LE PCM_S16LE PCM_S24LE PCM_S32LE PCM_U8 PNG VORBIS",
    "ENCODER": "AAC FLAC LIBMP3LAME LIBOPUS LIBVORBIS PCM_F32LE PCM_F64LE PCM_S16LE PCM_S24LE PCM_S32LE PCM_U8",
    "PARSER": "AAC AC3 FLAC MJPEG MPEGAUDIO OPUS PNG VORBIS",
    "DEMUXER": "FLAC MOV MP3 OGG WAV",
    "MUXER": "FLAC IPOD MOV MP3 OGG OPUS PCM_F64LE WAV",
    "PROTOCOL": "FD FILE PIPE",
    "FILTER": "AFORMAT ANULL ARESAMPLE ATRIM CROP FORMAT HFLIP NULL ROTATE TRANSPOSE TRIM VFLIP",
    "BSF": "AAC_ADTSTOASC VP9_SUPERFRAME",
    "HWACCEL": "", "INDEV": "", "OUTDEV": "",
}


def main():
    directory = pathlib.Path(sys.argv[1]).resolve()
    scratch = pathlib.Path(__file__).resolve().parents[2] / ".codex-temp"
    if not directory.is_relative_to(scratch) or directory == scratch:
        raise ValueError("Configuration checks must remain in repository scratch")
    paths = [directory / name for name in ("config.h", "config_components.h")]
    text = "\n".join(path.read_text() for path in paths)
    macros = dict(re.findall(r"^#define\s+(\w+)\s+([01])$", text, re.MULTILINE))
    components = dict(re.findall(r"^#define\s+(\w+)\s+([01])$", paths[1].read_text(), re.MULTILINE))
    actual = {}
    for category, names in EXPECTED.items():
        suffix = "_" + category
        enabled = {name.removeprefix("CONFIG_").removesuffix(suffix) for name, value in components.items()
                   if name.startswith("CONFIG_") and name.endswith(suffix) and value == "1"}
        if enabled != set(names.split()):
            raise ValueError(f"Unexpected {category}: missing {sorted(set(names.split()) - enabled)}, extra {sorted(enabled - set(names.split()))}")
        actual[category.lower()] = sorted(enabled)
    for name in ("ARCH_X86_64", "HAVE_X86ASM", "CONFIG_SHARED", "CONFIG_FFMPEG", "CONFIG_FFPROBE",
                 "CONFIG_AVCODEC", "CONFIG_AVFORMAT", "CONFIG_AVFILTER", "CONFIG_AVUTIL", "CONFIG_SWRESAMPLE",
                 "CONFIG_LIBMP3LAME", "CONFIG_LIBOPUS", "CONFIG_LIBVORBIS", "CONFIG_ZLIB"):
        if macros.get(name) != "1":
            raise ValueError("Required configuration missing: " + name)
    for name in ("ARCH_X86_32", "CONFIG_NETWORK", "CONFIG_AVDEVICE", "CONFIG_SWSCALE", "CONFIG_FFPLAY",
                 "CONFIG_GPL", "CONFIG_NONFREE", "CONFIG_STATIC"):
        if macros.get(name) != "0":
            raise ValueError("Forbidden or unknown configuration: " + name)
    result = {"status": "Reviewed configuration only; runtime acceptance pending", "components": actual,
              "headers": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in paths},
              "checkerSha256": hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()}
    (directory / "configuration.json").write_text(json.dumps(result, indent=2))
    print("Exact audio/artwork component selection and x64/shared/local-I/O gates passed.")


if __name__ == "__main__":
    main()
