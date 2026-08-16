#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from zipfile import ZipFile

from lxml import etree


NS = {
    "w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
    "wp": "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
}
W = "{%s}" % NS["w"]


def attr(node, name):
    return node.get(W + name) if node is not None else None


def audit(path: Path):
    failures = []
    warnings = []
    with ZipFile(path) as z:
        document = etree.fromstring(z.read("word/document.xml"))
        styles = etree.fromstring(z.read("word/styles.xml"))
        numbering = etree.fromstring(z.read("word/numbering.xml"))

    sect = document.find(".//w:sectPr", NS)
    pg = sect.find("w:pgSz", NS)
    mar = sect.find("w:pgMar", NS)
    if (attr(pg, "w"), attr(pg, "h")) != ("12240", "15840"):
        failures.append("page size is not US Letter portrait")
    expected = {"top": "1440", "right": "1440", "bottom": "1440", "left": "1440", "header": "708", "footer": "708"}
    for key, value in expected.items():
        if attr(mar, key) != value:
            failures.append(f"margin {key} mismatch")

    style_expect = {
        "Normal": ("Microsoft YaHei", "22", None, "120", "300"),
        "Heading1": ("Microsoft YaHei", "32", "2E74B5", "200", None),
        "Heading2": ("Microsoft YaHei", "26", "2E74B5", "140", None),
        "Heading3": ("Microsoft YaHei", "24", "1F4D78", "100", None),
    }
    for sid, (font, size, color, after, line) in style_expect.items():
        nodes = styles.xpath(f"//w:style[@w:styleId='{sid}']", namespaces=NS)
        if not nodes:
            failures.append(f"missing style {sid}")
            continue
        rpr = nodes[0].find("w:rPr", NS)
        ppr = nodes[0].find("w:pPr", NS)
        rf = rpr.find("w:rFonts", NS)
        sz = rpr.find("w:sz", NS)
        col = rpr.find("w:color", NS)
        sp = ppr.find("w:spacing", NS)
        if attr(rf, "eastAsia") != font or attr(sz, "val") != size:
            failures.append(f"style {sid} font/size mismatch")
        if color and attr(col, "val") != color:
            failures.append(f"style {sid} color mismatch")
        if attr(sp, "after") != after:
            failures.append(f"style {sid} after spacing mismatch")
        if line and attr(sp, "line") != line:
            failures.append(f"style {sid} line spacing mismatch")

    formats = [attr(x, "val") for x in numbering.findall(".//w:numFmt", NS)]
    if "bullet" not in formats or "decimal" not in formats:
        failures.append("real numbering definitions missing")

    tables = document.findall(".//w:tbl", NS)
    for idx, table in enumerate(tables, 1):
        tbl_pr = table.find("w:tblPr", NS)
        tbl_w = tbl_pr.find("w:tblW", NS)
        tbl_ind = tbl_pr.find("w:tblInd", NS)
        if attr(tbl_w, "type") != "dxa":
            failures.append(f"table {idx}: width is not DXA")
            continue
        total = int(attr(tbl_w, "w"))
        grid = [int(attr(x, "w")) for x in table.findall("w:tblGrid/w:gridCol", NS)]
        if sum(grid) != total:
            failures.append(f"table {idx}: grid width mismatch")
        if attr(tbl_ind, "w") != "120":
            failures.append(f"table {idx}: indent mismatch")
        for row in table.findall("w:tr", NS):
            for cno, cell in enumerate(row.findall("w:tc", NS)):
                tcw = cell.find("w:tcPr/w:tcW", NS)
                if cno < len(grid) and int(attr(tcw, "w")) != grid[cno]:
                    failures.append(f"table {idx}: cell width mismatch")

    full_text = "".join(document.itertext())
    for token in (":codex-file-citation", "turn0search", "{{PLACEHOLDER}}"):
        if token in full_text:
            failures.append(f"internal token present: {token}")
    fake = []
    for para in document.findall(".//w:p", NS):
        text = "".join(para.itertext()).lstrip()
        if text.startswith(("• ", "● ")):
            fake.append(text[:50])
    if fake:
        failures.append(f"fake bullet paragraphs: {len(fake)}")

    image_count = len(document.xpath("//wp:docPr", namespaces=NS))
    alt_count = len(document.xpath("//wp:docPr[string-length(@descr) > 0]", namespaces=NS))
    if image_count != alt_count:
        failures.append(f"image alt text missing: {alt_count}/{image_count}")

    headings = {}
    for level in (1, 2, 3):
        headings[str(level)] = len(document.xpath(f"//w:p[w:pPr/w:pStyle[@w:val='Heading{level}']]", namespaces=NS))

    return {
        "file": str(path),
        "preset": "compact_reference_guide",
        "named_overrides": ["CJK font Microsoft YaHei", "technical tables 8.8-9.5 pt", "editorial cover", "UI wireframe figures with alt text"],
        "table_count": len(tables),
        "image_count": image_count,
        "image_alt_count": alt_count,
        "heading_counts": headings,
        "failures": failures,
        "warnings": warnings,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    args = parser.parse_args()
    results = [audit(Path(x)) for x in args.files]
    print(json.dumps(results, ensure_ascii=False, indent=2))
    if any(x["failures"] for x in results):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
