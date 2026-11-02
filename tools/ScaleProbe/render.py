"""Shaded relief of the worlds tools/ScaleProbe dumped (2026-11-01, the 3.5 km world).

python3 tools/ScaleProbe/render.py <dir> <map> <seeds,comma> <out prefix>

Writes <prefix>_<map>_<seed>.png: the relief at true scale (heights from Map.ReliefM, cells GEO_CELL_M), lit from the
north-west, tinted by height, crags by rock (crystalline grey, limestone pale, volcanic dark), lakes blue, streams drawn
along their cells (at least a pixel: their true widths of 0.3-3 m are far below a pixel), with a 1 km scale bar.
"""
import sys
import numpy as np
from PIL import Image, ImageDraw

d, mp, seeds, out = sys.argv[1], int(sys.argv[2]), [int(s) for s in sys.argv[3].split(',')], sys.argv[4]
for sd in seeds:
    fw, sea, relief, cell, lat = open(f'{d}/meta_{mp}_{sd}.txt').read().split()
    fw = int(fw); sea = float(sea); relief = float(relief); cell = float(cell); lat = float(lat)
    ld = lambda n: np.fromfile(f'{d}/{n}_{mp}_{sd}.f32', dtype=np.float32).reshape(fw, fw)
    e, rock, cl, rw = ld('elev'), ld('rock'), ld('cell'), ld('rivwm')
    up = max(1, 768 // fw)
    h = np.maximum(e - sea, 0) / (1 - sea) * relief                      # m above the lakes
    # upsample (periodic Catmull-Rom, as the 3D view's ground is smooth between cells) for a smooth hillshade
    def ups(a):
        n = a.shape[0] * up
        t = (np.arange(n) + 0.5) / up - 0.5
        i0 = np.floor(t).astype(int); f = t - i0
        def cr(x):
            x = np.abs(x)
            return np.where(x < 1, 1.5 * x**3 - 2.5 * x**2 + 1, np.where(x < 2, -0.5 * x**3 + 2.5 * x**2 - 4 * x + 2, 0))
        wts = [cr(k - f) for k in (-1, 0, 1, 2)]
        a1 = sum(a[(i0 + k) % fw, :] * wts[j][:, None] for j, k in enumerate((-1, 0, 1, 2)))
        return sum(a1[:, (i0 + k) % fw] * wts[j][None, :] for j, k in enumerate((-1, 0, 1, 2)))
    H = ups(h); px = cell / up
    gy, gx = np.gradient(np.pad(H, 1, mode='wrap'), px)
    gy, gx = gy[1:-1, 1:-1], gx[1:-1, 1:-1]
    az, alt = np.radians(315), np.radians(40)
    slope = np.arctan(np.hypot(gx, gy)); aspect = np.arctan2(-gx, gy)
    shade = np.sin(alt) * np.cos(slope) + np.cos(alt) * np.sin(slope) * np.cos(az - aspect)
    shade = np.clip(shade, 0, 1)
    top = max(1.0, h.max())
    t = np.clip(H / top, 0, 1)[..., None]
    lo, mid, hi = np.array([0.42, 0.55, 0.30]), np.array([0.62, 0.58, 0.38]), np.array([0.80, 0.76, 0.70])
    col = np.where(t < 0.5, lo + (mid - lo) * (t / 0.5), mid + (hi - mid) * ((t - 0.5) / 0.5))
    R = np.kron(rock, np.ones((up, up)))[..., None]
    steep = np.clip((np.degrees(slope) - 25) / 15, 0, 1)[..., None]
    rockc = np.where(R == 0, np.array([0.62, 0.62, 0.64]), np.where(R == 3, np.array([0.86, 0.84, 0.76]),
                     np.where(R == 1, np.array([0.38, 0.34, 0.33]), np.array([0.70, 0.62, 0.48]))))
    col = col * (1 - 0.6 * steep) + rockc * 0.6 * steep
    img = col * (0.35 + 0.75 * shade[..., None])
    water = np.kron((cl != 1) & (rw <= 0), np.ones((up, up))).astype(bool)
    img[water] = np.array([0.20, 0.36, 0.55]) * (0.85 + 0.15 * shade[water][:, None])
    img = (np.clip(img, 0, 1) * 255).astype(np.uint8)
    im = Image.fromarray(img); dr = ImageDraw.Draw(im)
    for y, x in zip(*np.nonzero(rw > 0)):
        c = ((x + 0.5) * up, (y + 0.5) * up); r = max(0.7, rw[y, x] / px / 2) + 0.6
        dr.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=(60, 110, 190))
    km = 1000 / px; W = im.size[0]
    dr.rectangle([20, W - 34, 20 + km, W - 28], fill=(255, 255, 255)); dr.text((20, W - 26), '1 km', fill=(255, 255, 255))
    dr.text((20, 12), f'map {mp} seed {sd}: {fw * cell / 1000:.2f} km across, top {h.max():.0f} m above the lakes, latitude {lat:.0f}',
            fill=(255, 255, 255))
    im.save(f'{out}_{mp}_{sd}.png')
    print('wrote', f'{out}_{mp}_{sd}.png')
