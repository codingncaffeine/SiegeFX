#!/usr/bin/env python3
"""tools/check-macos-signatures.py <folder>

An Apple Silicon Mac runs no code without a valid signature: the kernel kills
a program whose pages do not hash to its embedded CodeDirectory. The macOS
tarball is built on Linux (publish.sh), where nothing can run it, so this
checks every Mach-O file in the folder the way the kernel does: each slice
(a universal binary holds one per processor) carries a code signature whose
CodeDirectory covers the file up to the signature, and every code page
hashes to its slot. Prints one line per file; exit 1 when any file fails or
the folder holds no Mach-O file at all."""
import hashlib
import os
import struct
import sys

LC_CODE_SIGNATURE = 0x1D
CSMAGIC_EMBEDDED_SIGNATURE = 0xFADE0CC0
CSMAGIC_CODEDIRECTORY = 0xFADE0C02
CS_ADHOC = 0x2
HASHES = {1: hashlib.sha1, 2: hashlib.sha256}
CPU = {0x01000007: "x86_64", 0x0100000C: "arm64"}


def check_slice(data):
    """One thin 64-bit Mach-O image: (ok, description)."""
    if struct.unpack_from("<I", data, 0)[0] != 0xFEEDFACF:
        return False, "not a 64-bit little-endian Mach-O"
    cpu, ncmds = struct.unpack_from("<I", data, 4)[0], struct.unpack_from("<I", data, 16)[0]
    arch = CPU.get(cpu, f"cpu {cpu:#x}")
    off, sig = 32, None
    for _ in range(ncmds):
        cmd, size = struct.unpack_from("<II", data, off)
        if cmd == LC_CODE_SIGNATURE:
            sig = struct.unpack_from("<II", data, off + 8)
        off += size
    if sig is None:
        return False, f"{arch}: NO code signature"
    dataoff, datasize = sig
    blob = data[dataoff:dataoff + datasize]
    magic, _, count = struct.unpack_from(">III", blob, 0)
    if magic != CSMAGIC_EMBEDDED_SIGNATURE:
        return False, f"{arch}: bad signature magic {magic:#x}"
    for i in range(count):
        _, boff = struct.unpack_from(">II", blob, 12 + 8 * i)
        if struct.unpack_from(">I", blob, boff)[0] != CSMAGIC_CODEDIRECTORY:
            continue
        (_, _, _, flags, hash_off, _, _, n_code, code_limit, hash_size, hash_type, _,
         page_log2) = struct.unpack_from(">IIIIIIIIIBBBB", blob, boff)
        h = HASHES.get(hash_type)
        if h is None:
            return False, f"{arch}: unknown hash type {hash_type}"
        if code_limit != dataoff:
            return False, f"{arch}: signature covers {code_limit} bytes, code ends at {dataoff}"
        page = 1 << page_log2
        bad = sum(
            h(data[s * page:min((s + 1) * page, code_limit)]).digest()[:hash_size]
            != blob[boff + hash_off + s * hash_size:boff + hash_off + (s + 1) * hash_size]
            for s in range(n_code))
        if bad:
            return False, f"{arch}: {bad} of {n_code} pages do not match the signature"
        kind = "ad-hoc" if flags & CS_ADHOC else "signed"
        return True, f"{arch} {kind}, {n_code} pages"
    return False, f"{arch}: no CodeDirectory"


def check_file(data):
    """(is Mach-O, ok, description) for one file's bytes."""
    if len(data) < 32:
        return False, True, ""
    magic_be = struct.unpack_from(">I", data, 0)[0]
    if magic_be == 0xCAFEBABE:                       # universal: one image per processor
        n = struct.unpack_from(">I", data, 4)[0]
        if n == 0 or n > 8:
            return False, True, ""                   # a Java class file shares the magic
        results = []
        for i in range(n):
            _, _, off, size, _ = struct.unpack_from(">IIIII", data, 8 + 20 * i)
            results.append(check_slice(data[off:off + size]))
        return True, all(ok for ok, _ in results), "universal: " + "; ".join(d for _, d in results)
    if struct.unpack_from("<I", data, 0)[0] == 0xFEEDFACF:
        ok, desc = check_slice(data)
        return True, ok, desc
    return False, True, ""


def main(folder):
    seen = failed = 0
    for root, _, files in os.walk(folder):
        for name in sorted(files):
            path = os.path.join(root, name)
            if os.path.islink(path):
                continue
            with open(path, "rb") as f:
                data = f.read()
            is_macho, ok, desc = check_file(data)
            if not is_macho:
                continue
            seen += 1
            failed += not ok
            print(f"  {'ok  ' if ok else 'FAIL'} {os.path.relpath(path, folder)}: {desc}")
    if seen == 0:
        print(f"check-macos-signatures: no Mach-O file in {folder}")
        return 1
    print(f"check-macos-signatures: {seen - failed} of {seen} Mach-O files signed and intact")
    return 1 if failed else 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    sys.exit(main(sys.argv[1]))
