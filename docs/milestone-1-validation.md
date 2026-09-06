Milestone 1 Foundation Validation
================================

Local verification: September 6, 2026. This covers the bounded production
foundation goal, not media processing or readiness for a paid release.

| Requirement | Evidence |
| --- | --- |
| WPF application, core, worker, private composition | Debug and Release production builds pass with no warnings; all projects appear in `ContextSuite.Production.slnx` |
| Complete selections and repeated activation | Native contracts cover all three commands and three-file handoff; managed framing covers 4,096 paths; real WPF process smoke accepts three successive three-file requests through the shipping entry point |
| One app per interactive session | First-instance pipe ownership, forwarding, duplicate IDs, and actual short-lived forwarding processes are tested |
| Bounded IPC and queue | Oversized/truncated frames, malformed clients, protocol version, peer checks, queue overflow, and slow acknowledgement readers are tested |
| On-demand worker and cancellation | Real private catalog connection, reuse, cancellation, graceful client disposal, crash recovery, and abrupt parent-exit cleanup are tested |
| No fake processing or shipping access bypass | Private catalog is empty; UI reports unsupported operations; source bytes remain unchanged; access states have no shipping provider |
| Public-only development | A copied public source tree without `proprietary/` passes component tests; both production projects reject a missing private implementation |
| Native shell preservation | Debug and Release prototype COM/manifest contracts pass; existing installed packages remain untouched |
| Repeatable commands and CI | Build/test/boundary scripts and public/private workflows are authored; their local commands pass; hosted workflows have not been published/run |

The complete managed integration run passes 50 contracts. The Windows process
smoke deliberately exercises the normal executable rather than a special demo
or permissive licensing mode. It caught an acknowledgement-loss race that was
fixed with a final client receipt before the server disconnects; a slow-reader
contract now covers that behavior explicitly.

Visual layout, keyboard/screen-reader UX, and installed Explorer-to-WPF smoke
remain manual checks: the computer-use helper was unavailable. These are not
represented as passing automated checks. Production installer registration and
media-engine execution remain later work. See [development](development.md) for
commands, protocol limits, temporary-fixture handling, and the private CI boundary.
