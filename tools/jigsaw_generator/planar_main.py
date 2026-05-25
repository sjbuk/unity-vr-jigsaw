"""
planar_main — CLI entry point for planar jigsaw slicing.

Usage:
    python planar_main.py --input model.glb --output out/ --pieces 24
"""

import json
import os
import sys

import trimesh

from planar_lib import Config, build_arg_parser
from planar_phase_010 import load_model, normalize_mesh
from planar_phase_021 import cut_pieces_planar
from planar_phase_022 import reassign_orphans
from planar_phase_030 import bake_backface_colours


# ---------------------------------------------------------------------------
# Phase 1 — ingest
# ---------------------------------------------------------------------------

def run_ingest(config: Config) -> trimesh.Trimesh:
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
    back_pieces: list[trimesh.Trimesh] | None = None,
) -> None:
    """Write all generated assets to the output directory."""
    out = config.output_path
    total_bounds = mesh.bounding_box

    pieces_dir = os.path.join(out, "pieces")
    os.makedirs(pieces_dir, exist_ok=True)

    for i, piece_mesh in enumerate(final_pieces):
        path = os.path.join(pieces_dir, f"piece_{i:04d}.glb")
        piece_mesh.export(path)
    print(f"[Export] Wrote {len(final_pieces)} individual front pieces to {pieces_dir}")

    if back_pieces is not None:
        for i, back_mesh in enumerate(back_pieces):
            path = os.path.join(pieces_dir, f"piece_{i:04d}_back.glb")
            back_mesh.export(path)
        print(f"[Export] Wrote {len(back_pieces)} individual back-face pieces to {pieces_dir}")

    scene = trimesh.Scene()
    for i, piece_mesh in enumerate(final_pieces):
        node_name = f"piece_{i:04d}"
        scene.add_geometry(piece_mesh, node_name=f"{node_name}_front", geom_name=f"{node_name}_front")
        if back_pieces is not None:
            scene.add_geometry(back_pieces[i], node_name=f"{node_name}_back", geom_name=f"{node_name}_back")
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

    mesh = run_ingest(config)

    print("[Phase 2] Planar BSP slicing …")
    final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

    if config.reassign_orphans:
        print("[Phase 2] Reassigning orphan fragments …")
        final_pieces = reassign_orphans(final_pieces)

    print("[Phase 3] Baking back-face colours …")
    back_pieces = bake_backface_colours(final_pieces, config.output_path)

    export_results(config, mesh, final_pieces, back_pieces)

    print(f"\n[Done] Output directory: {config.output_path}")
    print(f"[Done] {len(final_pieces)} pieces exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
