Video Audio Source Kit
======================

This is a local review kit for the restricted Context Suite audio candidate.
It contains original source archives and the authored build recipes. It does
not contain private application code or grant a license to Context Suite code.
The owner has not yet selected the public build scripts' redistribution terms.
Do not publish this kit as an approved release.


Rebuild
-------

Extract the kit to a short writable Windows path with working short-path names
(for example, `C:\AudioReview`). Deep scratch paths can exceed MSVC FileTracker's
path limit during CMake's dependency checks.
The existing build environment must provide Python 3.14, Git for Windows Bash,
Visual Studio 2026 C++ x64 Build Tools with MSVC 14.51.36231, CMake/CTest and
Windows SDK 10.0.26100.0. This kit neither installs nor redistributes those tools.
Tool licensing/installation eligibility is a separate owner responsibility.

From the extracted kit root, run:

```powershell
.\tools\audio-engine\Rebuild-AudioSource.ps1 -VideoAudioInputs
```

The command verifies the eight retained archives, builds/tests Opus, Ogg/Vorbis,
stable LAME, GNU make, NASM and zlib, then builds the restricted FFmpeg candidate
and checks its native diagnostics, imports and security metadata. Each build
uses a new directory under this kit's `.codex-temp`. No downloads, installation,
Explorer commands or production staging run. Original archives stay unchanged;
five included patches apply only to newly extracted FFmpeg source. The video
option adds Matroska/WebM demuxing and the audio timestamp-reset filter to the
existing MOV and repaired audio
selection. Completed paths, input/output hashes and logs remain in each directory.

The source manifest also records two historical research inputs: the supplier
recipe and alpha LAME SVN export. They are not required by this selected build
and are not in this kit. Use `-BuildInputsOnly -VerifyOnly` when running the source
verification command directly. The legacy alpha `-Dependency Lame` experiment
requires its separately retained research inputs and is outside this kit.

This reconstructs the recipe and produces a newly identified candidate; it does
not promise identical binary bytes. FFmpeg embeds the configure paths, and PE
timestamps/build metadata can differ. The application admits exact reviewed
binary identities, so a rebuilt engine is not automatically accepted by the
private application. Resolve modified-library use and corresponding-source
delivery requirements before release. The standalone rebuilt executables remain
available for local inspection and testing.


Source and runtime relationship
-------------------------------

FFmpeg uses shared avcodec, avfilter, avformat, avutil and swresample libraries.
Opus, Ogg/Vorbis, stable LAME and zlib are built as static inputs to those DLLs.
GNU make and NASM are build tools only. The build disables GPL/nonfree/version3
selection, network, devices, ffplay and motion-video encoders/decoders; PNG/JPEG decoding supports
embedded artwork. Required shared filter/parser infrastructure remains enabled.
MOV and Matroska/WebM demuxing permits selected audio extraction from MP4, MOV
and WebM. Application admission still gates exact streams; this engine selection
does not admit MKV or M4V customer actions.
The exact configuration, source inventories and runtime hashes accompany the
original candidate's separate evidence bundle.

All original archive contents and upstream notices are preserved. Repository
wrappers control configuration, compatibility flags and generated files outside
the upstream trees. The included patches repair WMA Pro terminal overlap and
transmitted channel-matrix precision, Standard terminal reservoir/overlap, Voice
packet caching, and timestamp-free raw PCM transport. Their exact identities and
intermediate source hashes accompany the original candidate evidence. The private
application uses Windows Voice instead of FFmpeg Voice because separate waveform
fidelity failures remain; enabling that native decoder is not customer admission. Root notices and collected
source-comment notices are engineering inventory, not complete legal clearance.
The source archives retain the full file-specific terms, including files outside
the selected runtime build. Product/source terms, notice delivery, Microsoft
runtime distribution and the release review remain separate.


This kit covers the seven FFmpeg runtime files. The separately authored Windows
Voice provider is not included, and no Windows codec DLL is redistributed. Its
source/build and deployment verification remain a separate packaging requirement.
