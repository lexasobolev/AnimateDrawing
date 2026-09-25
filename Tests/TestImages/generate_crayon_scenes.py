"""Generates children's-drawing-style background scenes with known object positions.

Real kids' landscape drawings with a licence that allows redistribution weren't reachable from
the build sandbox, so these imitate them instead: wobbly hand-drawn outlines, crayon fills that
leave paper showing through, lopsided suns, lollipop trees, a house with a triangle roof.
Each scene is written with a <name>.truth.json listing what's in it (normalized, y DOWN) so the
analyzer tests can score detection.

    python generate_crayon_scenes.py      # needs Pillow + numpy
"""
import json
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1000, 700
OUT = Path(__file__).resolve().parent / "backgrounds"

PAPER = (247, 244, 236)
COLORS = {
    "sky": (120, 180, 235), "sky2": (150, 205, 245), "grass": (70, 170, 60), "grass2": (110, 190, 70),
    "trunk": (130, 80, 40), "crown": (50, 150, 50), "sun": (250, 215, 40), "sun_orange": (250, 160, 40),
    "roof": (210, 50, 40), "wall": (245, 205, 90), "wall_blue": (100, 140, 220), "window": (140, 200, 250),
    "door": (120, 70, 40), "sand": (240, 200, 110), "water": (40, 110, 210), "rock": (140, 140, 145),
    "mountain": (130, 110, 170), "flower_red": (230, 40, 60), "flower_pink": (240, 110, 180),
    "flower_purple": (150, 70, 200), "flower_yellow": (250, 220, 30), "cloud_line": (90, 140, 220),
    "pencil": (40, 40, 45),
}


class Crayon:
    def __init__(self, seed):
        self.rng = random.Random(seed)
        self.img = Image.new("RGB", (W, H), PAPER)
        self.draw = ImageDraw.Draw(self.img)
        self.truth = []

    def jitter(self, v, amount):
        return v + self.rng.uniform(-amount, amount)

    def wobbly(self, points, closed=True, amount=4, step=12):
        # resample a polygon/polyline and add hand wobble
        pts = list(points) + ([points[0]] if closed else [])
        out = []
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            n = max(1, int(math.hypot(x1 - x0, y1 - y0) / step))
            for i in range(n):
                t = i / n
                out.append((self.jitter(x0 + (x1 - x0) * t, amount), self.jitter(y0 + (y1 - y0) * t, amount)))
        if not closed:
            out.append(pts[-1])
        return out

    def fill(self, polygon, color, coverage=0.8):
        # crayon: many short strokes in one direction, leaving some paper visible
        mask = Image.new("L", (W, H), 0)
        ImageDraw.Draw(mask).polygon(polygon, fill=255)
        m = np.array(mask) > 0
        ys, xs = np.nonzero(m)
        if len(xs) == 0:
            return
        layer = Image.new("RGB", (W, H), PAPER)
        d = ImageDraw.Draw(layer)
        angle = self.rng.uniform(-0.6, 0.6)
        x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
        length = x1 - x0 + 40
        slack = abs(length * math.tan(angle)) + 20
        y = y0 - slack
        while y < y1 + slack:
            if self.rng.random() < coverage:
                c = tuple(max(0, min(255, int(ch + self.rng.uniform(-18, 18)))) for ch in color)
                d.line([(x0 - 20, y), (x0 - 20 + length, y + length * math.tan(angle))], fill=c, width=self.rng.randint(3, 6))
            y += self.rng.uniform(2.5, 5.5)
        arr = np.array(self.img)
        lay = np.array(layer)
        # paper speckle: crayon doesn't reach the paper's pits
        speckle = np.random.default_rng(self.rng.randint(0, 10**6)).random(m.shape) < (1 - coverage) * 0.35
        sel = m & ~speckle & np.any(lay != PAPER, axis=2)
        arr[sel] = lay[sel]
        self.img = Image.fromarray(arr)
        self.draw = ImageDraw.Draw(self.img)

    def outline(self, polygon, color=COLORS["pencil"], width=3, closed=True):
        self.draw.line(self.wobbly(polygon, closed, amount=2.5, step=10), fill=color, width=width, joint="curve")

    def ellipse_pts(self, cx, cy, rx, ry, n=36, bumps=0.0):
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            r = 1 + bumps * math.sin(a * 7) + self.rng.uniform(-0.04, 0.04)
            pts.append((cx + rx * r * math.cos(a), cy + ry * r * math.sin(a)))
        return pts

    def add_truth(self, kind, x0, y0, x1, y1):
        self.truth.append({"kind": kind, "bounds": [round(x0 / W, 3), round(y0 / H, 3), round(x1 / W, 3), round(y1 / H, 3)]})

    # --- scene elements -------------------------------------------------------------------
    def sky(self, bottom, color="sky"):
        poly = [(0, 0), (W, 0)] + [(x, bottom + 15 * math.sin(x / 90)) for x in range(W, -1, -50)]
        self.fill(poly, COLORS[color], 0.75)
        self.add_truth("Sky", 0, 0, W, bottom)

    def ground(self, top, color="grass", kind="Grass", hills=20):
        pts = [(x, top + hills * math.sin(x / 140 + 1) + self.rng.uniform(-4, 4)) for x in range(0, W + 1, 25)]
        poly = pts + [(W, H), (0, H)]
        self.fill(poly, COLORS[color], 0.85)
        self.outline(pts, COLORS["pencil"], 2, closed=False)
        self.add_truth(kind, 0, top - hills, W, H)
        return lambda x: top + hills * math.sin(x / 140 + 1)

    def sun(self, cx, cy, r, color="sun"):
        pts = self.ellipse_pts(cx, cy, r, r)
        self.fill(pts, COLORS[color], 0.9)
        self.outline(pts, COLORS["sun_orange"], 3)
        for i in range(10):
            a = 2 * math.pi * i / 10 + 0.2
            self.draw.line([(cx + math.cos(a) * r * 1.2, cy + math.sin(a) * r * 1.2),
                            (cx + math.cos(a) * r * 1.7, cy + math.sin(a) * r * 1.7)], fill=COLORS["sun_orange"], width=4)
        self.add_truth("Sun", cx - r, cy - r, cx + r, cy + r)

    def tree(self, x, ground_y, height, crown_r):
        tw = max(14, crown_r * 0.28)
        top = ground_y - height
        trunk = [(x - tw / 2, ground_y), (x - tw / 2 * 0.8, top + crown_r * 0.6), (x + tw / 2 * 0.8, top + crown_r * 0.6), (x + tw / 2, ground_y)]
        self.fill(trunk, COLORS["trunk"], 0.95)
        self.outline(trunk)
        crown = self.ellipse_pts(x, top, crown_r, crown_r * 0.85, bumps=0.05)
        self.fill(crown, COLORS["crown"], 0.9)
        self.outline(crown)
        self.add_truth("Tree", x - crown_r, top - crown_r * 0.85, x + crown_r, ground_y)

    def house(self, x, ground_y, w, h, wall="wall"):
        x0, x1 = x - w / 2, x + w / 2
        wall_poly = [(x0, ground_y), (x0, ground_y - h), (x1, ground_y - h), (x1, ground_y)]
        self.fill(wall_poly, COLORS[wall], 0.9)
        self.outline(wall_poly)
        roof = [(x0 - w * 0.1, ground_y - h), (x, ground_y - h - h * 0.7), (x1 + w * 0.1, ground_y - h)]
        self.fill(roof, COLORS["roof"], 0.9)
        self.outline(roof)
        door = [(x - w * 0.1, ground_y), (x - w * 0.1, ground_y - h * 0.5), (x + w * 0.1, ground_y - h * 0.5), (x + w * 0.1, ground_y)]
        self.fill(door, COLORS["door"], 0.95)
        self.outline(door)
        for wx in (x0 + w * 0.12, x1 - w * 0.32):
            win = [(wx, ground_y - h * 0.85), (wx + w * 0.2, ground_y - h * 0.85), (wx + w * 0.2, ground_y - h * 0.6), (wx, ground_y - h * 0.6)]
            self.fill(win, COLORS["window"], 0.9)
            self.outline(win)
        self.add_truth("House", x0 - w * 0.1, ground_y - h * 1.7, x1 + w * 0.1, ground_y)

    def flower(self, x, ground_y, color):
        top = ground_y - self.rng.uniform(30, 55)
        self.draw.line([(x, ground_y), (x + self.rng.uniform(-4, 4), top)], fill=(40, 120, 40), width=3)
        for i in range(5):
            a = 2 * math.pi * i / 5
            px, py = x + math.cos(a) * 9, top + math.sin(a) * 9
            self.draw.ellipse([px - 8, py - 8, px + 8, py + 8], fill=COLORS[color])
        self.draw.ellipse([x - 5, top - 5, x + 5, top + 5], fill=COLORS["flower_yellow"] if color != "flower_yellow" else COLORS["sun_orange"])
        self.add_truth("Flower", x - 17, top - 17, x + 17, top + 17)

    def cloud(self, cx, cy, w, filled=False):
        pts = self.ellipse_pts(cx, cy, w / 2, w / 4.2, n=40, bumps=0.08)
        if filled:
            self.fill(pts, (230, 235, 240), 0.95)
            self.draw.polygon(pts, fill=PAPER)
        else:
            self.draw.polygon(pts, fill=PAPER)
        self.outline(pts, COLORS["cloud_line"], 3)
        self.add_truth("Cloud", cx - w / 2, cy - w / 4.2, cx + w / 2, cy + w / 4.2)

    def water(self, x0, y0, x1, y1, kind="Water"):
        pts = [(x, y0 + 6 * math.sin(x / 40)) for x in range(int(x0), int(x1) + 1, 20)] + [(x1, y1), (x0, y1)]
        self.fill(pts, COLORS["water"], 0.9)
        self.add_truth(kind, x0, y0, x1, y1)

    def rock(self, x, ground_y, r):
        pts = [(x - r, ground_y), (x - r * 0.8, ground_y - r * 0.6), (x - r * 0.2, ground_y - r * 0.9),
               (x + r * 0.6, ground_y - r * 0.7), (x + r, ground_y)]
        self.fill(pts, COLORS["rock"], 0.95)
        self.outline(pts)
        self.add_truth("Rock", x - r, ground_y - r * 0.9, x + r, ground_y)

    def mountain(self, x, base_y, w, h):
        pts = [(x - w / 2, base_y), (x, base_y - h), (x + w / 2, base_y)]
        self.fill(pts, COLORS["mountain"], 0.9)
        self.outline(pts)
        self.add_truth("Mountain", x - w / 2, base_y - h, x + w / 2, base_y)

    def save(self, name):
        OUT.mkdir(parents=True, exist_ok=True)
        img = self.img.filter(ImageFilter.GaussianBlur(0.6))
        # photographed-paper feel: slight vignette and warm tint
        arr = np.array(img).astype(np.float32)
        yy, xx = np.mgrid[0:H, 0:W]
        vignette = 1 - 0.08 * (((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
        arr *= vignette[..., None]
        Image.fromarray(arr.clip(0, 255).astype(np.uint8)).save(OUT / f"{name}.png")
        (OUT / f"{name}.truth.json").write_text(json.dumps({"objects": self.truth}, indent=1))


def meadow():
    s = Crayon(1)
    s.sky(170)
    s.sun(870, 95, 55)
    s.cloud(300, 90, 190, filled=False)
    g = s.ground(470, hills=25)
    s.tree(210, g(210) + 20, 260, 95)
    s.tree(640, g(640) + 20, 220, 80)
    for x, c in [(380, "flower_red"), (430, "flower_pink"), (800, "flower_purple"), (880, "flower_red")]:
        s.flower(x, g(x) + 60, c)
    s.save("meadow_trees_sun")


def house_scene():
    s = Crayon(2)
    s.sky(150, "sky2")
    s.sun(110, 100, 60)
    g = s.ground(500, hills=10)
    s.house(560, g(560) + 30, 280, 190)
    s.tree(170, g(170) + 25, 250, 90)
    s.flower(850, g(850) + 70, "flower_purple")
    s.flower(900, g(900) + 60, "flower_yellow")
    s.save("house_tree_garden")


def beach():
    s = Crayon(3)
    s.sky(200)
    s.sun(820, 110, 65)
    s.water(0, 420, 1000, 560)
    s.ground(560, "sand", "Sand", hills=6)
    s.rock(250, 620, 60)
    s.save("beach_sea_rock")


def mountains():
    s = Crayon(4)
    s.sky(180)
    s.mountain(300, 430, 520, 280)
    s.mountain(700, 430, 460, 230)
    g = s.ground(430, hills=12)
    s.tree(860, g(860) + 40, 230, 75)
    s.water(420, 560, 640, 640, "Water")
    s.save("mountains_pond")


def pencil_only():
    s = Crayon(5)
    ground = 520
    s.outline([(0, ground), (W, ground)], closed=False, width=4)
    s.add_truth("Floor", 0, ground, W, H)
    sun = s.ellipse_pts(150, 120, 60, 60)
    s.outline(sun, width=3)
    s.add_truth("Sun", 90, 60, 210, 180)
    for i in range(8):
        a = 2 * math.pi * i / 8
        s.draw.line([(150 + math.cos(a) * 75, 120 + math.sin(a) * 75), (150 + math.cos(a) * 105, 120 + math.sin(a) * 105)], fill=COLORS["pencil"], width=3)
    s.cloud(600, 120, 240)
    box = [(640, ground), (640, ground - 170), (860, ground - 170), (860, ground)]
    s.outline(box, width=3)
    s.add_truth("Shape", 640, ground - 170, 860, ground)
    s.save("pencil_sketch")


if __name__ == "__main__":
    meadow()
    house_scene()
    beach()
    mountains()
    pencil_only()
    print("scenes written to", OUT)
