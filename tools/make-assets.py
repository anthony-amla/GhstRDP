"""
Gera os assets do app a partir da arte da logo (fantasma pixel-art em fundo escuro).

    python tools/make-assets.py art/logo-source.webp src/GhstRDP/Assets

Saídas:
  ghst.png  - logo em branco-gelo com fundo transparente (512 px)
  ghst.ico  - ícone do app: quadrado arredondado escuro com a logo (16..256 px)
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

LOGO_COLOR = (238, 235, 232)      # branco-gelo da arte
ICON_BG = (36, 37, 46)            # quadrado do ícone (cinza-azulado escuro)
ICON_BG_TOP = (44, 45, 56)


def extract_logo(src: Path) -> Image.Image:
    img = Image.open(src).convert("RGB")
    lum = img.convert("L")
    # fundo escuro -> transparente, traço claro -> opaco (bordas suaves preservadas)
    alpha = lum.point(lambda v: 0 if v < 70 else 255 if v > 190 else int((v - 70) * 255 / 120))
    bbox = alpha.point(lambda v: 255 if v > 40 else 0).getbbox()
    alpha = alpha.crop(bbox)
    w, h = alpha.size
    side = int(max(w, h) * 1.04)
    canvas = Image.new("RGBA", (side, side), LOGO_COLOR + (0,))
    solid = Image.new("RGBA", (w, h), LOGO_COLOR + (255,))
    solid.putalpha(alpha)
    canvas.paste(solid, ((side - w) // 2, (side - h) // 2), solid)
    return canvas


def icon_frame(logo: Image.Image, size: int) -> Image.Image:
    scale = 4  # desenha grande e reduz (cantos suaves)
    big = size * scale
    frame = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    mask = Image.new("L", (big, big), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, big - 1, big - 1), radius=int(big * 0.23), fill=255)
    bg = Image.new("RGBA", (big, big))
    draw = ImageDraw.Draw(bg)
    for y in range(big):  # leve gradiente vertical
        t = y / (big - 1)
        c = tuple(int(ICON_BG_TOP[i] + (ICON_BG[i] - ICON_BG_TOP[i]) * t) for i in range(3))
        draw.line((0, y, big, y), fill=c + (255,))
    frame.paste(bg, (0, 0), mask)
    inner = int(big * (0.70 if size >= 32 else 0.80))
    lg = logo.resize((inner, inner), Image.LANCZOS)
    frame.alpha_composite(lg, ((big - inner) // 2, (big - inner) // 2))
    return frame.resize((size, size), Image.LANCZOS)


def main() -> None:
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    logo = extract_logo(src)
    logo.resize((512, 512), Image.LANCZOS).save(out / "ghst.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [icon_frame(logo, s) for s in sizes]
    frames[-1].save(out / "ghst.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    frames[-1].save(out / "icon-preview.png")
    print("ok", logo.size)


if __name__ == "__main__":
    main()
