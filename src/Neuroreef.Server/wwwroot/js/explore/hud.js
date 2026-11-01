// Explore mode HUD: crosshair, hit markers, the animal under the crosshair, health, weapons and arrows, the draw of the
// bow, and short messages. Plain DOM over the 3D view in the console's style; it builds its own elements and styles.

const CSS = `
#xp-hud { position: absolute; inset: 0; pointer-events: none; font-family: var(--mono, ui-monospace, monospace); color: var(--fg, #d7e3ee); z-index: 5; }
#xp-hud.hidden { display: none; }
#xp-cross { position: absolute; left: 50%; top: 50%; width: 22px; height: 22px; transform: translate(-50%, -50%); }
#xp-cross i { position: absolute; background: #e8f4ffd0; box-shadow: 0 0 2px #000; }
#xp-cross i:nth-child(1) { left: 10px; top: 0; width: 2px; height: 7px; } #xp-cross i:nth-child(2) { left: 10px; bottom: 0; width: 2px; height: 7px; }
#xp-cross i:nth-child(3) { top: 10px; left: 0; height: 2px; width: 7px; } #xp-cross i:nth-child(4) { top: 10px; right: 0; height: 2px; width: 7px; }
#xp-cross.on i { background: #ff8a80; }
#xp-hit { position: absolute; left: 50%; top: 50%; width: 34px; height: 34px; transform: translate(-50%, -50%) rotate(45deg); opacity: 0; transition: opacity .25s; }
#xp-hit i { position: absolute; background: #fff; } #xp-hit i:nth-child(1) { left: 16px; top: 0; width: 2px; height: 10px; } #xp-hit i:nth-child(2) { left: 16px; bottom: 0; width: 2px; height: 10px; }
#xp-hit i:nth-child(3) { top: 16px; left: 0; height: 2px; width: 10px; } #xp-hit i:nth-child(4) { top: 16px; right: 0; height: 2px; width: 10px; }
#xp-hit.show { opacity: 1; transition: none; } #xp-hit.kill i { background: #ff4d5e; }
#xp-target { position: absolute; left: 50%; top: calc(50% + 26px); transform: translateX(-50%); font-size: 12px; text-shadow: 0 0 3px #000; white-space: nowrap; }
#xp-target .d { color: var(--dim, #8aa0b5); }
#xp-draw { position: absolute; left: 50%; top: calc(50% + 46px); transform: translateX(-50%); width: 80px; height: 4px; background: #ffffff22; border-radius: 2px; display: none; }
#xp-draw b { display: block; height: 100%; background: var(--accent, #4fd1c5); border-radius: 2px; }
#xp-panel { position: absolute; left: 50%; bottom: 14px; transform: translateX(-50%); display: flex; gap: 10px; align-items: flex-end; background: #070b10b0; padding: 6px 10px; border-radius: 6px; }
#xp-hp { width: 150px; font-size: 11px; color: var(--dim, #8aa0b5); }
#xp-hp .bar { height: 7px; background: #ffffff1a; border-radius: 4px; margin-top: 3px; } #xp-hp .bar b { display: block; height: 100%; border-radius: 4px; background: #7bd46b; }
#xp-weapons { display: flex; gap: 4px; }
#xp-weapons span { font-size: 11px; padding: 3px 7px; border: 1px solid var(--line2, #2a3a4a); border-radius: 4px; color: var(--dim, #8aa0b5); }
#xp-weapons span.on { border-color: var(--accent, #4fd1c5); color: #fff; background: #12353a; }
#xp-weapons em { font-style: normal; color: var(--faint, #56697c); margin-right: 4px; }
#xp-info { font-size: 11px; color: var(--dim, #8aa0b5); min-width: 120px; text-align: right; }
#xp-msg { position: absolute; left: 50%; top: 28%; transform: translateX(-50%); font-size: 14px; text-shadow: 0 0 4px #000; text-align: center; }
#xp-msg.bad { color: #ff8a80; font-size: 18px; }
body.exploring #legend, body.exploring #hint, body.exploring #experience { display: none; }
#xp-help { position: absolute; left: 10px; bottom: 64px; font-size: 10px; color: var(--faint, #6c8195); line-height: 1.45; background: #070b1080; padding: 5px 7px; border-radius: 5px; }
#xp-vign { position: absolute; inset: 0; box-shadow: inset 0 0 120px #ff000000; transition: box-shadow .4s; }
#xp-vign.hurt { box-shadow: inset 0 0 140px #ff1a1aa0; transition: none; }
`;

export class ExploreHud {
  constructor(parent) {
    if (!document.getElementById('xp-style')) { const s = document.createElement('style'); s.id = 'xp-style'; s.textContent = CSS; document.head.append(s); }
    const d = this.el = document.createElement('div');
    d.id = 'xp-hud'; d.className = 'hidden';
    d.innerHTML = `<div id="xp-vign"></div><div id="xp-cross"><i></i><i></i><i></i><i></i></div><div id="xp-hit"><i></i><i></i><i></i><i></i></div>
      <div id="xp-target"></div><div id="xp-draw"><b></b></div><div id="xp-msg"></div>
      <div id="xp-panel"><div id="xp-hp">health <span id="xp-hpv"></span><div class="bar"><b></b></div></div><div id="xp-weapons"></div><div id="xp-info"></div></div>
      <div id="xp-help">WASD move · mouse look · Shift run · Space jump · Ctrl (hold) or F: ×10 travel<br>left button: use · right button: throw the spear · 1/2/3 or wheel: weapon · arrows: look · Esc: leave</div>`;
    parent.append(d);
    this.$ = (id) => d.querySelector('#' + id);
    this._msgT = 0;
  }

  show(on) { this.el.classList.toggle('hidden', !on); document.body.classList.toggle('exploring', on); }

  weapons(list, cur, ammo) {
    this.$('xp-weapons').innerHTML = list.map((w, i) => `<span class="${i === cur ? 'on' : ''}"><em>${w.key}</em>${w.name}${w.primary.ammo ? ` ${ammo}` : ''}</span>`).join('');
  }

  health(hp, max) {
    const f = Math.max(0, Math.min(1, hp / max));
    this.$('xp-hpv').textContent = `${Math.round(hp)} / ${max}`;
    const b = this.$('xp-hp').querySelector('b');
    b.style.width = `${f * 100}%`; b.style.background = f > 0.5 ? '#7bd46b' : f > 0.25 ? '#e6c35a' : '#ff5d73';
  }

  hurt() { const v = this.$('xp-vign'); v.classList.add('hurt'); setTimeout(() => v.classList.remove('hurt'), 60); }

  info(text) { this.$('xp-info').innerHTML = text; }

  target(html) { this.$('xp-target').innerHTML = html || ''; this.$('xp-cross').classList.toggle('on', !!html); }

  draw(f) { const d = this.$('xp-draw'); d.style.display = f > 0 ? 'block' : 'none'; d.firstChild.style.width = `${Math.min(1, f) * 100}%`; }

  hitMarker(kill) {
    const h = this.$('xp-hit');
    h.classList.toggle('kill', !!kill); h.classList.add('show');
    clearTimeout(this._hitT); this._hitT = setTimeout(() => h.classList.remove('show'), kill ? 420 : 160);
  }

  message(text, bad, ms = 2200) {
    const m = this.$('xp-msg');
    m.innerHTML = text; m.classList.toggle('bad', !!bad);
    clearTimeout(this._msgT); this._msgT = setTimeout(() => { m.innerHTML = ''; }, ms);
  }
}
