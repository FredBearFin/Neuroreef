#!/usr/bin/env python3
"""Maps of the caves CaveProbe dumped, over the shaded relief.

usage: render.py <dumpdir> <map> <seeds,...> <png-prefix>

Per seed, <prefix>_<seed>.png: the shaded relief (water blue), carbonate rock tinted, karst passages drawn by their
discharge (dark red, wider and darker where more water runs), lava tubes orange, sinkholes (black rings), ponors (black
rings on a stream), entrances (yellow dots), karst springs (cyan dots, larger where drinkable) and losing river reaches
(magenta). Pixels are scaled up so features show.
"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw

KARST, LAVA, SINK, ENTR, SPRING, LOSING, PONOR = 1, 2, 4, 8, 16, 32, 64


def load(d, name, m, s, side, dt):
    return np.fromfile(os.path.join(d, f"{name}_{m}_{s}.{'f32' if dt == np.float32 else 'u8'}"), dtype=dt).reshape(side, side)


def render(d, m, s, out, zoom=0):
    side, sea = open(os.path.join(d, f"meta_{m}_{s}.txt")).read().split()
    side = int(side); sea = float(sea)
    elev = load(d, "elev", m, s, side, np.float32)
    carb = load(d, "carb", m, s, side, np.float32)
    flow = load(d, "flow", m, s, side, np.float32)
    kind = load(d, "kind", m, s, side, np.uint8)
    cell = load(d, "cell", m, s, side, np.uint8)
    rock = load(d, "rock", m, s, side, np.uint8)
    seep = load(d, "seep", m, s, side, np.float32)
    # hillshade (light from the north-west)
    z = (elev - sea) / (1 - sea) * 3000.0
    gx = (np.roll(z, -1, 1) - np.roll(z, 1, 1)) / (2 * 218.0)
    gy = (np.roll(z, -1, 0) - np.roll(z, 1, 0)) / (2 * 218.0)
    shade = np.clip(0.75 + 0.9 * (-gx - gy) / np.sqrt(2) / np.sqrt(1 + gx * gx + gy * gy), 0.25, 1.15)
    h = np.clip((elev - sea) / (1 - sea), 0, 1)
    base = np.stack([0.62 + 0.25 * h, 0.66 + 0.15 * h, 0.52 + 0.2 * h], -1)
    base[rock == 1] = base[rock == 1] * np.array([0.85, 0.75, 0.75])          # volcanic: darker, reddish
    base[rock == 0] = base[rock == 0] * np.array([0.88, 0.88, 0.92])          # crystalline: grey
    k = carb >= 0.5
    base[k] = np.array([0.96, 0.93, 0.72])                                    # limestone: pale yellow-cream
    img = np.clip(base * shade[..., None], 0, 1)
    water = (cell & 6) != 0
    img[water] = np.array([0.30, 0.48, 0.70])
    lose = (kind & LOSING) != 0
    img[lose] = np.array([0.85, 0.20, 0.75])
    S = max(1, 1024 // side)
    if zoom:
        # the zoom x zoom window holding the most karst passage, drawn at 1024 px
        k1 = ((kind & KARST) != 0).astype(np.float64)
        c = np.cumsum(np.cumsum(np.pad(k1, ((0, zoom), (0, zoom)), mode="wrap"), 0), 1)
        win = c[zoom:zoom + side, zoom:zoom + side] - c[:side, zoom:zoom + side] - c[zoom:zoom + side, :side] + c[:side, :side]
        y0, x0 = np.unravel_index(np.argmax(win), win.shape)
        y0, x0 = y0 + 1, x0 + 1
        def cut(a): return np.roll(np.roll(a, -y0, 0), -x0, 1)[:zoom, :zoom]
        img, flow, kind, seep = cut(img), cut(flow), cut(kind), cut(seep)
        side, S = zoom, 1024 // zoom
        print(f"zoom window at x {x0}, y {y0}, {zoom} cells")
    im = Image.fromarray((img * 255).astype(np.uint8)).resize((side * S, side * S), Image.NEAREST)
    dr = ImageDraw.Draw(im)
    qmin = 2.0 * (m // 4) ** 2 / 65536.0
    ys, xs = np.nonzero((kind & KARST) != 0)
    for y, x in zip(ys, xs):
        r = 0.18 + 0.12 * np.log2(max(1.0, flow[y, x] / qmin))
        r = min(r, 0.7) * S
        c = (120, 20, 20) if flow[y, x] > 8 * qmin else (170, 50, 40)
        cx, cy = (x + 0.5) * S, (y + 0.5) * S
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c)
    ys, xs = np.nonzero((kind & LAVA) != 0)
    for y, x in zip(ys, xs):
        cx, cy = (x + 0.5) * S, (y + 0.5) * S; r = 0.32 * S
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(235, 120, 20))
    ys, xs = np.nonzero((kind & SINK) != 0)
    for y, x in zip(ys, xs):
        cx, cy = (x + 0.5) * S, (y + 0.5) * S; r = (0.9 if kind[y, x] & PONOR else 0.6) * S
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(0, 0, 0), width=max(1, S // 3))
    spring_min = 2.0 * 365 * 50 / 3000
    ys, xs = np.nonzero((kind & SPRING) != 0)
    for y, x in zip(ys, xs):
        cx, cy = (x + 0.5) * S, (y + 0.5) * S; r = (1.0 if seep[y, x] >= spring_min else 0.6) * S
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(0, 230, 240), outline=(0, 60, 80))
    ys, xs = np.nonzero((kind & ENTR) != 0)
    for y, x in zip(ys, xs):
        cx, cy = (x + 0.5) * S, (y + 0.5) * S; r = 0.55 * S
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 220, 0), outline=(90, 60, 0))
    im.save(out)
    print(out)


def main():
    d, m, seeds, prefix = sys.argv[1], int(sys.argv[2]), sys.argv[3].split(","), sys.argv[4]
    os.makedirs(os.path.dirname(prefix) or ".", exist_ok=True)
    for s in seeds:
        render(d, m, s, f"{prefix}_{s}.png")
        render(d, m, s, f"{prefix}_zoom_{s}.png", zoom=64)


if __name__ == "__main__":
    main()
