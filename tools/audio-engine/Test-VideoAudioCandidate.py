"""Author bounded video fixtures and compare audio extraction with an independent pinned FFmpeg."""
import array
import hashlib
import importlib.util
import json
import math
import os
import pathlib
import subprocess
import struct
import sys
import uuid
import wave
import zlib


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_candidate", TOOLS / "Test-AudioCandidate.py")
audio_candidate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio_candidate)


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest().upper()


def run(arguments, cwd, allow_failure=False):
    environment = dict(os.environ)
    environment.pop("FFREPORT", None)
    environment["AV_LOG_FORCE_NOCOLOR"] = "1"
    result = subprocess.run([str(part) for part in arguments], cwd=cwd, env=environment,
                            capture_output=True, text=True, timeout=30)
    if not allow_failure and result.returncode:
        raise ValueError(f"Native command failed ({result.returncode}): {arguments}\n{result.stderr[-3000:]}")
    return result


def probe(executable, source, cwd):
    result = run([executable, "-v", "error", "-probesize", "65536", "-analyzeduration", "1000000",
                  "-show_entries", "stream=index,codec_type,codec_name,sample_rate,channels,channel_layout,start_time,duration:stream_tags=language,title,handler_name:format=format_name,duration",
                  "-of", "json", source], cwd)
    return json.loads(result.stdout)


def write_tone(path, rate, channels, frequency):
    with wave.open(str(path), "wb") as output:
        output.setnchannels(channels)
        output.setsampwidth(2)
        output.setframerate(rate)
        samples = bytearray()
        for position in range(rate):
            value = round(12000 * math.sin(2 * math.pi * frequency * position / rate))
            samples.extend(value.to_bytes(2, "little", signed=True) * channels)
        output.writeframes(samples)


def decode(executable, source, index, cwd, label):
    target = cwd / (label + "-" + source.name.replace(".", "-") + f"-{index}.f64le")
    if target.exists():
        raise ValueError("Decoded output path was reused: " + str(target))
    run([executable, "-v", "error", "-nostdin", "-i", source, "-map", f"0:a:{index}", "-vn",
         "-c:a", "pcm_f64le", "-f", "f64le", target], cwd)
    if not target.is_file() or target.stat().st_size == 0 or target.stat().st_size % 8 or target.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("Decoded output is absent or outside its sample bounds: " + str(target))
    samples = array.array("d")
    samples.frombytes(target.read_bytes())
    if sys.byteorder != "little":
        samples.byteswap()
    return samples, target


def main():
    if len(sys.argv) != 3:
        raise ValueError("Use <completed video-audio FFmpeg workspace> <pinned independent evaluation bin>")
    candidate = pathlib.Path(sys.argv[1]).resolve()
    independent = pathlib.Path(sys.argv[2]).resolve()
    record = json.loads((candidate / "build-result.json").read_text())
    if record.get("exitCode") != 0 or not record.get("videoAudioInputs"):
        raise ValueError("A completed opt-in video-audio build is required")
    inventory = json.loads((candidate / "runtime-files.json").read_text())
    if {file["path"] for file in inventory} != {path.name for path in (candidate / "bin").iterdir()}:
        raise ValueError("Candidate runtime membership differs")
    for file in inventory:
        path = candidate / "bin" / file["path"]
        if path.stat().st_size != file["bytes"] or digest(path) != file["sha256"]:
            raise ValueError("Candidate runtime differs")
    independent_identity = audio_candidate.verify_evaluation(independent)
    evidence = candidate / ("video-audio-smoke-" + uuid.uuid4().hex)
    evidence.mkdir()
    print("Video audio smoke evidence: " + str(evidence), flush=True)
    first = evidence / "first.wav"
    second = evidence / "second.wav"
    write_tone(first, 48000, 2, 440)
    write_tone(second, 44100, 1, 660)
    generator = independent / "ffmpeg.exe"
    ffmpeg = candidate / "bin/ffmpeg.exe"
    ffprobe = candidate / "bin/ffprobe.exe"
    independent_probe = independent / "ffprobe.exe"
    observations = []
    for suffix in ("mp4", "mov", "webm", "mkv"):
        fixture = evidence / ("two-tracks." + suffix)
        video_options = (["-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8"]
                         if suffix == "webm" else ["-c:v", "mpeg4"])
        audio_options = (["-c:a:0", "libopus", "-c:a:1", "libvorbis"]
                         if suffix == "webm" else ["-c:a", "aac"])
        run([generator, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=48x32:r=5:d=1.25",
             "-i", first, "-itsoffset", "0.125", "-i", second, "-map", "0:v:0", "-map", "1:a:0", "-map", "2:a:0",
             *video_options, *audio_options, "-metadata:s:a:0", "language=eng", "-metadata:s:a:0", "title=First",
             "-metadata:s:a:0", "handler_name=First", "-metadata:s:a:1", "language=fra",
             "-metadata:s:a:1", "title=Second", "-metadata:s:a:1", "handler_name=Second", "-y", fixture], evidence)
        candidate_facts = probe(ffprobe, fixture, evidence)
        reference_facts = probe(independent_probe, fixture, evidence)
        streams = candidate_facts["streams"]
        audio = [stream for stream in streams if stream["codec_type"] == "audio"]
        if len(audio) != 2 or len([stream for stream in streams if stream["codec_type"] == "video"]) != 1:
            raise ValueError("Video and two distinct audio tracks were not inventoried: " + suffix)
        if [stream["tags"].get("language") for stream in audio] != ["eng", "fra"]:
            raise ValueError("Audio language inventory differs: " + suffix)
        if [int(stream["channels"]) for stream in audio] != [2, 1] or [int(stream["sample_rate"]) for stream in audio] != [48000, 44100]:
            raise ValueError("Audio rate or layout inventory differs: " + suffix)
        name_key = "title" if suffix in ("webm", "mkv") else "handler_name"
        if [stream["tags"].get(name_key) for stream in audio] != ["First", "Second"]:
            raise ValueError("Audio track names differ: " + suffix)
        if [stream["codec_name"] for stream in audio] != [stream["codec_name"] for stream in reference_facts["streams"] if stream["codec_type"] == "audio"]:
            raise ValueError("Candidate codec inventory differs: " + suffix)
        if not float(audio[1].get("start_time", "0")) > float(audio[0].get("start_time", "0")):
            raise ValueError("Second-track offset was not retained: " + suffix)
        tracks = []
        decoded_tracks = []
        for index in (0, 1):
            actual, actual_path = decode(ffmpeg, fixture, index, evidence, "candidate")
            expected, expected_path = decode(generator, fixture, index, evidence, "independent")
            if len(actual) != len(expected) or not actual or max(abs(a - b) for a, b in zip(actual, expected)) > 0.000001:
                raise ValueError(f"Decoded samples differ: {suffix} track {index}")
            decoded_tracks.append(actual)
            tracks.append({"index": index, "samples": len(actual), "candidatePcmSha256": digest(actual_path),
                           "independentPcmSha256": digest(expected_path), "startTime": audio[index].get("start_time"),
                           "codec": audio[index]["codec_name"], "sampleRate": audio[index]["sample_rate"],
                           "channels": audio[index]["channels"], "channelLayout": audio[index].get("channel_layout"),
                           "tags": audio[index].get("tags", {})})
        outputs = []
        for target in ("wav", "mp3", "flac"):
            output = evidence / (suffix + "-first." + target)
            run([ffmpeg, "-v", "error", "-nostdin", "-i", fixture, "-map", "0:a:0", "-vn", "-c:a",
                 {"wav": "pcm_s16le", "mp3": "libmp3lame", "flac": "flac"}[target], output], evidence)
            facts = probe(ffprobe, output, evidence)
            if len(facts["streams"]) != 1 or facts["streams"][0]["codec_type"] != "audio":
                raise ValueError("Extraction output has unexpected streams: " + str(output))
            outputs.append({"path": output.name, "sha256": digest(output), "bytes": output.stat().st_size,
                            "codec": facts["streams"][0]["codec_name"]})
        filtered_target = {"mp4": "wav", "mov": "mp3", "webm": "flac", "mkv": "wav"}[suffix]
        selected_index = 1 if suffix == "webm" else 0
        channels = int(audio[selected_index]["channels"])
        filtered_frames = len(decoded_tracks[selected_index]) // channels // 2
        filtered_output = evidence / (suffix + f"-filtered-{selected_index}." + filtered_target)
        if filtered_output.exists():
            raise ValueError("Filtered output path was reused")
        run([ffmpeg, "-v", "error", "-nostdin", "-i", fixture, "-map", f"0:a:{selected_index}", "-vn",
             "-af", f"atrim=end_sample={filtered_frames},asetpts=PTS-STARTPTS", "-c:a",
             {"wav": "pcm_s16le", "mp3": "libmp3lame", "flac": "flac"}[filtered_target], filtered_output], evidence)
        if not filtered_output.is_file() or filtered_output.stat().st_size == 0:
            raise ValueError("Filtered output is absent or empty")
        filtered_facts = probe(ffprobe, filtered_output, evidence)
        if len(filtered_facts["streams"]) != 1 or filtered_facts["streams"][0]["codec_type"] != "audio":
            raise ValueError("Filtered output has unexpected streams")
        filtered_samples, filtered_pcm = decode(ffmpeg, filtered_output, 0, evidence, "filtered")
        if len(filtered_samples) != filtered_frames * channels or not any(abs(sample) > 0.00001 for sample in filtered_samples):
            raise ValueError("Filtered output sample extent or signal differs")
        observations.append({"fixture": fixture.name, "sha256": digest(fixture), "bytes": fixture.stat().st_size,
                             "format": candidate_facts["format"]["format_name"], "tracks": tracks, "outputs": outputs,
                             "filtered": {"path": filtered_output.name, "sha256": digest(filtered_output),
                                          "codec": filtered_facts["streams"][0]["codec_name"],
                                          "frames": filtered_frames, "samples": len(filtered_samples),
                                          "decodedPcmSha256": digest(filtered_pcm)}})
    codec_variants = []
    for suffix, codec, expected_codec in (("mp4", "mp3", "mp3"), ("mov", "pcm_s16le", "pcm_s16le"),
                                          ("mp4", "flac", "flac"), ("mkv", "mp3", "mp3"),
                                          ("mkv", "pcm_s16le", "pcm_s16le"), ("mkv", "flac", "flac"),
                                          ("mkv", "libopus", "opus"), ("mkv", "libvorbis", "vorbis")):
        fixture = evidence / (codec + "-in-" + suffix + "." + suffix)
        run([generator, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=48x32:r=5:d=1",
             "-i", first, "-map", "0:v:0", "-map", "1:a:0", "-c:v", "mpeg4", "-c:a", codec,
             *(["-strict", "-2"] if codec == "flac" else []), fixture], evidence)
        facts = probe(ffprobe, fixture, evidence)
        tracks = [stream for stream in facts["streams"] if stream["codec_type"] == "audio"]
        if len(tracks) != 1 or tracks[0]["codec_name"] != expected_codec:
            raise ValueError("Additional video audio codec was not inventoried: " + codec)
        actual, actual_path = decode(ffmpeg, fixture, 0, evidence, "candidate")
        expected, expected_path = decode(generator, fixture, 0, evidence, "independent")
        if len(actual) != len(expected) or not actual or max(abs(a - b) for a, b in zip(actual, expected)) > 0.000001:
            raise ValueError("Additional video audio codec differs from independent decode: " + codec)
        codec_variants.append({"fixture": fixture.name, "sha256": digest(fixture), "codec": codec,
                               "samples": len(actual), "candidatePcmSha256": digest(actual_path),
                               "independentPcmSha256": digest(expected_path)})
    long_recordings = []
    long_tone = evidence / "long-tone.wav"
    write_tone(long_tone, 8000, 1, 330)
    for suffix, codec in (("webm", "libvorbis"), ("mkv", "pcm_s16le")):
        fixture = evidence / ("long-recording." + suffix)
        video_options = (["-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8"]
                         if suffix == "webm" else ["-c:v", "mpeg4"])
        run([generator, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=48x32:r=1:d=600",
             "-stream_loop", "599", "-i", long_tone, "-map", "0:v:0", "-map", "1:a:0",
             *video_options, "-c:a", codec, "-t", "600", fixture], evidence)
        actual, actual_path = decode(ffmpeg, fixture, 0, evidence, "candidate")
        expected, expected_path = decode(generator, fixture, 0, evidence, "independent")
        if len(actual) != 4800000 or len(expected) != len(actual) or max(abs(a-b) for a,b in zip(actual, expected)) > .000001:
            raise ValueError("Ten-minute extraction differs: " + suffix)
        long_recordings.append({"fixture": fixture.name, "sha256": digest(fixture), "frames": len(actual),
                                "candidatePcmSha256": digest(actual_path), "independentPcmSha256": digest(expected_path)})
    surround = evidence / "surround.mov"
    tones = "|".join(f"0.1*sin(2*PI*{frequency}*t)" for frequency in (220, 330, 440, 550, 660, 770))
    run([generator, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=blue:s=48x32:r=5:d=1",
         "-f", "lavfi", "-i", f"aevalsrc={tones}:s=48000:d=1:c=5.1", "-map", "0:v:0", "-map", "1:a:0",
         "-c:v", "mpeg4", "-c:a", "pcm_s16le", surround], evidence)
    cover = evidence / "cover.png"
    def png_chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    cover.write_bytes(b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 16, 16, 8, 2, 0, 0, 0)) +
                     png_chunk(b"IDAT", zlib.compress((b"\0" + b"\x20\x60\xa0" * 16) * 16)) + png_chunk(b"IEND", b""))
    chapters = evidence / "chapters.txt"
    chapters.write_text(";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Opening\n")
    metadata_fixture = evidence / "chapter-artwork.mp4"
    run([generator, "-v", "error", "-nostdin", "-i", evidence / "two-tracks.mp4", "-i", cover,
         "-f", "ffmetadata", "-i", chapters, "-map", "0", "-map", "1:v:0", "-c", "copy",
         "-disposition:v:1", "attached_pic", "-map_chapters", "2", metadata_fixture], evidence)
    no_audio_results = []
    for suffix in ("mp4", "mov", "webm", "mkv"):
        no_audio = evidence / ("no-audio." + suffix)
        video_options = (["-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8"]
                         if suffix == "webm" else ["-c:v", "mpeg4"])
        run([generator, "-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=red:s=48x32:r=5:d=1",
             "-map", "0:v:0", *video_options, no_audio], evidence)
        if any(stream["codec_type"] == "audio" for stream in probe(ffprobe, no_audio, evidence)["streams"]):
            raise ValueError("Video-only fixture was misidentified as audio: " + suffix)
        refused_output = evidence / ("should-not-exist-" + suffix + ".wav")
        refusal = run([ffmpeg, "-v", "error", "-nostdin", "-i", no_audio, "-map", "0:a:0", "-vn",
                       refused_output], evidence, allow_failure=True)
        if refusal.returncode == 0 or refused_output.exists():
            raise ValueError("Video-only input did not refuse audio extraction: " + suffix)
        no_audio_results.append({"path": no_audio.name, "sha256": digest(no_audio),
                                 "refusalExitCode": refusal.returncode})
    result = {"status": "Bounded video-audio engine smoke passed; application integration remains separate",
              "candidate": str(candidate), "candidateFiles": inventory, "independentFiles": independent_identity,
              "fixtures": observations, "codecVariants": codec_variants,
              "noAudio": no_audio_results, "longRecordings": long_recordings,
              "policyFixtures": [{"path": path.name, "sha256": digest(path)} for path in (surround, metadata_fixture)]}
    (evidence / "video-audio-smoke.json").write_text(json.dumps(result, indent=2))
    print("Passed four containers, two tracks, six audio codecs, independent decoded samples, twelve target outputs, four filtered outputs and four no-audio refusals.")


if __name__ == "__main__":
    main()
