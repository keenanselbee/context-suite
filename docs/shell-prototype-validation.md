Shell Prototype Validation
==========================

Status: shell-layout experiment complete. Automated activation contracts pass,
and manual Windows 11 observation confirms the renamed three-package layout.


Validated Evidence
------------------

The following checks passed on Windows 11 build 26100 on September 5–6, 2026:

| Requirement | Evidence | Result |
| --- | --- | --- |
| Native x64 shell and host build | `tools/Build.ps1` in Debug and Release | Passed |
| Three independent verb registrations | Manifest contract checks three package identities, applications, verbs, and CLSIDs | Passed |
| Installed sparse package health | All three package statuses are `Ok` | Passed |
| Packaged COM activation | All three registered CLSIDs activate through Windows COM | Passed |
| Host rejects unknown schemas | Negative host contract | Passed |
| Analyze receives one multi-file request | Three-item `IShellItemArray` COM invocation and host acknowledgement | Passed |
| Convert receives one multi-file batch | Three-item `IShellItemArray` COM invocation and host acknowledgement | Passed |
| Optimize receives one multi-file batch | Three-item `IShellItemArray` COM invocation and host acknowledgement | Passed |
| Analyze is topmost and direct; Convert and Optimize have isolated child menus | Real Windows 11 Explorer screenshots | Passed |
| Invoked UI reports the complete Explorer selection | Real Windows 11 Explorer observation | Pending |

The automated activation contract includes filenames with spaces and non-ASCII
characters. It invokes the actual shell DLL, not only the host parser.


Environment Finding
-------------------

The September 5 manual screenshots show the classic Explorer context menu: they
lack the Windows 11 command icon row and the **Show more options** entry. The
profile contains the known classic-menu override at:

```text
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
```

The sparse package remains installed with status `Ok`, and all three packaged
COM classes still activate. This observation does not prove or disprove the
three-peer layout because packaged `IExplorerCommand` extensions are intended
for the modern menu being bypassed. Temporarily removing the exact override and
restarting Explorer is required to perform the intended visual test. Preserve a
backup and restore the user's preference after the experiment unless they choose
to keep the modern menu.

After temporarily disabling the override and restarting Explorer, the modern
menu exposed a crucial package-attribution behavior. The first prototype put
three application identities and three verbs in one sparse package. Windows
rendered three attributed roots, but opening any root showed all three package
verbs—**Convert**, the former **Inspect**, and **Optimize**—rather than isolating
the owning application's verb. Windows also used each application's
`uap:VisualElements/@DisplayName` for the root label.

The prototype now gives each root its own sparse identity package while sharing
the same DLL and host. This preserves one suite implementation while matching
Windows' package-level grouping boundary. The application display names are the
required short labels **Analyze**, **Convert**, and **Optimize**. Analyze invokes
the host directly; Convert and Optimize expose isolated child menus. Explorer
ignored internal verb prefixes and ordered independent roots by their displayed
labels in the observed environment. The Analyze, Convert, Optimize names now
produce the intended order without unsupported prefixes.


Manual Explorer Smoke Test
--------------------------

Run:

```powershell
.\tools\Open-ShellPrototypeTestFolder.ps1
```

Then:

1. Select `alpha.png`, `beta.jpg`, and `gamma.webp` together.
2. Right-click the selection in File Explorer.
3. Confirm **Analyze** is the top entry, has its distinct icon, and has no submenu
   arrow.
4. Invoke **Analyze** and confirm the host opens directly, reports
   `Selected files: 3`, and lists all three filenames.
5. Confirm **Convert** and **Optimize** retain their distinct icons and isolated
   submenu arrows.
6. Invoke **Convert** > **Choose format...** and confirm the host reports
   `Selected files: 3` and lists all three filenames.
7. Repeat step 6 with **Optimize** > **Choose preset...**.

The rendered layout is recorded. Capturing the three host dialogs remains an
optional manual confirmation; the native COM contract already verifies that
each operation hands one complete three-file selection to the host.
