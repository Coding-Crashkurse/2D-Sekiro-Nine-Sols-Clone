"""
ASHEN SOL — procedural sprite generator.

Style: ink-black silhouettes with luminous accents (teal / red / gold), soft baked glows,
rim light from the upper right, subtle grain. Everything faces RIGHT. 100 px = 1 unit.

Run:  python Tools/gen_sprites.py
Out:  AshenSol/Assets/_Game/Resources/Sprites/*.png  +  Tools/contact_sheet.png
"""
import os, math, random, urllib.request
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Sprites")
FONTDIR = os.path.join(ROOT, "AshenSol", "Assets", "_Game", "Resources", "Fonts")
SS = 3  # supersample factor

os.makedirs(OUT, exist_ok=True)
os.makedirs(FONTDIR, exist_ok=True)
random.seed(7)
np.random.seed(7)

# ---------------------------------------------------------------- palette
INK    = (7, 9, 15)
NIGHT  = (13, 21, 38)
DEEP   = (19, 34, 56)
SLATE  = (36, 59, 82)
STONE  = (90, 107, 122)
BONE   = (232, 224, 208)
TEAL   = (79, 227, 208)
TEALD  = (26, 143, 134)
RED    = (255, 58, 58)
REDD   = (163, 18, 31)
GOLD   = (255, 204, 85)
AMBER  = (255, 154, 60)
VIOLET = (123, 92, 255)
WHITE  = (255, 255, 255)

def mix(a, b, t):
    return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))

def shade(c, f):
    return tuple(max(0, min(255, int(v * f))) for v in c)

# ---------------------------------------------------------------- helpers
def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))

def mask_new(w, h):
    return Image.new("L", (w * SS, h * SS), 0)

def P(pts):
    return [(x * SS, y * SS) for x, y in pts]

def R(box):
    return (box[0] * SS, box[1] * SS, box[2] * SS, box[3] * SS)

def draw_on(mask, fn):
    d = ImageDraw.Draw(mask)
    fn(d)
    return mask

def poly(mask, pts, v=255):
    ImageDraw.Draw(mask).polygon(P(pts), fill=v)
    return mask

def ell(mask, box, v=255):
    ImageDraw.Draw(mask).ellipse(R(box), fill=v)
    return mask

def rect(mask, box, v=255):
    ImageDraw.Draw(mask).rectangle(R(box), fill=v)
    return mask

def rrect(mask, box, r, v=255):
    ImageDraw.Draw(mask).rounded_rectangle(R(box), radius=r * SS, fill=v)
    return mask

def line(mask, pts, w, v=255):
    ImageDraw.Draw(mask).line(P(pts), fill=v, width=max(1, int(w * SS)), joint="curve")
    return mask

def grad_fill(mask, c_top, c_bot):
    """Fill a mask with a vertical gradient."""
    W, H = mask.size
    t = np.linspace(0.0, 1.0, H)[:, None]
    arr = np.zeros((H, W, 4), np.uint8)
    for i in range(3):
        arr[:, :, i] = (c_top[i] * (1 - t) + c_bot[i] * t).astype(np.uint8)
    img = Image.fromarray(arr, "RGBA")
    img.putalpha(mask)
    return img

def solid(mask, color):
    img = Image.new("RGBA", mask.size, color + (0,))
    img.putalpha(mask)
    return img

def rim(mask, color, ox=-2.0, oy=2.0, alpha=210, blur=0.7, inner=True):
    """Edge highlight: the sliver of `mask` not covered by a shifted copy."""
    sh = ImageChops.offset(mask, int(ox * SS), int(oy * SS))
    edge = ImageChops.subtract(mask, sh)
    if blur > 0:
        edge = edge.filter(ImageFilter.GaussianBlur(blur * SS))
    if inner:
        edge = ImageChops.multiply(edge, mask)
    layer = Image.new("RGBA", mask.size, color + (0,))
    layer.putalpha(edge.point(lambda v: int(v * alpha / 255)))
    return layer

def inner_shadow(mask, color=(0, 0, 0), ox=2.0, oy=-2.0, alpha=120, blur=1.6):
    sh = ImageChops.offset(mask, int(ox * SS), int(oy * SS))
    edge = ImageChops.subtract(mask, sh).filter(ImageFilter.GaussianBlur(blur * SS))
    edge = ImageChops.multiply(edge, mask)
    layer = Image.new("RGBA", mask.size, color + (0,))
    layer.putalpha(edge.point(lambda v: int(v * alpha / 255)))
    return layer

def glow(mask, color, radius, strength=1.0):
    g = mask.filter(ImageFilter.GaussianBlur(radius * SS))
    layer = Image.new("RGBA", mask.size, color + (0,))
    layer.putalpha(g.point(lambda v: min(255, int(v * strength))))
    return layer

def over(base, *layers):
    for l in layers:
        if l is not None:
            base = Image.alpha_composite(base, l)
    return base

def noise(img, amount=7, seed=0, only_opaque=True):
    arr = np.array(img).astype(np.int16)
    rng = np.random.default_rng(seed)
    n = rng.integers(-amount, amount + 1, size=arr.shape[:2])
    if only_opaque:
        n = n * (arr[:, :, 3] > 8)
    for i in range(3):
        arr[:, :, i] = np.clip(arr[:, :, i] + n, 0, 255)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")

SAVED = []

def save(img, w, h, name, grain=0):
    if grain:
        img = noise(img, grain, seed=abs(hash(name)) % 9999)
    out = img.resize((w, h), Image.LANCZOS)
    out.save(os.path.join(OUT, name + ".png"))
    SAVED.append((name, out))
    return out

def body(w, h, shape_fn, c_top, c_bot, rim_color=BONE, rim_alpha=170, glow_color=None,
         glow_radius=4, glow_strength=0.5, detail_fn=None, grain=6, rim_ox=-2.0, rim_oy=2.0):
    """Standard character/prop part: silhouette + gradient + rim light + optional glow + details."""
    m = mask_new(w, h)
    shape_fn(m)
    layers = []
    if glow_color:
        layers.append(glow(m, glow_color, glow_radius, glow_strength))
    layers.append(grad_fill(m, c_top, c_bot))
    layers.append(inner_shadow(m, (0, 0, 0), -rim_ox, -rim_oy, 110, 2.0))
    layers.append(rim(m, rim_color, rim_ox, rim_oy, rim_alpha))
    img = over(canvas(w, h), *layers)
    if detail_fn:
        img = detail_fn(img, m)
    return img

def stroke(img, pts, color, width, alpha=255, blur=0.0, glow_color=None, glow_r=2.5, glow_s=0.7):
    """Draw a line/curve onto an RGBA image (optionally with a glow)."""
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).line(P(pts), fill=alpha, width=max(1, int(width * SS)), joint="curve")
    if blur:
        m = m.filter(ImageFilter.GaussianBlur(blur * SS))
    layers = []
    if glow_color:
        layers.append(glow(m, glow_color, glow_r, glow_s))
    layers.append(solid(m, color))
    return over(img, *layers)

def blob(img, box, color, alpha=255, blur=0.0, glow_color=None, glow_r=3, glow_s=0.8, kind="ellipse", radius=3):
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    if kind == "ellipse":
        d.ellipse(R(box), fill=alpha)
    elif kind == "rrect":
        d.rounded_rectangle(R(box), radius=radius * SS, fill=alpha)
    else:
        d.rectangle(R(box), fill=alpha)
    if blur:
        m = m.filter(ImageFilter.GaussianBlur(blur * SS))
    layers = []
    if glow_color:
        layers.append(glow(m, glow_color, glow_r, glow_s))
    layers.append(solid(m, color))
    return over(img, *layers)

def polyfill(img, pts, color, alpha=255, blur=0.0, glow_color=None, glow_r=3, glow_s=0.8):
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).polygon(P(pts), fill=alpha)
    if blur:
        m = m.filter(ImageFilter.GaussianBlur(blur * SS))
    layers = []
    if glow_color:
        layers.append(glow(m, glow_color, glow_r, glow_s))
    layers.append(solid(m, color))
    return over(img, *layers)

# ================================================================ PLAYER
ROBE_T = (26, 34, 52)
ROBE_B = (11, 14, 24)

def player_torso():
    w, h = 46, 62
    def shape(m):
        poly(m, [(15, 0), (31, 0), (40, 8), (43, 22), (37, 38), (40, 55), (36, 62),
                 (10, 62), (6, 55), (9, 38), (3, 22), (6, 8)])
    def details(img, m):
        # red sash across the waist
        img = polyfill(img, [(5, 34), (41, 30), (41, 40), (6, 44)], REDD, 235)
        img = polyfill(img, [(5, 34), (41, 30), (41, 33), (5, 37)], RED, 200)
        # sash knot + tail
        img = blob(img, (28, 31, 39, 42), RED, 240, kind="rrect", radius=3)
        # gold trim on the robe opening
        img = stroke(img, [(30, 2), (34, 16), (31, 32)], GOLD, 1.2, 210, glow_color=GOLD, glow_r=2, glow_s=0.35)
        img = stroke(img, [(11, 4), (8, 18), (10, 32)], shade(GOLD, 0.6), 1.0, 150)
        # teal chest glyph
        img = blob(img, (22, 12, 34, 24), TEAL, 0, blur=0.0, glow_color=TEAL, glow_r=3.5, glow_s=0.55)
        m2 = Image.new("L", img.size, 0)
        d = ImageDraw.Draw(m2)
        d.ellipse(R((24, 14, 32, 22)), outline=255, width=int(1.2 * SS))
        d.line(P([(28, 14), (28, 22)]), fill=255, width=int(1.0 * SS))
        img = over(img, glow(m2, TEAL, 2.5, 0.8), solid(m2, mix(TEAL, WHITE, 0.4)))
        # shoulder plate
        img = polyfill(img, [(30, 1), (41, 8), (43, 20), (34, 14)], mix(ROBE_T, BONE, 0.16), 220)
        return img
    return body(w, h, shape, ROBE_T, ROBE_B, BONE, 150, detail_fn=details)

def player_head():
    w, h = 44, 46
    def shape(m):
        # hood: pointed at the back-left, brim over the face at the right
        poly(m, [(20, 1), (31, 3), (39, 12), (41, 24), (36, 34), (26, 40), (14, 41),
                 (5, 34), (2, 20), (7, 8)])
        poly(m, [(2, 22), (10, 30), (0, 44), (0, 30)])   # hood tail
    def details(img, m):
        # face slit
        img = polyfill(img, [(26, 18), (39, 21), (37, 30), (25, 29)], (18, 20, 30), 255)
        img = polyfill(img, [(27, 20), (37, 22), (35, 27), (26, 27)], mix(BONE, (120, 110, 110), 0.5), 220)
        # eye glint
        img = blob(img, (31, 22, 36, 26), mix(TEAL, WHITE, 0.5), 255, glow_color=TEAL, glow_r=3, glow_s=0.9)
        # hood brim highlight
        img = stroke(img, [(22, 2), (33, 5), (40, 14)], BONE, 1.1, 160)
        # red headband tail
        img = polyfill(img, [(6, 12), (2, 20), (0, 34), (5, 24), (9, 16)], REDD, 235)
        img = stroke(img, [(7, 13), (3, 22), (2, 32)], RED, 0.9, 190)
        return img
    return body(w, h, shape, mix(ROBE_T, (34, 44, 66), 0.4), ROBE_B, BONE, 150, detail_fn=details)

def limb(w, h, name, c_top, c_bot, accent=None, hand=True, rim_c=BONE, wrap=None):
    """Generic arm/leg: pivot at the TOP CENTER, tapering downward."""
    def shape(m):
        poly(m, [(w * 0.16, 1), (w * 0.84, 1), (w * 0.92, h * 0.35), (w * 0.78, h * 0.78),
                 (w * 0.80, h - 1), (w * 0.20, h - 1), (w * 0.22, h * 0.78), (w * 0.08, h * 0.35)])
        ell(m, (w * 0.10, 0, w * 0.90, h * 0.22))
        if hand:
            ell(m, (w * 0.12, h * 0.80, w * 0.88, h))
    def details(img, m):
        if wrap:
            for y in wrap:
                img2 = polyfill(img, [(w * 0.14, y), (w * 0.86, y - 1), (w * 0.86, y + 2.5), (w * 0.14, y + 3.5)], accent or REDD, 220)
                img = img2
        img = stroke(img, [(w * 0.72, h * 0.15), (w * 0.80, h * 0.5), (w * 0.72, h * 0.85)], mix(c_top, BONE, 0.35), 1.0, 130)
        return img
    return body(w, h, shape, c_top, c_bot, rim_c, 150, detail_fn=details)

def player_sword():
    w, h = 14, 96
    def shape(m):
        poly(m, [(7, 0), (10, 12), (10, 66), (4, 66), (4, 12)])        # blade
        rect(m, (1, 66, 13, 70))                                        # guard
        rrect(m, (5, 70, 9, 92), 2)                                     # grip
        ell(m, (4, 90, 10, 96))                                         # pommel
    def details(img, m):
        blade = mask_new(w, h)
        poly(blade, [(7, 0), (10, 12), (10, 66), (4, 66), (4, 12)])
        img = over(img, glow(blade, TEAL, 4.5, 0.55))
        img = polyfill(img, [(7, 1), (9.4, 13), (9.4, 65), (4.6, 65), (4.6, 13)], mix(BONE, TEAL, 0.25), 255)
        img = stroke(img, [(7, 3), (7, 64)], WHITE, 1.0, 230, glow_color=TEAL, glow_r=2.5, glow_s=0.9)
        img = blob(img, (1, 66, 13, 70), GOLD, 235, kind="rrect", radius=1)
        img = blob(img, (5, 70, 9, 92), (28, 20, 24), 255, kind="rrect", radius=2)
        img = stroke(img, [(6, 72), (6, 90)], REDD, 0.8, 200)
        img = blob(img, (4, 90, 10, 96), GOLD, 235)
        return img
    return body(w, h, shape, mix(BONE, TEAL, 0.2), mix(SLATE, INK, 0.4), WHITE, 120, detail_fn=details, grain=3)

def player_sash_seg():
    w, h = 12, 12
    img = canvas(w, h)
    img = blob(img, (1, 1, 11, 11), REDD, 255, kind="rrect", radius=4, glow_color=RED, glow_r=2.5, glow_s=0.3)
    img = blob(img, (2, 1.5, 10, 6), RED, 170, kind="rrect", radius=3)
    return img

# ================================================================ GRUNT (husk soldier)
HUSK_T = (52, 60, 70)
HUSK_B = (20, 24, 32)

def crack_lines(img, w, h, n=5, color=RED, seed=3):
    rng = random.Random(seed)
    for _ in range(n):
        x = rng.uniform(w * 0.2, w * 0.8)
        y = rng.uniform(h * 0.15, h * 0.8)
        pts = [(x, y)]
        for _ in range(3):
            x += rng.uniform(-w * 0.12, w * 0.12)
            y += rng.uniform(h * 0.06, h * 0.16)
            pts.append((x, y))
        img = stroke(img, pts, color, 0.8, 190, glow_color=color, glow_r=2.5, glow_s=0.45)
    return img

def grunt_torso():
    w, h = 52, 62
    def shape(m):
        poly(m, [(16, 0), (34, 0), (45, 7), (48, 24), (41, 40), (44, 58), (38, 62),
                 (13, 62), (8, 56), (11, 40), (4, 24), (7, 7)])
    def details(img, m):
        img = polyfill(img, [(8, 30), (44, 27), (44, 36), (9, 39)], shade(HUSK_B, 1.4), 220)   # belt
        img = polyfill(img, [(32, 1), (46, 8), (48, 22), (36, 15)], mix(HUSK_T, BONE, 0.2), 230)  # pauldron
        img = stroke(img, [(30, 3), (33, 18), (30, 30)], shade(BONE, 0.55), 1.0, 130)
        img = crack_lines(img, w, h, 5, RED, 3)
        img = blob(img, (22, 12, 30, 20), REDD, 0, glow_color=RED, glow_r=4, glow_s=0.5)
        return img
    return body(w, h, shape, HUSK_T, HUSK_B, mix(BONE, STONE, 0.4), 150, detail_fn=details)

def grunt_head():
    w, h = 38, 38
    def shape(m):
        poly(m, [(14, 1), (28, 4), (34, 14), (32, 28), (22, 35), (10, 33), (5, 22), (7, 8)])
    def details(img, m):
        img = polyfill(img, [(12, 8), (32, 12), (30, 26), (11, 24)], mix(BONE, STONE, 0.35), 245)  # mask
        img = stroke(img, [(20, 8), (19, 26)], shade(STONE, 0.7), 0.9, 200)
        img = blob(img, (15, 14, 20, 19), REDD, 255, glow_color=RED, glow_r=3.5, glow_s=1.0)
        img = blob(img, (24, 15, 29, 20), REDD, 255, glow_color=RED, glow_r=3.5, glow_s=1.0)
        img = crack_lines(img, w, h, 3, RED, 11)
        return img
    return body(w, h, shape, HUSK_T, HUSK_B, BONE, 130, detail_fn=details)

def grunt_blade():
    w, h = 12, 72
    def shape(m):
        poly(m, [(6, 0), (10, 14), (9, 52), (3, 52), (2, 14)])
        rect(m, (1, 52, 11, 56))
        rrect(m, (4, 56, 8, 70), 2)
    def details(img, m):
        img = polyfill(img, [(6, 2), (9, 15), (8.4, 51), (3.4, 51), (3, 15)], (86, 74, 64), 255)
        img = stroke(img, [(7.6, 6), (7.8, 50)], AMBER, 0.9, 215, glow_color=AMBER, glow_r=2.5, glow_s=0.7)
        img = blob(img, (1, 52, 11, 56), shade(AMBER, 0.55), 235, kind="rrect", radius=1)
        img = blob(img, (4, 56, 8, 70), (30, 24, 22), 255, kind="rrect", radius=2)
        return img
    return body(w, h, shape, (96, 82, 70), (36, 30, 28), mix(BONE, AMBER, 0.3), 120, detail_fn=details, grain=4)

# ================================================================ SPEAR SENTINEL
SENT_T = (34, 52, 66)
SENT_B = (13, 20, 30)

def spear_torso():
    w, h = 50, 66
    def shape(m):
        poly(m, [(17, 0), (33, 0), (43, 8), (46, 26), (39, 42), (42, 60), (36, 66),
                 (13, 66), (7, 60), (10, 42), (3, 26), (6, 8)])
    def details(img, m):
        for y in (16, 26, 36):
            img = polyfill(img, [(6, y), (44, y - 2), (44, y + 2), (6, y + 4)], mix(SENT_T, BONE, 0.14), 200)
        img = polyfill(img, [(31, 1), (44, 9), (46, 24), (35, 16)], mix(SENT_T, BONE, 0.25), 235)
        img = stroke(img, [(25, 4), (25, 40)], TEALD, 1.0, 190, glow_color=TEAL, glow_r=3, glow_s=0.4)
        img = blob(img, (21, 44, 29, 52), TEALD, 200, glow_color=TEAL, glow_r=4, glow_s=0.6)
        return img
    return body(w, h, shape, SENT_T, SENT_B, mix(BONE, TEAL, 0.15), 160, detail_fn=details)

def spear_head():
    w, h = 36, 42
    def shape(m):
        poly(m, [(16, 0), (26, 3), (31, 12), (30, 30), (20, 39), (10, 36), (6, 22), (9, 7)])
        poly(m, [(14, 0), (22, 1), (20, 8), (12, 6)])   # crest
    def details(img, m):
        img = polyfill(img, [(18, 12), (30, 15), (29, 25), (17, 23)], (10, 16, 24), 255)   # visor
        img = stroke(img, [(19, 17), (29, 19)], TEAL, 1.4, 255, glow_color=TEAL, glow_r=4, glow_s=1.0)
        img = stroke(img, [(15, 2), (24, 5), (30, 13)], mix(BONE, TEAL, 0.25), 1.1, 170)
        img = polyfill(img, [(13, 0), (21, 1), (19, 7), (12, 5)], TEALD, 220)
        return img
    return body(w, h, shape, SENT_T, SENT_B, mix(BONE, TEAL, 0.2), 150, detail_fn=details)

def spear_spear():
    w, h = 10, 160
    def shape(m):
        poly(m, [(5, 0), (9, 20), (5, 30), (1, 20)])     # head
        rect(m, (3.6, 26, 6.4, 158))
        rect(m, (2.5, 150, 7.5, 158))
    def details(img, m):
        tip = mask_new(w, h)
        poly(tip, [(5, 0), (9, 20), (5, 30), (1, 20)])
        img = over(img, glow(tip, TEAL, 5, 0.6))
        img = polyfill(img, [(5, 1), (8.4, 20), (5, 28), (1.6, 20)], mix(BONE, TEAL, 0.35), 255)
        img = stroke(img, [(5, 3), (5, 27)], WHITE, 0.8, 230, glow_color=TEAL, glow_r=2.5, glow_s=0.9)
        img = blob(img, (2.6, 26, 7.4, 30), GOLD, 220, kind="rrect", radius=1)
        for y in range(40, 150, 22):
            img = polyfill(img, [(3.4, y), (6.6, y), (6.6, y + 2.5), (3.4, y + 2.5)], shade(GOLD, 0.55), 190)
        return img
    return body(w, h, shape, (60, 52, 46), (26, 24, 26), mix(BONE, TEAL, 0.2), 110, detail_fn=details, grain=3)

# ================================================================ FOUNDRY BRUTE (hammer)
BRUTE_T = (64, 48, 42)
BRUTE_B = (20, 15, 16)

def brute_torso():
    w, h = 68, 84
    def shape(m):
        poly(m, [(18, 0), (50, 0), (64, 10), (67, 34), (58, 54), (60, 78), (52, 84),
                 (16, 84), (8, 78), (10, 54), (1, 34), (4, 10)])
    def details(img, m):
        for y in (20, 34, 48):   # plate seams
            img = polyfill(img, [(8, y), (60, y - 3), (60, y + 1), (8, y + 4)], mix(BRUTE_T, BONE, 0.12), 190)
        img = polyfill(img, [(44, 1), (64, 11), (66, 30), (50, 20)], mix(BRUTE_T, STONE, 0.35), 240)   # pauldron
        img = polyfill(img, [(4, 1), (22, 0), (18, 18), (2, 28)], mix(BRUTE_T, STONE, 0.25), 220)
        # furnace grate in the chest
        img = blob(img, (26, 30, 42, 46), shade(AMBER, 0.6), 220, glow_color=AMBER, glow_r=6, glow_s=0.8, kind="rrect", radius=3)
        for y in (34, 38, 42):
            img = stroke(img, [(28, y), (40, y)], INK, 1.2, 230)
        img = polyfill(img, [(10, 62), (58, 60), (58, 66), (10, 68)], (38, 28, 26), 235)   # belt
        img = blob(img, (30, 60, 38, 68), shade(GOLD, 0.6), 220, kind="rrect", radius=1)
        return img
    return body(w, h, shape, BRUTE_T, BRUTE_B, mix(BONE, AMBER, 0.25), 150, detail_fn=details)

def brute_head():
    w, h = 46, 48
    def shape(m):
        poly(m, [(14, 2), (32, 2), (42, 12), (44, 30), (36, 44), (24, 48), (10, 42), (3, 28), (5, 12)])
        rect(m, (18, 0, 30, 6))   # crest ridge
    def details(img, m):
        img = polyfill(img, [(12, 16), (42, 20), (40, 30), (12, 28)], (12, 10, 14), 255)   # visor slot
        img = stroke(img, [(14, 22), (40, 25)], AMBER, 1.8, 255, glow_color=AMBER, glow_r=5, glow_s=1.0)
        img = stroke(img, [(8, 12), (22, 6), (38, 10)], mix(BONE, AMBER, 0.2), 1.1, 150)   # brow ridge
        for x in (12, 20, 28, 36):
            img = blob(img, (x, 34, x + 3, 37), mix(BRUTE_T, BONE, 0.4), 200)   # rivets
        return img
    return body(w, h, shape, BRUTE_T, BRUTE_B, mix(BONE, AMBER, 0.25), 140, detail_fn=details)

def brute_hammer():
    w, h = 40, 140
    IRON = (48, 40, 40)
    HAFT = (70, 52, 40)
    def shape(m):
        rrect(m, (2, 2, 38, 38), 3)        # head block
        rect(m, (16, 38, 24, 136))         # haft
        rrect(m, (13, 118, 27, 138), 3)    # grip wrap
    def details(img, m):
        head = mask_new(w, h)
        rrect(head, (2, 2, 38, 38), 3)
        img = over(img, glow(head, AMBER, 6, 0.45))
        img = polyfill(img, [(4, 4), (36, 4), (36, 36), (4, 36)], IRON, 255)
        img = polyfill(img, [(4, 4), (36, 4), (32, 10), (8, 10)], mix(IRON, BONE, 0.3), 220)   # top bevel
        # forge cracks glowing in the head
        img = stroke(img, [(8, 30), (16, 18), (14, 12)], AMBER, 1.3, 240, glow_color=AMBER, glow_r=3.5, glow_s=0.9)
        img = stroke(img, [(30, 32), (24, 22), (28, 14)], AMBER, 1.1, 220, glow_color=AMBER, glow_r=3, glow_s=0.8)
        img = stroke(img, [(16, 18), (24, 22)], AMBER, 1.0, 200, glow_color=AMBER, glow_r=3, glow_s=0.7)
        img = blob(img, (2, 12, 38, 16), (30, 26, 28), 230, kind="rrect", radius=1)   # iron bands
        img = blob(img, (2, 26, 38, 30), (30, 26, 28), 230, kind="rrect", radius=1)
        img = polyfill(img, [(17, 38), (23, 38), (23, 118), (17, 118)], HAFT, 255)
        img = stroke(img, [(20, 40), (20, 116)], mix(HAFT, BONE, 0.3), 0.8, 130)
        img = blob(img, (13, 118, 27, 138), (34, 26, 24), 255, kind="rrect", radius=3)
        for y in range(120, 136, 4):
            img = stroke(img, [(14, y), (26, y + 1)], shade(AMBER, 0.45), 0.8, 150)
        return img
    return body(w, h, shape, (56, 46, 44), (24, 20, 22), mix(BONE, AMBER, 0.3), 120, detail_fn=details, grain=4)

# ================================================================ DRONE
def drone_body():
    w, h = 58, 42
    def shape(m):
        poly(m, [(6, 20), (16, 6), (40, 4), (52, 14), (54, 24), (44, 36), (18, 38), (7, 28)])
    def details(img, m):
        img = polyfill(img, [(30, 8), (50, 14), (52, 24), (32, 32)], (14, 18, 28), 255)   # lens housing
        img = polyfill(img, [(10, 14), (24, 10), (26, 28), (12, 26)], mix(SLATE, INK, 0.4), 240)
        img = stroke(img, [(14, 12), (36, 7)], mix(BONE, STONE, 0.4), 1.1, 150)
        img = stroke(img, [(16, 22), (26, 21)], AMBER, 0.9, 170, glow_color=AMBER, glow_r=2.5, glow_s=0.4)
        img = blob(img, (2, 16, 12, 26), shade(SLATE, 0.8), 235, kind="rrect", radius=4)
        return img
    return body(w, h, shape, (44, 54, 66), (14, 18, 26), mix(BONE, STONE, 0.5), 165, detail_fn=details)

def drone_eye():
    w, h = 18, 18
    img = canvas(w, h)
    m = mask_new(w, h); ell(m, (3, 3, 15, 15))
    img = over(img, glow(m, AMBER, 5, 1.0), glow(m, AMBER, 2.5, 1.0), solid(m, AMBER))
    img = blob(img, (6, 6, 12, 12), mix(AMBER, WHITE, 0.75), 255)
    return img

def drone_ring():
    w, h = 76, 76
    img = canvas(w, h)
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    d.ellipse(R((3, 3, 73, 73)), outline=255, width=int(1.4 * SS))
    d.ellipse(R((11, 11, 65, 65)), outline=140, width=int(0.8 * SS))
    for i in range(12):
        a = i * math.pi / 6
        x0, y0 = 38 + 30 * math.cos(a), 38 + 30 * math.sin(a)
        x1, y1 = 38 + 36 * math.cos(a), 38 + 36 * math.sin(a)
        d.line(P([(x0, y0), (x1, y1)]), fill=255, width=int(1.2 * SS))
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        cx, cy = 38 + 33 * math.cos(a), 38 + 33 * math.sin(a)
        d.ellipse(R((cx - 3, cy - 3, cx + 3, cy + 3)), fill=255)
    img = over(img, glow(m, GOLD, 4, 0.55), solid(m, mix(GOLD, WHITE, 0.25)))
    return img

# ================================================================ BOSS
BOSS_T = (40, 44, 58)
BOSS_B = (10, 12, 20)

def boss_torso():
    w, h = 112, 150
    def shape(m):
        poly(m, [(34, 0), (78, 0), (98, 14), (106, 46), (92, 92), (100, 132), (86, 150),
                 (26, 150), (12, 132), (20, 92), (6, 46), (14, 14)])
    def details(img, m):
        # chest plates
        img = polyfill(img, [(20, 34), (92, 26), (96, 52), (18, 60)], mix(BOSS_T, BONE, 0.12), 220)
        img = polyfill(img, [(22, 66), (90, 60), (92, 84), (22, 90)], mix(BOSS_T, BONE, 0.07), 210)
        # heavy pauldron
        img = polyfill(img, [(72, 1), (100, 16), (107, 46), (86, 30)], mix(BOSS_T, BONE, 0.22), 240)
        img = polyfill(img, [(12, 4), (34, 1), (30, 26), (8, 34)], mix(BOSS_T, BONE, 0.12), 220)
        img = stroke(img, [(76, 4), (98, 18), (104, 44)], mix(BONE, GOLD, 0.4), 1.6, 190)
        # gold filigree
        img = stroke(img, [(56, 6), (60, 40), (56, 88), (60, 128)], GOLD, 1.4, 170, glow_color=GOLD, glow_r=3, glow_s=0.3)
        for y in (100, 112, 124):
            img = polyfill(img, [(24, y), (88, y - 3), (88, y + 3), (24, y + 6)], shade(BOSS_B, 1.6), 210)
        # core socket (the glowing core sprite sits on top at runtime)
        img = blob(img, (44, 84, 72, 112), (8, 8, 14), 255)
        img = blob(img, (46, 86, 70, 110), REDD, 90, glow_color=RED, glow_r=8, glow_s=0.55)
        return img
    return body(w, h, shape, BOSS_T, BOSS_B, mix(BONE, STONE, 0.5), 165, detail_fn=details, rim_ox=-3, rim_oy=3)

def boss_head():
    w, h = 72, 82
    def shape(m):
        poly(m, [(24, 12), (48, 10), (62, 22), (64, 46), (52, 68), (32, 74), (16, 62), (10, 36), (14, 20)])
        poly(m, [(20, 16), (8, 0), (26, 8)])       # left horn
        poly(m, [(50, 14), (68, 0), (58, 18)])     # right horn
    def details(img, m):
        img = polyfill(img, [(22, 24), (60, 22), (58, 54), (26, 60)], (94, 74, 44), 250)          # bronze mask
        img = polyfill(img, [(24, 26), (58, 24), (57, 36), (25, 38)], (128, 100, 58), 230)
        img = stroke(img, [(41, 24), (40, 58)], (60, 46, 26), 1.4, 220)
        img = blob(img, (28, 34, 38, 42), TEAL, 255, glow_color=TEAL, glow_r=5, glow_s=1.0)
        img = blob(img, (46, 33, 56, 41), TEAL, 255, glow_color=TEAL, glow_r=5, glow_s=1.0)
        for x in (30, 38, 46, 54):
            img = polyfill(img, [(x, 50), (x + 4, 50), (x + 3, 60), (x + 1, 60)], (52, 40, 24), 230)
        img = polyfill(img, [(20, 16), (8, 0), (26, 8)], mix(BONE, STONE, 0.25), 235)
        img = polyfill(img, [(50, 14), (68, 0), (58, 18)], mix(BONE, STONE, 0.25), 235)
        return img
    return body(w, h, shape, BOSS_T, BOSS_B, BONE, 150, detail_fn=details)

def boss_glaive():
    w, h = 26, 270
    def shape(m):
        poly(m, [(13, 0), (23, 30), (24, 70), (14, 96), (7, 70), (5, 30)])   # blade
        poly(m, [(3, 92), (23, 92), (20, 106), (6, 106)])                    # collar
        rect(m, (10, 104, 16, 262))                                          # shaft
        ell(m, (8, 258, 18, 270))
    def details(img, m):
        blade = mask_new(w, h)
        poly(blade, [(13, 0), (23, 30), (24, 70), (14, 96), (7, 70), (5, 30)])
        img = over(img, glow(blade, RED, 7, 0.5))
        img = polyfill(img, [(13, 2), (22, 31), (23, 69), (14, 92), (8, 69), (6, 31)], (58, 54, 62), 255)
        img = stroke(img, [(13, 4), (14, 90)], mix(BONE, RED, 0.25), 1.3, 210)
        img = stroke(img, [(21.5, 32), (22.5, 68)], RED, 1.4, 245, glow_color=RED, glow_r=4, glow_s=1.0)
        img = stroke(img, [(6.5, 32), (7.5, 68)], RED, 1.0, 190, glow_color=RED, glow_r=3, glow_s=0.7)
        img = blob(img, (3, 92, 23, 106), GOLD, 230, kind="rrect", radius=2)
        img = blob(img, (10, 104, 16, 262), (32, 28, 30), 255, kind="rrect", radius=2)
        for y in range(120, 258, 26):
            img = polyfill(img, [(9, y), (17, y), (17, y + 3), (9, y + 3)], shade(GOLD, 0.5), 200)
        img = blob(img, (8, 258, 18, 270), GOLD, 230)
        return img
    return body(w, h, shape, (70, 66, 74), (26, 24, 28), mix(BONE, RED, 0.2), 130, detail_fn=details, grain=4)

def boss_cape_seg():
    w, h = 24, 24
    img = canvas(w, h)
    img = blob(img, (1, 1, 23, 23), shade(REDD, 0.75), 255, kind="rrect", radius=6, glow_color=REDD, glow_r=3, glow_s=0.25)
    img = blob(img, (3, 2, 21, 12), REDD, 160, kind="rrect", radius=5)
    return img

def boss_core():
    w, h = 34, 34
    img = canvas(w, h)
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    pts = []
    for i in range(6):
        a = i * math.pi / 3 - math.pi / 2
        pts.append((17 + 14 * math.cos(a), 17 + 14 * math.sin(a)))
    d.polygon(P(pts), fill=255)
    img = over(img, glow(m, RED, 8, 0.9), glow(m, RED, 3, 1.0), solid(m, mix(RED, (255, 220, 200), 0.35)))
    inner = mask_new(w, h)
    ip = [(17 + 7 * math.cos(i * math.pi / 3 - math.pi / 2), 17 + 7 * math.sin(i * math.pi / 3 - math.pi / 2)) for i in range(6)]
    ImageDraw.Draw(inner).polygon(P(ip), fill=255)
    img = over(img, solid(inner, WHITE))
    return img

# ================================================================ TILES (tileable)
def tile_stone():
    w = h = 100
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    base = mask_new(w, h); rect(base, (0, 0, w, h))
    img = over(img, grad_fill(base, (34, 44, 60), (18, 24, 36)))
    rng = random.Random(21)
    # brick courses that wrap horizontally
    rows = [(0, 24), (24, 50), (50, 74), (74, 100)]
    for ri, (y0, y1) in enumerate(rows):
        offset = 0 if ri % 2 == 0 else 25
        for i in range(-1, 3):
            x0 = offset + i * 50
            for xoff in (-100, 0, 100):
                bx0, bx1 = x0 + xoff + 1.5, x0 + xoff + 48.5
                f = rng.uniform(0.86, 1.12)
                col_t = shade((44, 56, 74), f)
                col_b = shade((22, 30, 44), f)
                bm = mask_new(w, h)
                rrect(bm, (bx0, y0 + 1.5, bx1, y1 - 1.5), 2)
                img = over(img, grad_fill(bm, col_t, col_b), rim(bm, mix(BONE, STONE, 0.55), -1.2, 1.2, 60))
    # cracks + teal moss
    for _ in range(9):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        pts = [(x, y)]
        for _ in range(3):
            x += rng.uniform(-14, 14); y += rng.uniform(2, 12)
            pts.append((x, y))
        img = stroke(img, pts, (10, 12, 18), 0.9, 190)
    for _ in range(6):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        img = blob(img, (x, y, x + rng.uniform(4, 12), y + rng.uniform(3, 7)), TEALD, 60, blur=1.5, glow_color=TEAL, glow_r=3, glow_s=0.12)
    return noise(img, 9, seed=1)

def tile_stone_top():
    w, h = 100, 24
    img = canvas(w, h)
    base = mask_new(w, h); rect(base, (0, 0, w, h))
    img = over(img, grad_fill(base, (46, 60, 72), (24, 32, 46)))
    rng = random.Random(5)
    # mossy irregular top edge
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    pts = [(0, 7)]
    x = 0
    while x < w:
        x += rng.uniform(4, 11)
        pts.append((min(x, w), rng.uniform(2, 9)))
    pts += [(w, 0), (0, 0)]
    d.polygon(P(pts), fill=255)
    img = over(img, solid(m, mix(TEALD, (40, 60, 60), 0.5)), glow(m, TEAL, 3, 0.18))
    img = stroke(img, [(0, 1.2), (w, 1.2)], mix(BONE, TEAL, 0.25), 1.2, 190)
    for _ in range(12):
        x = rng.uniform(0, w)
        img = stroke(img, [(x, 8), (x + rng.uniform(-2, 2), 8 + rng.uniform(3, 9))], (14, 18, 26), 0.8, 150)
    return noise(img, 7, seed=2)

def tile_platform():
    w, h = 100, 30
    img = canvas(w, h)
    base = mask_new(w, h); rrect(base, (0, 1, w, 29), 2)
    img = over(img, grad_fill(base, (58, 46, 40), (24, 20, 22)), rim(base, mix(BONE, GOLD, 0.35), -1.2, 1.2, 140))
    for x in range(0, w, 25):
        img = polyfill(img, [(x + 2, 3), (x + 23, 3), (x + 23, 27), (x + 2, 27)], (46, 38, 34), 120)
        img = stroke(img, [(x + 12.5, 6), (x + 12.5, 24)], GOLD, 1.0, 180, glow_color=GOLD, glow_r=2.5, glow_s=0.3)
    img = stroke(img, [(0, 2.5), (w, 2.5)], mix(BONE, GOLD, 0.5), 1.2, 200)
    return noise(img, 6, seed=3)

def tile_spikes():
    w, h = 100, 40
    img = canvas(w, h)
    m = mask_new(w, h)
    for i in range(4):
        x = 12.5 + i * 25
        poly(m, [(x - 9, 40), (x, 3), (x + 9, 40)])
    img = over(img, glow(m, RED, 6, 0.35), grad_fill(m, mix(BONE, (200, 190, 180), 0.4), (70, 60, 60)),
               rim(m, WHITE, -1.4, 1.4, 200))
    for i in range(4):
        x = 12.5 + i * 25
        img = stroke(img, [(x, 6), (x, 34)], WHITE, 1.0, 120)
        img = polyfill(img, [(x - 9, 40), (x - 5, 28), (x + 5, 28), (x + 9, 40)], REDD, 150, blur=1.0)
    img = blob(img, (0, 34, w, 40), REDD, 120, blur=2.0, kind="rect", glow_color=RED, glow_r=5, glow_s=0.4)
    return noise(img, 5, seed=4)

# ================================================================ PROPS
def prop_lantern():
    w, h = 32, 52
    img = canvas(w, h)
    img = stroke(img, [(16, 0), (16, 8)], (40, 34, 30), 1.4, 255)
    m = mask_new(w, h)
    poly(m, [(9, 10), (23, 10), (26, 22), (23, 40), (9, 40), (6, 22)])
    img = over(img, glow(m, AMBER, 9, 0.75), grad_fill(m, mix(AMBER, (255, 220, 170), 0.5), shade(AMBER, 0.65)),
               rim(m, WHITE, -1.2, 1.2, 150))
    img = blob(img, (7, 8, 25, 12), (44, 34, 30), 255, kind="rrect", radius=2)
    img = blob(img, (8, 38, 24, 43), (44, 34, 30), 255, kind="rrect", radius=2)
    for y in (18, 26, 34):
        img = stroke(img, [(7, y), (25, y)], shade(AMBER, 0.45), 0.9, 190)
    img = stroke(img, [(16, 43), (16, 50)], REDD, 1.2, 230)
    return img

def pillar(w, h, broken, name):
    img = canvas(w, h)
    top = 0 if not broken else int(h * 0.18)
    m = mask_new(w, h)
    if broken:
        poly(m, [(w * 0.16, h), (w * 0.14, h * 0.30), (w * 0.34, h * 0.14), (w * 0.52, h * 0.26),
                 (w * 0.7, h * 0.10), (w * 0.86, h * 0.32), (w * 0.84, h)])
    else:
        rect(m, (w * 0.16, h * 0.10, w * 0.84, h))
        rect(m, (w * 0.05, h * 0.02, w * 0.95, h * 0.10))
        rect(m, (w * 0.02, 0, w * 0.98, h * 0.03))
    rect(m, (w * 0.06, h * 0.94, w * 0.94, h))
    img = over(img, grad_fill(m, (46, 56, 72), (16, 20, 30)), rim(m, mix(BONE, STONE, 0.5), -2, 2, 130))
    rng = random.Random(hash(name) % 999)
    for i in range(6):
        y = h * (0.15 + i * 0.13)
        img = stroke(img, [(w * 0.18, y), (w * 0.82, y - 1)], (12, 16, 24), 0.9, 130)
    for _ in range(4):
        x = rng.uniform(w * 0.2, w * 0.8); y = rng.uniform(h * 0.2, h * 0.9)
        img = stroke(img, [(x, y), (x + rng.uniform(-4, 4), y + rng.uniform(6, 18))], (10, 12, 18), 0.9, 160)
    img = stroke(img, [(w * 0.3, h * 0.35), (w * 0.3, h * 0.75)], TEALD, 1.0, 120, glow_color=TEAL, glow_r=3, glow_s=0.2)
    return noise(img, 6, seed=7)

def prop_banner():
    w, h = 44, 130
    img = canvas(w, h)
    img = blob(img, (2, 0, 42, 6), (48, 38, 32), 255, kind="rrect", radius=2)
    m = mask_new(w, h)
    poly(m, [(6, 5), (38, 5), (38, 116), (22, 128), (6, 116)])
    img = over(img, glow(m, REDD, 6, 0.3), grad_fill(m, shade(REDD, 1.25), shade(REDD, 0.55)),
               rim(m, mix(RED, BONE, 0.3), -1.4, 1.4, 130))
    img = stroke(img, [(9, 8), (9, 114)], GOLD, 1.0, 160)
    img = stroke(img, [(35, 8), (35, 114)], GOLD, 1.0, 160)
    gm = mask_new(w, h)
    d = ImageDraw.Draw(gm)
    d.ellipse(R((14, 30, 30, 46)), outline=255, width=int(1.6 * SS))
    d.line(P([(22, 30), (22, 46)]), fill=255, width=int(1.4 * SS))
    d.polygon(P([(22, 58), (30, 72), (22, 86), (14, 72)]), outline=255, width=int(1.4 * SS))
    img = over(img, glow(gm, GOLD, 3, 0.5), solid(gm, GOLD))
    return noise(img, 5, seed=8)

def prop_torii():
    w, h = 320, 280
    img = canvas(w, h)
    m = mask_new(w, h)
    poly(m, [(8, 26), (312, 4), (312, 34), (8, 56)])            # top lintel (curved feel)
    rect(m, (30, 60, 290, 84))                                   # second beam
    poly(m, [(46, 60), (86, 60), (96, 280), (56, 280)])          # left post
    poly(m, [(234, 60), (274, 60), (264, 280), (224, 280)])      # right post
    rect(m, (150, 34, 170, 62))                                  # centre plaque post
    img = over(img, grad_fill(m, (74, 30, 34), (26, 14, 20)), rim(m, mix(RED, BONE, 0.35), -2.5, 2.5, 120))
    img = blob(img, (128, 34, 192, 66), (30, 18, 22), 255, kind="rrect", radius=3)
    gm = mask_new(w, h)
    d = ImageDraw.Draw(gm)
    d.ellipse(R((146, 40, 174, 60)), outline=255, width=int(2 * SS))
    img = over(img, glow(gm, GOLD, 4, 0.5), solid(gm, GOLD))
    for x in (66, 254):
        img = stroke(img, [(x - 14, 120), (x + 14, 118)], (18, 12, 16), 1.6, 170)
        img = stroke(img, [(x - 14, 200), (x + 14, 198)], (18, 12, 16), 1.6, 170)
    return noise(img, 6, seed=9)

def prop_gate_sealed():
    w, h = 170, 280
    img = canvas(w, h)
    frame = mask_new(w, h)
    rect(frame, (0, 0, 18, 280)); rect(frame, (152, 0, 170, 280)); rect(frame, (0, 0, 170, 22))
    img = over(img, grad_fill(frame, (56, 62, 78), (20, 24, 34)), rim(frame, mix(BONE, STONE, 0.5), -2, 2, 140))
    door = mask_new(w, h)
    rect(door, (18, 22, 152, 280))
    img = over(img, grad_fill(door, (40, 34, 34), (16, 14, 18)), rim(door, mix(BONE, GOLD, 0.3), -1.6, 1.6, 90))
    img = stroke(img, [(85, 24), (85, 278)], (10, 10, 14), 1.6, 220)
    for y in range(46, 276, 38):
        img = stroke(img, [(20, y), (150, y)], (24, 20, 22), 1.4, 170)
        img = blob(img, (30, y - 4, 38, y + 4), shade(GOLD, 0.5), 200)
        img = blob(img, (132, y - 4, 140, y + 4), shade(GOLD, 0.5), 200)
    img = blob(img, (60, 120, 110, 170), (12, 10, 14), 255, kind="rrect", radius=6)
    return noise(img, 6, seed=10)

def prop_gate_glyph():
    w = h = 90
    img = canvas(w, h)
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    d.ellipse(R((8, 8, 82, 82)), outline=255, width=int(2.4 * SS))
    d.ellipse(R((22, 22, 68, 68)), outline=200, width=int(1.4 * SS))
    for i in range(8):
        a = i * math.pi / 4
        d.line(P([(45 + 24 * math.cos(a), 45 + 24 * math.sin(a)), (45 + 36 * math.cos(a), 45 + 36 * math.sin(a))]),
               fill=255, width=int(1.8 * SS))
    d.polygon(P([(45, 30), (60, 45), (45, 60), (30, 45)]), outline=255, width=int(2 * SS))
    img = over(img, glow(m, WHITE, 6, 0.5), glow(m, WHITE, 2, 0.8), solid(m, WHITE))
    return img

def prop_shrine():
    w, h = 130, 150
    img = canvas(w, h)
    m = mask_new(w, h)
    poly(m, [(4, 46), (65, 22), (126, 46), (126, 58), (4, 58)])     # roof
    rect(m, (26, 58, 104, 72))
    rect(m, (34, 72, 96, 128))
    rect(m, (16, 128, 114, 150))
    img = over(img, grad_fill(m, (48, 58, 74), (16, 20, 30)), rim(m, mix(BONE, STONE, 0.5), -2, 2, 140))
    img = blob(img, (48, 82, 82, 122), (10, 12, 18), 255, kind="rrect", radius=3)
    bowl = mask_new(w, h)
    poly(bowl, [(52, 100), (78, 100), (74, 118), (56, 118)])
    img = over(img, glow(bowl, TEAL, 10, 0.8), grad_fill(bowl, mix(TEAL, WHITE, 0.5), TEALD))
    img = stroke(img, [(65, 24), (65, 44)], mix(BONE, GOLD, 0.4), 1.6, 190)
    for x in (30, 100):
        img = stroke(img, [(x, 60), (x, 128)], (12, 16, 24), 1.4, 150)
    return noise(img, 6, seed=11)

def prop_statue():
    w, h = 150, 280
    img = canvas(w, h)
    m = mask_new(w, h)
    poly(m, [(20, 280), (26, 210), (44, 190), (40, 150), (54, 120), (48, 96), (58, 72),
             (92, 72), (102, 96), (96, 120), (110, 150), (106, 190), (124, 210), (130, 280)])
    ell(m, (56, 28, 96, 76))                      # head
    poly(m, [(52, 30), (76, 6), (100, 30), (94, 40), (58, 40)])   # crown
    rect(m, (10, 262, 140, 280))
    img = over(img, grad_fill(m, (44, 54, 70), (12, 16, 26)), rim(m, mix(BONE, STONE, 0.45), -2.5, 2.5, 130))
    img = blob(img, (64, 44, 74, 54), TEAL, 200, glow_color=TEAL, glow_r=6, glow_s=0.7)
    img = blob(img, (80, 44, 90, 54), TEAL, 200, glow_color=TEAL, glow_r=6, glow_s=0.7)
    img = stroke(img, [(60, 110), (94, 110)], (10, 14, 22), 1.6, 180)
    img = stroke(img, [(52, 150), (102, 150)], (10, 14, 22), 1.6, 160)
    rng = random.Random(13)
    for _ in range(7):
        x = rng.uniform(30, 120); y = rng.uniform(90, 250)
        img = stroke(img, [(x, y), (x + rng.uniform(-8, 8), y + rng.uniform(8, 24))], (8, 10, 16), 1.0, 170)
    return noise(img, 7, seed=12)

def prop_bamboo():
    w, h = 70, 320
    img = canvas(w, h)
    rng = random.Random(17)
    for i, x in enumerate((14, 34, 54)):
        m = mask_new(w, h)
        top = rng.uniform(6, 40)
        rect(m, (x - 5, top, x + 5, h))
        img = over(img, grad_fill(m, (44, 62, 58), (14, 22, 26)), rim(m, mix(BONE, TEALD, 0.4), -1.2, 1.2, 110))
        for y in range(int(top) + 26, h, 44):
            img = stroke(img, [(x - 6, y), (x + 6, y - 1)], (10, 16, 18), 1.2, 190)
        for y in range(int(top) + 40, h - 60, 90):
            s = 1 if i % 2 == 0 else -1
            img = stroke(img, [(x, y), (x + 16 * s, y - 12), (x + 26 * s, y - 8)], (30, 48, 44), 1.4, 200)
    return noise(img, 6, seed=14)

def prop_rock():
    w, h = 90, 56
    img = canvas(w, h)
    m = mask_new(w, h)
    poly(m, [(4, 56), (10, 30), (28, 12), (52, 8), (74, 20), (86, 40), (88, 56)])
    img = over(img, grad_fill(m, (46, 54, 68), (16, 20, 28)), rim(m, mix(BONE, STONE, 0.5), -2, 2, 140))
    img = stroke(img, [(24, 20), (40, 34), (34, 50)], (10, 14, 22), 1.2, 170)
    img = stroke(img, [(58, 16), (66, 34)], (10, 14, 22), 1.0, 150)
    img = blob(img, (52, 14, 68, 22), TEALD, 70, blur=1.6, glow_color=TEAL, glow_r=3, glow_s=0.15)
    return noise(img, 7, seed=15)

def prop_sign():
    w, h = 54, 64
    img = canvas(w, h)
    img = blob(img, (24, 24, 30, 64), (44, 34, 28), 255, kind="rrect", radius=2)
    m = mask_new(w, h)
    poly(m, [(4, 4), (50, 8), (48, 34), (6, 30)])
    img = over(img, grad_fill(m, (62, 50, 40), (30, 24, 22)), rim(m, mix(BONE, GOLD, 0.3), -1.4, 1.4, 150))
    for i, y in enumerate((13, 20, 27)):
        img = stroke(img, [(12, y), (40, y + 1)], mix(BONE, (150, 130, 110), 0.5), 1.0, 190 - i * 30)
    return noise(img, 6, seed=16)

def prop_chain():
    w, h = 20, 300
    img = canvas(w, h)
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    y = 0
    while y < h:
        d.ellipse(R((5, y, 15, y + 16)), outline=255, width=int(1.8 * SS))
        d.ellipse(R((7, y + 12, 13, y + 26)), outline=255, width=int(1.6 * SS))
        y += 24
    img = over(img, grad_fill(m, (72, 78, 92), (28, 32, 42)), rim(m, mix(BONE, STONE, 0.5), -1, 1, 120))
    return noise(img, 6, seed=18)

def prop_wall_panel():
    w = h = 100
    img = canvas(w, h)
    base = mask_new(w, h); rect(base, (0, 0, w, h))
    img = over(img, grad_fill(base, (26, 34, 48), (14, 19, 30)))
    rng = random.Random(19)
    for i in range(4):
        x0 = i * 25
        img = polyfill(img, [(x0 + 2, 2), (x0 + 23, 2), (x0 + 23, 98), (x0 + 2, 98)], (32, 42, 58), 130)
        img = stroke(img, [(x0 + 12.5, 6), (x0 + 12.5, 94)], (18, 24, 36), 1.0, 150)
    # circuit glyph tracery (wraps)
    for _ in range(7):
        x, y = rng.uniform(0, w), rng.uniform(6, h - 6)
        pts = [(x, y)]
        for _ in range(3):
            if rng.random() < 0.5:
                x += rng.choice([-1, 1]) * rng.uniform(8, 22)
            else:
                y += rng.choice([-1, 1]) * rng.uniform(8, 22)
            pts.append((x, y))
        img = stroke(img, pts, TEALD, 0.9, 120, glow_color=TEAL, glow_r=3, glow_s=0.18)
        img = blob(img, (pts[-1][0] - 2, pts[-1][1] - 2, pts[-1][0] + 2, pts[-1][1] + 2), TEAL, 150, glow_color=TEAL, glow_r=3, glow_s=0.35)
    return noise(img, 8, seed=20)

def prop_lattice():
    w = h = 100
    img = canvas(w, h)
    m = mask_new(w, h)
    d = ImageDraw.Draw(m)
    for i in range(5):
        p = i * 20 + 10
        d.line(P([(p, 0), (p, 100)]), fill=255, width=int(2.2 * SS))
        d.line(P([(0, p), (100, p)]), fill=255, width=int(2.2 * SS))
    for i in range(3):
        for j in range(3):
            cx, cy = 20 + i * 30, 20 + j * 30
            d.ellipse(R((cx - 6, cy - 6, cx + 6, cy + 6)), outline=255, width=int(1.6 * SS))
    img = over(img, grad_fill(m, (40, 48, 62), (18, 22, 32)), rim(m, mix(BONE, STONE, 0.4), -1, 1, 90))
    return noise(img, 6, seed=22)


def prop_vent():
    w, h = 72, 36
    img = canvas(w, h)
    m = mask_new(w, h)
    poly(m, [(4, 36), (10, 8), (62, 8), (68, 36)])
    img = over(img, glow(m, TEAL, 7, 0.35), grad_fill(m, (52, 62, 74), (20, 26, 36)),
               rim(m, mix(BONE, TEAL, 0.3), -1.4, 1.4, 150))
    for i in range(5):
        x = 12 + i * 11
        img = blob(img, (x, 11, x + 7, 32), (8, 12, 18), 255, kind="rrect", radius=2)
        img = stroke(img, [(x + 3.5, 13), (x + 3.5, 30)], TEAL, 1.6, 230, glow_color=TEAL, glow_r=3.5, glow_s=0.9)
    img = stroke(img, [(4, 34.5), (68, 34.5)], mix(BONE, TEAL, 0.4), 1.2, 190)
    return noise(img, 5, seed=61)


def prop_climb():
    w, h = 48, 100
    img = canvas(w, h)
    # two chains with rungs between them: tileable vertically
    m = mask_new(w, h)
    rect(m, (9, 0, 14, h))
    rect(m, (34, 0, 39, h))
    for y in range(6, h, 20):
        rect(m, (12, y, 36, y + 5))
    img = over(img, grad_fill(m, (62, 70, 84), (24, 30, 40)), rim(m, mix(BONE, STONE, 0.5), -1.2, 1.2, 130))
    for y in range(6, h, 20):
        img = stroke(img, [(13, y + 2.5), (35, y + 2.5)], mix(BONE, TEAL, 0.2), 1.0, 120)
    for y in range(0, h, 25):
        img = blob(img, (10, y + 2, 13, y + 5), TEALD, 150, glow_color=TEAL, glow_r=2.5, glow_s=0.3)
    return noise(img, 6, seed=62)



def boss_horn():
    """A single curved horn, tip up, pivot at the base. Grows out of the mask in phase two."""
    w, h = 46, 116
    img = canvas(w, h)
    m = mask_new(w, h)
    # outer curve then inner curve back down: a crescent that thins to a point
    poly(m, [(16, 116), (30, 112), (36, 92), (37, 64), (32, 36), (24, 12), (18, 2),
             (16, 16), (20, 40), (23, 66), (22, 90), (16, 104)])
    img = over(img, glow(m, RED, 9, 0.5), grad_fill(m, mix(BONE, (200, 180, 170), 0.4), (52, 30, 30)),
               rim(m, WHITE, -1.6, 1.6, 190))
    # burning fissures along the length
    img = stroke(img, [(20, 100), (25, 74), (27, 48), (23, 20)], RED, 1.6, 235, glow_color=RED, glow_r=4, glow_s=1.0)
    img = stroke(img, [(28, 96), (31, 70), (30, 46)], AMBER, 1.0, 170, glow_color=AMBER, glow_r=3, glow_s=0.6)
    for y in (96, 78, 60, 42):
        img = stroke(img, [(17, y), (33, y - 3)], (40, 22, 22), 1.0, 150)
    return noise(img, 5, seed=71)


# ================================================================ BACKGROUNDS
def vgradient(w, h, stops):
    """stops: list of (t, color)."""
    arr = np.zeros((h, w, 3), np.float64)
    ts = np.linspace(0, 1, h)
    cols = np.zeros((h, 3))
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        seg = (ts >= t0) & (ts <= t1)
        if not seg.any():
            continue
        local = (ts[seg] - t0) / max(1e-6, (t1 - t0))
        for ch in range(3):
            cols[seg, ch] = c0[ch] * (1 - local) + c1[ch] * local
    arr[:, :, :] = cols[:, None, :]
    img = Image.fromarray(arr.astype(np.uint8), "RGB").convert("RGBA")
    return img

def ridge_mask(w, h, base_y, amp, freqs, phase=0.0, seed=0):
    """Tileable ridge silhouette (sum of integer-frequency sines)."""
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    pts = []
    for x in range(0, w * SS + 1, 2):
        u = x / (w * SS)
        y = base_y
        for f, a in freqs:
            y += math.sin(2 * math.pi * (f * u) + phase + f * 1.7 + seed) * a
        pts.append((x, y * SS))
    pts += [(w * SS, h * SS), (0, h * SS)]
    d.polygon(pts, fill=255)
    return m

def bg_sky():
    w = h = 1024
    img = vgradient(w, h, [(0.0, (9, 12, 24)), (0.42, (16, 26, 46)), (0.72, (30, 36, 58)), (1.0, (48, 40, 52))]).resize((w * SS, h * SS))
    rng = random.Random(31)
    stars = Image.new("L", img.size, 0)
    ds = ImageDraw.Draw(stars)
    for _ in range(260):
        x, y = rng.uniform(0, w), rng.uniform(0, h * 0.75)
        r = rng.uniform(0.6, 2.0)
        ds.ellipse(R((x - r, y - r, x + r, y + r)), fill=rng.randint(60, 210))
    img = over(img, solid(stars.filter(ImageFilter.GaussianBlur(0.6 * SS)), BONE))
    # huge dim red sun
    sun = mask_new(w, h)
    ell(sun, (300, 250, 740, 690))
    img = over(img, glow(sun, (150, 40, 40), 60, 0.55), glow(sun, (200, 70, 60), 22, 0.35))
    inner = mask_new(w, h); ell(inner, (320, 270, 720, 670))
    img = over(img, solid(inner, (86, 26, 34)))
    ring = mask_new(w, h)
    ImageDraw.Draw(ring).ellipse(R((300, 250, 740, 690)), outline=255, width=int(3 * SS))
    img = over(img, glow(ring, RED, 8, 0.6), solid(ring, mix(RED, (255, 160, 120), 0.4)))
    haze = vgradient(w, h, [(0.0, (0, 0, 0)), (0.6, (0, 0, 0)), (1.0, (60, 60, 80))]).resize(img.size)
    haze.putalpha(Image.linear_gradient("L").resize(img.size).point(lambda v: int(v * 0.5)))
    img = over(img, haze)
    return noise(img, 5, seed=33, only_opaque=False)

def bg_boss_sky():
    w = h = 1024
    img = vgradient(w, h, [(0.0, (5, 6, 12)), (0.45, (16, 10, 20)), (0.8, (40, 16, 24)), (1.0, (60, 22, 28))]).resize((w * SS, h * SS))
    ecl = mask_new(w, h); ell(ecl, (280, 180, 760, 660))
    corona = mask_new(w, h)
    ImageDraw.Draw(corona).ellipse(R((272, 172, 768, 668)), outline=255, width=int(10 * SS))
    img = over(img, glow(corona, RED, 55, 0.75), glow(corona, (255, 120, 90), 16, 0.8))
    img = over(img, solid(ecl, (4, 4, 8)))
    img = over(img, glow(corona, WHITE, 3, 0.5))
    rng = random.Random(41)
    ash = Image.new("L", img.size, 0)
    da = ImageDraw.Draw(ash)
    for _ in range(180):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        r = rng.uniform(0.8, 2.4)
        da.ellipse(R((x - r, y - r, x + r, y + r)), fill=rng.randint(40, 150))
    img = over(img, solid(ash.filter(ImageFilter.GaussianBlur(0.8 * SS)), AMBER))
    return noise(img, 5, seed=42, only_opaque=False)

def bg_far():
    w, h = 1024, 512
    img = canvas(w, h)
    layers = [
        (ridge_mask(w, h, 300, 0, [(1, 46), (2, 26), (3, 14), (5, 8)], 0.0, 0), (26, 34, 54), (18, 24, 40)),
        (ridge_mask(w, h, 350, 0, [(1, 34), (3, 22), (4, 12), (7, 6)], 1.4, 1), (20, 27, 44), (13, 18, 30)),
    ]
    for m, ct, cb in layers:
        img = over(img, grad_fill(m, ct, cb))
    # pagoda silhouettes on the near ridge
    def pagoda(x, base_y, s, col):
        nonlocal img
        for i in range(3):
            yy = base_y - i * 26 * s
            wd = (46 - i * 10) * s
            img = polyfill(img, [(x - wd, yy), (x + wd, yy), (x + wd * 0.66, yy - 9 * s), (x - wd * 0.66, yy - 9 * s)], col, 255)
            img = polyfill(img, [(x - wd * 1.25, yy), (x + wd * 1.25, yy), (x + wd * 0.7, yy - 4 * s), (x - wd * 0.7, yy - 4 * s)], col, 255)
        img = polyfill(img, [(x - 2 * s, base_y - 78 * s), (x + 2 * s, base_y - 78 * s), (x + 2 * s, base_y - 92 * s), (x - 2 * s, base_y - 92 * s)], col, 255)
    for x, s in ((170, 1.0), (620, 0.8), (880, 0.6)):
        pagoda(x, 356, s, (15, 20, 34))
    mist = vgradient(w, h, [(0.0, (0, 0, 0)), (0.55, (0, 0, 0)), (1.0, (60, 74, 96))]).resize(img.size)
    ma = Image.linear_gradient("L").resize(img.size).point(lambda v: int(max(0, v - 90) * 1.5))
    mist.putalpha(ma)
    img = over(img, mist)
    return noise(img, 4, seed=44)

def bg_mid():
    w, h = 1024, 512
    img = canvas(w, h)
    m = ridge_mask(w, h, 380, 0, [(1, 16), (3, 10), (6, 6)], 0.7, 2)
    img = over(img, grad_fill(m, (18, 24, 38), (10, 14, 24)))
    rng = random.Random(51)
    # ruined temple blocks
    for _ in range(16):
        x = rng.uniform(0, w)
        bw = rng.uniform(60, 150)
        bh = rng.uniform(70, 210)
        by = 400 - rng.uniform(0, 30)
        col = (12, 17, 28)
        for xoff in (-w, 0, w):
            img = polyfill(img, [(x + xoff, by), (x + bw + xoff, by), (x + bw + xoff, by - bh), (x + xoff, by - bh)], col, 255)
            # roof
            img = polyfill(img, [(x - 10 + xoff, by - bh), (x + bw + 10 + xoff, by - bh),
                                 (x + bw * 0.7 + xoff, by - bh - 16), (x + bw * 0.3 + xoff, by - bh - 16)], col, 255)
            # lit windows
            for _ in range(int(bh / 40)):
                wx = x + rng.uniform(8, bw - 16) + xoff
                wy = by - rng.uniform(14, bh - 12)
                c = TEAL if rng.random() < 0.6 else AMBER
                img = blob(img, (wx, wy, wx + 7, wy + 10), c, 190, glow_color=c, glow_r=5, glow_s=0.5)
    # hanging cables
    for _ in range(9):
        x0 = rng.uniform(0, w); x1 = x0 + rng.uniform(120, 300); y = rng.uniform(120, 300)
        img = stroke(img, [(x0, y), ((x0 + x1) / 2, y + rng.uniform(20, 50)), (x1, y - rng.uniform(0, 30))], (8, 11, 18), 1.6, 220)
    mist = vgradient(w, h, [(0.0, (0, 0, 0)), (0.6, (0, 0, 0)), (1.0, (40, 52, 72))]).resize(img.size)
    mist.putalpha(Image.linear_gradient("L").resize(img.size).point(lambda v: int(max(0, v - 120) * 1.6)))
    img = over(img, mist)
    return noise(img, 4, seed=52)

def bg_near():
    w, h = 1024, 512
    img = canvas(w, h)
    rng = random.Random(61)
    col = (7, 9, 15)
    for _ in range(11):
        x = rng.uniform(0, w)
        bw = rng.uniform(18, 34)
        top = rng.uniform(120, 300)
        for xoff in (-w, 0, w):
            img = polyfill(img, [(x + xoff, 512), (x + bw + xoff, 512), (x + bw * 0.9 + xoff, top), (x + bw * 0.1 + xoff, top)], col, 255)
            img = polyfill(img, [(x - 8 + xoff, top + 14), (x + bw + 8 + xoff, top + 14), (x + bw + xoff, top), (x + xoff, top)], col, 255)
    for _ in range(26):
        x = rng.uniform(0, w)
        top = rng.uniform(60, 260)
        for xoff in (-w, 0, w):
            img = polyfill(img, [(x + xoff, 512), (x + 7 + xoff, 512), (x + 6 + xoff, top), (x + 1 + xoff, top)], (9, 13, 18), 255)
            for yy in range(int(top) + 30, 512, 60):
                img = stroke(img, [(x - 1 + xoff, yy), (x + 8 + xoff, yy - 1)], (5, 8, 11), 1.2, 200)
    fade = Image.new("RGBA", img.size, (0, 0, 0, 0))
    a = Image.linear_gradient("L").resize(img.size).point(lambda v: 255 - v)
    fade.putalpha(a.point(lambda v: int(v * 0.0)))
    alpha = np.array(img)[:, :, 3].astype(np.float32)
    ramp = np.linspace(1.0, 1.0, img.size[1])[:, None]
    top_fade = np.clip(np.linspace(0.0, 1.6, img.size[1]), 0, 1)[:, None]
    alpha = alpha * np.minimum(1.0, top_fade * 3.0 + 0.15)
    arr = np.array(img)
    arr[:, :, 3] = alpha.astype(np.uint8)
    img = Image.fromarray(arr, "RGBA")
    return img

def bg_boss_far():
    w, h = 1024, 512
    img = canvas(w, h)
    m = ridge_mask(w, h, 420, 0, [(1, 14), (3, 8)], 0.3, 5)
    img = over(img, grad_fill(m, (28, 16, 22), (14, 8, 12)))
    # colossal seated statues
    def colossus(x, s, col):
        nonlocal img
        base = 430
        for xoff in (-w, 0, w):
            X = x + xoff
            img = polyfill(img, [(X - 90 * s, base), (X + 90 * s, base), (X + 70 * s, base - 150 * s), (X - 70 * s, base - 150 * s)], col, 255)
            img = polyfill(img, [(X - 60 * s, base - 150 * s), (X + 60 * s, base - 150 * s), (X + 44 * s, base - 250 * s), (X - 44 * s, base - 250 * s)], col, 255)
            img = polyfill(img, [(X - 30 * s, base - 250 * s), (X + 30 * s, base - 250 * s), (X + 22 * s, base - 300 * s), (X - 22 * s, base - 300 * s)], col, 255)
            img = polyfill(img, [(X - 40 * s, base - 300 * s), (X + 40 * s, base - 300 * s), (X + 30 * s, base - 340 * s), (X - 30 * s, base - 340 * s)], col, 255)
            img = blob(img, (X - 22 * s, base - 330 * s, X - 8 * s, base - 318 * s), REDD, 200, glow_color=RED, glow_r=6, glow_s=0.6)
            img = blob(img, (X + 8 * s, base - 330 * s, X + 22 * s, base - 318 * s), REDD, 200, glow_color=RED, glow_r=6, glow_s=0.6)
    for x, s in ((230, 1.0), (700, 0.85)):
        colossus(x, s, (13, 8, 12))
    rng = random.Random(71)
    for _ in range(10):
        x = rng.uniform(0, w); y0 = rng.uniform(0, 80)
        img = stroke(img, [(x, y0), (x + rng.uniform(-14, 14), y0 + rng.uniform(160, 330))], (20, 14, 18), 2.0, 235)
    mist = vgradient(w, h, [(0.0, (0, 0, 0)), (0.6, (0, 0, 0)), (1.0, (70, 34, 38))]).resize(img.size)
    mist.putalpha(Image.linear_gradient("L").resize(img.size).point(lambda v: int(max(0, v - 110) * 1.6)))
    img = over(img, mist)
    return noise(img, 4, seed=72)

def bg_fog():
    w, h = 512, 256
    img = canvas(w, h)
    rng = random.Random(81)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    for _ in range(26):
        cx = rng.uniform(w * 0.15, w * 0.85)
        cy = rng.uniform(h * 0.3, h * 0.72)
        rx = rng.uniform(50, 150)
        ry = rng.uniform(24, 60)
        d.ellipse(R((cx - rx, cy - ry, cx + rx, cy + ry)), fill=rng.randint(70, 150))
    m = m.filter(ImageFilter.GaussianBlur(22 * SS))
    # feather the borders so tiled copies never show a seam
    arr = np.array(m).astype(np.float32)
    H, W = arr.shape
    fx = np.clip(np.minimum(np.arange(W), W - 1 - np.arange(W)) / (W * 0.22), 0, 1)[None, :]
    fy = np.clip(np.minimum(np.arange(H), H - 1 - np.arange(H)) / (H * 0.3), 0, 1)[:, None]
    arr = arr * fx * fy
    m = Image.fromarray(arr.astype(np.uint8), "L")
    img = over(img, solid(m, WHITE))
    return img

# ================================================================ FX
def fx_slash():
    w, h = 220, 130
    img = canvas(w, h)
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    # crescent: outer arc minus inner arc
    d.pieslice(R((-40, -30, 250, 170)), start=205, end=335, fill=255)
    inner = Image.new("L", m.size, 0)
    ImageDraw.Draw(inner).pieslice(R((-16, 4, 226, 150)), start=200, end=340, fill=255)
    m = ImageChops.subtract(m, inner)
    m = m.filter(ImageFilter.GaussianBlur(0.8 * SS))
    soft = m.filter(ImageFilter.GaussianBlur(5 * SS))
    img = over(img, solid(soft, WHITE).point(lambda v: v) if False else solid(soft, WHITE))
    a = np.array(img)
    a[:, :, 3] = (a[:, :, 3] * 0.45).astype(np.uint8)
    img = Image.fromarray(a, "RGBA")
    img = over(img, solid(m, WHITE))
    # sharpen the leading edge
    edge = ImageChops.subtract(m, ImageChops.offset(m, 0, int(3 * SS)))
    img = over(img, solid(edge.filter(ImageFilter.GaussianBlur(0.6 * SS)), WHITE))
    return img

def fx_ring():
    w = h = 128
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).ellipse(R((6, 6, 122, 122)), outline=255, width=int(3.5 * SS))
    m = m.filter(ImageFilter.GaussianBlur(1.0 * SS))
    img = over(img, glow(m, WHITE, 5, 0.5), solid(m, WHITE))
    return img

def fx_glow():
    w = h = 64
    arr = np.zeros((h * SS, w * SS, 4), np.uint8)
    yy, xx = np.mgrid[0:h * SS, 0:w * SS]
    cx = cy = (w * SS) / 2
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (w * SS / 2)
    a = np.clip(1.0 - r, 0, 1) ** 2.2
    arr[:, :, 0] = arr[:, :, 1] = arr[:, :, 2] = 255
    arr[:, :, 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")

def fx_spark():
    w = h = 16
    img = canvas(w, h)
    img = polyfill(img, [(8, 0), (11, 8), (8, 16), (5, 8)], WHITE, 255, blur=0.4)
    return img

def fx_dust():
    w = h = 32
    arr = np.zeros((h * SS, w * SS, 4), np.uint8)
    yy, xx = np.mgrid[0:h * SS, 0:w * SS]
    c = (w * SS) / 2
    r = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / (w * SS / 2)
    a = np.clip(1.0 - r, 0, 1) ** 1.4
    arr[:, :, 0] = arr[:, :, 1] = arr[:, :, 2] = 255
    arr[:, :, 3] = (a * 210).astype(np.uint8)
    img = Image.fromarray(arr, "RGBA")
    return noise(img, 12, seed=91)

def fx_ink():
    w = h = 24
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    rng = random.Random(93)
    pts = []
    for i in range(9):
        a = i * 2 * math.pi / 9
        rr = 8 + rng.uniform(-2.5, 2.5)
        pts.append((12 + rr * math.cos(a), 12 + rr * math.sin(a) * 0.9))
    d.polygon(P(pts), fill=255)
    m = m.filter(ImageFilter.GaussianBlur(0.5 * SS))
    img = over(img, solid(m, WHITE))
    return img

def fx_bolt():
    w, h = 44, 18
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).polygon(P([(44, 9), (26, 2), (4, 9), (26, 16)]), fill=255)
    soft = m.filter(ImageFilter.GaussianBlur(3 * SS))
    img = over(img, solid(soft, WHITE))
    a = np.array(img); a[:, :, 3] = (a[:, :, 3] * 0.55).astype(np.uint8)
    img = Image.fromarray(a, "RGBA")
    img = over(img, solid(m.filter(ImageFilter.GaussianBlur(0.6 * SS)), WHITE))
    core = Image.new("L", img.size, 0)
    ImageDraw.Draw(core).polygon(P([(40, 9), (26, 5), (12, 9), (26, 13)]), fill=255)
    img = over(img, solid(core.filter(ImageFilter.GaussianBlur(0.5 * SS)), WHITE))
    return img

def fx_shockwave():
    w, h = 256, 64
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse(R((4, 12, 252, 60)), outline=255, width=int(5 * SS))
    m = m.filter(ImageFilter.GaussianBlur(1.4 * SS))
    # fade the top so it reads as a ground ring
    arr = np.array(m).astype(np.float32)
    H = arr.shape[0]
    fade = np.clip(np.linspace(0.15, 1.0, H), 0, 1)[:, None]
    arr *= fade
    m = Image.fromarray(arr.astype(np.uint8), "L")
    img = over(img, glow(m, WHITE, 6, 0.45), solid(m, WHITE))
    return img

def fx_flare():
    w = h = 256
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    d.polygon(P([(128, 4), (140, 116), (252, 128), (140, 140), (128, 252), (116, 140), (4, 128), (116, 116)]), fill=255)
    m = m.filter(ImageFilter.GaussianBlur(3.5 * SS))
    core = Image.new("L", img.size, 0)
    ImageDraw.Draw(core).ellipse(R((104, 104, 152, 152)), fill=255)
    core = core.filter(ImageFilter.GaussianBlur(6 * SS))
    img = over(img, solid(m, WHITE), solid(core, WHITE))
    return img

def fx_parry_glyph():
    w = h = 100
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    d.ellipse(R((6, 6, 94, 94)), outline=255, width=int(2.6 * SS))
    d.arc(R((6, 6, 94, 94)), start=0, end=180, fill=200, width=int(1.2 * SS))
    d.ellipse(R((28, 28, 72, 72)), outline=190, width=int(1.4 * SS))
    for i in range(4):
        a = i * math.pi / 2 + math.pi / 4
        d.line(P([(50 + 30 * math.cos(a), 50 + 30 * math.sin(a)), (50 + 44 * math.cos(a), 50 + 44 * math.sin(a))]),
               fill=255, width=int(2.2 * SS))
    d.ellipse(R((44, 22, 56, 34)), fill=255)
    d.ellipse(R((44, 66, 56, 78)), outline=255, width=int(1.6 * SS))
    img = over(img, glow(m, WHITE, 4, 0.45), solid(m, WHITE))
    return img

# ================================================================ UI
def find_font(size):
    cands = [
        os.path.join(FONTDIR, "Cinzel-Regular.ttf"),
        "C:/Windows/Fonts/pala.ttf",
        "C:/Windows/Fonts/constan.ttf",
        "C:/Windows/Fonts/georgia.ttf",
        "C:/Windows/Fonts/times.ttf",
        "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arial.ttf",
    ]
    for c in cands:
        try:
            if os.path.exists(c):
                return ImageFont.truetype(c, size)
        except Exception:
            pass
    return ImageFont.load_default()

def try_download_font():
    target = os.path.join(FONTDIR, "Cinzel-Regular.ttf")
    if os.path.exists(target):
        return True
    urls = [
        "https://github.com/google/fonts/raw/main/ofl/cinzel/static/Cinzel-Regular.ttf",
        "https://github.com/google/fonts/raw/main/ofl/cinzel/Cinzel%5Bwght%5D.ttf",
    ]
    for u in urls:
        try:
            req = urllib.request.Request(u, headers={"User-Agent": "Mozilla/5.0"})
            data = urllib.request.urlopen(req, timeout=12).read()
            if len(data) > 20000:
                with open(target, "wb") as f:
                    f.write(data)
                print("[font] downloaded", u)
                return True
        except Exception as e:
            print("[font] failed", u, e)
    return False

def text_sprite(w, h, text, size, color, glow_color, spacing_px, name, sub=None, sub_size=0, sub_color=BONE, glow_r=14, glow_s=0.55):
    img = canvas(w, h)
    font = find_font(size * SS)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    widths = []
    for ch in text:
        # advance width, not the ink bounding box: glyphs with tails (Q) would otherwise
        # push the following letter away and open a gap in the middle of a word
        widths.append(font.getlength(ch) if ch != " " else size * SS * 0.35)
    total = sum(widths) + spacing_px * SS * (len(text) - 1)
    x = (w * SS - total) / 2
    y = h * SS * (0.5 if not sub else 0.38)
    for ch, cw in zip(text, widths):
        d.text((x, y), ch, font=font, fill=255, anchor="lm")
        x += cw + spacing_px * SS
    if sub:
        sfont = find_font(sub_size * SS)
        sm = Image.new("L", img.size, 0)
        sd = ImageDraw.Draw(sm)
        swidths = []
        for ch in sub:
            swidths.append(sfont.getlength(ch) if ch != " " else sub_size * SS * 0.35)
        stotal = sum(swidths) + 4 * SS * (len(sub) - 1)
        sx = (w * SS - stotal) / 2
        sy = h * SS * 0.78
        for ch, cw in zip(sub, swidths):
            sd.text((sx, sy), ch, font=sfont, fill=255, anchor="lm")
            sx += cw + 4 * SS
        img = over(img, glow(sm, glow_color, 6, 0.35), solid(sm, sub_color))
    img = over(img, glow(m, glow_color, glow_r, glow_s), glow(m, glow_color, 4, 0.4), solid(m, color))
    return img

def ui_bar_frame():
    w, h = 420, 26
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    d.rectangle(R((0, 0, w - 1, h - 1)), outline=255, width=int(1.6 * SS))
    d.line(P([(6, h - 1), (w - 6, h - 1)]), fill=180, width=int(1.0 * SS))
    for x in (0, w - 1):
        d.line(P([(x, 2), (x, h - 2)]), fill=255, width=int(2.2 * SS))
    img = over(img, glow(m, BONE, 3, 0.25), solid(m, BONE))
    corner = Image.new("L", img.size, 0)
    dc = ImageDraw.Draw(corner)
    for (cx, cy) in ((3, 3), (w - 4, 3), (3, h - 4), (w - 4, h - 4)):
        dc.ellipse(R((cx - 2.4, cy - 2.4, cx + 2.4, cy + 2.4)), fill=255)
    img = over(img, glow(corner, GOLD, 3, 0.5), solid(corner, GOLD))
    return img

def ui_pip(full):
    w = h = 30
    img = canvas(w, h)
    m = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(m)
    pts = [(15 + 11 * math.cos(i * math.pi / 3 - math.pi / 2), 15 + 11 * math.sin(i * math.pi / 3 - math.pi / 2)) for i in range(6)]
    if full:
        d.polygon(P(pts), fill=255)
        img = over(img, glow(m, GOLD, 6, 0.8), solid(m, GOLD))
        inner = Image.new("L", img.size, 0)
        ip = [(15 + 5 * math.cos(i * math.pi / 3 - math.pi / 2), 15 + 5 * math.sin(i * math.pi / 3 - math.pi / 2)) for i in range(6)]
        ImageDraw.Draw(inner).polygon(P(ip), fill=255)
        img = over(img, solid(inner, mix(GOLD, WHITE, 0.7)))
    else:
        d.polygon(P(pts), outline=255, width=int(1.6 * SS))
        img = over(img, solid(m, BONE))
    return img

# ================================================================ main
def main():
    try_download_font()
    print("[gen] characters")
    save(player_torso(), 46, 62, "player_torso")
    save(player_head(), 44, 46, "player_head")
    save(limb(16, 46, "player_arm_back", shade(ROBE_T, 0.75), shade(ROBE_B, 0.8), REDD, True, BONE, wrap=[26]), 16, 46, "player_arm_back")
    save(limb(16, 46, "player_arm_front", ROBE_T, ROBE_B, REDD, True, BONE, wrap=[26]), 16, 46, "player_arm_front")
    save(limb(20, 54, "player_leg_back", shade(ROBE_T, 0.75), shade(ROBE_B, 0.8), None, True, BONE, wrap=[38]), 20, 54, "player_leg_back")
    save(limb(20, 54, "player_leg_front", ROBE_T, ROBE_B, None, True, BONE, wrap=[38]), 20, 54, "player_leg_front")
    save(player_sword(), 14, 96, "player_sword")
    save(player_sash_seg(), 12, 12, "player_sash_seg")

    save(grunt_torso(), 52, 62, "grunt_torso")
    save(grunt_head(), 38, 38, "grunt_head")
    save(limb(14, 42, "grunt_arm", HUSK_T, HUSK_B, None, True, mix(BONE, STONE, 0.5)), 14, 42, "grunt_arm")
    save(limb(16, 46, "grunt_leg", HUSK_T, HUSK_B, None, False, mix(BONE, STONE, 0.5)), 16, 46, "grunt_leg")
    save(grunt_blade(), 12, 72, "grunt_blade")

    save(spear_torso(), 50, 66, "spear_torso")
    save(spear_head(), 36, 42, "spear_head")
    save(limb(14, 42, "spear_arm", SENT_T, SENT_B, None, True, mix(BONE, TEAL, 0.2)), 14, 42, "spear_arm")
    save(limb(16, 48, "spear_leg", SENT_T, SENT_B, None, False, mix(BONE, TEAL, 0.2)), 16, 48, "spear_leg")
    save(spear_spear(), 10, 160, "spear_spear")

    save(brute_torso(), 68, 84, "brute_torso")
    save(brute_head(), 46, 48, "brute_head")
    save(limb(18, 54, "brute_arm", BRUTE_T, BRUTE_B, AMBER, True, mix(BONE, AMBER, 0.25), wrap=[30]), 18, 54, "brute_arm")
    save(limb(22, 58, "brute_leg", BRUTE_T, BRUTE_B, None, False, mix(BONE, AMBER, 0.25)), 22, 58, "brute_leg")
    save(brute_hammer(), 40, 140, "brute_hammer")

    save(drone_body(), 58, 42, "drone_body")
    save(drone_eye(), 18, 18, "drone_eye")
    save(drone_ring(), 76, 76, "drone_ring")

    print("[gen] boss")
    save(boss_torso(), 112, 150, "boss_torso")
    save(boss_head(), 72, 82, "boss_head")
    save(limb(28, 92, "boss_arm", BOSS_T, BOSS_B, None, True, mix(BONE, STONE, 0.5)), 28, 92, "boss_arm")
    save(limb(36, 110, "boss_leg", BOSS_T, BOSS_B, None, False, mix(BONE, STONE, 0.5)), 36, 110, "boss_leg")
    save(boss_glaive(), 26, 270, "boss_glaive")
    save(boss_cape_seg(), 24, 24, "boss_cape_seg")
    save(boss_core(), 34, 34, "boss_core")
    save(boss_horn(), 46, 116, "boss_horn")

    print("[gen] tiles")
    save(tile_stone(), 100, 100, "tile_stone")
    save(tile_stone_top(), 100, 24, "tile_stone_top")
    save(tile_platform(), 100, 30, "tile_platform")
    save(tile_spikes(), 100, 40, "tile_spikes")

    print("[gen] props")
    save(prop_lantern(), 32, 52, "prop_lantern")
    save(pillar(64, 230, False, "prop_pillar"), 64, 230, "prop_pillar")
    save(pillar(64, 130, True, "prop_pillar_broken"), 64, 130, "prop_pillar_broken")
    save(prop_banner(), 44, 130, "prop_banner")
    save(prop_torii(), 320, 280, "prop_torii")
    save(prop_gate_sealed(), 170, 280, "prop_gate_sealed")
    save(prop_gate_glyph(), 90, 90, "prop_gate_glyph")
    save(prop_shrine(), 130, 150, "prop_shrine")
    save(prop_statue(), 150, 280, "prop_statue")
    save(prop_bamboo(), 70, 320, "prop_bamboo")
    save(prop_rock(), 90, 56, "prop_rock")
    save(prop_sign(), 54, 64, "prop_sign")
    save(prop_chain(), 20, 300, "prop_chain")
    save(prop_wall_panel(), 100, 100, "prop_wall_panel")
    save(prop_lattice(), 100, 100, "prop_lattice")
    save(prop_vent(), 72, 36, "prop_vent")
    save(prop_climb(), 48, 100, "prop_climb")

    print("[gen] backgrounds")
    save(bg_sky(), 1024, 1024, "bg_sky")
    save(bg_boss_sky(), 1024, 1024, "bg_boss_sky")
    save(bg_far(), 1024, 512, "bg_far")
    save(bg_mid(), 1024, 512, "bg_mid")
    save(bg_near(), 1024, 512, "bg_near")
    save(bg_boss_far(), 1024, 512, "bg_boss_far")
    save(bg_fog(), 512, 256, "bg_fog")

    print("[gen] fx")
    save(fx_slash(), 220, 130, "fx_slash")
    save(fx_ring(), 128, 128, "fx_ring")
    save(fx_glow(), 64, 64, "fx_glow")
    save(fx_spark(), 16, 16, "fx_spark")
    save(fx_dust(), 32, 32, "fx_dust")
    save(fx_ink(), 24, 24, "fx_ink")
    save(fx_bolt(), 44, 18, "fx_bolt")
    save(fx_shockwave(), 256, 64, "fx_shockwave")
    save(fx_flare(), 256, 256, "fx_flare")
    save(fx_parry_glyph(), 100, 100, "fx_parry_glyph")

    print("[gen] ui")
    save(ui_bar_frame(), 420, 26, "ui_bar_frame")
    save(ui_pip(False), 30, 30, "ui_pip")
    save(ui_pip(True), 30, 30, "ui_pip_full")
    save(text_sprite(900, 260, "ASHEN SOL", 96, BONE, TEAL, 12, "ui_title", sub="THE SEALED GATE", sub_size=26, sub_color=mix(TEAL, BONE, 0.4), glow_r=18, glow_s=0.6), 900, 260, "ui_title")
    save(text_sprite(800, 120, "YOU HAVE FALLEN", 54, mix(BONE, RED, 0.35), RED, 9, "ui_text_fallen", glow_r=16, glow_s=0.55), 800, 120, "ui_text_fallen")
    save(text_sprite(900, 120, "SOL VANQUISHED", 56, GOLD, GOLD, 9, "ui_text_vanquished", glow_r=18, glow_s=0.6), 900, 120, "ui_text_vanquished")
    save(text_sprite(1000, 140, "THE FORSAKEN WARDEN", 46, mix(BONE, RED, 0.2), RED, 6, "ui_bossname", sub="KEEPER OF THE SEALED GATE", sub_size=18, sub_color=mix(BONE, RED, 0.3), glow_r=14, glow_s=0.5), 1000, 140, "ui_bossname")

    contact_sheet()
    print("[gen] done:", len(SAVED), "sprites ->", OUT)

def contact_sheet():
    cols = 8
    cell = 180
    rows = (len(SAVED) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 22)), (14, 16, 24, 255))
    d = ImageDraw.Draw(sheet)
    f = find_font(13)
    for i, (name, img) in enumerate(SAVED):
        cx = (i % cols) * cell
        cy = (i // cols) * (cell + 22)
        thumb = img.copy()
        thumb.thumbnail((cell - 16, cell - 16), Image.LANCZOS)
        sheet.alpha_composite(thumb, (cx + (cell - thumb.width) // 2, cy + (cell - thumb.height) // 2))
        d.text((cx + cell // 2, cy + cell + 8), name, font=f, fill=(200, 200, 210, 255), anchor="mm")
    sheet.convert("RGB").save(os.path.join(ROOT, "Tools", "contact_sheet.png"))

if __name__ == "__main__":
    main()
