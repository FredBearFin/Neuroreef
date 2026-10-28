# Neuroreef 2

An artificial-life ecosystem built on the energy cycle. It has 20 species on land and in freshwater,
each with its own learning, evolving brain. Only the laws of physics and physiology are designed.
Behaviour is not: every animal starts with a random brain, learns during its life, and evolves across
generations.

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

**Evolve the brains once.** Every animal starts with a random brain. Double-click **Evolve.bat** in
the download. It runs the world at full speed without graphics for about an hour, then saves
`evolved.nrbank` (the evolved brains) and `evolve-report.txt` (what happened to each species, including
which died out and why). After that, `Neuroreef.exe` starts from the evolved brains automatically.
Send `evolved.nrbank` back if it should be shipped in the exe for everyone.

`RUN.txt` in the download lists the options. The most useful ones:

| Option | Effect |
|---|---|
| `--genesis` | start from random brains even if `evolved.nrbank` exists |
| `--evolve` | what Evolve.bat runs: a full-speed headless genesis that saves `evolved.nrbank` |
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
