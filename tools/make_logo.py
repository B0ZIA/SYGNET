#!/usr/bin/env python3
"""
Znak SYGNET: sygnet z boku – obrączka, oprawa oddzielona od niej równym odstępem po łuku
i płaska płytka pieczęci na górze (tą częścią się „odbija”). Monochromatyczny, geometryczny.
Rysowany w PIL z 4× nadpróbkowaniem. Wymaga: pillow.

  python tools/make_logo.py

Unity (SYGNET_Unity/Assets):
  Sygnet/Art/icon_background.png   tło ikony adaptacyjnej Androida
  Sygnet/Art/icon_foreground.png   znak w strefie bezpiecznej (Ø 66/108), przezroczyste tło
  Sygnet/Art/icon_legacy.png       tło + znak (ikona zwykła i okrągła)
  Resources/sygnet_logo.png        biały znak do UI aplikacji
docs/brand/ (konsola, prezentacja, plakaty):
  sygnet_mark_white.png / sygnet_mark_black.png        sam znak, przezroczyste tło
  sygnet_lockup_dark.png                               znak + napis na ciemnym tle
  sygnet_lockup_white.png / sygnet_lockup_black.png    znak + napis, przezroczyste tło
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
UNITY = os.path.join(ROOT, "SYGNET_Unity", "Assets")
BRAND = os.path.join(ROOT, "docs", "brand")

SS = 4
BG = (10, 12, 15)           # #0A0C0F
WHITE = (236, 240, 244)     # #ECF0F4
BLACK = (10, 12, 15)
FONT = "C:/Windows/Fonts/bahnschrift.ttf"   # geometryczny krój techniczny (Windows 10/11)

# geometria w jednostkach 0..100
BAND_C, BAND_R, BAND_W = (50, 68), 24, 8.5  # obrączka
GAP = 3.2                                   # odstęp obrączka ↔ oprawa, stały na całym łuku
MOUNT = [(25, 24), (75, 24), (63, 52), (37, 52)]   # oprawa: trapez zwężający się ku obrączce
PLATE = (21, 13, 79, 20.5, 1.2)             # płytka pieczęci: x0, y0, x1, y1, promień rogów


def mark_mask(size=2048):
    S = size * SS
    k = S / 100
    img = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(img)

    def circle(c, r, v):
        d.ellipse([(c[0] - r) * k, (c[1] - r) * k, (c[0] + r) * k, (c[1] + r) * k], fill=v)

    d.polygon([(x * k, y * k) for x, y in MOUNT], fill=255)
    circle(BAND_C, BAND_R + GAP, 0)                 # wycięcie oprawy = równy odstęp po łuku
    circle(BAND_C, BAND_R, 255)                     # obrączka
    circle(BAND_C, BAND_R - BAND_W, 0)
    x0, y0, x1, y1, r = PLATE
    d.rounded_rectangle([x0 * k, y0 * k, x1 * k, y1 * k], radius=r * k, fill=255)
    return img.resize((size, size), Image.LANCZOS)


def fit(mask, size, radius=None, height=None):
    """Wycina znak i centruje go: albo w kole o promieniu radius·size, albo na wysokość height·size."""
    crop = mask.crop(mask.getbbox())
    w, h = crop.size
    if radius is not None:
        px, far = crop.load(), 0
        for y in range(0, h, 4):
            for x in range(0, w, 4):
                if px[x, y] > 8:
                    far = max(far, math.hypot(x - w / 2, y - h / 2))
        s = radius * size / far
    else:
        s = height * size / h
    crop = crop.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    out = Image.new("L", (size, size), 0)
    out.paste(crop, ((size - crop.size[0]) // 2, (size - crop.size[1]) // 2))
    return out


def colorize(mask, color, background=None):
    img = Image.new("RGBA", mask.size, (background + (255,)) if background else (0, 0, 0, 0))
    img.paste(Image.new("RGBA", mask.size, color + (255,)), (0, 0), mask)
    return img


def lockup(height, fg, bg=None):
    """Znak + rozstrzelony napis SYGNET w poziomie."""
    m = mark_mask()
    crop = m.crop(m.getbbox())
    mh = int(height * 0.86)
    crop = crop.resize((round(crop.size[0] * mh / crop.size[1]), mh), Image.LANCZOS)
    font = ImageFont.truetype(FONT, int(height * 0.40))
    font.set_variation_by_name("SemiBold")
    text, spacing = "SYGNET", height * 0.12
    probe = ImageDraw.Draw(Image.new("L", (1, 1)))
    widths = [probe.textlength(ch, font=font) for ch in text]
    gap = height * 0.32
    W = int(height * 0.1 + crop.size[0] + gap + sum(widths) + spacing * (len(text) - 1) + height * 0.1)
    img = Image.new("RGBA", (W, height), (bg + (255,)) if bg else (0, 0, 0, 0))
    x0 = int(height * 0.1)
    img.paste(Image.new("RGBA", crop.size, fg + (255,)), (x0, (height - mh) // 2), crop)
    d = ImageDraw.Draw(img)
    bbox = d.textbbox((0, 0), "SYGNET", font=font)
    y = (height - (bbox[3] - bbox[1])) / 2 - bbox[1]
    x = x0 + crop.size[0] + gap
    for ch, w in zip(text, widths):
        d.text((x, y), ch, font=font, fill=fg + (255,))
        x += w + spacing
    return img


def main():
    art = os.path.join(UNITY, "Sygnet", "Art")
    os.makedirs(art, exist_ok=True)
    os.makedirs(BRAND, exist_ok=True)
    m = mark_mask()

    Image.new("RGB", (1024, 1024), BG).save(os.path.join(art, "icon_background.png"))
    # ikona adaptacyjna: launcher może przyciąć do koła Ø 66/108 → znak w promieniu 0,28
    colorize(fit(m, 1024, radius=0.28), WHITE).save(os.path.join(art, "icon_foreground.png"))
    colorize(fit(m, 1024, radius=0.37), WHITE, BG).convert("RGB").save(os.path.join(art, "icon_legacy.png"))
    colorize(fit(m, 512, height=0.96), WHITE).save(os.path.join(UNITY, "Resources", "sygnet_logo.png"))

    colorize(fit(m, 1024, height=0.9), WHITE).save(os.path.join(BRAND, "sygnet_mark_white.png"))
    colorize(fit(m, 1024, height=0.9), BLACK).save(os.path.join(BRAND, "sygnet_mark_black.png"))
    lockup(256, WHITE, BG).save(os.path.join(BRAND, "sygnet_lockup_dark.png"))
    lockup(256, WHITE).save(os.path.join(BRAND, "sygnet_lockup_white.png"))
    lockup(256, BLACK).save(os.path.join(BRAND, "sygnet_lockup_black.png"))
    stale = os.path.join(BRAND, "sygnet_mark_color.png")       # z poprzedniego projektu (ośmiokąt)
    if os.path.exists(stale):
        os.remove(stale)
    print("OK")


if __name__ == "__main__":
    main()
