#!/usr/bin/env python3
"""Read a managed assembly's PE + CLI (COR20) header and say what it really is.

BATCH-LITEXL-14 section 4 requires an MSIL / AnyCPU assembly targeting .NET
Framework 4.0. `/platform:anycpu` is a request; this reads the bytes and checks.

The distinction that matters for this project: an x86-only assembly (ILONLY +
32BITREQUIRED) will NOT run on Windows RT's ARM32 CLR. AnyCPU is ILONLY with
32BITREQUIRED clear -- one binary that the ARM32 tablet and the x64 dev box can
both JIT. Both are COFF machine 0x014C, so the machine word alone cannot tell
them apart, which is exactly why this looks at the CLI flags too.

Exit 0 if the file is AnyCPU MSIL on a v4.0 runtime, 1 otherwise.
"""
import struct
import sys

# COR20 header Flags (ECMA-335 II.25.3.3.1)
ILONLY = 0x00000001
BIT32REQUIRED = 0x00000002
STRONGNAMESIGNED = 0x00000008
BIT32PREFERRED = 0x00020000


def rva_to_off(sections, rva):
    for name, vaddr, vsize, praw, rsize in sections:
        if vaddr <= rva < vaddr + max(vsize, rsize):
            return praw + (rva - vaddr)
    return None


def read(path):
    d = open(path, "rb").read()
    if d[:2] != b"MZ":
        return None, "not a PE (no MZ)"
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    if d[pe:pe + 4] != b"PE\0\0":
        return None, "not a PE (no PE signature)"
    machine, nsec = struct.unpack_from("<HH", d, pe + 4)
    optsize = struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", d, opt)[0]
    pe32plus = (magic == 0x20B)
    ddir = opt + (112 if pe32plus else 96)
    sec = pe + 24 + optsize
    sections = []
    for i in range(nsec):
        o = sec + i * 40
        nm = d[o:o + 8].rstrip(b"\0").decode("ascii", "replace")
        vsize, vaddr, rsize, praw = struct.unpack_from("<IIII", d, o + 8)
        sections.append((nm, vaddr, vsize, praw, rsize))

    # DataDirectory[14] = CLR Runtime Header
    clr_rva, clr_size = struct.unpack_from("<II", d, ddir + 14 * 8)
    info = {
        "machine": machine,
        "pe32plus": pe32plus,
        "managed": clr_rva != 0 and clr_size != 0,
    }
    if not info["managed"]:
        return info, "unmanaged PE -- no CLI header"

    off = rva_to_off(sections, clr_rva)
    if off is None:
        return info, "CLI header RVA does not map into any section"
    (cb, rt_major, rt_minor, md_rva, md_size, flags, entry) = struct.unpack_from(
        "<IHHIIII", d, off)
    info["rt_version"] = "%d.%d" % (rt_major, rt_minor)
    info["flags"] = flags

    mo = rva_to_off(sections, md_rva)
    info["metadata_version"] = "?"
    if mo is not None and d[mo:mo + 4] == b"BSJB":
        vlen = struct.unpack_from("<I", d, mo + 12)[0]
        info["metadata_version"] = d[mo + 16:mo + 16 + vlen].rstrip(b"\0").decode(
            "ascii", "replace")
    return info, None


def main(argv):
    if len(argv) < 2:
        print("usage: cliheader.py <assembly.exe> [...]")
        return 2
    bad = 0
    for path in argv[1:]:
        info, err = read(path)
        print("  file            : %s" % path)
        if info is None:
            print("  RESULT          : FAIL -- %s" % err)
            bad += 1
            continue
        print("  COFF machine    : 0x%04X %s" % (
            info["machine"],
            {0x014C: "(I386 -- the AnyCPU/MSIL encoding)",
             0x01C4: "(ARMNT -- native ARM32, NOT MSIL)",
             0x8664: "(AMD64 -- native x64, NOT MSIL)"}.get(info["machine"], "")))
        if err:
            print("  RESULT          : FAIL -- %s" % err)
            bad += 1
            continue
        f = info["flags"]
        names = []
        if f & ILONLY:
            names.append("ILONLY")
        if f & BIT32REQUIRED:
            names.append("32BITREQUIRED")
        if f & BIT32PREFERRED:
            names.append("32BITPREFERRED")
        if f & STRONGNAMESIGNED:
            names.append("STRONGNAMESIGNED")
        print("  CLI runtime     : v%s" % info["rt_version"])
        print("  metadata version: %s" % info["metadata_version"])
        print("  COR20 flags     : 0x%08X  %s" % (f, " | ".join(names) if names else "(none)"))

        ok = True
        why = []
        if info["machine"] != 0x014C:
            ok = False
            why.append("machine is not 0x014C, so this is not MSIL")
        if not (f & ILONLY):
            ok = False
            why.append("ILONLY is clear, so it contains native code")
        if f & BIT32REQUIRED:
            ok = False
            why.append("32BITREQUIRED is SET -- x86 only, will NOT run on ARM32")
        if f & BIT32PREFERRED:
            why.append("note: 32BITPREFERRED is set")
        if not info["metadata_version"].startswith("v4.0"):
            why.append("note: metadata version is %s, expected v4.0.*"
                       % info["metadata_version"])
        print("  platform        : %s" % ("AnyCPU (MSIL)" if ok else "NOT AnyCPU"))
        for w in why:
            print("                    %s" % w)
        print("  RESULT          : %s" % ("PASS -- MSIL/AnyCPU, runs on ARM32 and x64"
                                          if ok else "FAIL"))
        if not ok:
            bad += 1
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
