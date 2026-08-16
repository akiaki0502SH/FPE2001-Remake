#!/usr/bin/env python3
"""Flatten parsed DFM JSON into a concise component/UI inventory."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


CORE = {"Caption", "Hint", "Text", "Items.Strings", "Lines.Strings", "Filter", "DefaultExt", "Value", "MinValue", "MaxValue", "Checked", "Enabled", "Visible", "Tag"}
GEOM = {"Left", "Top", "Width", "Height", "Align", "TabOrder"}


def is_cjk(ch: str) -> bool:
    cp = ord(ch)
    return 0x3400 <= cp <= 0x9FFF or 0xF900 <= cp <= 0xFAFF


def fix_legacy(s: str) -> str:
    if not any(0x80 <= ord(ch) <= 0xFF for ch in s):
        return s
    raw = s.encode("latin-1", errors="replace")
    candidates = [s]
    for enc in ("cp950", "gb18030"):
        try:
            candidates.append(raw.decode(enc))
        except UnicodeDecodeError:
            pass
    def rank(x: str):
        return (sum(is_cjk(ch) for ch in x), -x.count("?"))
    return max(candidates, key=rank)


def clean(v):
    if isinstance(v, str):
        return fix_legacy(v)
    if isinstance(v, list):
        return [clean(x) for x in v]
    if isinstance(v, dict):
        if set(v) == {"ident"}:
            return v["ident"]
        if set(v) == {"set"}:
            return v["set"]
        return {k: clean(x) for k, x in v.items()}
    return v


def flatten(node: dict, parent: str = ""):
    name = node.get("name") or node.get("class")
    path = f"{parent}/{name}" if parent else name
    props = node.get("properties", {})
    selected = {}
    for k, v in props.items():
        if k in CORE or k in GEOM or k.startswith("On"):
            selected[k] = clean(v)
    yield {"path": path, "class": node.get("class"), "properties": selected}
    for child in node.get("children", []):
        yield from flatten(child, path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("input", type=Path)
    ap.add_argument("--out", required=True, type=Path)
    args = ap.parse_args()
    streams = json.loads(args.input.read_text(encoding="utf-8"))
    out = []
    for stream in streams:
        if "object" not in stream:
            continue
        out.append({
            "offset": stream["offset"],
            "form": stream["object"].get("name"),
            "components": list(flatten(stream["object"])),
        })
    args.out.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
