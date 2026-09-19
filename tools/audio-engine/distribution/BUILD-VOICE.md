Windows Voice Provider Source Kit
================================

This local review kit contains the authored Windows Voice host and its build
recipe. It contains no private application source or Microsoft codec DLLs.
Public/source distribution terms and release approval remain separate decisions.

Extract the kit to a writable Windows directory. Use Python 3.14, Visual Studio
2026 C++ x64 Build Tools, MSVC 14.51.36231 and Windows SDK 10.0.26100.0 already
installed on the build machine. From the extracted root, run:

```powershell
python -B tools/audio-engine/Build-WindowsVoice.py
```

The command creates a fresh directory under this kit's `.codex-temp`, compiles
the host with warnings treated as errors and reviews x64 PE hardening and exact
imports. It records source, recipe, executable and tool identities. No downloads,
installation, registration, playback or network request is performed.

The host uses the Windows Media Foundation Voice codec at runtime. That system
component must be available; there is no fallback to FFmpeg Voice. The host is
built with MSVC `/MT`, which statically links Microsoft C/C++ runtime code.
Tool/runtime eligibility and applicable distribution terms require review before
release. Windows codec DLLs must not be copied into this kit or payload.

The application admits the separately tested exact helper identity. A rebuild
does not automatically change that identity or promise identical binary bytes.
The host's path mode supports disposable generated-fixture verification;
application operation uses inherited handles under its parent-owned process job.
The source kit alone does not certify audio fidelity, process recovery or
availability on other Windows installations. See the companion provider notice.
