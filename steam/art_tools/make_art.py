# Steam store art for GAFIL. Sprites are parsed from the app's own C# source; the logo uses an original 5x7 pixel font.
import re, glob, os, sys
from PIL import Image, ImageDraw, ImageFilter

SRC = sys.argv[1]          # ClaudeDesk source folder
OUT = sys.argv[2]          # output folder (steam/art)
HERE = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

# ---------- sprites from source ----------
F = {}
for path in glob.glob(os.path.join(SRC, '*.cs')):
    text = re.sub(r'//[^\n]*', '', open(path, encoding='utf-8-sig').read())   # drop comments
    for m in re.finditer(r'string\[\]\s+(\w+)\s*=[^{;(]*\{(.*?)\};', text, re.S):
        rows = re.findall(r'"([^"]*)"', m.group(2))
        if len(rows) == 12 and all(len(r) == 16 for r in rows):
            F[m.group(1)] = rows

def hx(v): return ((v >> 16) & 255, (v >> 8) & 255, v & 255)
SKINS = {
    'orange': (0xE07B4F, 0xB95A36, 0xFBE3D0, 0x1A1A1A, 0xF39C9C),
    'black':  (0x34302F, 0x221F1F, 0x56504E, 0xC8E05A, 0xE79AA4),
    'white':  (0xF4F0EA, 0xDDD4C8, 0xFFFFFF, 0x2A5BA8, 0xF3A7B0),
    'gray':   (0x9DA1A8, 0x6B6F77, 0xE3E5E8, 0x1A1A1A, 0xEFA3A8),
    'cream':  (0xF2C57C, 0xD69D4C, 0xFFF3DC, 0x1A1A1A, 0xF3A0A0),
}
def palette(skin='orange'):
    o, d, l, e, p = SKINS[skin]
    return {'K': (0x3A, 0x22, 0x18), 'O': hx(o), 'D': hx(d), 'L': hx(l), 'E': hx(e), 'P': hx(p),
            'Z': (255, 255, 255), 'W': (255, 255, 255), 'H': (255, 105, 150)}

def sprite(name, cell, skin='orange', flip=False):
    rows = F[name]
    pal = palette(skin)
    im = Image.new('RGBA', (16 * cell, 12 * cell), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    for y, r in enumerate(rows):
        for x, c in enumerate(r):
            if c in pal:
                d.rectangle([x * cell, y * cell, x * cell + cell - 1, y * cell + cell - 1], fill=pal[c])
    return im.transpose(Image.FLIP_LEFT_RIGHT) if flip else im

# ---------- original pixel font (5x7) ----------
GLYPH = {
    'G': [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
    'A': [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    'F': ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
    'I': ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
    'L': ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
}
INK = (0x3A, 0x22, 0x18); ACCENT = (0xD9, 0x65, 0x3B); PAPER = (0xFF, 0xFB, 0xF5)

def logo(cell, color=ACCENT, shadow=INK):
    word = "GAFIL"; gap = 1
    w = (len(word) * (5 + gap) - gap) * cell + cell; h = 7 * cell + cell
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    off = max(2, cell // 4)
    for layer, (dx, col) in enumerate([(off, shadow), (0, color)]):
        x0 = 0
        for ch in word:
            for y, r in enumerate(GLYPH[ch]):
                for x, c in enumerate(r):
                    if c == '#':
                        px, py = x0 + x * cell + dx, y * cell + dx
                        d.rectangle([px, py, px + cell - 1, py + cell - 1], fill=col)
            x0 += (5 + gap) * cell
    return im

# ---------- backgrounds ----------
def gradient(w, h, top=(0x9C, 0xB8, 0xD8), bot=(0xF6, 0xD9, 0xC4), hills=True, sun=True):
    im = Image.new('RGB', (w, h)); d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bot)))
    if hills:
        layer = Image.new('RGBA', (w, h), (0, 0, 0, 0)); g = ImageDraw.Draw(layer)
        g.ellipse([-0.25 * w, 0.66 * h, 0.6 * w, 1.45 * h], fill=(0xE9, 0xB8, 0x9C, 150))
        g.ellipse([0.35 * w, 0.72 * h, 1.3 * w, 1.5 * h], fill=(0xDD, 0xA5, 0x8A, 140))
        if sun: g.ellipse([0.72 * w, 0.14 * h, 0.72 * w + 0.16 * h, 0.30 * h], fill=(255, 244, 226, 120))
        layer = layer.filter(ImageFilter.GaussianBlur(max(2, w // 400)))
        im = Image.alpha_composite(im.convert('RGBA'), layer).convert('RGB')
    return im.convert('RGBA')

def mock_window(w, h, s=1.0, title_lines=True):
    """A plain, generic app window (no real app UI)."""
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    r = int(10 * s); bar = int(36 * s)
    d.rounded_rectangle([0, 0, w - 1, h - 1], r, fill=(250, 250, 252), outline=(205, 208, 216), width=max(1, int(s)))
    d.rounded_rectangle([0, 0, w - 1, bar], r, fill=(236, 238, 243))
    d.rectangle([0, bar - r, w - 1, bar], fill=(236, 238, 243))
    for i in range(3):   # caption buttons
        cx = w - int((22 + i * 34) * s)
        d.line([cx - 5 * s, bar / 2, cx + 5 * s, bar / 2], fill=(150, 154, 165), width=max(1, int(1.5 * s)))
    if title_lines:
        y = bar + int(24 * s)
        while y < h - int(30 * s):
            d.rounded_rectangle([int(24 * s), y, int(24 * s) + int((w - 48 * s) * (0.55 + 0.4 * ((y // 7) % 3) / 2)), y + int(10 * s)], int(4 * s), fill=(226, 229, 236))
            y += int(26 * s)
    return im

def shadow(im, off=8, blur=14, alpha=70):
    sh = Image.new('RGBA', (im.width + off * 4, im.height + off * 4), (0, 0, 0, 0))
    a = im.split()[3].point(lambda v: alpha if v > 0 else 0)
    sh.paste((40, 20, 10, 255), (off * 2, off * 2 + off), a)
    return sh.filter(ImageFilter.GaussianBlur(blur))

def paste(base, im, x, y, drop=False):
    if drop:
        sh = shadow(im); base.alpha_composite(sh, (int(x - 16), int(y - 16)))
    base.alpha_composite(im, (int(x), int(y)))

# ---------- screenshots (1920x1080) ----------
W, H = 1920, 1080
def bubble_shot(win_png, out, side='right'):
    bg = gradient(W, H)
    paste(bg, mock_window(1000, 640, 1.4), 120, 170, drop=True)
    win = Image.open(win_png).convert('RGBA')
    win = win.resize((win.width * 2 // 3, win.height * 2 // 3), Image.LANCZOS)   # 3x render -> 2x (cat cells stay crisp)
    x = W - win.width - 160 if side == 'right' else 160
    paste(bg, win, x, H - win.height - 6)
    bg.convert('RGB').save(out, quality=95)

def life_shot(out):
    bg = gradient(W, H); c = 10
    # big window: cat lying on the top edge with its tail hanging
    big = (140, 300, 980, 600); paste(bg, mock_window(big[2], big[3], 1.4), big[0], big[1], drop=True)
    led = sprite('LedgeA', c); paste(bg, led, big[0] + 520, big[1] - 9 * c)
    # small new window: cat sitting in it like a box
    small = (1250, 330, 420, 300); paste(bg, mock_window(small[2], small[3], 1.2), small[0], small[1], drop=True)
    box = sprite('BoxA', c); paste(bg, box, small[0] + 120, small[1] - 11 * c)
    # another window: peeking down from its edge
    mid = (1080, 700, 640, 300); paste(bg, mock_window(mid[2], mid[3], 1.2), mid[0], mid[1], drop=True)
    peek = sprite('PeekFrame', c, flip=True); paste(bg, peek, mid[0] + 40, mid[1] - 9 * c)
    # on the floor: walking and kneading
    paste(bg, sprite('WalkA', c), 420, H - 12 * c - 4)
    paste(bg, sprite('KneadA', c), 760, H - 12 * c - 4)
    bg.convert('RGB').save(out, quality=95)

def skins_shot(out):
    bg = gradient(W, H); c = 12
    shelf = (260, 560, 1400, 360); paste(bg, mock_window(shelf[2], shelf[3], 1.4), shelf[0], shelf[1], drop=True)
    names = ['orange', 'black', 'white', 'gray', 'cream']
    poses = ['SitFrame', 'LedgeA', 'SitFrame', 'PeekFrame', 'SitFrame']
    for i, (sk, po) in enumerate(zip(names, poses)):
        im = sprite(po, c, sk, flip=(i % 2 == 1))
        x = shelf[0] + 70 + i * 270
        edge_row = 12 if po == 'SitFrame' else 9
        paste(bg, im, x, shelf[1] - edge_row * c)
    for i, sk in enumerate(['cream', 'gray']):
        paste(bg, sprite('WalkA' if i == 0 else 'LieFrame', c, sk, flip=(i == 0)), 520 + i * 700, H - 12 * c - 4)
    bg.convert('RGB').save(out, quality=95)

for lang in ['en', 'ko']:
    d = os.path.join(OUT, 'screenshots', lang); os.makedirs(d, exist_ok=True)
    bubble_shot(os.path.join(HERE, f'win_{lang}_chat.png'), os.path.join(d, '1_chat.png'))
    bubble_shot(os.path.join(HERE, f'win_{lang}_quick.png'), os.path.join(d, '2_quick_answers.png'), side='left')
    life_shot(os.path.join(d, '3_windows.png'))
    bubble_shot(os.path.join(HERE, f'win_{lang}_intro.png'), os.path.join(d, '4_skills_and_secrets.png'))
    skins_shot(os.path.join(d, '5_looks.png'))
    bubble_shot(os.path.join(HERE, f'win_{lang}_churu.png'), os.path.join(d, '6_churu.png'), side='left')

# ---------- capsules ----------
def capsule(w, h, cat_cell, logo_cell, layout='side', with_logo=True, pose='SitFrame'):
    bg = gradient(w, h, sun=False)
    cat = sprite(pose, cat_cell)
    if layout == 'side':
        lg = logo(logo_cell)
        total = cat.width + int(0.06 * w) + lg.width
        x0 = (w - total) // 2
        paste(bg, cat, x0, h - cat.height - int(0.12 * h))
        if with_logo: paste(bg, lg, x0 + cat.width + int(0.06 * w), (h - lg.height) // 2 - int(0.04 * h))
    else:   # stacked: logo on top, cat below
        lg = logo(logo_cell)
        if with_logo: paste(bg, lg, (w - lg.width) // 2, int(0.12 * h))
        paste(bg, cat, (w - cat.width) // 2, h - cat.height - int(0.10 * h))
    return bg.convert('RGB')

caps = os.path.join(OUT, 'capsules'); os.makedirs(caps, exist_ok=True)
capsule(920, 430, 16, 16).save(os.path.join(caps, 'header_capsule_920x430.png'))
capsule(462, 174, 7, 8).save(os.path.join(caps, 'small_capsule_462x174.png'))
capsule(1232, 706, 26, 22).save(os.path.join(caps, 'main_capsule_1232x706.png'))
capsule(748, 896, 30, 18, layout='stacked').save(os.path.join(caps, 'vertical_capsule_748x896.png'))

lib = os.path.join(OUT, 'library'); os.makedirs(lib, exist_ok=True)
capsule(600, 900, 24, 15, layout='stacked').save(os.path.join(lib, 'library_capsule_600x900.png'))
capsule(920, 430, 16, 16).save(os.path.join(lib, 'library_header_920x430.png'))
# hero: no logo or text, cat lying on a window edge
hero = gradient(3840, 1240)
paste(hero, mock_window(1500, 700, 2.4), 1170, 620, drop=True)
paste(hero, sprite('LedgeA', 30), 1170 + 700, 620 - 9 * 30)
hero.convert('RGB').save(os.path.join(lib, 'library_hero_3840x1240.png'))
lg = logo(40); canvas = Image.new('RGBA', (1280, 720), (0, 0, 0, 0))
canvas.alpha_composite(lg, ((1280 - lg.width) // 2, (720 - lg.height) // 2)); canvas.save(os.path.join(lib, 'library_logo_1280x720.png'))

# page background (optional, subtle) and community icon (cat head)
gradient(1438, 810).convert('RGB').save(os.path.join(OUT, 'page_background_1438x810.png'))
head = sprite('SitFrame', 23).crop((9 * 23, 0, 16 * 23, 6 * 23))
icon = Image.new('RGBA', (184, 184), PAPER + (255,))
icon.alpha_composite(head, ((184 - head.width) // 2, (184 - head.height) // 2))
icon.convert('RGB').save(os.path.join(OUT, 'community_icon_184x184.jpg'), quality=95)
print('frames:', sorted(F))
print('done')
