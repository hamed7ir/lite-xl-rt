# Lite XL 2.1.8 for Windows on ARM32 (Windows RT)

> This file documents the **ARM32 build only**. Upstream Lite XL's own README is
> [`README.md`](README.md), unchanged.

**[Download the release](https://github.com/hamed7ir/lite-xl-rt/releases)**

A build of the [Lite XL](https://lite-xl.com) text editor that runs on 32-bit ARM Windows —
Windows RT 8.0 and 8.1 on Surface RT / Tegra-class tablets, and Windows 10 ARM32.

Upstream does not ship an ARM32 Windows build, and until now one could not be produced with clang:
the compiler emitted the wrong frame value for `setjmp`, so Lua's error handling terminated the
process on the first recoverable error. That defect is fixed in the compiler used here. The fix is
written up as an LLVM report and **has not been filed**.

---

## What runs where

This table is what has been **measured**, not what is expected to work. The two rows are
**different packages** and are deliberately not combined.

| Package | Windows RT 8.0 | Windows RT 8.1 | Windows 10 ARM32 |
|---|---|---|---|
| **2.1.8 + SDL3** — what this release is | **measured**, both passes | **measured**, both passes | **measured**, both passes |
| 2.1.7 + SDL2 — an earlier build, superseded | measured | measured | measured |

**Measured** means: the editor was launched twice on the device by a harness that verifies all 375
shipped files by SHA-256 before running anything, then records its own work. On each device and
on both passes it was alive at three and ten seconds, a visible window belonging to it existed,
and it created its config directory.

| device | OS, from `RtlGetVersion` | hardware |
|---|---|---|
| Surface RT | 6.2.9200 — Windows RT 8.0 | Tegra 3, 4 cores, 1990 MB |
| Surface RT | 6.3.9600 — Windows RT 8.1 | Tegra 3 |
| Surface 2 | 10.0.15035 — Windows 10 ARM32 | Tegra 4 |

The 2.1.7 row is in the table because it is the evidence that the *compiler fix* travels across
all three OSes. It is not what you install.

The editor opens in about a second on the oldest hardware here.

---

## Install

1. Download the package and unzip it anywhere.
2. Run `LiteXLSetup.exe`.

It installs **per user**, into `%LOCALAPPDATA%\Programs\Lite XL ARM32`, and needs **no
administrator rights**. It adds an *"Open with Lite XL (ARM32)"* entry to the right-click menu for
files, folders, and folder backgrounds.

To remove it: **Settings → Apps**, or run `LiteXLSetup.exe /uninstall`. Uninstall removes exactly
what the install wrote — it keeps a record of every file and registry value it created and replays
that list. It does not delete anything it did not create.

**Requirements:** .NET Framework 4.0 for the installer only (present on Windows RT out of the box).
The editor itself needs no runtime — the C runtime is linked statically, so there is no
`VCRUNTIME140.dll` to find.

**The installer is not code-signed.** Windows will say so. There is no way around that short of a
certificate, and pretending otherwise would be worse.

You can also just run `lite-xl.exe` out of the folder without installing anything. The `data`
directory must stay next to it — the editor resolves its data path relative to its own location.

---

## The gear button

The sidebar has a gear/cog button. In stock Lite XL it opens your `init.lua` for editing. This
package includes the same add-on bundle the official Windows installer ships, which rebinds that
button to a graphical **Settings** tab.

**On ARM32 this has not been confirmed.** The one time the gear was pressed on an ARM32 device, it
was a build *without* the bundle, and it opened `init.lua` — correct for that build, and not an
answer for this one. The rebinding has been read in the add-on's own source and exercised on x86-64;
nobody has clicked it on an ARM32 device yet. It is the one thing left to confirm by hand.

If you get `init.lua`, that is useful information — please say so.

---

## What is in the package

- **Lite XL 2.1.8**, built for `armv7-pc-windows-msvc` (COFF machine `0x01C4`, Thumb-2).
- **SDL3 3.2.14**, linked statically. This is the first SDL3 build for 32-bit ARM Windows that we
  know of.
- The **add-on bundle** the official Windows installer ships: 105 plugins, 51 colour themes, and the
  37-file widget library. Every one of the 193 files is attributed to its upstream repository in
  `THIRD-PARTY-NOTICES`.
- Minimum OS in the PE headers is **6.0**, so the loader accepts it on Windows 8-era systems.

---

## Known limits

- **The gear/Settings behaviour on ARM32 is unknown** — see above. It is the one user-facing
  promise here that has not been exercised on the target.
- **`lpm`, the Lite XL plugin manager, has no ARM32 Windows build.** It is a native binary and
  upstream publishes it for x86-64 and ARM64 only. The Settings UI — including its Plugins tab —
  works without it; that was measured, not assumed. What you cannot do is install further plugins
  through the GUI. Adding `.lua` files to `data/plugins` by hand still works.
- **No OpenGL.** `opengl32.dll` is not present on Windows RT at all. Lite XL's default renderer
  draws through a software framebuffer, which is the path that was tested; this is not a
  regression, it is what the hardware offers.
- **The add-on bundle roughly doubles time-to-window.** It adds 105 plugins that the editor loads
  at startup. That was measured on the device, and the bundle was kept because the result is
  about a second on the oldest hardware supported here. If you want the difference back, delete
  what you do not use from `data/plugins`.
- **Not code-signed**, and there is no auto-update.

---

## Building it

clang-cl 18.1.8 with the setjmp fix, MSVC 14.16.27023 and Windows SDK 10.0.19041.0 (both pinned,
because newer MSVC has removed 32-bit ARM support outright), Meson for lite-xl, CMake for SDL3.
See **Toolchain** below.

The two changes to Lite XL itself are the two commits on this branch — nothing is edited in place.
SDL3 needed **no source changes at all**; its toolchain file, the exact configure invocation and
what was measured are at
[hamed7ir/SDL-rt](https://github.com/hamed7ir/SDL-rt/tree/rt-arm32/rt-arm32).

---

## Toolchain

**You do not need any of this to run the editor.** It is statically linked; nothing about the
compiler is required at runtime. This section matters only if you want to rebuild it.

### What built it

`clang-cl 18.1.8` with the rt patch series plus the ARM32 Windows `setjmp` fix — the
configuration called **rt1.2**. Measured, not asserted:

| | |
|---|---|
| compiler `clang-cl.exe` sha256 | `6ec8ee3f61a03ddf2b372988bbe2eac20b143fed6d25bd2a15ccf87856e6d999` |
| it reports itself as | `clang version 18.1.8-rt1 (rt12-D42-sponentry-arm32-uncommitted)` |
| recorded by the build | `"version": "18.1.8-rt1"` in `meson-info/intro-compilers.json` |
| the binary it produced | `lite-xl.exe` sha256 `7de87fcbeff0783f313e02fea671f2d8d8f2af3269149f5f8380f234c888761f` |
| qualifying measurement | **`sp-form=1`** — the fixed instruction sequence is present in the shipped binary |

(`sp-form` is read out of the binary's own bytes at the `setjmp` call site: the patched form is
`add r1, sp, #N`, the unpatched one `mov r1, fp`.)

### Why the toolchain is load-bearing, not incidental

**Without that one fix this binary crashes at the first Lua error** with `0xC0000028`
(`STATUS_BAD_STACK`). On Windows ARM32, clang gives `setjmp` the **frame pointer** where the
platform expects the **stack pointer at function entry**; the CRT stores that value in the
`jmp_buf`, and `longjmp` later hands it to `RtlUnwindEx` as the frame to unwind to. It matches no
real frame boundary, so the unwind is rejected and the process dies. Lua uses `setjmp`/`longjmp`
for every recoverable error, so the editor could not finish starting.

It is not a detail. It is the reason the editor runs at all.

### Where the toolchain lives

The rt toolchain is published at **<https://github.com/hamed7ir/llvm-rt1>**. The first release,
**rt1**, is there as tag `rt1-18.1.8`.

⚠ **rt1.2 — the configuration that built this — is not published yet.** Publication is planned,
and it will appear in that repository. There is no date, and this page will not give one.

### How to reproduce it without waiting

You do not need rt1.2 to build this. The fix is **one predicate** in
`clang/lib/CodeGen/CGBuiltin.cpp`, in the branch that chooses the second argument to
`_setjmpex`. Stock clang 18.1.8 tests for AArch64 only, so 32-bit Arm falls through to
`llvm.frameaddress(0)` instead of `llvm.sponentry`:

```diff
-    if (CGF.getTarget().getTriple().getArch() == llvm::Triple::aarch64) {
+    const llvm::Triple &T = CGF.getTarget().getTriple();
+    if (T.getArch() == llvm::Triple::aarch64 || T.isARM() || T.isThumb()) {
```

Apply that to a stock clang 18.1.8 and you can build this today. The full patch, with its
reasoning and its `clang/test/CodeGen/ms-setjmp.c` additions, is
`patches/0010-clang-ms-setjmp-sponentry-arm32.patch` in the llvm-rt tree.

### Upstream status

**The fix is not filed upstream.** It is written up as an LLVM report, and it has not been
submitted. Do not read anything here as saying it is in LLVM.

---

## Licences

Lite XL is MIT. SDL3 is zlib. FreeType is used under the **FreeType License (FTL)**, not GPLv2.
Lua is MIT, PCRE2 is BSD, the three bundled fonts are OFL 1.1. The 193 bundled add-ons are MIT, from
three Lite XL repositories, attributed file by file.

Full text and per-file attribution: **`THIRD-PARTY-NOTICES`**, included in the package.

---

## Credit

This port was **heavily AI-assisted and human-directed**. The direction, the device trips, the
hardware and every judgement call are the maintainer's; a large share of the code, the build
plumbing, the test harnesses and the write-ups were produced by an AI assistant working under that
direction. Saying "AI generated" would be wrong in both directions — nothing here was produced
unsupervised, and nothing here pretends to have been typed by hand.

Lite XL itself is by the Lite XL team, and before that `lite` by rxi.

**This is an unofficial build.** Please do not report its bugs to upstream Lite XL — report them
here.
