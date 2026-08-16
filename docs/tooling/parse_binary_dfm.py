#!/usr/bin/env python3
"""Best-effort parser for classic Delphi binary DFM streams (TPF0)."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path


class ParseError(Exception):
    pass


class Reader:
    def __init__(self, data: bytes, start: int):
        self.data = data
        self.pos = start

    def need(self, n: int) -> None:
        if self.pos + n > len(self.data):
            raise ParseError(f"unexpected EOF at 0x{self.pos:X}")

    def take(self, n: int) -> bytes:
        self.need(n)
        b = self.data[self.pos:self.pos + n]
        self.pos += n
        return b

    def byte(self) -> int:
        return self.take(1)[0]

    def shortstr(self) -> str:
        n = self.byte()
        return self.take(n).decode("latin-1", errors="replace")

    def i8(self) -> int:
        return struct.unpack("<b", self.take(1))[0]

    def i16(self) -> int:
        return struct.unpack("<h", self.take(2))[0]

    def i32(self) -> int:
        return struct.unpack("<i", self.take(4))[0]

    def i64(self) -> int:
        return struct.unpack("<q", self.take(8))[0]

    def value(self):
        kind = self.byte()
        if kind == 0:
            return None
        if kind == 1:  # vaList
            vals = []
            while self.data[self.pos] != 0:
                vals.append(self.value())
            self.pos += 1
            return vals
        if kind == 2:
            return self.i8()
        if kind == 3:
            return self.i16()
        if kind == 4:
            return self.i32()
        if kind == 5:  # 80-bit extended
            return {"extended_hex": self.take(10).hex()}
        if kind == 6:
            return self.shortstr()
        if kind == 7:
            return {"ident": self.shortstr()}
        if kind == 8:
            return False
        if kind == 9:
            return True
        if kind == 10:
            n = self.i32()
            blob = self.take(n)
            return {"binary_bytes": n, "sha1_prefix": __import__("hashlib").sha1(blob).hexdigest()[:12]}
        if kind == 11:  # vaSet
            vals = []
            while True:
                s = self.shortstr()
                if not s:
                    break
                vals.append(s)
            return {"set": vals}
        if kind == 12:  # vaLString
            n = self.i32()
            return self.take(n).decode("latin-1", errors="replace")
        if kind == 13:
            return {"nil": True}
        if kind == 14:  # vaCollection
            items = []
            while True:
                marker = self.byte()
                if marker == 0:
                    break
                if marker != 1:
                    raise ParseError(f"unexpected collection marker {marker} at 0x{self.pos-1:X}")
                props = {}
                while True:
                    name = self.shortstr()
                    if not name:
                        break
                    props[name] = self.value()
                items.append(props)
            return {"collection": items}
        if kind == 15:
            return struct.unpack("<f", self.take(4))[0]
        if kind == 16:
            return {"currency_raw": self.i64()}
        if kind == 17:
            return {"date_raw": struct.unpack("<d", self.take(8))[0]}
        if kind == 18:
            n = self.i32()
            return self.take(n * 2).decode("utf-16le", errors="replace")
        if kind == 19:
            return self.i64()
        if kind == 20:
            n = self.i32()
            return self.take(n).decode("utf-8", errors="replace")
        if kind == 21:
            return struct.unpack("<d", self.take(8))[0]
        raise ParseError(f"unknown value kind {kind} at 0x{self.pos-1:X}")

    def obj(self):
        class_name = self.shortstr()
        if not class_name:
            return None
        name = self.shortstr()
        props = {}
        while True:
            prop = self.shortstr()
            if not prop:
                break
            props[prop] = self.value()
        children = []
        while True:
            child = self.obj()
            if child is None:
                break
            children.append(child)
        return {"class": class_name, "name": name, "properties": props, "children": children}


def find_streams(data: bytes):
    pos = 0
    while True:
        pos = data.find(b"TPF0", pos)
        if pos < 0:
            break
        yield pos
        pos += 4


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("input", type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()
    data = args.input.read_bytes()
    out = []
    for offset in find_streams(data):
        r = Reader(data, offset + 4)
        try:
            obj = r.obj()
            if not obj or not str(obj.get("class", "")).startswith("T"):
                raise ParseError("not a plausible component stream")
            out.append({"offset": offset, "end": r.pos, "object": obj})
        except Exception as exc:
            out.append({"offset": offset, "error": str(exc)})
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
