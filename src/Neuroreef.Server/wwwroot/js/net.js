// The link to the simulation (docs/ICD.md). Decodes frames into plain objects and hands them to listeners.

export const REC = 42;   // bytes per AGENTS record (header byte 1 = 6)
export const T_WORLD = 1, T_AGENTS = 2, T_FIELDS = 3, T_TELEMETRY = 4, T_HISTORY = 5, T_PROBE = 6;

export class Link {
  constructor() { this.on = {}; this.ws = null; this.open = false; }

  listen(type, fn) { (this.on[type] ||= []).push(fn); }
  emit(type, v) { for (const fn of this.on[type] || []) fn(v); }

  connect() {
    const url = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws';
    const ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    ws.onopen = () => { this.open = true; this.emit('open'); };
    ws.onclose = () => { this.open = false; this.emit('close'); setTimeout(() => this.connect(), 1500); };
    ws.onmessage = (ev) => {
      if (typeof ev.data === 'string') { const j = JSON.parse(ev.data); this.emit(j.type, j); return; }
      this.decode(ev.data);
    };
    this.ws = ws;
  }

  send(obj) { if (this.open) this.ws.send(JSON.stringify(obj)); }

  decode(buf) {
    const dv = new DataView(buf);
    const type = dv.getUint8(0), step = dv.getUint32(4, true);
    switch (type) {
      case T_WORLD: {
        const fw = dv.getUint16(8, true), sea = dv.getFloat32(12, true), n = fw * fw;
        // rivers: course and size per cell (a server without the plane sends null; world3d.js then derives them)
        const river = buf.byteLength >= 16 + 4 * n ? new Uint8Array(buf, 16 + 3 * n, n) : null;
        this.emit('world', { step, fw, sea, elev: new Uint8Array(buf, 16, n), cell: new Uint8Array(buf, 16 + n, n), depth: new Uint8Array(buf, 16 + 2 * n, n), river });
        break;
      }
      case T_AGENTS: {
        const n = dv.getUint32(8, true);
        this.emit('agents', { step, n, bytes: new Uint8Array(buf, 12, n * REC), dv: new DataView(buf, 12, n * REC) });
        break;
      }
      case T_FIELDS: {
        const d = dv.getUint16(8, true), layers = dv.getUint16(10, true);
        this.emit('fields', { step, d, layers, data: new Uint8Array(buf, 12, d * d * layers) });
        break;
      }
      case T_TELEMETRY: {
        const head = dv.getUint32(8, true);
        this.emit('telemetry', { step, head, s: new Float32Array(buf, 12, (buf.byteLength - 12) / 4) });
        break;
      }
      case T_HISTORY: {
        const count = dv.getUint32(8, true), len = dv.getUint32(12, true), stride = dv.getUint32(16, true);
        const all = new Float32Array(buf, 20, count * len), rows = [];
        for (let k = 0; k < count; k++) rows.push(all.subarray(k * len, (k + 1) * len));
        this.emit('history', { step, count, len, stride, rows });
        break;
      }
      case T_PROBE: {
        const serial = dv.getUint32(8, true);
        if (serial === 0) { this.emit('probe', null); break; }
        this.emit('probe', { step, serial, slot: dv.getUint32(12, true), mother: dv.getUint32(16, true), child: dv.getUint32(20, true),
          f: new Float32Array(buf, 24, (buf.byteLength - 24) / 4) });
        break;
      }
    }
  }
}

/** Decode one format-6 AGENTS record; reuse out and its appearance buffer. */
export function agentAt(a, k, world, out) {
  agentState(a, k, world, out);
  return agentGenes(a, k, out);
}

/** The part of record k that changes from step to step: position, heading, clade, stage, flags, actions, serial, calls. */
export function agentState(a, k, world, out) {
  const o = k * REC, dv = a.dv, b = a.bytes;
  out.x = dv.getUint16(o, true) * world / 65536;
  out.y = dv.getUint16(o + 2, true) * world / 65536;
  out.h = b[o + 4] * (Math.PI * 2 / 256);
  out.sp = b[o + 5] & 31;      // clade slot
  out.sex = (b[o + 5] >> 5) & 1;
  const st = b[o + 6];
  out.stage = st & 3;          // 0 egg, 1 juvenile, 2 adult
  out.carrying = (st >> 2) & 1; out.scan = (st >> 3) & 1; out.rest = (st >> 4) & 1;
  out.energy = (st >> 5) / 7;
  out.acts = b[o + 7];
  out.serial = dv.getUint32(o + 8, true);
  out.call = b[o + 21] >> 4;                             // the first sound of its utterance this step: 0 none, 1..8
  out.call2 = b[o + 24] & 15; out.call3 = b[o + 24] >> 4; // its second and third sounds (Revision N), 0 none
  return out;
}

/** The part of record k its genes give, the same for its whole life: body size, organs, pairs, locomotion, fur, appearance. */
export function agentGenes(a, k, out) {
  const o = k * REC, dv = a.dv, b = a.bytes;
  // the organism's own body genes (bytes 12-19, int8): organ sizes are (g + 127) / 254, 0..1
  const gm = dv.getInt8(o + 12);
  out.gMass = gm; out.m01 = (gm + 128) / 255; out.mass = 0.1 * Math.pow(40 / 0.1, out.m01);   // the body's actual size (its size class's floor included)
  out.legs = (dv.getInt8(o + 13) + 127) / 254; out.fins = (dv.getInt8(o + 14) + 127) / 254; out.wings = (dv.getInt8(o + 15) + 127) / 254;
  out.sight = (dv.getInt8(o + 16) + 127) / 254; out.armour = (dv.getInt8(o + 17) + 127) / 254; out.strike = (dv.getInt8(o + 18) + 127) / 254;
  out.meat = (dv.getInt8(o + 19) + 127) / 254;    // meat-eating efficiency, 0..0.95
  out.legPairs = Math.min(4, Math.max(1, b[o + 20]));   // 2 to 8 legs
  out.finPairs = Math.min(4, b[o + 22] || 1);             // side fins
  out.eyePairs = Math.min(4, b[o + 23] || 1);
  out.locoCode = b[o + 21] & 15;                         // computed by the sim: 0 walk, 1 wade, 2 swim, 3 fly, 4 settled
  out.fur = (b[o + 25] >> 4) / 15; out.append = (b[o + 25] & 15) / 15;   // fur and appendages, 0..1 (Revision O)
  const appearance = out.appearance ||= new Float32Array(16);
  for (let j = 0; j < 16; j++) appearance[j] = (dv.getInt8(o + 26 + j) + 127) / 254;
  out.loco = LOCO[out.locoCode] || locoOf(out.legs, out.fins, out.wings, out.mass);
  return out;
}

export const LOCO = ['Walk', 'Wade', 'Swim', 'Fly', 'Sessile'];

/** Locomotion from the organs, only a fallback: the sim sends each animal's own (locoCode, Body.cs).
 *  A body flies when its flight muscles' power reaches the least power flight requires (2026-10-31):
 *  wings x (FLY_M_MAX / mass)^(EXP_FLY_REQ - EXP_FLY_AVAIL) >= 1, FLY_M_MAX = 16, exponents 7/6 and 0.7,
 *  and wings >= FLY_WING_MIN (0.45, the flight muscle to take off at all), so no body heavier than 16 flies,
 *  and one with half-size wings no heavier than about 3.6. */
export function locoOf(legs, fins, wings, mass = 1) {
  if (wings >= 0.45 && wings * Math.pow(16 / mass, 7 / 6 - 0.7) >= 1) return 'Fly';
  const walk = legs >= 0.3, swim = fins >= 0.3;
  return walk && swim ? 'Wade' : walk ? 'Walk' : swim ? 'Swim' : 'Sessile';
}

// action bits in AGENTS byte 7 (primitive order from EAT)
export const ACT = { EAT: 1, STRIKE: 2, DRINK: 4, MATE: 8, REST: 16, MARK: 32, LAY: 64, CACHE: 128 };
