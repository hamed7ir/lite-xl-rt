# The Lite XL ARM32 installer

Source for `LiteXLSetup.exe` and `LiteXLUninstall.exe` — the per-user installer that ships with the
[ARM32 release](https://github.com/hamed7ir/lite-xl-rt/releases).

Both are **MSIL / AnyCPU, .NET Framework 4.0, WinForms**.

---

## Why it is built this way

**Not Inno Setup, not NSIS.** Both produce **native x86** executables. Windows RT is ARM with no x86
emulation, so an Inno or NSIS installer cannot run on the machine this targets — you would ship an
installer that works everywhere except the platform it is for.

Compiled `/platform:anycpu` this is MSIL, which the .NET runtime present on RT JITs to ARM. **One
binary installs on RT, on x86 and on x64.** The pattern is carried from the Varan and TelegArm
projects, which hit exactly this wall first.

**WinForms, never WPF.** WPF is not dependable on these RT images.

### Three constraints that are fatal if dropped

1. **`/platform:anycpu` only.** csc's *default* for `/target:winexe` is `anycpu32bitpreferred`,
   which sets the `32BITREQUIRED` flag. That is **ARM32-fatal** — the exe simply will not start on
   RT. It has to be stated, not assumed, and gate 1 reads the flag back out of the built file rather
   than trusting the switch.
2. **Nothing from `System.IO.Compression[.FileSystem]`.** Both are **.NET 4.5-only** and absent from
   some Windows RT images. This was a real field failure on a Spanish-locale RT device in a sibling
   project: *"could not load file or assembly System.IO.Compression.FileSystem"*. There is no way to
   install a newer .NET on RT. The payload here is a **directory**, not a zip, so the dependency
   never arises — and gate 2 asserts it has not crept back.
3. **Per-user only.** `%LOCALAPPDATA%` and `HKCU`, never elevation. Windows RT will not grant a
   self-signed app machine-wide rights, and asking for elevation it cannot use would just fail later
   and less clearly.

---

## Build

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Output goes to `installer\bin\`. Pass `-Out <dir>` to build elsewhere.

Needs `csc.exe` from **.NET Framework v4.0.30319** (in-box on Windows) and, for gate 1, `python`.

⚠ Builds are **not byte-reproducible**: csc embeds a timestamp and a fresh MVID unless
`/deterministic` is passed, so two builds of identical source differ. The gates verify *properties*
of the output, not its bytes.

### What the build gates

| Gate | Checks | Control |
|---|---|---|
| **1** | `cliheader.py` reads the PE + CLI headers: `ILONLY` set, **`32BITREQUIRED` clear** | an x86-only assembly is *also* COFF `0x014C`, so the machine word alone cannot tell them apart — that is why this reads the CLI flags |
| **2** | `System.IO.Compression*` absent from the referenced assemblies | **must-fail control:** `System.Windows.Forms` must come back *present* in the same read. A checker that reports "absent" for everything proves nothing |
| **3** | Every registry **write** is `Registry.CurrentUser` | `Registry.LocalMachine` is permitted only as `OpenSubKey` (read) — the VC++ runtime probe. Any HKLM write path fails the build |

---

## What it does

**Install** — one window, no wizard. Choose a folder (default `%LOCALAPPDATA%\Programs\Lite XL`),
tick Start menu shortcut / Desktop shortcut / right-click entry, press Install.

If the chosen folder cannot be written to, it says so **before starting** and refuses, rather than
copying half the files and failing. It finds that out by **writing and deleting a probe file**, not
by pattern-matching the path — a folder that merely looks system-ish but is writable should not be
refused, and one that looks fine but is not should not be accepted.

**Shortcuts** are created through `IShellLink` + `IPersistFile` COM interop, declared in
`Common.cs`. **Not `WScript.Shell`** — that is a Windows Script Host feature and can be absent or
disabled on a locked-down tablet.

**Uninstall** — `LiteXLUninstall.exe` is copied into the install directory and registered as the
Add/Remove Programs `UninstallString`. It replays `install-record.txt` and removes **exactly** what
that lists: files, both shortcuts, registry values and keys.

**There is no wildcard delete anywhere in this source and there must never be one.** If the record
is missing, uninstall removes nothing and says so — that is the safe failure. A registry key that
still holds something not in the record belongs to someone else and is left alone. A shortcut whose
`.lnk` no longer resolves into the install directory is the user's now and is left alone.

Removing the install directory itself is a **detached `cmd /c` retry loop**: a running executable
cannot delete itself, so it retries until this process exits and releases its own image. Deliberately
*not* `MoveFileEx(..., MOVEFILE_DELAY_UNTIL_REBOOT)`, which writes to `HKLM` and needs elevation.

### Command line

Both accept `/silent`. The setup also takes `/dir <path>`, `/nostartmenu`, `/nodesktop`, `/noverbs`.
Because these are `/target:winexe`, they have no console — silent mode writes its result to
`%TEMP%\litexl-setup-silent.log` and `%TEMP%\litexl-uninstall-silent.log`.

### It does not need the Visual C++ runtime

`lite-xl.exe` is linked with the **static CRT** (`/MT`). Measured on the shipped binary: 10 imported
DLLs, all of them OS DLLs, and **zero** of `VCRUNTIME140` / `MSVCP*` / `api-ms-win-crt-*` /
`ucrtbase`. The installer reports the VC++ runtime state as a grey informational line and **never
blocks on it** — a hard prerequisite check would refuse to install on machines where the editor
demonstrably runs, including the one this was built on, which has no VC++ 2015+ runtime registered
at all.

---

## Files

| | |
|---|---|
| `Common.cs` | shared: app identity, the write probe, `IShellLink` interop, the install record, the self-delete, the VC++ probe |
| `Setup.cs` | `LiteXLSetup.exe` — the install window and the install logic |
| `Uninstall.cs` | `LiteXLUninstall.exe` — the confirm/result window and the removal logic |
| `build.ps1` | builds both, then the three gates |
| `cliheader.py` | reads a managed PE's CLI header; gate 1's instrument |
