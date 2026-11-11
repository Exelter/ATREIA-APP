# The pictures of the setup wizard (atreia.iss): the tall one on its first and last pages, the small one in the top corner of the others, each at
# 100 %, 150 % and 200 % for the screen's scaling (the wizard picks the closest). Made from the app's icon: its gold lines on the app's dark
# background, "ATREIA" under them. Run again after the icon changes: python Tools/release/installer/make-images.py (Pillow, and the Inter font).
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.normpath(os.path.join(here, '..', '..', '..'))
ico = Image.open(os.path.join(root, 'Client', 'assets', 'app', 'aiondps.ico'))
ico.size = max(ico.info['sizes'])
icon = ico.convert('RGBA')

GOLD = (220, 184, 119)
TOP, BOTTOM, GLOW = (27, 17, 12), (9, 7, 6), (78, 40, 22)


def font(size, weight='SemiBold'):
    for name in (f'InterDisplay-{weight}.otf', f'InterDisplay-{weight}.ttf', f'Inter-{weight}.ttf', 'DejaVuSans.ttf'):
        for folder in ('/usr/share/fonts/truetype/inter', '/usr/share/fonts/opentype/inter', '/usr/share/fonts/truetype/dejavu', 'C:/Windows/Fonts'):
            path = os.path.join(folder, name)
            if os.path.exists(path):
                return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def lines(size):
    """The icon's gold lines alone (its dark square and glow left out), size x size."""
    src = icon.resize((size, size), Image.LANCZOS)
    out = Image.new('RGBA', src.size, (0, 0, 0, 0))
    px, po = src.load(), out.load()
    for y in range(src.height):
        for x in range(src.width):
            r, g, b, a = px[x, y]
            lum = 0.3 * r + 0.59 * g + 0.11 * b
            alpha = max(0, min(255, int((lum - 70) * 255 / 110)))
            po[x, y] = (r, g, b, alpha * a // 255)
    return out


def background(w, h, glow_y):
    img = Image.new('RGB', (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)))
    glow = Image.new('L', (w, h), 0)
    ImageDraw.Draw(glow).ellipse([w * -0.15, glow_y - w * 0.75, w * 1.15, glow_y + w * 0.75], fill=150)
    glow = glow.filter(ImageFilter.GaussianBlur(w * 0.28))
    img.paste(Image.new('RGB', (w, h), GLOW), (0, 0), glow)
    return img


def tall(scale):
    S = 4  # drawn 4x bigger, then made smaller: smooth lines and letters
    w, h = 164 * scale * S // 100, 314 * scale * S // 100
    img = background(w, h, h * 0.36)
    logo = lines(int(w * 0.62))
    img.paste(logo, ((w - logo.width) // 2, int(h * 0.36 - logo.height / 2)), logo)
    d = ImageDraw.Draw(img)
    word, f = 'ATREIA', font(int(w * 0.135))
    spacing = w * 0.03
    widths = [d.textlength(ch, font=f) for ch in word]
    x = (w - (sum(widths) + spacing * (len(word) - 1))) / 2
    y = h * 0.575
    for ch, cw in zip(word, widths):
        d.text((x, y), ch, font=f, fill=GOLD)
        x += cw + spacing
    sub, fs = 'APP', font(int(w * 0.06), 'Medium')
    sw = [d.textlength(ch, font=fs) for ch in sub]
    sp = w * 0.045
    x = (w - (sum(sw) + sp * (len(sub) - 1))) / 2
    for ch, cw in zip(sub, sw):
        d.text((x, y + w * 0.2), ch, font=fs, fill=(170, 140, 95))
        x += cw + sp
    d.line([(w * 0.3, h * 0.92), (w * 0.7, h * 0.92)], fill=(120, 92, 58), width=max(1, S * scale // 100))
    return img.resize((w // S, h // S), Image.LANCZOS)


def small(scale):
    side = 55 * scale // 100
    return icon.resize((side, side), Image.LANCZOS).convert('RGB')


for scale in (100, 150, 200):
    tall(scale).save(os.path.join(here, f'wizard-{scale}.bmp'))
    small(scale).save(os.path.join(here, f'small-{scale}.bmp'))
    print('made', scale)
