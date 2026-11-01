// Explore vehicles HUD: the spawn menu (V), the "E: drive" prompt, and while driving a panel with speed, altitude (the
// plane), throttle, the weapon and its rounds, the gun's heat (the jeep), the torpedoes' depth and mode (the boat), and
// warnings. Plain DOM in the Explore HUD's style.

const CSS = `
#xv { position: absolute; inset: 0; pointer-events: none; font-family: var(--mono, ui-monospace, monospace); color: var(--fg, #d7e3ee); z-index: 6; }
#xv.hidden { display: none; }
#xv-prompt { position: absolute; left: 50%; top: 62%; transform: translateX(-50%); font-size: 13px; text-shadow: 0 0 4px #000; background: #070b10a0; padding: 4px 10px; border-radius: 5px; display: none; }
#xv-prompt b { color: var(--accent, #4fd1c5); }
#xv-menu { position: absolute; left: 50%; top: 40%; transform: translate(-50%, -50%); background: #070b10e0; border: 1px solid var(--line2, #2a3a4a); border-radius: 8px; padding: 12px 16px; display: none; min-width: 300px; pointer-events: auto; }
#xv-menu h3 { margin: 0 0 8px; font-size: 13px; font-weight: 600; color: #fff; }
#xv-menu .it { display: flex; gap: 10px; padding: 6px 4px; border-radius: 5px; cursor: pointer; font-size: 12px; align-items: baseline; }
#xv-menu .it:hover { background: #12353a; }
#xv-menu .it em { font-style: normal; color: var(--accent, #4fd1c5); border: 1px solid var(--line2, #2a3a4a); border-radius: 3px; padding: 0 5px; }
#xv-menu .it span { color: var(--dim, #8aa0b5); font-size: 11px; }
#xv-menu .foot { margin-top: 8px; font-size: 10px; color: var(--faint, #6c8195); }
#xv-panel { position: absolute; left: 50%; bottom: 128px; transform: translateX(-50%); white-space: nowrap; background: #070b10b8; border-radius: 7px; padding: 7px 14px; display: none; gap: 18px; align-items: center; font-size: 11px; }
#xv-panel .big { font-size: 22px; color: #fff; line-height: 1; }
#xv-panel .lbl { color: var(--dim, #8aa0b5); font-size: 10px; }
#xv-panel .col { display: flex; flex-direction: column; align-items: center; gap: 2px; }
#xv-panel .bar { width: 70px; height: 5px; background: #ffffff1a; border-radius: 3px; } #xv-panel .bar b { display: block; height: 100%; border-radius: 3px; background: var(--accent, #4fd1c5); }
#xv-panel .warn { color: #ff8a80; font-weight: 600; }
#xv-weapon { border: 1px solid var(--accent, #4fd1c5); border-radius: 4px; padding: 3px 8px; color: #fff; background: #12353a; }
#xv-help { position: absolute; left: 10px; bottom: 64px; font-size: 10px; color: var(--faint, #6c8195); line-height: 1.45; background: #070b1080; padding: 5px 7px; border-radius: 5px; display: none; }
#xv-sightinfo { position: absolute; left: 50%; top: calc(50% + 40px); transform: translateX(-50%); font-size: 11px; color: #ffb3a8; text-shadow: 0 0 3px #000; }
body.driving #xp-weapons, body.driving #xp-help, body.driving #xp-draw, body.driving #xp-info { display: none !important; }
body.driving #xv-help { display: block; }
`;

const HELP = {
  jeep: 'W/S drive · A/D steer · Space brake · mouse aim · hold left button: fire · C: camera · E: get out · V: vehicles',
  plane: 'R/F throttle · W/S nose down/up · A/D roll · Z/X rudder · Space brakes · left button or B: bomb · C: camera · E: get out (landed)',
  boat: 'W/S throttle · A/D steer · mouse aim · left button: torpedo · R/F torpedo depth ∓ · T or right button: homing / straight · C: camera · E: get out',
};

export class VehicleHud {
  constructor(parent) {
    if (!document.getElementById('xv-style')) { const s = document.createElement('style'); s.id = 'xv-style'; s.textContent = CSS; document.head.append(s); }
    const d = this.el = document.createElement('div');
    d.id = 'xv'; d.className = 'hidden';
    d.innerHTML = `<div id="xv-prompt"></div><div id="xv-panel"></div><div id="xv-help"></div><div id="xv-sightinfo"></div>
      <div id="xv-menu"><h3>Vehicles</h3>
        <div class="it" data-k="jeep"><em>1</em><div>Jeep<br><span>mounted automatic rifle · on land near you</span></div></div>
        <div class="it" data-k="plane"><em>2</em><div>Plane<br><span>bombs · on the nearest flat ground</span></div></div>
        <div class="it" data-k="boat"><em>3</em><div>Boat<br><span>torpedoes · on the nearest water</span></div></div>
        <div class="foot">E near a vehicle to get in or out · V or Esc closes</div></div>`;
    parent.append(d);
    this.$ = (id) => d.querySelector('#' + id);
    this.onPick = null;
    for (const it of d.querySelectorAll('#xv-menu .it')) it.addEventListener('mousedown', (e) => { e.stopPropagation(); e.preventDefault(); this.onPick && this.onPick(it.dataset.k); });
  }

  show(on) { this.el.classList.toggle('hidden', !on); if (!on) { this.menu(false); this.driving(null); this.prompt(''); } }

  menu(on) { this.$('xv-menu').style.display = on ? 'block' : 'none'; this.menuOpen = on; }

  prompt(html) { const p = this.$('xv-prompt'); p.innerHTML = html || ''; p.style.display = html ? 'block' : 'none'; }

  driving(kind) {
    document.body.classList.toggle('driving', !!kind);
    this.$('xv-panel').style.display = kind ? 'flex' : 'none';
    this.$('xv-help').textContent = kind ? HELP[kind] : '';
    if (!kind) this.sight('');
  }

  sight(text) { this.$('xv-sightinfo').textContent = text || ''; }

  /** The panel while driving: v the vehicle state, w its weapon mode, extra { heat, overheat, depth, homing, cam }. */
  panel(v, w, x) {
    const kmh = Math.abs(v.v) * 3.6;
    const cols = [`<div class="col"><div class="big">${kmh.toFixed(0)}</div><div class="lbl">km/h · ${Math.abs(v.v).toFixed(1)} m/s</div></div>`];
    if (v.kind === 'plane') {
      cols.push(`<div class="col"><div class="big">${v.agl.toFixed(0)}</div><div class="lbl">m above ground · ${(x.asl).toFixed(0)} m asl</div></div>`);
      cols.push(`<div class="col"><div class="bar"><b style="width:${(v.throttle * 100).toFixed(0)}%"></b></div><div class="lbl">throttle ${(v.throttle * 100).toFixed(0)}%</div></div>`);
    }
    if (v.kind === 'boat') cols.push(`<div class="col"><div class="big">${v.depthM.toFixed(1)}</div><div class="lbl">m of water</div></div>`);
    const rounds = v.rounds ?? 0;
    let wl = `<span id="xv-weapon">${w ? w.name : '—'}</span> <b>${rounds}</b>`;
    if (v.kind === 'jeep') wl += `<div class="bar" style="margin-top:3px"><b style="width:${Math.min(100, x.heat * 100).toFixed(0)}%;background:${x.overheat ? '#ff5d73' : x.heat > 0.7 ? '#e6c35a' : 'var(--accent,#4fd1c5)'}"></b></div><div class="lbl">${x.overheat ? '<span class="warn">OVERHEATED</span>' : 'heat'}</div>`;
    if (v.kind === 'boat') wl += `<div class="lbl">depth ${x.depth.toFixed(1)} m · ${x.homing ? 'homing' : 'straight'}</div>`;
    if (v.kind === 'plane') wl += `<div class="lbl">blast ${w ? w.radiusM : 0} m</div>`;
    cols.push(`<div class="col">${wl}</div>`);
    const warn = v.stalled && !v.onGround ? 'STALL' : v.blocked ? v.blocked.toUpperCase() : v.mud > 0.3 ? 'MUD' : v.kind === 'jeep' && v.depthM > 0.1 ? 'WADING' : v.kind === 'boat' && v.depthM < 1.5 ? 'SHALLOWS' : '';
    cols.push(`<div class="col"><div class="lbl">${x.cam} view</div><div class="warn">${warn}</div></div>`);
    this.$('xv-panel').innerHTML = cols.join('');
  }
}
