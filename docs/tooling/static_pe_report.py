#!/usr/bin/env python3
"""Small, dependency-free PE inventory for static analysis only.

The script never maps or executes the inspected binaries. It parses headers,
imports, exports, section metadata, resource-directory counts, and printable
ASCII/UTF-16LE strings.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
from pathlib import Path


MACHINE = {0x14C: "x86", 0x8664: "x64", 0x1C0: "ARM", 0xAA64: "ARM64"}
RESOURCE_TYPES = {
    1: "CURSOR", 2: "BITMAP", 3: "ICON", 4: "MENU", 5: "DIALOG",
    6: "STRING", 7: "FONTDIR", 8: "FONT", 9: "ACCELERATOR",
    10: "RCDATA", 11: "MESSAGETABLE", 12: "GROUP_CURSOR",
    14: "GROUP_ICON", 16: "VERSION", 24: "MANIFEST",
}


def u16(data: bytes, off: int) -> int:
    return struct.unpack_from("<H", data, off)[0]


def u32(data: bytes, off: int) -> int:
    return struct.unpack_from("<I", data, off)[0]


def u64(data: bytes, off: int) -> int:
    return struct.unpack_from("<Q", data, off)[0]


def cstr(data: bytes, off: int, limit: int = 4096) -> str:
    end = data.find(b"\0", off, min(len(data), off + limit))
    if end < 0:
        end = min(len(data), off + limit)
    return data[off:end].decode("latin-1", errors="replace")


def entropy(blob: bytes) -> float:
    if not blob:
        return 0.0
    counts = [0] * 256
    for b in blob:
        counts[b] += 1
    n = len(blob)
    return -sum((c / n) * math.log2(c / n) for c in counts if c)


def ascii_strings(data: bytes, min_len: int = 4) -> list[str]:
    pat = re.compile(rb"[\x20-\x7e]{%d,}" % min_len)
    return [m.group().decode("ascii", errors="replace") for m in pat.finditer(data)]


def utf16_strings(data: bytes, min_len: int = 4) -> list[str]:
    pat = re.compile(rb"(?:[\x20-\x7e\x80-\xff]\x00){%d,}" % min_len)
    out = []
    for m in pat.finditer(data):
        text = m.group().decode("utf-16le", errors="replace")
        if text.strip():
            out.append(text)
    return out


class PE:
    def __init__(self, path: Path):
        self.path = path
        self.data = path.read_bytes()
        if self.data[:2] != b"MZ":
            raise ValueError("not an MZ executable")
        self.pe_off = u32(self.data, 0x3C)
        if self.data[self.pe_off:self.pe_off + 4] != b"PE\0\0":
            raise ValueError("missing PE signature")
        self.coff = self.pe_off + 4
        self.machine = u16(self.data, self.coff)
        self.section_count = u16(self.data, self.coff + 2)
        self.timestamp = u32(self.data, self.coff + 4)
        self.opt_size = u16(self.data, self.coff + 16)
        self.characteristics = u16(self.data, self.coff + 18)
        self.opt = self.coff + 20
        self.magic = u16(self.data, self.opt)
        self.is64 = self.magic == 0x20B
        if self.magic not in (0x10B, 0x20B):
            raise ValueError(f"unsupported optional-header magic 0x{self.magic:x}")
        self.entry_rva = u32(self.data, self.opt + 16)
        self.image_base = u64(self.data, self.opt + 24) if self.is64 else u32(self.data, self.opt + 28)
        self.subsystem = u16(self.data, self.opt + (68 if self.is64 else 68))
        self.dll_chars = u16(self.data, self.opt + 70)
        self.size_image = u32(self.data, self.opt + 56)
        self.size_headers = u32(self.data, self.opt + 60)
        self.dir_count = u32(self.data, self.opt + (108 if self.is64 else 92))
        self.dir_off = self.opt + (112 if self.is64 else 96)
        self.directories = []
        for i in range(min(self.dir_count, 16)):
            self.directories.append((u32(self.data, self.dir_off + i * 8), u32(self.data, self.dir_off + i * 8 + 4)))
        sec_off = self.opt + self.opt_size
        self.sections = []
        for i in range(self.section_count):
            off = sec_off + i * 40
            name = self.data[off:off + 8].split(b"\0", 1)[0].decode("latin-1", errors="replace")
            self.sections.append({
                "name": name,
                "virtual_size": u32(self.data, off + 8),
                "virtual_address": u32(self.data, off + 12),
                "raw_size": u32(self.data, off + 16),
                "raw_offset": u32(self.data, off + 20),
                "characteristics": f"0x{u32(self.data, off + 36):08X}",
            })

    def rva_to_off(self, rva: int) -> int | None:
        if rva < self.size_headers:
            return rva
        for s in self.sections:
            start = s["virtual_address"]
            span = max(s["virtual_size"], s["raw_size"])
            if start <= rva < start + span:
                off = s["raw_offset"] + (rva - start)
                return off if off < len(self.data) else None
        return None

    def imports(self) -> list[dict]:
        if len(self.directories) < 2 or not self.directories[1][0]:
            return []
        off = self.rva_to_off(self.directories[1][0])
        if off is None:
            return []
        result = []
        ptr_size = 8 if self.is64 else 4
        ordinal_mask = 1 << (63 if self.is64 else 31)
        for _ in range(2048):
            if off + 20 > len(self.data):
                break
            oft, _, _, name_rva, ft = struct.unpack_from("<IIIII", self.data, off)
            if not any((oft, name_rva, ft)):
                break
            noff = self.rva_to_off(name_rva)
            dll = cstr(self.data, noff) if noff is not None else f"<bad-rva:{name_rva:x}>"
            thunk_off = self.rva_to_off(oft or ft)
            funcs = []
            if thunk_off is not None:
                for j in range(65536):
                    pos = thunk_off + j * ptr_size
                    if pos + ptr_size > len(self.data):
                        break
                    value = u64(self.data, pos) if self.is64 else u32(self.data, pos)
                    if not value:
                        break
                    if value & ordinal_mask:
                        funcs.append(f"ordinal:{value & 0xFFFF}")
                    else:
                        ioff = self.rva_to_off(value)
                        funcs.append(cstr(self.data, ioff + 2) if ioff is not None else f"<bad-rva:{value:x}>")
            result.append({"dll": dll, "functions": funcs})
            off += 20
        return result

    def exports(self) -> list[str]:
        if not self.directories or not self.directories[0][0]:
            return []
        off = self.rva_to_off(self.directories[0][0])
        if off is None or off + 40 > len(self.data):
            return []
        count = u32(self.data, off + 24)
        names_rva = u32(self.data, off + 32)
        names_off = self.rva_to_off(names_rva)
        if names_off is None:
            return []
        out = []
        for i in range(min(count, 100000)):
            nrva = u32(self.data, names_off + i * 4)
            noff = self.rva_to_off(nrva)
            if noff is not None:
                out.append(cstr(self.data, noff))
        return out

    def resource_summary(self) -> dict:
        if len(self.directories) < 3 or not self.directories[2][0]:
            return {}
        base = self.rva_to_off(self.directories[2][0])
        if base is None or base + 16 > len(self.data):
            return {}
        named = u16(self.data, base + 12)
        ids = u16(self.data, base + 14)
        result = {}
        for i in range(named + ids):
            pos = base + 16 + i * 8
            if pos + 8 > len(self.data):
                break
            name_or_id = u32(self.data, pos)
            child = u32(self.data, pos + 4)
            if name_or_id & 0x80000000:
                npos = base + (name_or_id & 0x7FFFFFFF)
                nlen = u16(self.data, npos)
                label = self.data[npos + 2:npos + 2 + nlen * 2].decode("utf-16le", errors="replace")
            else:
                rid = name_or_id & 0xFFFF
                label = RESOURCE_TYPES.get(rid, f"TYPE_{rid}")
            if child & 0x80000000:
                cpos = base + (child & 0x7FFFFFFF)
                if cpos + 16 <= len(self.data):
                    result[label] = u16(self.data, cpos + 12) + u16(self.data, cpos + 14)
                else:
                    result[label] = "invalid"
            else:
                result[label] = 1
        return result

    def report(self) -> dict:
        all_ascii = ascii_strings(self.data)
        all_utf16 = utf16_strings(self.data)
        for s in self.sections:
            blob = self.data[s["raw_offset"]:s["raw_offset"] + s["raw_size"]]
            s["entropy"] = round(entropy(blob), 3)
        raw_end = max([self.size_headers] + [s["raw_offset"] + s["raw_size"] for s in self.sections])
        return {
            "file": self.path.name,
            "size": len(self.data),
            "sha256": hashlib.sha256(self.data).hexdigest().upper(),
            "machine": MACHINE.get(self.machine, f"0x{self.machine:04X}"),
            "pe_format": "PE32+" if self.is64 else "PE32",
            "timestamp_raw": self.timestamp,
            "image_base": f"0x{self.image_base:X}",
            "entry_rva": f"0x{self.entry_rva:X}",
            "image_size": self.size_image,
            "subsystem": self.subsystem,
            "dll_characteristics": f"0x{self.dll_chars:04X}",
            "aslr": bool(self.dll_chars & 0x40),
            "nx_compat": bool(self.dll_chars & 0x100),
            "sections": self.sections,
            "overlay_bytes": max(0, len(self.data) - raw_end),
            "imports": self.imports(),
            "exports": self.exports(),
            "resources": self.resource_summary(),
            "strings_ascii": all_ascii,
            "strings_utf16le": all_utf16,
        }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs", nargs="+", type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()
    reports = []
    for path in args.inputs:
        try:
            reports.append(PE(path).report())
        except Exception as exc:
            reports.append({"file": path.name, "error": str(exc)})
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
