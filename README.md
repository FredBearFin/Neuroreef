# Neuroreef 2

An artificial-life ecosystem being rebuilt from the single-file WebGPU version (`reference/neuroreef-v1.html`).

**Status: design review.** No code yet. These documents define the model and are open for review:

| Document | Contents |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | The model: shared senses, one behaviour library, per-species decision matrices, ecology equations, memory map, step contract |
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | Numbered requirements (NR-xxx) with verification methods |
| [docs/CONSTANTS.md](docs/CONSTANTS.md) | Every tuning value, with unit and origin |
| [docs/ICD.md](docs/ICD.md) | Server ↔ browser frame formats |

Planned delivery: one Windows `Neuroreef.exe` (C#/.NET 8). It runs the simulation and serves a 3D, analytics-heavy console at `http://localhost:5800`, which can also be opened from a tablet on the same network.
