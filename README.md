# Neuroreef 2

An artificial-life ecosystem built on the energy cycle, with **open-ended evolution**. Only the laws of
physics, physiology, plants, water and weather are designed. Life is not: it starts from 10 random
ancestors (6 on land, 4 in water), all simple plant-eaters. Every organism's body (size, legs, fins,
wings, armour, weapons, senses, diet, how it reproduces) comes from its own genes, and its brain learns
during life and evolves across generations. New species form on their own when lineages diverge.
Whatever predators, scavengers, fliers or anything else exist, evolution produced.

- **Simulation:** C# / .NET 8, written in a fixed-memory, fixed-step, deterministic style (the same
  seed gives the same run).
- **Console:** a browser page with a real-time 3D world, follow and ride-along cameras, and analytics
  dashboards. It works on the PC and on a tablet on the same network.

## Download and run (Windows)

1. Open the repository on GitHub and go to **Actions**. Select the latest green **build** run. Under
   **Artifacts**, download **Neuroreef-win-x64**, then unzip it.
2. Double-click **Neuroreef.exe**. Windows may say the app is unrecognised, because it isn't signed.
   Click **More info**, then **Run anyway**.
3. Your browser opens at `http://localhost:5800`. Keep the black console window open; closing it
   stops the simulation.
4. **Tablet:** the console window prints `on your network: http://192.168.x.x:5800`. Open that
   address on the tablet (same Wi-Fi). If Windows asks, allow Neuroreef on private networks.

**The world you watch is births and deaths only.** No animal is ever spawned into it, so an extinct
species stays extinct. Its first animals need brains that can keep them alive, and those come from a
**prehistory**: a separate run that stands for the long evolution before the world begins.

**The first start is the prehistory, and you can watch it.** Without evolved brains, the console
opens on the **Prehistory** tab. Three branches (separate births-only worlds on the same land; nothing is ever added) evolve at full
speed, 500,000 steps shown as 500 million symbolic years:
- tap a branch card to watch that branch in 3D;
- each square on a card is a clade (a species that evolved in that branch): green self-sustaining, yellow breeding, orange dying (no births),
  red gone;
- the sliders change mutation and the length while it runs;
- **Begin the world now** ends it early.

When it ends, the world begins from the branch where life did best: its clades are saved as
`evolved.nrbank`, and the world begins: births and deaths only. The next start skips the prehistory
(start with `--prehistory` to run a new one). Closing the window during the prehistory saves
`prehistory-partial.nrbank`.

**Evolve.bat** runs the same prehistory without graphics (a little faster) and also writes
`evolve-report.txt`: which clades evolved in each branch, what they became, and how they died.

`RUN.txt` in the download lists the options. The most useful ones:

| Option | Effect |
|---|---|
| `--random-brains` | open the world with random brains even if `evolved.nrbank` exists (no spawning, so most species die out) |
| `--prehistory` | run a new prehistory even if `evolved.nrbank` exists |
| `--branches 4` / `--prehistory-steps 1000000` | more branches (more memory and cores) / a longer prehistory |
| `--evolve` | what Evolve.bat runs: the prehistory, headless at full speed, which saves `evolved.nrbank` |
| `--seed 7` | a different world and run |
| `--save-bank my.nrbank` / `--load-bank my.nrbank` | keep evolved genomes between runs |
| `--headless --steps 200000 --csv run.csv` | a long run without the console, with a report at the end |
| `--capacity 32768` | half the memory (the default 65,536 slots use about 0.9 GB) |

## Build it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). On Windows:

```
.\build.ps1
```

This runs the tests and writes `dist\Neuroreef.exe`. On Linux or macOS, run `./build.sh` (or
`./build.sh win-x64` to build the Windows exe).

## What is inside

| Document | Contents |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | the model: energy cycle and roster, shared senses and behaviour library, brains and learning, evolvable bodies, reproduction modes, ecology equations, step contract, memory map, and how the laws were set (§15) |
| [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md) | numbered requirements (NR-xxx) and how each is verified |
| [docs/CONSTANTS.md](docs/CONSTANTS.md) | every law and tuning value with its unit and origin; generated from the code, and a test keeps it in sync |
| [docs/ICD.md](docs/ICD.md) | the server ↔ browser frame formats |
| `reference/neuroreef-v1.html` | the original single-file WebGPU version |

The code:
- `src/Neuroreef.Core`: the simulation. It has no dependencies, and nothing is allocated in the step
  loop.
- `src/Neuroreef.Server`: the web host, headless runner and console.
- `tests/Neuroreef.Tests`:
  - determinism across thread counts
  - zero allocation
  - energy-cycle closure
  - reproduction modes
  - learning
  - body trade-offs
  - energy conservation in growth
  - gene bank
  - frame encoding

## Honest limits

- **Hardware:** it has not been run on Windows hardware, an AMD GPU or a tablet. CI builds and tests
  it on Windows, and the console was checked in a software-rendered browser.
- **Emergence takes time:** behaviour emerges from random brains only over long runs. The console
  measures which combinations of primitives each species uses (Behaviour tab); it does not assume
  them.
- **Fixed laws:** the laws were set once and are not adjusted to keep species alive. Extinctions are
  results, not bugs.
