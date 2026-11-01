#!/usr/bin/env python3
"""Mountain statistics and images of the relief TerrainProbe dumped (high, jagged mountains, 2026-11-01).

usage: mtn.py <dumpdir> <map> <seeds,...> <png-prefix or -> [T_ELA]

Heights are those the game reads: z = (elev - sea) / (1 - sea) x H_RELIEF m above the lakes (H_RELIEF from the meta
file, so before and after are each on their own scale). A field cell is GEO_CELL_M = 218 m. The snowline is the
deep-time ELA Geology uses: where the Earth-like annual mean T_EQ - DT_EP sin^2(lat) - LAPSE z falls to T_ELA (-2 C).
Jaggedness: (a) crest sharpness, the mean drop (m) from a ridge cell (higher than both neighbours along a row, a column or
a diagonal) to the mean of its 8 neighbours, over the ridge cells above half the top; (b) mean |Laplacian| (m per cell^2)
over the land above half the top; (c) the share of mountain land (above half the top) steeper than 40 degrees.
"""
import sys, os
import numpy as np

CELL_M = 218.0
ROCK = ["crystalline", "volcanic", "sedimentary", "limestone"]


def load(d, name, m, s, side):
    return np.fromfile(os.path.join(d, f"{name}_{m}_{s}.f32"), dtype=np.float32).reshape(side, side)


def slope_deg(zm):  # steepest of the 8 neighbours' slopes (degrees): what a crag is
    best = np.zeros_like(zm)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0: continue
            dist = CELL_M * (1.4142135 if dx and dy else 1.0)
            best = np.maximum(best, (zm - np.roll(np.roll(zm, dy, 0), dx, 1)) / dist)
    return np.degrees(np.arctan(best))


def stats(d, m, s, t_ela):
    meta = open(os.path.join(d, f"meta_{m}_{s}.txt")).read().split()
    FW, CW, sea = int(meta[0]), int(meta[1]), float(meta[2])
    H, clim, teq, dtep, lapse = (float(v) for v in meta[6:11])
    e = load(d, "elev", m, s, FW); rock = load(d, "rock", m, s, FW).astype(int)
    land = e >= sea
    zm = np.clip((e - sea) / (1 - sea), 0, None) * H
    top = zm.max()
    o = {"H": H, "top": top, "secs": float(meta[4])}
    o["p99"] = np.percentile(zm[land], 99); o["median"] = np.median(zm[land])
    sd = slope_deg(np.where(land, zm, 0))
    o["slope"] = {}
    for k in range(len(ROCK)):
        sel = land & (rock == k)
        if sel.sum() < 20: continue
        v = sd[sel]
        o["slope"][ROCK[k]] = (sel.mean() / land.mean(), np.percentile(v, [50, 90, 99]), v.max(), (v > 40).mean(), (v > 50).mean())
    # snowline (ELA) by row
    y = np.arange(FW)
    sn = np.sin(2 * np.pi * y / FW) ** 2
    ela = np.clip((teq - dtep * sn - t_ela) / lapse * 1000.0, 0, None)[:, None]
    o["snow"] = (land & (zm > ela)).sum() / land.sum()
    o["snow_lowlat"] = ((land & (zm > ela) & (sn[:, None] < 0.5)).sum() / max(1, (land & (sn[:, None] < 0.5)).sum()))
    o["above_ela"] = np.percentile((zm - ela)[land & (zm > ela)], 99) if (land & (zm > ela)).any() else 0
    # jaggedness
    mtn = land & (zm > 0.5 * top)
    nb = sum(np.roll(np.roll(zm, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dx or dy) / 8.0
    ridge = np.zeros_like(land)
    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        a = np.roll(np.roll(zm, dy, 0), dx, 1); b = np.roll(np.roll(zm, -dy, 0), -dx, 1)
        ridge |= (zm > a) & (zm > b)
    rsel = mtn & ridge
    o["crest"] = (zm - nb)[rsel].mean() if rsel.any() else 0
    lap = np.roll(zm, 1, 0) + np.roll(zm, -1, 0) + np.roll(zm, 1, 1) + np.roll(zm, -1, 1) - 4 * zm
    o["lap"] = np.abs(lap[mtn]).mean() if mtn.any() else 0
    o["steep_mtn"] = (sd[mtn] > 40).mean() if mtn.any() else 0
    o["mtn_land"] = mtn.sum() / land.sum()
    o["land_gt1500"] = (zm[land] > 1500).mean(); o["land_gt3000"] = (zm[land] > 3000).mean()
    return o, e, zm, land, rock, sd, sea, FW, ela


def fmt(o):
    lines = [f"  vertical scale H_RELIEF {o['H']:.0f} m; max relief {o['top']:.0f} m, land p99 {o['p99']:.0f} m, median {o['median']:.0f} m;"
             f" land > 1500 m {100 * o['land_gt1500']:.1f}%, > 3000 m {100 * o['land_gt3000']:.1f}%; shape {o['secs']:.2f} s"]
    for k, (share, p, mx, s40, s50) in o["slope"].items():
        lines.append(f"  {k:12s} {100 * share:5.1f}% of land: slope p50/p90/p99 {p[0]:4.1f}/{p[1]:4.1f}/{p[2]:4.1f} deg, max {mx:4.1f}; > 40 deg {100 * s40:5.2f}%, > 50 deg {100 * s50:5.2f}%")
    lines.append(f"  land above the snowline (ELA) {100 * o['snow']:.1f}% (latitudes under 45 deg: {100 * o['snow_lowlat']:.1f}%); p99 height above the ELA {o['above_ela']:.0f} m")
    lines.append(f"  jaggedness (land above half the top, {100 * o['mtn_land']:.1f}% of land): crest sharpness {o['crest']:.1f} m, mean |laplacian| {o['lap']:.1f} m/cell^2, steeper than 40 deg {100 * o['steep_mtn']:.1f}%")
    return "\n".join(lines)


ROCK_RGB = np.array([[150, 140, 140], [120, 90, 80], [190, 170, 120], [215, 210, 190]], float)


def shade_rgb(zm, land, rock, sd, ela, light=(-1.0, -1.0, 1.2), amb=0.3):
    gx = (np.roll(zm, -1, 1) - np.roll(zm, 1, 1)) / (2 * CELL_M)
    gy = (np.roll(zm, -1, 0) - np.roll(zm, 1, 0)) / (2 * CELL_M)
    L = np.array(light, float); L /= np.linalg.norm(L)
    sh = np.clip((-gx * L[0] - gy * L[1] + L[2]) / np.sqrt(gx * gx + gy * gy + 1), 0, 1)
    stops = np.array([0, 300, 1000, 2000, 3500, 5000, 7000])
    cols = np.array([[70, 120, 60], [105, 145, 75], [150, 150, 95], [140, 120, 95], [125, 110, 105], [150, 145, 145], [235, 235, 240]], float)
    rgb = np.stack([np.interp(zm, stops, cols[:, k]) for k in range(3)], -1)
    # bare steep rock shows its kind; snow above the ELA where it is not too steep to hold
    steep = np.clip((sd - 30) / 15, 0, 1)[..., None]
    rgb = rgb * (1 - 0.6 * steep) + ROCK_RGB[np.clip(rock, 0, 3)] * 0.6 * steep
    snow = ((zm > ela) & (sd < 40))[..., None]
    rgb = np.where(snow, 0.5 * rgb + 0.5 * np.array([245, 248, 255.0]), rgb)
    rgb = rgb * (amb + 0.8 * sh[..., None])
    rgb[~land] = [40, 80, 150]
    return np.clip(rgb, 0, 255).astype(np.uint8)


def bilinear(a, wx, wy, FW):
    x0 = np.floor(wx).astype(int); y0 = np.floor(wy).astype(int); tx = (wx - x0)[..., None] if a.ndim == 3 else wx - x0
    ty = (wy - y0)[..., None] if a.ndim == 3 else wy - y0
    x0 %= FW; y0 %= FW; x1 = (x0 + 1) % FW; y1 = (y0 + 1) % FW
    return (a[y0, x0] * (1 - tx) + a[y0, x1] * tx) * (1 - ty) + (a[y1, x0] * (1 - tx) + a[y1, x1] * tx) * ty


def perspective(rgb, zm, land, FW, out, tx, ty, back, up):
    """A true-scale perspective view (no vertical exaggeration) of the point (tx, ty): the camera stands back cells
    from it toward the south-west, up m above the higher of the ground under it and the target; heights and colours
    sampled bilinearly; voxel-space column rendering on the torus, looking slightly down at the target."""
    from PIL import Image
    Wpx, Hpx = 960, 540
    img = np.zeros((Hpx, Wpx, 3), np.uint8)
    sky = np.linspace(0, 1, Hpx)[:, None]
    img[:] = (np.array([150, 185, 230]) * (1 - sky) + np.array([215, 225, 240]) * sky)[:, None, :].astype(np.uint8)
    hz = np.where(land, zm, 0.0)
    ang = np.pi / 4
    fx, fy = np.cos(ang), np.sin(ang)
    cx, cy = tx - back * fx, ty - back * fy
    camz = max(hz[int(cy) % FW, int(cx) % FW], 0.6 * hz[int(ty) % FW, int(tx) % FW]) + up
    focal = Wpx * 0.8
    # tilt so the target sits 45% down the image
    horizon = Hpx * 0.45 - (camz - hz[int(ty) % FW, int(tx) % FW]) / (back * CELL_M) * focal
    ybuf = np.full(Wpx, Hpx, int)
    cols = (np.arange(Wpx) / Wpx - 0.5) * 2 * 0.6
    dd, dist = 0.5, FW * 0.9
    while dd < dist:
        wx = cx + fx * dd - fy * cols * dd
        wy = cy + fy * dd + fx * cols * dd
        h = bilinear(hz, wx, wy, FW)
        sy = np.clip((horizon + (camz - h) / (dd * CELL_M) * focal).astype(int), 0, Hpx)
        c = bilinear(rgb.astype(float), wx, wy, FW)
        fog = min(1.0, dd / dist) ** 1.5
        c = (c * (1 - 0.7 * fog) + np.array([205, 218, 235]) * 0.7 * fog).astype(np.uint8)
        for k in np.nonzero(sy < ybuf)[0]:
            img[sy[k]:ybuf[k], k] = c[k]
            ybuf[k] = sy[k]
        dd += 0.15 + dd * 0.004
    Image.fromarray(img).save(out)


def images(prefix, zm, land, rock, sd, ela, FW):
    from PIL import Image
    rgb = shade_rgb(zm, land, rock, sd, ela)
    big = max(1, 768 // FW)
    Image.fromarray(rgb).resize((FW * big, FW * big), Image.NEAREST).save(prefix + "_shade.png")
    # the highest peak, and the highest peak at latitudes under 45 degrees, from 40 cells (9 km) to the south-west
    # for the views: low sun from the camera's right (it looks north-east), so slopes facing it and away read apart
    side = shade_rgb(zm, land, rock, sd, ela, light=(1.0, -1.0, 0.7), amb=0.15)
    py, px = np.unravel_index(np.argmax(zm), zm.shape)
    perspective(side, zm, land, FW, prefix + "_view.png", px, py, 40, 600.0)
    sn = (np.sin(2 * np.pi * np.arange(FW) / FW) ** 2)[:, None]
    py, px = np.unravel_index(np.argmax(np.where(sn < 0.5, zm, -1)), zm.shape)
    perspective(side, zm, land, FW, prefix + "_view2.png", px, py, 40, 600.0)


if __name__ == "__main__":
    d, m, seeds, prefix = sys.argv[1], sys.argv[2], sys.argv[3].split(","), sys.argv[4]
    t_ela = float(sys.argv[5]) if len(sys.argv) > 5 else -2.0
    for s in seeds:
        o, e, zm, land, rock, sd, sea, FW, ela = stats(d, m, s, t_ela)
        print(f"map {m} seed {s}:\n{fmt(o)}")
        if prefix != "-":
            images(f"{prefix}_s{s}", zm, land, rock, sd, ela, FW)
            if os.environ.get("MTN_DIAG"):   # slope | rock | glacier ice at the end of deep time
                from PIL import Image
                g = np.clip(sd / 60 * 255, 0, 255).astype(np.uint8)
                a = np.stack([g, g, g], -1)
                b = ROCK_RGB[np.clip(rock, 0, 3)].astype(np.uint8); b[~land] = [40, 80, 150]
                ip = os.path.join(d, f"ice_{m}_{s}.f32")
                ice = np.fromfile(ip, dtype=np.float32).reshape(FW, FW) if os.path.exists(ip) else np.zeros((FW, FW))
                c = np.stack([np.clip(zm / zm.max() * 200, 0, 255)] * 3, -1)
                c[ice > 0] = [60, 140, 255]; c[~land] = [20, 40, 90]
                Image.fromarray(np.concatenate([a, b, c.astype(np.uint8)], 1)).resize((FW * 6, FW * 2), Image.NEAREST).save(f"{prefix}_s{s}_diag.png")
