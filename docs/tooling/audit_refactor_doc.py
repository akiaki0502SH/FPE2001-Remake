#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
from zipfile import ZipFile

from lxml import etree


DOC = Path(__file__).resolve().parents[1] / "deliverables" / "FPE2001_Win11重构分析与架构设计.docx"
NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main", "wp": "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"}
W = "{%s}" % NS["w"]


def attr(node, name):
    return node.get(W + name) if node is not None else None


def main():
    failures = []
    warnings = []
    with ZipFile(DOC) as z:
        doc = etree.fromstring(z.read("word/document.xml"))
        styles = etree.fromstring(z.read("word/styles.xml"))
        numbering = etree.fromstring(z.read("word/numbering.xml"))

    sect = doc.find(".//w:sectPr", NS)
    pg = sect.find("w:pgSz", NS)
    mar = sect.find("w:pgMar", NS)
    if (attr(pg, "w"), attr(pg, "h")) != ("12240", "15840"):
        failures.append("page size is not US Letter portrait")
    expected_margins = {"top": "1440", "right": "1440", "bottom": "1440", "left": "1440", "header": "708", "footer": "708"}
    for key, expected in expected_margins.items():
        if attr(mar, key) != expected:
            failures.append(f"margin {key}={attr(mar, key)} expected {expected}")

    style_expect = {
        "Normal": ("Microsoft YaHei", "22", None, "0", "120", "300"),
        "Heading1": ("Microsoft YaHei", "32", "2E74B5", "360", "200", None),
        "Heading2": ("Microsoft YaHei", "26", "2E74B5", "280", "140", None),
        "Heading3": ("Microsoft YaHei", "24", "1F4D78", "200", "100", None),
    }
    for sid, (font, size, color, before, after, line) in style_expect.items():
        st = styles.xpath(f"//w:style[@w:styleId='{sid}']", namespaces=NS)
        if not st:
            failures.append(f"missing style {sid}")
            continue
        st = st[0]
        rpr = st.find("w:rPr", NS)
        ppr = st.find("w:pPr", NS)
        rf = rpr.find("w:rFonts", NS) if rpr is not None else None
        sz = rpr.find("w:sz", NS) if rpr is not None else None
        col = rpr.find("w:color", NS) if rpr is not None else None
        sp = ppr.find("w:spacing", NS) if ppr is not None else None
        if attr(rf, "eastAsia") != font or attr(sz, "val") != size:
            failures.append(f"style {sid} font/size mismatch")
        if color and attr(col, "val") != color:
            failures.append(f"style {sid} color mismatch")
        if attr(sp, "before") not in (before, None if before == "0" else "__never__") or attr(sp, "after") != after:
            failures.append(f"style {sid} spacing mismatch")
        if line and attr(sp, "line") != line:
            failures.append(f"style {sid} line spacing mismatch")

    num_formats = [attr(x, "val") for x in numbering.findall(".//w:numFmt", NS)]
    if "bullet" not in num_formats or "decimal" not in num_formats:
        failures.append("real bullet/decimal numbering definitions missing")

    tables = doc.findall(".//w:tbl", NS)
    for idx, table in enumerate(tables, 1):
        tbl_pr = table.find("w:tblPr", NS)
        tbl_w = tbl_pr.find("w:tblW", NS)
        tbl_ind = tbl_pr.find("w:tblInd", NS)
        if attr(tbl_w, "type") != "dxa":
            failures.append(f"table {idx}: tblW not DXA")
            continue
        total = int(attr(tbl_w, "w"))
        grid = [int(attr(x, "w")) for x in table.findall("w:tblGrid/w:gridCol", NS)]
        if sum(grid) != total:
            failures.append(f"table {idx}: grid sum {sum(grid)} != tblW {total}")
        if attr(tbl_ind, "type") != "dxa" or attr(tbl_ind, "w") != "120":
            failures.append(f"table {idx}: tblInd is not 120 DXA")
        for rno, row in enumerate(table.findall("w:tr", NS), 1):
            cells = row.findall("w:tc", NS)
            for cno, cell in enumerate(cells, 1):
                tcw = cell.find("w:tcPr/w:tcW", NS)
                if cno <= len(grid) and (attr(tcw, "type") != "dxa" or int(attr(tcw, "w")) != grid[cno - 1]):
                    failures.append(f"table {idx} row {rno} cell {cno}: tcW mismatch")

    text = "".join(doc.itertext())
    for token in ("TODO", "TBD", ":codex-file-citation", "turn0search"):
        if token in text:
            failures.append(f"placeholder/internal token present: {token}")
    descr = doc.xpath("//wp:docPr/@descr", namespaces=NS)
    if not any("分层架构" in x for x in descr):
        failures.append("architecture image alt text missing")
    fake_bullets = []
    for p in doc.findall(".//w:p", NS):
        ptext = "".join(p.itertext()).lstrip()
        if ptext.startswith(("• ", "- ", "● ")):
            fake_bullets.append(ptext[:60])
    if fake_bullets:
        failures.append(f"fake bullet paragraphs found: {len(fake_bullets)}")

    result = {
        "preset": "compact_reference_guide",
        "named_overrides": [
            "CJK font: Microsoft YaHei replaces Calibri",
            "dense technical tables use 8.3-9.5 pt",
            "editorial cover; running header has no decorative rule",
            "architecture figure inserted as a raster diagram with alt text",
        ],
        "table_count": len(tables),
        "image_alt_count": len(descr),
        "failures": failures,
        "warnings": warnings,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
