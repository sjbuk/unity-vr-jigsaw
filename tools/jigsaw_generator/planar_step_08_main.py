"""
planar_step_08_main.py — CLI entry point for planar jigsaw slicing.

Pipeline:
    Phase 1: Model ingestion, normalization
    Phase 2: Planar BSP slicing
    Export: GLB + checkpoint JSON

Usage:
    python planar_step_08_main.py --input model.glb --output out/ --pieces 24
"""

import json
import os
import sys

import numpy as np
import trimesh

try:
    from .planar_step_01_config import Config, build_arg_parser
    from .planar_step_03_mesh_io import load_model, normalize_mesh
    from .planar_step_04_mesh_cutter import cut_pieces_planar
except ImportError:
    from planar_step_01_config import Config, build_arg_parser
    from planar_step_03_mesh_io import load_model, normalize_mesh
    from planar_step_04_mesh_cutter import cut_pieces_planar


# ---------------------------------------------------------------------------
# Phase 1
# ---------------------------------------------------------------------------

def run_phase1(config: Config) -> trimesh.Trimesh:
    """Load and normalize the input mesh."""
    print(f"[Phase 1] Loading: {config.input_path}")
    mesh = load_model(config.input_path)
    print(f"           Vertices: {len(mesh.vertices):,}  Faces: {len(mesh.faces):,}")

    print("[Phase 1] Normalizing to unit bounding box …")
    mesh = normalize_mesh(mesh)
    bb = mesh.bounding_box
    print(
        f"           Center:  [{bb.centroid[0]:.3f}, "
        f"{bb.centroid[1]:.3f}, {bb.centroid[2]:.3f}]"
    )
    print(
        f"           Extents: [{bb.extents[0]:.3f}, "
        f"{bb.extents[1]:.3f}, {bb.extents[2]:.3f}]"
    )
    return mesh


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def export_results(
    config: Config,
    mesh: trimesh.Trimesh,
    final_pieces: list[trimesh.Trimesh],
) -> None:
    """Write all generated assets to the output directory."""
    out = config.output_path
    total_bounds = mesh.bounding_box

    pieces_dir = os.path.join(out, "pieces")
    os.makedirs(pieces_dir, exist_ok=True)

    for i, piece_mesh in enumerate(final_pieces):
        path = os.path.join(pieces_dir, f"piece_{i:04d}.glb")
        piece_mesh.export(path)
    print(f"[Export] Wrote {len(final_pieces)} individual pieces to {pieces_dir}")

    scene = trimesh.Scene()
    for i, piece_mesh in enumerate(final_pieces):
        node_name = f"piece_{i}"
        scene.add_geometry(piece_mesh, node_name=node_name, geom_name=node_name)
    consolidated_path = os.path.join(out, "pieces.glb")
    scene.export(consolidated_path)
    print(f"[Export] Wrote consolidated multi-node GLB to {consolidated_path}")

    piece_centroids = [p.triangles_center.mean(axis=0) for p in final_pieces]
    piece_vertex_counts = [len(p.vertices) for p in final_pieces]

    checkpoint = {
        "source": os.path.basename(config.input_path),
        "piece_count": config.pieces,
        "gap": config.gap,
        "seed": config.seed,
        "total_bounds": {
            "center": total_bounds.centroid.tolist(),
            "extents": total_bounds.extents.tolist(),
        },
        "piece_centroids": [c.tolist() for c in piece_centroids],
        "piece_vertex_counts": piece_vertex_counts,
    }
    checkpoint_path = os.path.join(out, "checkpoint.json")
    with open(checkpoint_path, "w") as f:
        json.dump(checkpoint, f, indent=2)
    print(f"[Export] Wrote checkpoint to {checkpoint_path}")


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)
    config = Config.from_args(args)

    try:
        config.validate()
    except ValueError as exc:
        print(f"Configuration error: {exc}", file=sys.stderr)
        return 1

    os.makedirs(config.output_path, exist_ok=True)

    mesh = run_phase1(config)

    print("[Phase 2] Planar BSP slicing …")
    final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

    export_results(config, mesh, final_pieces)

    print(f"\n[Done] Output directory: {config.output_path}")
    print(f"[Done] {len(final_pieces)} pieces exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
