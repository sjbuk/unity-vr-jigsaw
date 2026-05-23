"""
main.py — CLI entry point for the 3D Jigsaw Piece Generator.

Phases implemented:
    Phase 1: Model ingestion, normalization, topology validation
    Phase 2: Centroidal geodesic Voronoi partitioning (CVT)

Usage:
    python main.py --input model.glb --output out/ --pieces 24
"""

import json
import os
import sys

import numpy as np
import trimesh

try:
    from .config import Config, build_arg_parser
    from .mesh_io import (
        load_model,
        normalize_mesh,
        is_closed,
        has_uv_maps,
        validate_topology,
    )
    from .partitioner import SurfacePartitioner
except ImportError:
    from config import Config, build_arg_parser
    from mesh_io import (
        load_model,
        normalize_mesh,
        is_closed,
        has_uv_maps,
        validate_topology,
    )
    from partitioner import SurfacePartitioner


def run_phase1(config: Config):
    """Load, normalize, and validate the input mesh."""
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

    watertight = is_closed(mesh)
    print(f"[Phase 1] Watertight: {watertight}")
    if not watertight and config.mode == "full_3d":
        print(
            "          WARNING: Non-watertight mesh detected – "
            "forcing fallback to shell mode."
        )
        config.mode = "shell"

    has_uv = has_uv_maps(mesh)
    print(f"[Phase 1] UV maps present: {has_uv}")

    valid, warning = validate_topology(mesh)
    if not valid:
        print(f"[Phase 1] WARNING: {warning}")

    return mesh


def run_phase2(mesh, config: Config):
    """Partition the mesh surface into puzzle-piece patches (CVT)."""
    print(
        f"[Phase 2] Partitioning into {config.pieces} pieces "
        f"(mode={config.mode}) …"
    )

    partitioner = SurfacePartitioner(
        mesh,
        n_pieces=config.pieces,
        seed=config.seed,
        max_iterations=7,
        sample_size=50,
    )

    seeds, labels = partitioner.partition()
    print(f"[Phase 2] CVT converged – {config.pieces} patches assigned.")

    patches = partitioner.get_patch_meshes()
    print(f"[Phase 2] Extracted {len(patches)} surface-patch meshes.")

    if config.mode == "shell":
        print(
            f"[Phase 2] Extruding patches inward by "
            f"{config.shell_thickness} …"
        )
        pieces = [
            partitioner.extrude_patch(p, config.shell_thickness, config.gap)
            for p in patches
        ]
        print(f"[Phase 2] Created {len(pieces)} extruded shell pieces.")
        return seeds, labels, patches, pieces

    return seeds, labels, patches, None


def export_results(
    config: Config,
    mesh: trimesh.Trimesh,
    seeds: np.ndarray,
    labels: np.ndarray,
    patches: list[trimesh.Trimesh],
    pieces: list[trimesh.Trimesh] | None,
) -> None:
    """Write all generated assets to the output directory."""
    out = config.output_path

    source_meshes = pieces if pieces else patches
    total_bounds = mesh.bounding_box

    # ---- individual piece GLB files ------------------------------------------
    pieces_dir = os.path.join(out, "pieces")
    os.makedirs(pieces_dir, exist_ok=True)

    for i, piece_mesh in enumerate(source_meshes):
        path = os.path.join(pieces_dir, f"piece_{i:04d}.glb")
        piece_mesh.export(path)
    print(f"[Export] Wrote {len(source_meshes)} individual pieces to {pieces_dir}")

    # ---- consolidated multi-node GLB -----------------------------------------
    scene = trimesh.Scene()
    for i, piece_mesh in enumerate(source_meshes):
        node_name = f"piece_{i}"
        scene.add_geometry(
            piece_mesh,
            node_name=node_name,
            geom_name=node_name,
        )
    consolidated_path = os.path.join(out, "pieces.glb")
    scene.export(consolidated_path)
    print(f"[Export] Wrote consolidated multi-node GLB to {consolidated_path}")

    # ---- checkpoint JSON -----------------------------------------------------
    checkpoint = {
        "source": os.path.basename(config.input_path),
        "piece_count": config.pieces,
        "mode": config.mode,
        "gap": config.gap,
        "shell_thickness": config.shell_thickness,
        "seed": config.seed,
        "total_bounds": {
            "center": total_bounds.centroid.tolist(),
            "extents": total_bounds.extents.tolist(),
        },
        "seeds": seeds.tolist(),
        "piece_vertex_counts": [
            int(np.sum(labels == i)) for i in range(config.pieces)
        ],
    }
    checkpoint_path = os.path.join(out, "checkpoint.json")
    with open(checkpoint_path, "w") as f:
        json.dump(checkpoint, f, indent=2)
    print(f"[Export] Wrote checkpoint to {checkpoint_path}")


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
    seeds, labels, patches, pieces = run_phase2(mesh, config)

    export_results(config, mesh, seeds, labels, patches, pieces)

    print(f"\n[Done] Output directory: {config.output_path}")
    print(f"[Done] {config.pieces} pieces ready for Phase 3 (joinery).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
