"""
planar_main — CLI entry point for planar jigsaw slicing.

Usage:
    python planar_main.py --input model.glb --output out/ --pieces 24
"""

import json
import os
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed

import trimesh

from planar_lib import Config, build_arg_parser
from planar_phase_010 import load_model, normalize_mesh
from planar_phase_021 import cut_pieces_planar
from planar_phase_022 import reassign_orphans
from planar_phase_030 import bake_backface_colours
from planar_phase_040 import compute_adjacency, generate_preview


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

def _export_one(mesh: trimesh.Trimesh, path: str) -> None:
    mesh.export(path)


def export_results(
    config: Config,
    mesh: trimesh.Trimesh,
    final_pieces: list[trimesh.Trimesh],
    back_pieces: list[trimesh.Trimesh] | None = None,
) -> None:
    """Write all generated assets to the output directory (parallel I/O)."""
    out = config.output_path
    total_bounds = mesh.bounding_box

    pieces_dir = os.path.join(out, "pieces")
    os.makedirs(pieces_dir, exist_ok=True)

    # ---- individual front pieces (parallel) ----
    print("[Export] Writing individual front pieces …", file=sys.stderr, flush=True)
    with ThreadPoolExecutor() as ex:
        futs = {
            ex.submit(_export_one, p, os.path.join(pieces_dir, f"piece_{i:04d}.glb")): i
            for i, p in enumerate(final_pieces)
        }
        for fut in as_completed(futs):
            fut.result()  # raise on error
    print(
        f"[Export]   {len(final_pieces)} front pieces written",
        file=sys.stderr,
        flush=True,
    )

    # ---- individual back-face pieces (parallel) ----
    if back_pieces is not None:
        print("[Export] Writing individual back-face pieces …", file=sys.stderr, flush=True)
        with ThreadPoolExecutor() as ex:
            futs = {
                ex.submit(_export_one, bm, os.path.join(pieces_dir, f"piece_{i:04d}_back.glb")): i
                for i, bm in enumerate(back_pieces)
            }
            for fut in as_completed(futs):
                fut.result()
        print(
            f"[Export]   {len(back_pieces)} back-face pieces written",
            file=sys.stderr,
            flush=True,
        )

    # ---- consolidated multi-node GLB ----
    print("[Export] Building consolidated scene …", file=sys.stderr, flush=True)
    scene = trimesh.Scene()
    for i, piece_mesh in enumerate(final_pieces):
        node_name = f"piece_{i:04d}"
        scene.add_geometry(piece_mesh, node_name=f"{node_name}_front", geom_name=f"{node_name}_front")
        if back_pieces is not None:
            scene.add_geometry(back_pieces[i], node_name=f"{node_name}_back", geom_name=f"{node_name}_back")

    consolidated_path = os.path.join(out, "pieces.glb")
    print("[Export] Exporting consolidated GLB …", file=sys.stderr, flush=True)
    scene.export(consolidated_path, include_normals=False)
    print(f"[Export]   consolidated GLB written", file=sys.stderr, flush=True)

    # ---- checkpoint JSON ----
    print("[Export] Writing checkpoint …", file=sys.stderr, flush=True)
    piece_centroids = [p.triangles_center.mean(axis=0) for p in final_pieces]
    piece_vertex_counts = [len(p.vertices) for p in final_pieces]

    # Phase 4: adjacency and preview
    print("[Phase 4] Computing piece adjacency …", file=sys.stderr, flush=True)
    adjacency = compute_adjacency(
        final_pieces,
        centroid_list=piece_centroids,
        threshold=config.adjacency_threshold,
    )
    print(
        f"[Phase 4]   {len(adjacency)} directed neighbour edges found",
        file=sys.stderr,
        flush=True,
    )

    print("[Phase 4] Generating preview thumbnail …", file=sys.stderr, flush=True)
    preview_path = os.path.join(out, "preview.png")
    preview_ok = generate_preview(
        final_pieces,
        preview_path,
        resolution=config.preview_resolution,
    )
    if preview_ok:
        print(f"[Phase 4]   preview written", file=sys.stderr, flush=True)
    else:
        print(
            "[Phase 4]   WARNING: placeholder preview generated "
            "(install pyrender for real renders)",
            file=sys.stderr,
            flush=True,
        )

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
        "adjacency": adjacency,
    }
    checkpoint_path = os.path.join(out, "checkpoint.json")
    with open(checkpoint_path, "w") as f:
        json.dump(checkpoint, f, indent=2)
    print(f"[Export]   checkpoint written", file=sys.stderr, flush=True)


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

    print("[Phase 4] Computing adjacency & generating preview …")
    export_results(config, mesh, final_pieces, back_pieces)

    print(f"\n[Done] Output directory: {config.output_path}")
    print(f"[Done] {len(final_pieces)} pieces exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
