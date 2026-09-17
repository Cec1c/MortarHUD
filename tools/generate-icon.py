"""沿用原托盘的准星：固定几何、单色线条，小尺寸优先。依赖 Pillow。"""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / "src/MortarHUD.App/Assets"
root.mkdir(exist_ok=True)
image = Image.new("RGBA", (1024, 1024))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((32, 32, 992, 992), radius=220, fill="#172128")
draw.ellipse((258, 258, 766, 766), outline="#E4EAF0", width=54)
for line in [(512, 164, 512, 336), (512, 688, 512, 860),
             (164, 512, 336, 512), (688, 512, 860, 512)]:
    draw.line(line, fill="#E4EAF0", width=54)
draw.ellipse((450, 450, 574, 574), fill="#7ECDB8")
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "MortarHUD.png")
image.save(root / "MortarHUD.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
