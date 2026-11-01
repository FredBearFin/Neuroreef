#!/usr/bin/env python3
"""Maps of the water depths and wetlands WetProbe dumped, over the shaded relief.

usage: draw.py <dumpdir> <seeds,...> <png-prefix> [depth-only]

Per seed, <prefix>depth_<seed>.png: water coloured by its depth in metres (log scale, 0.1 m pale cyan to 1,000 m
navy; the legend's ticks at 0.3, 1, 2, 5, 10, 30, 100, 300 m), the shallow realm (<= 2 m) outlined in white.
Unless depth-only, also <prefix>wetlands_<seed>.png, three panels: the wetlands by kind (marsh light green, swamp dark
green, fen ochre, bog plum) with peat as brown on the rest of the land; the share of the year each cell is waterlogged;
the cover-weighted flood tolerance of the plants (blue tolerant, red intolerant), wetlands outlined.
"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw

S = 4  # pixels per cell


def load(d, name, s, side, dt=np.float32):
    p = os.path.join(d, f"{name}_{s}.{'f32' if dt == np.float32 else 'u8'}")
    return np.fromfile(p, dtype=dt).reshape(side, side) if os.path.exists(p) else None


def shade(d, s, side, sea):
    elev = load(d, "elev", s, side)
    z = (elev - sea) / (1 - sea) * 8000.0
    gx = (np.roll(z, -1, 1) - np.roll(z, 1, 1)) / (2 * 218.0)
    gy = (np.roll(z, -1, 0) - np.roll(z, 1, 0)) / (2 * 218.0)
    sh = np.clip(0.8 + 0.5 * (-gx - gy) / np.sqrt(2) / np.sqrt(1 + gx * gx + gy * gy), 0.35, 1.1)
    h = np.clip((elev - sea) / (1 - sea) / 0.6, 0, 1)
    base = np.stack([0.70 + 0.2 * h, 0.72 + 0.1 * h, 0.58 + 0.25 * h], -1)
    return np.clip(base * sh[..., None], 0, 1)


def depth_rgb(m):
    t = np.clip((np.log10(np.maximum(m, 0.1)) + 1) / 4, 0, 1)  # 0.1 m .. 1,000 m
    stops = np.array([[0.75, 0.95, 0.97], [0.35, 0.80, 0.85], [0.15, 0.55, 0.78], [0.08, 0.30, 0.62], [0.03, 0.10, 0.35]])
    x = t * (len(stops) - 1)
    k = np.minimum(x.astype(int), len(stops) - 2)
    f = (x - k)[..., None]
    return stops[k] * (1 - f) + stops[k + 1] * f


def up(img):
    return np.repeat(np.repeat(img, S, 0), S, 1)


def outline(mask):
    m = up(mask)
    e = m & ~(np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1))
    return e


def legend(draw, x0, y0, title):
    w = 220
    for k in range(w):
        m = 10 ** (-1 + 4 * k / (w - 1))
        c = tuple(int(255 * v) for v in depth_rgb(np.array(m)))
        draw.line([(x0 + k, y0), (x0 + k, y0 + 10)], fill=c)
    for m in [0.3, 1, 2, 5, 10, 30, 100, 300]:
        k = int((np.log10(m) + 1) / 4 * (w - 1))
        draw.line([(x0 + k, y0 + 10), (x0 + k, y0 + 14)], fill=(0, 0, 0))
        draw.text((x0 + k - 6, y0 + 15), f"{m:g}", fill=(0, 0, 0))
    draw.text((x0, y0 - 13), title, fill=(0, 0, 0))


def depth_map(d, s, side, sea, out, label):
    cell = load(d, "cell", s, side, np.uint8)
    dep = load(d, "depth", s, side)
    img = shade(d, s, side, sea)
    water = (cell & 6) != 0
    img[water] = depth_rgb(dep[water])
    big = up(img)
    big[outline(water & (dep <= 2.0))] = [1, 1, 1]
    pad = 46
    canvas = np.ones((big.shape[0] + pad, big.shape[1], 3))
    canvas[pad:] = big
    im = Image.fromarray((canvas * 255).astype(np.uint8))
    dr = ImageDraw.Draw(im)
    legend(dr, 8, 18, f"seed {s} {label}: water depth (m); white: the shallow realm (<= 2 m)")
    im.save(out)


def wet_map(d, s, side, sea, out):
    cell = load(d, "cell", s, side, np.uint8)
    wk = load(d, "wetland", s, side, np.uint8)
    peat = load(d, "peat", s, side)
    dur = load(d, "wetdur", s, side)
    tol = load(d, "floodtol", s, side)
    dep = load(d, "depth", s, side)
    water = (cell & 6) != 0
    land = cell == 1
    # panel 1: kinds and peat
    a = shade(d, s, side, sea)
    a[water] = depth_rgb(dep[water]) * 0.8 + 0.2
    pe = np.clip(peat / 3.0, 0, 1)[..., None]
    a = np.where((land & (peat > 0.01))[..., None], a * (1 - 0.6 * pe) + np.array([0.45, 0.28, 0.12]) * 0.6 * pe, a)
    cols = {1: [0.60, 0.88, 0.35], 2: [0.10, 0.45, 0.15], 3: [0.85, 0.62, 0.15], 4: [0.55, 0.20, 0.50]}
    for k, c in cols.items():
        a[wk == k] = c
    # panel 2: waterlogged share of the year
    b = shade(d, s, side, sea) * 0.6 + 0.4
    b[water] = [0.75, 0.85, 0.95]
    t = np.clip(dur, 0, 1)[..., None]
    b = np.where((land & (dur > 0.02))[..., None], b * (1 - t) + np.array([0.05, 0.25, 0.75]) * t, b)
    # panel 3: flood tolerance of the plants
    c3 = np.ones_like(a) * 0.9
    c3[water] = [0.75, 0.85, 0.95]
    tv = np.clip(tol, 0, 1)[..., None]
    col = np.array([0.85, 0.15, 0.10]) * (1 - tv) + np.array([0.10, 0.30, 0.90]) * tv
    c3 = np.where((land & (tol >= 0))[..., None], col, c3)
    panels = [up(a), up(b), up(c3)]
    o = outline((wk > 0) & land)
    panels[0][o] = [0, 0, 0]   # wetlands are often single cells: outline them to be seen
    panels[2][o] = [0, 0, 0]
    gap = 8
    H = panels[0].shape[0]; W = panels[0].shape[1]
    pad = 40
    canvas = np.ones((H + pad, 3 * W + 2 * gap, 3))
    for k, p in enumerate(panels): canvas[pad:, k * (W + gap):k * (W + gap) + W] = p
    im = Image.fromarray((canvas * 255).astype(np.uint8))
    dr = ImageDraw.Draw(im)
    dr.text((4, 4), f"seed {s}: wetlands (marsh light green, swamp dark green, fen ochre, bog plum); peat brown elsewhere", fill=(0, 0, 0))
    dr.text((W + gap + 4, 4), "share of the year waterlogged (water table within 0.3 m): 0 light, 1 blue", fill=(0, 0, 0))
    dr.text((2 * (W + gap) + 4, 4), "plants' flood tolerance (cover-weighted): red 0, blue 1; wetlands outlined", fill=(0, 0, 0))
    n = [(wk == k).sum() for k in range(5)]
    dr.text((4, 20), f"marsh {n[1]}  swamp {n[2]}  fen {n[3]}  bog {n[4]} cells  of {land.sum()} land", fill=(0, 0, 0))
    im.save(out)


def main():
    d, seeds, prefix = sys.argv[1], sys.argv[2].split(","), sys.argv[3]
    only_depth = len(sys.argv) > 4
    label = sys.argv[4] if only_depth else "after"
    os.makedirs(os.path.dirname(prefix) or ".", exist_ok=True)
    for s in seeds:
        side, sea = open(os.path.join(d, f"meta_{s}.txt")).read().split()
        side = int(side); sea = float(sea)
        depth_map(d, s, side, sea, f"{prefix}depth_{label}_{s}.png", label)
        if not only_depth: wet_map(d, s, side, sea, f"{prefix}wetlands_{s}.png")


if __name__ == "__main__":
    main()
