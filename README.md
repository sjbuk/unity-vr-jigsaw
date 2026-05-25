# JigSaw — 3D Jigsaw Piece Generator

Tauri + Python desktop app that decomposes 3D models (GLB/GLTF) into jigsaw puzzle pieces and lets you view/reassemble them in a 3D viewport.

## Architecture

```
User (Svelte UI)  ←IPC→  Rust Backend (Tauri)  ──spawns──►  Python Pipeline
       │                        │                               │
  Three.js viewer         slice_model()               planar_run.py → GLB exports
  PieceViewer             read_text_file()            planar_main.py (standalone CLI)
```

## Key Modules

### 1. Python Generator — `tools/jigsaw_generator/`

**Entry points:**
- `planar_main.py` — standalone CLI (`--input`, `--output`, `--pieces`, `--gap`, `--seed`)
- `planar_run.py` — sidecar mode: reads JSON config from a file path argument, writes JSON result to stdout (called by the Rust backend)

**Pipeline phases:**
| File | Phase | What it does |
|------|-------|--------------|
| `planar_phase_010.py` | Ingest | Load GLB via `trimesh`, merge scene nodes, normalize to unit bounding box |
| `planar_phase_020.py` | Plane split | Split a mesh by a cutting plane (centroid signed-distance; preserves UVs) |
| `planar_phase_021.py` | BSP slice | Recursively split the largest piece with random planes until N pieces reached |
| `planar_phase_022.py` | Orphan fix | Detect disconnected fragments and reassign to nearest parent piece |
| `planar_phase_030.py` | Back-face | Generate back-face mesh + colour atlas (one HSV colour per piece) |

**Dependencies:** `trimesh`, `numpy`, `scipy`, `Pillow` (see `requirements.txt`)

### 2. Tauri Desktop App — `tauri-app/`

**Rust backend** (`src-tauri/src/lib.rs`):
- `slice_model(params)` — locates Python, writes temp JSON config, spawns `planar_run.py`, streams stderr as progress events, parses stdout JSON as `SliceResult`
- `read_text_file(path)` — reads arbitrary text files (e.g. `checkpoint.json`)

**Svelte 5 frontend** (`src/`):
| Component | Role |
|-----------|------|
| `App.svelte` | Root layout: sidebar params + 3D viewport + piece list |
| `FilePicker.svelte` | Native GLB/GLTF file open dialog |
| `ParamForm.svelte` | Pieces (2–100), gap, seed, orphan toggle |
| `PieceViewer.svelte` | Three.js 3D viewer with Split / Assembled / Simulate modes |
| `PieceList.svelte` | Scrollable piece list with visibility toggles |

**Dependencies:** Tauri v2, Three.js, Svelte 5, Vite (see `package.json` and `Cargo.toml`)

## Quickstart

```bash
# Python standalone
cd tools/jigsaw_generator
pip install -r requirements.txt
python planar_main.py --input my_model.glb --output out/ --pieces 24

# Tauri dev (needs Rust + Python on PATH)
cd tauri-app
npm install
npm run tauri dev
```

## Design Docs

- `docs/jigsaw_tool_plan.md` — master spec describing a more ambitious 6-phase pipeline (geodesic Voronoi, interlocking pegs, manifold3d booleans). Current implementation uses the simpler planar BSP approach.
- `docs/slicing.md` — notes on surface BFS vs. voxel BFS slicing alternatives
