"""
CLI wrapper that accepts a config JSON file, runs the planar slicing
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
from planar_step_08_main import run_phase1, export_results
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

    with contextlib.redirect_stdout(sys.stderr):
        _log("[Phase 1] Loading and normalizing model…")
        mesh = run_phase1(config)

        _log("[Phase 2] Planar BSP slicing…")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

        _log("[Export] Writing output files…")
        export_results(config, mesh, final_pieces)

    pieces_info = []
    for i in range(len(final_pieces)):
        pieces_info.append({
            "index": i,
            "path": os.path.abspath(
                os.path.join(config.output_path, "pieces", f"piece_{i:04d}.glb")
            ),
            "vertices": len(final_pieces[i].vertices),
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
    }

    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
