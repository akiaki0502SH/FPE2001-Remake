#!/usr/bin/env python3
"""Extract likely East-Asian legacy strings without loading target binaries."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


RUN = re.compile(rb"[\x20-\x7e\x80-\xff]{4,160}")


def is_cjk(ch: str) -> bool:
    cp = ord(ch)
    return 0x3400 <= cp <= 0x9FFF or 0xF900 <= cp <= 0xFAFF


def score(text: str) -> float:
    if not text or "\ufffd" in text:
        return -999.0
    cjk = sum(is_cjk(ch) for ch in text)
    ascii_ok = sum(ch.isascii() and (ch.isalnum() or ch in " .,:;!?+-_()[]/%\\'\"") for ch in text)
    control = sum(ord(ch) < 32 for ch in text)
    odd = sum((not ch.isprintable()) for ch in text)
    return cjk * 5 + ascii_ok * 0.05 - control * 6 - odd * 4 - max(0, len(text) - 80) * 0.1


def extract(path: Path) -> list[dict]:
    data = path.read_bytes()
    results = []
    seen = set()
    for m in RUN.finditer(data):
        blob = m.group()
        if not any(b >= 0x80 for b in blob):
            continue
        for enc in ("gb18030", "big5", "cp932"):
            try:
                text = blob.decode(enc).strip()
            except UnicodeDecodeError:
                continue
            s = score(text)
            cjk = sum(is_cjk(ch) for ch in text)
            if cjk < 2 or s < 8:
                continue
            key = (enc, text)
            if key in seen:
                continue
            seen.add(key)
            results.append({"offset": m.start(), "encoding": enc, "score": round(s, 2), "text": text})
    return sorted(results, key=lambda x: (-x["score"], x["offset"]))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("inputs", nargs="+", type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()
    obj = {p.name: extract(p) for p in args.inputs}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
