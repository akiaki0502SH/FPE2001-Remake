#!/usr/bin/env python3
from pathlib import Path
import argparse
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("source", nargs="?", default=str(Path(__file__).resolve().parents[1] / "rendered_docx"))
args = parser.parse_args()
src = Path(args.source)
files = sorted(src.glob("page-*.png"), key=lambda p: int(p.stem.split("-")[-1]))
font_path = Path(r"C:\Windows\Fonts\arial.ttf")
font = ImageFont.truetype(str(font_path), 22) if font_path.exists() else ImageFont.load_default()
for group_no in range(0, len(files), 4):
    group = files[group_no:group_no + 4]
    thumbs = []
    for p in group:
        im = Image.open(p).convert("RGB")
        im.thumbnail((612, 792))
        canvas = Image.new("RGB", (632, 832), "white")
        canvas.paste(im, ((632 - im.width) // 2, 30))
        d = ImageDraw.Draw(canvas)
        d.text((12, 4), p.stem, fill="#1F4D78", font=font)
        thumbs.append(canvas)
    sheet = Image.new("RGB", (1264, 1664), "#D7DEE6")
    for i, im in enumerate(thumbs):
        sheet.paste(im, ((i % 2) * 632, (i // 2) * 832))
    sheet.save(src / f"contact-{group_no // 4 + 1}.png")
