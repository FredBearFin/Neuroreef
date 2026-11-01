# Draws what tools/RiverProbe dumped (2026-11-01, rivers that look like rivers):
#   python3 tools/RiverProbe/draw.py <dumpdir> <seeds, e.g. 3,7,11> <png prefix, e.g. docs/art/terrain2/riv_before>
# writes <prefix>_<seed>_map.png (shaded relief with lakes, the sea and every channel drawn along its centreline at its
# width; beside it the discharge, log10 Flow, with the channel cells), <prefix>_<seed>_zoom.png (three close-ups, the
# grid's cells outlined) and <prefix>_stats.png (direction histograms of steps and reaches, Horton's plot, sinuosity
# against valley slope).
import json, math, sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

d, seeds, prefix = sys.argv[1], [int(s) for s in sys.argv[2].split(',')], sys.argv[3]
os.makedirs(os.path.dirname(prefix) or '.', exist_ok=True)
try:
    FONT = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 13)
except Exception:
    FONT = ImageFont.load_default()


def load(name, s, dt):
    return np.fromfile(os.path.join(d, f'{name}_{s}.{dt}'), {'f32': np.float32, 'i32': np.int32, 'u8': np.uint8}[dt])


def world(s):
    FW, sea, _ = open(os.path.join(d, f'meta_{s}.txt')).read().split()
    FW = int(FW); sea = float(sea)
    g = {k: load(k, s, t).reshape(FW, FW) for k, t in
         [('elev', 'f32'), ('flow', 'f32'), ('rivx', 'f32'), ('rivy', 'f32'), ('rivw', 'f32'), ('lake', 'i32'), ('recv', 'i32'), ('from', 'i32'), ('cell', 'u8')]}
    g['FW'] = FW; g['sea'] = sea
    idx = np.arange(FW * FW).reshape(FW, FW)
    g['chan'] = (g['cell'] != 1) & (g['lake'] < 0) & (g['from'] == idx)
    return g


def shade(g):
    z = g['elev'] * 3000.0 / max(1e-6, 1 - g['sea'])
    gx = (np.roll(z, -1, 1) - np.roll(z, 1, 1)) / (2 * 218.0)
    gy = (np.roll(z, -1, 0) - np.roll(z, 1, 0)) / (2 * 218.0)
    nx, ny, nz = -gx * 2, -gy * 2, np.ones_like(z)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    lx, ly, lz = -0.5, -0.6, 0.62
    sh = np.clip((nx * lx + ny * ly + nz * lz) / l, 0, 1)
    h = np.clip((g['elev'] - g['sea']) / max(1e-6, 1 - g['sea']), 0, 1)
    base = np.stack([150 + 70 * h, 140 + 60 * h, 100 + 80 * h], -1)
    rgb = base * (0.35 + 0.75 * sh[..., None])
    sea = (g['lake'] >= 0)
    rgb[sea] = [40, 80, 150]
    return np.clip(rgb, 0, 255).astype(np.uint8)


def lines(g):
    """Each channel cell's segment to its receiver's point (cells, unwrapped), with the width at both ends."""
    FW = g['FW']; recv = g['recv'].ravel(); chan = g['chan'].ravel(); lake = g['lake'].ravel()
    rx, ry, rw = g['rivx'].ravel(), g['rivy'].ravel(), g['rivw'].ravel()
    out = []
    for c in np.nonzero(chan)[0]:
        x0, y0 = c % FW + rx[c], c // FW + ry[c]
        r = recv[c]
        if r < 0: continue
        x1, y1 = r % FW + (rx[r] if chan[r] else 0), r // FW + (ry[r] if chan[r] else 0)
        x1 += FW * round((x0 - x1) / FW); y1 += FW * round((y0 - y1) / FW)
        out.append((x0, y0, x1, y1, max(1.0, rw[c]), max(1.0, rw[r] if chan[r] else rw[c])))
    return out


def draw_rivers(img, segs, sc, ox=0, oy=0, col=(30, 110, 230), minw=1.5):
    dr = ImageDraw.Draw(img)
    for x0, y0, x1, y1, w0, w1 in segs:
        p0 = ((x0 + 0.5 - ox) * sc, (y0 + 0.5 - oy) * sc); p1 = ((x1 + 0.5 - ox) * sc, (y1 + 0.5 - oy) * sc)
        wd = max(minw, 0.5 * (w0 + w1) * sc * 0.55)
        dr.line([p0, p1], fill=col, width=int(round(wd)))
        r = wd / 2
        dr.ellipse([p1[0] - r, p1[1] - r, p1[0] + r, p1[1] + r], fill=col)


def map_png(g, s, segs):
    FW = g['FW']; S = 3
    rgb = shade(g)
    a = Image.fromarray(rgb).resize((FW * S * 2, FW * S * 2), Image.NEAREST)
    draw_rivers(a, segs, S * 2)
    a = a.resize((FW * S, FW * S), Image.LANCZOS)
    f = np.log10(np.maximum(g['flow'], 1e-3))
    fl = np.clip((f + 0.5) / 4.0, 0, 1)
    b = np.stack([255 * fl ** 0.8, 200 * fl ** 1.6, 80 + 120 * fl ** 3], -1).astype(np.uint8)
    b[g['lake'] >= 0] = [40, 40, 60]
    b[g['chan']] = [120, 220, 255]
    b = Image.fromarray(b).resize((FW * S, FW * S), Image.NEAREST)
    out = Image.new('RGB', (FW * S * 2 + 10, FW * S + 24), (20, 20, 20))
    out.paste(a, (0, 24)); out.paste(b, (FW * S + 10, 24))
    dr = ImageDraw.Draw(out)
    dr.text((4, 4), f'seed {s}: channels along their centrelines at their width', fill=(230, 230, 230), font=FONT)
    dr.text((FW * S + 14, 4), 'discharge log10 Flow (cells of mean rain); channel cells cyan', fill=(230, 230, 230), font=FONT)
    out.save(f'{prefix}_{s}_map.png')


def zoom_png(g, s, segs):
    """Three close-ups: around the largest river, a mid-sized one, and the densest headwaters."""
    FW = g['FW']; Z = 12; W = 48
    flow = np.where(g['chan'], g['flow'], 0)
    ys, xs = np.nonzero(g['chan'])
    order = np.argsort(-flow[ys, xs])
    picks = []
    for k in [0, len(order) // 6, len(order) // 2]:
        if len(order) == 0: break
        y, x = ys[order[k]], xs[order[k]]
        if all(abs(((x - px + FW // 2) % FW) - FW // 2) > W // 2 or abs(((y - py + FW // 2) % FW) - FW // 2) > W // 2 for px, py in picks):
            picks.append((x, y))
    rgb = shade(g)
    tiles = []
    for (cx, cy) in picks:
        ox, oy = cx - W // 2, cy - W // 2
        yy = (np.arange(oy, oy + W) % FW); xx = (np.arange(ox, ox + W) % FW)
        t = rgb[np.ix_(yy, xx)]
        water = ((g['cell'] != 1))[np.ix_(yy, xx)]
        t = t.copy(); t[water & ~(g['lake'] >= 0)[np.ix_(yy, xx)]] = (t[water & ~(g['lake'] >= 0)[np.ix_(yy, xx)]] * 0.6 + np.array([60, 140, 200]) * 0.4).astype(np.uint8)
        im = Image.fromarray(t).resize((W * Z, W * Z), Image.NEAREST)
        dr = ImageDraw.Draw(im)
        for k in range(0, W * Z, Z):
            dr.line([(k, 0), (k, W * Z)], fill=(0, 0, 0, 40), width=1); dr.line([(0, k), (W * Z, k)], fill=(0, 0, 0), width=1)
        near = [sg for sg in segs if any(-2 <= ((v - o) % FW) <= W + 2 for v, o in [(sg[0], ox)]) and -2 <= ((sg[1] - oy) % FW) <= W + 2]
        shifted = []
        for x0, y0, x1, y1, w0, w1 in near:
            dx = ((x0 - ox) % FW) - (x0 - ox); dy = ((y0 - oy) % FW) - (y0 - oy)
            shifted.append((x0 + dx, y0 + dy, x1 + dx, y1 + dy, w0, w1))
        draw_rivers(im, shifted, Z, ox, oy, col=(20, 90, 220), minw=3)
        for x0, y0, x1, y1, w0, w1 in shifted:
            p = ((x0 + 0.5 - ox) * Z, (y0 + 0.5 - oy) * Z)
            dr.ellipse([p[0] - 2, p[1] - 2, p[0] + 2, p[1] + 2], fill=(255, 255, 255))
        tiles.append(im)
    out = Image.new('RGB', (len(tiles) * (W * Z + 8), W * Z + 24), (20, 20, 20))
    for k, t in enumerate(tiles): out.paste(t, (k * (W * Z + 8), 24))
    ImageDraw.Draw(out).text((4, 4), f'seed {s}: close-ups ({W} x {W} cells; white dots: centreline points; tinted: river water cells)', fill=(230, 230, 230), font=FONT)
    out.save(f'{prefix}_{s}_zoom.png')


def bars(dr, x0, y0, w, h, vals, title, col, ymax=None, labels=None):
    dr.rectangle([x0, y0, x0 + w, y0 + h], outline=(120, 120, 120))
    dr.text((x0 + 4, y0 - 18), title, fill=(230, 230, 230), font=FONT)
    m = ymax or max(1e-9, max(vals))
    bw = w / len(vals)
    for k, v in enumerate(vals):
        hh = min(1, v / m) * (h - 4)
        dr.rectangle([x0 + k * bw + 1, y0 + h - hh, x0 + (k + 1) * bw - 1, y0 + h], fill=col)
    if labels:
        for k, lab in labels: dr.text((x0 + k * bw - 6, y0 + h + 2), lab, fill=(200, 200, 200), font=FONT)


def stats_png(seeds):
    st = {s: json.load(open(os.path.join(d, f'stats_{s}.json'))) for s in seeds}
    W, H = 1500, 300 * len(seeds) + 40
    out = Image.new('RGB', (W, H), (20, 20, 20)); dr = ImageDraw.Draw(out)
    labs = [(0, '0'), (9, '45'), (18, '90'), (27, '135'), (35, '175')]
    for r, s in enumerate(seeds):
        j = st[s]; y = 40 + r * 300
        bars(dr, 20, y, 330, 220, j['step'], f'seed {s}: step direction ({100 * j["stepSpike"]:.0f}% at k x 45)', (90, 170, 255), labels=labs)
        bars(dr, 380, y, 330, 220, j['reach'], f'reach direction, 6 cells ({100 * j["reachSpike"]:.0f}%)', (90, 220, 160), labels=labs)
        bars(dr, 740, y, 330, 220, j['aspect'], f'relief slope aspect ({100 * j["aspectSpike"]:.0f}%)', (220, 180, 90), labels=labs)
        # Horton: log N against order
        x0 = 1100; dr.rectangle([x0, y, x0 + 160, y + 220], outline=(120, 120, 120))
        dr.text((x0 + 4, y - 18), f'Horton N(w): Rb {j["rb"]:.2f} RL {j["rl"]:.2f}', fill=(230, 230, 230), font=FONT)
        o = j['orders']
        for k in range(1, len(o)):
            if o[k] <= 0: continue
            px = x0 + 20 + (k - 1) * 30; py = y + 220 - 50 * math.log10(o[k]) - 5
            dr.ellipse([px - 4, py - 4, px + 4, py + 4], fill=(255, 120, 120)); dr.text((px + 6, py - 6), str(o[k]), fill=(230, 230, 230), font=FONT)
        # sinuosity against valley slope
        x0 = 1290; dr.rectangle([x0, y, x0 + 190, y + 220], outline=(120, 120, 120))
        dr.text((x0 + 4, y - 18), f'sinuosity vs slope (log)', fill=(230, 230, 230), font=FONT)
        for lx, ly, sn, sl, od in j['links']:
            px = x0 + 190 * (math.log10(max(sl, 1e-4)) + 4) / 3.5; py = y + 220 - 220 * min(1, (sn - 1) / 1.5)
            if x0 <= px <= x0 + 190: dr.point((px, py), fill=(255, 255, 255)); dr.ellipse([px - 1.5, py - 1.5, px + 1.5, py + 1.5], fill=(240, 240, 140))
        dr.text((x0 + 2, y + 222), '1e-4      1e-3      1e-2   slope', fill=(200, 200, 200), font=FONT)
    dr.text((10, 8), 'channel direction histograms (5-degree bins over 0-180, length-weighted); Horton; sinuosity (1..2.5) vs valley slope', fill=(230, 230, 230), font=FONT)
    out.save(f'{prefix}_stats.png')


for s in seeds:
    g = world(s)
    segs = lines(g)
    map_png(g, s, segs)
    zoom_png(g, s, segs)
stats_png(seeds)
print('drawn', prefix)
