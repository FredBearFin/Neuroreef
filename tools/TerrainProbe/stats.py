#!/usr/bin/env python3
"""Geomorphology statistics and images of the relief TerrainProbe dumped.

usage: stats.py <dumpdir> <map> <seeds,...> <png-prefix or ->

Heights are given as in the game: z = (elev - sea) / (1 - sea) of the land, times H_RELIEF = 3000 m above the lakes.
Slopes are given (a) as the 3D view draws them (world3d.js: RELIEF = 55 units of height, 4 units per field cell) and
(b) as a real gradient over the geology's horizontal scale (GEO_KM = 5000 km across the map).
"""
import sys, os
import numpy as np

H_RELIEF, RELIEF3D, CELL3D, GEO_KM, H_MAX = 3000.0, 55.0, 4.0, 5000.0, 5.0


def load(d, name, m, s, side):
    return np.fromfile(os.path.join(d, f"{name}_{m}_{s}.f32"), dtype=np.float32).reshape(side, side)


def grad(z):  # central differences on the torus, per cell
    gx = (np.roll(z, -1, 1) - np.roll(z, 1, 1)) * 0.5
    gy = (np.roll(z, -1, 0) - np.roll(z, 1, 0)) * 0.5
    return np.hypot(gx, gy)


def autocorr_peak(a, rmin):
    a = a - a.mean()
    f = np.fft.fft2(a)
    ac = np.real(np.fft.ifft2(f * np.conj(f)))
    ac /= ac[0, 0]
    n = a.shape[0]
    yy, xx = np.meshgrid(np.arange(n), np.arange(n), indexing="ij")
    dy = np.minimum(yy, n - yy); dx = np.minimum(xx, n - xx)
    r = np.hypot(dx, dy)
    mask = r >= rmin
    k = np.argmax(np.where(mask, ac, -9))
    return ac.flat[k], (int(dx.flat[k]), int(dy.flat[k])), ac


def stats(d, m, s):
    FW, CW, sea, csea, secs, resid = open(os.path.join(d, f"meta_{m}_{s}.txt")).read().split()
    FW, CW, sea, csea = int(FW), int(CW), float(sea), float(csea)
    e = load(d, "elev", m, s, FW); flow = load(d, "flow", m, s, FW); ch = load(d, "coarse", m, s, CW)
    land = e >= sea
    z = np.clip((e - sea) / (1 - sea), 0, None)
    out = {}
    out["secs"] = float(secs); out["resid"] = float(resid)
    out["lake"] = 1 - land.mean()
    zl = z[land]
    out["hist"] = np.histogram(zl, bins=10, range=(0, 1))[0] / zl.size
    out["hyps"] = [(zl > q).mean() for q in (0.1, 0.2, 0.3, 0.5, 0.7, 0.9)]
    out["HI"] = zl.mean(); out["median_m"] = np.median(zl) * H_RELIEF
    out["top2"] = (zl > 0.98).mean()
    cl = ch[ch >= csea]
    out["cap"] = (cl >= H_MAX - 0.01).mean()
    out["cmax"] = ch.max()
    # slopes
    g = grad(z)                                   # relief share per cell
    deg3d = np.degrees(np.arctan(g * RELIEF3D / CELL3D))
    real = g * H_RELIEF / (GEO_KM / FW * 1000)    # m/m
    dl = deg3d[land]
    out["deg_pct"] = np.percentile(dl, [50, 90, 99, 99.9])
    out["deg_bins"] = [((dl >= a) & (dl < b)).mean() for a, b in ((0, 2), (2, 10), (10, 25), (25, 35), (35, 45), (45, 91))]
    out["real_p99"] = np.percentile(real[land], 99) * 1000   # m/km
    hi = land & (z > 0.5)
    out["high_flat"] = (hi & (deg3d < 3)).sum() / max(1, hi.sum())
    # local relief and ridge/valley sharpness: Laplacian (per cell, in render units)
    zr = z * RELIEF3D
    lap = np.roll(zr, 1, 0) + np.roll(zr, -1, 0) + np.roll(zr, 1, 1) + np.roll(zr, -1, 1) - 4 * zr
    out["lap_hi"] = np.abs(lap[hi]).mean() if hi.any() else 0
    # drainage: share of land with flow over A cells (of mean rain), and the area-channel scaling
    out["chan"] = [(flow[land] > a).mean() for a in (8, 32, 128)]
    # valley shape: around channel cells (flow over 32 cells) in the uplands (over 0.25 of the relief) and the lowlands
    # (under 0.08), the share of a 9 x 9 window within 30 m of the channel's height (a wide flat floor: high; a V: low),
    # and the mean 3D-view slope of the 3 x 3 cells around it
    zm = z * H_RELIEF
    vs = []
    for zlo, zhi in ((0.25, 9), (0.0, 0.08)):
        sel = np.nonzero(land & (flow >= 32) & (z >= zlo) & (z < zhi))
        if len(sel[0]) == 0: vs.append((0, 0, 0)); continue
        k = np.random.default_rng(0).choice(len(sel[0]), min(1500, len(sel[0])), replace=False)
        floor = side = 0.0
        for y, x in zip(sel[0][k], sel[1][k]):
            rows = [(y + d) % FW for d in range(-4, 5)]; colsw = [(x + d) % FW for d in range(-4, 5)]
            win = zm[np.ix_(rows, colsw)]
            floor += (np.abs(win - zm[y, x]) < 30).mean()
            side += deg3d[np.ix_(rows[3:6], colsw[3:6])].mean()
        vs.append((len(sel[0]), floor / len(k), side / len(k)))
    out["valley"] = vs
    # periodicity
    pk, off, _ = autocorr_peak(np.where(land, z, 0), FW / 8)
    out["ac_peak"] = pk; out["ac_off"] = off
    pkc, offc, _ = autocorr_peak(ch, CW / 8)
    out["ac_c"] = pkc; out["ac_coff"] = offc
    return out, e, z, land, deg3d, sea, FW


def shade(z, land, FW):
    zr = z * RELIEF3D
    gx = (np.roll(zr, -1, 1) - np.roll(zr, 1, 1)) / (2 * CELL3D)
    gy = (np.roll(zr, -1, 0) - np.roll(zr, 1, 0)) / (2 * CELL3D)
    nx, ny, nz = -gx, -gy, np.ones_like(gx)
    L = np.array([-1, -1, 1.4]); L = L / np.linalg.norm(L)
    sh = (nx * L[0] + ny * L[1] + nz * L[2]) / np.sqrt(nx * nx + ny * ny + nz * nz)
    sh = np.clip(sh, 0, 1)
    # hypsometric tint
    stops = np.array([0, 0.08, 0.25, 0.5, 0.75, 1.0])
    cols = np.array([[70, 120, 60], [110, 150, 75], [180, 170, 110], [150, 120, 90], [130, 110, 100], [245, 245, 245]], float)
    rgb = np.stack([np.interp(z, stops, cols[:, k]) for k in range(3)], -1)
    rgb = rgb * (0.35 + 0.75 * sh[..., None])
    rgb[~land] = [40, 80, 150]
    return np.clip(rgb, 0, 255).astype(np.uint8)


def images(prefix, z, land, FW, e, sea):
    from PIL import Image, ImageDraw
    rgb = shade(z, land, FW)
    big = max(1, 512 // FW)
    im = Image.fromarray(rgb).resize((FW * big, FW * big), Image.NEAREST)
    im.save(prefix + "_shade.png")
    # 2 x 2 tiled, as the 3D view's zoomed-out torus shows it
    Image.fromarray(np.tile(rgb, (2, 2, 1))).save(prefix + "_tiled.png")
    # side views: three rows as silhouettes at the 3D view's vertical scale (x4 to be legible)
    W, Hh = FW * 4, 3 * 160
    pr = Image.new("RGB", (W, Hh), (230, 235, 240)); dr = ImageDraw.Draw(pr)
    for k, row in enumerate((FW // 5, FW // 2, 4 * FW // 5)):
        base = (k + 1) * 160 - 10
        hs = np.where(e[row] >= sea, (e[row] - sea) / (1 - sea), (e[row] - sea) / (1 - sea))
        pts = [(x * 4, base - hs[x] * RELIEF3D * 4 / CELL3D * 1.0) for x in range(FW)]   # 4 px per cell, true 3D-view aspect
        dr.polygon(pts + [(W, base + 8), (0, base + 8)], fill=(120, 100, 80))
        dr.line([(0, base), (W, base)], fill=(40, 80, 150))
    pr.save(prefix + "_profile.png")
    oblique(rgb, z, land, FW, prefix + "_view.png")


def oblique(rgb, z, land, FW, out, sea_e=None):
    """A perspective view from above one edge, as the 3D view's overview sees it (voxel-space column rendering):
    heights z x RELIEF3D over CELL3D a cell; the map is drawn twice in depth (the torus), the camera 40 cells back."""
    from PIL import Image
    Wpx, Hpx = 768, 432
    hz = np.where(land, z, 0.0) * RELIEF3D
    img = np.zeros((Hpx, Wpx, 3), np.uint8); img[:] = (200, 215, 235)
    ybuf = np.full(Wpx, Hpx, int)
    camH, horizon, focal = 0.5 * RELIEF3D + 60.0, Hpx * 0.18, 300.0
    cols = np.arange(Wpx)
    for dstep in range(1, int(FW * 1.5)):
        d = dstep * CELL3D                                    # depth in units
        half = d * 0.6                                        # half-width of view at that depth (units)
        wx = (cols / Wpx - 0.5) * 2 * half / CELL3D + FW / 2  # world x in cells
        wy = (FW - 40 + dstep) % FW                           # camera 40 cells before the south edge, looking north
        xi = np.floor(wx).astype(int) % FW
        h = hz[wy, xi]
        sy = (horizon + (camH - h) / d * focal).astype(int)
        sy = np.clip(sy, 0, Hpx)
        c = rgb[wy, xi]
        fog = min(1.0, d / (FW * 1.5 * CELL3D))
        c = (c * (1 - 0.6 * fog) + np.array([200, 215, 235]) * 0.6 * fog).astype(np.uint8)
        for k in np.nonzero(sy < ybuf)[0]:
            img[sy[k]:ybuf[k], k] = c[k]
            ybuf[k] = sy[k]
    Image.fromarray(img).save(out)


def fmt(o):
    p = lambda a: " ".join(f"{100 * v:4.1f}" for v in a)
    return (f"  time {o['secs']:.2f} s  residual {o['resid']:.3f}  lake {100 * o['lake']:.1f}%\n"
            f"  land elevation histogram (tenths of relief, %): {p(o['hist'])}\n"
            f"  hypsometry: share of land above 0.1 0.2 0.3 0.5 0.7 0.9 of relief (%): {p(o['hyps'])}; HI {o['HI']:.3f}, median {o['median_m']:.0f} m\n"
            f"  coarse land at the 5 km cap {100 * o['cap']:.1f}% (coarse max {o['cmax']:.2f} km); fine land in top 2% of relief {100 * o['top2']:.2f}%\n"
            f"  slope (3D view deg) p50/p90/p99/p99.9: {' '.join(f'{v:.1f}' for v in o['deg_pct'])}; bins <2,2-10,10-25,25-35,35-45,>45 (%): {p(o['deg_bins'])}\n"
            f"  gradient p99 at the plates' scale (GEO_KM) {o['real_p99']:.1f} m/km; high land (>0.5) that is flat (<3 deg): {100 * o['high_flat']:.1f}%; mean |lap| on high land {o['lap_hi']:.2f}\n"
            f"  channels: land with flow > 8/32/128 cells (%): {p(o['chan'])}\n"
            f"  valleys (channels, upland / lowland): floor share within 30 m {o['valley'][0][1]:.2f} / {o['valley'][1][1]:.2f}; slope around {o['valley'][0][2]:.1f} / {o['valley'][1][2]:.1f} deg\n"
            f"  periodicity: max autocorrelation beyond FW/8 {o['ac_peak']:.3f} at {o['ac_off']}; coarse {o['ac_c']:.3f} at {o['ac_coff']}")


if __name__ == "__main__":
    d, m, seeds, prefix = sys.argv[1], sys.argv[2], sys.argv[3].split(","), sys.argv[4]
    for s in seeds:
        o, e, z, land, deg, sea, FW = stats(d, m, s)
        print(f"map {m} seed {s}:\n{fmt(o)}")
        if prefix != "-":
            images(f"{prefix}_{m}_s{s}", z, land, FW, e, sea)
