"""
CLI wrapper that accepts a config JSON file, runs the full slicing
pipeline, prints progress to stderr, and prints the structured result
JSON to stdout (for consumption by the Tauri sidecar).

Usage:
    python planar_step_09_run_slice.py <config.json>
"""

import contextlib
import json
import os
import sys

import numpy as np

from planar_step_01_config import Config
from planar_step_08_main import run_phase1, run_phase2, run_phase3, run_phase4, export_results
from planar_step_04_mesh_cutter import cut_pieces_planar


def _log(msg: str) -> None:
    print(msg, file=sys.stderr, flush=True)


def main() -> int:
    if len(sys.argv) < 2:
        print(json.dumps({"error": "Missing config path argument"}), file=sys.stdout)
        return 1

    config_path = sys.argv[1]
    if not os.path.isfile(config_path):
        print(json.dumps({"error": f"Config not found: {config_path}"}), file=sys.stdout)
        return 1

    with open(config_path) as f:
        config_data = json.load(f)

    try:
        config = Config(**config_data)
    except TypeError as e:
        print(json.dumps({"error": f"Invalid config keys: {e}"}), file=sys.stdout)
        return 1

    try:
        config.validate()
    except ValueError as e:
        print(json.dumps({"error": str(e)}), file=sys.stdout)
        return 1

    # Redirect stdout to stderr during slicing so that main.py's print()
    # calls (progress) don't pollute the JSON result on stdout.
    with contextlib.redirect_stdout(sys.stderr):
        _log("[Phase 1] Loading and normalizing model…")
        mesh = run_phase1(config)

        if config.mode == "planar":
            _log("[Main] Planar mode — skipping Phases 2 (Voronoi) and 3 (joinery).")
            _log("[Phase 4] Planar BSP slicing…")
            final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)
            seeds = np.zeros(config.pieces, dtype=np.int64)
            piece_centroids = [p.triangles_center.mean(axis=0) for p in final_pieces]
            if len(piece_centroids) > 0:
                all_piece_ctrs = np.array(piece_centroids)
                face_ctrs = mesh.triangles_center
                dists = np.linalg.norm(
                    face_ctrs[:, None, :] - all_piece_ctrs[None, :, :], axis=2
                )
                labels = np.argmin(dists, axis=1).astype(np.int64)
            else:
                labels = np.zeros(len(mesh.faces), dtype=np.int64)
        else:
            _log("[Phase 2] Partitioning model into pieces…")
            working_mesh, seeds, labels, patches, pieces = run_phase2(mesh, config)

            _log("[Phase 3] Applying interlocking joinery…")
            patches, pieces = run_phase3(patches, pieces, labels, working_mesh, config)

            _log("[Phase 4] Boolean cutting and UV mapping…")
            final_pieces = run_phase4(mesh, patches, pieces, labels, config)

        _log("[Export] Writing output files…")
        export_results(config, mesh, seeds, labels, final_pieces)

    pieces_info = []
    for i in range(len(final_pieces)):
        pieces_info.append({
            "index": i,
            "path": os.path.abspath(
                os.path.join(config.output_path, "pieces", f"piece_{i:04d}.glb")
            ),
            "vertices": int(np.sum(labels == i)),
        })

    result = {
        "piece_count": len(final_pieces),
        "output_dir": os.path.abspath(config.output_path),
        "consolidated": os.path.abspath(
            os.path.join(config.output_path, "pieces.glb")
        ),
        "checkpoint": os.path.abspath(
            os.path.join(config.output_path, "checkpoint.json")
        ),
        "pieces": pieces_info,
        "mode": config.mode,
    }

    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
