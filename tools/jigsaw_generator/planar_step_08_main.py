"""
main.py — CLI entry point for the 3D Jigsaw Piece Generator.

Phases:
    Phase 1: Model ingestion, normalization, topology validation
    Phase 2: Concurrent BFS flood-fill partitioning (surface / volumetric)
    Phase 3: Interlocking joinery (sinusoidal tabs / tapered pegs)
    Phase 4: Boolean slicing, hole capping, triplanar UV mapping
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
    from .planar_step_03_mesh_io import (
        load_model,
        normalize_mesh,
        is_closed,
        has_uv_maps,
        validate_topology,
    )
    from .planar_step_06_partitioner import FloodFillPartitioner
    from .planar_step_07_jigsaw_nubs import apply_joinery
    from .planar_step_04_mesh_cutter import cut_pieces_full_3d, cut_pieces_planar, cut_pieces_shell
except ImportError:
    from planar_step_01_config import Config, build_arg_parser
    from planar_step_03_mesh_io import (
        load_model,
        normalize_mesh,
        is_closed,
        has_uv_maps,
        validate_topology,
    )
    from planar_step_06_partitioner import FloodFillPartitioner
    from planar_step_07_jigsaw_nubs import apply_joinery
    from planar_step_04_mesh_cutter import cut_pieces_full_3d, cut_pieces_planar, cut_pieces_shell


# ---------------------------------------------------------------------------
# Phase 1
# ---------------------------------------------------------------------------

def run_phase1(config: Config) -> trimesh.Trimesh:
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


# ---------------------------------------------------------------------------
# Phase 2
# ---------------------------------------------------------------------------

def run_phase2(
    mesh: trimesh.Trimesh, config: Config
) -> tuple[trimesh.Trimesh, np.ndarray, np.ndarray, list[trimesh.Trimesh], list[trimesh.Trimesh] | None]:
    """Partition the mesh surface via concurrent BFS flood fill.  Extrude for shell mode.

    Returns:
        (working_mesh, seeds, labels, patches, shell_pieces)
        working_mesh is the (possibly boundary-smoothed) mesh used for extraction.
    """
    print(
        f"[Phase 2] Partitioning into {config.pieces} pieces "
        f"(mode={config.mode}) …"
    )
    partitioner = FloodFillPartitioner(
        mesh,
        n_pieces=config.pieces,
        seed=config.seed,
        mode=config.mode,
    )
    seeds, labels = partitioner.partition()
    print(f"[Phase 2] Flood fill complete – {config.pieces} patches assigned.")

    patches = partitioner.get_patch_meshes()
    print(f"[Phase 2] Extracted {len(patches)} surface-patch meshes.")

    if config.mode == "shell":
        print(
            f"[Phase 2] Extruding patches inward by "
            f"{config.shell_thickness} …"
        )
        shell_pieces = [
            partitioner.extrude_patch(p, config.shell_thickness, config.gap,
                                       original_mesh=partitioner.working_mesh)
            for p in patches
        ]
        print(f"[Phase 2] Created {len(shell_pieces)} extruded shell pieces.")
        return partitioner.working_mesh, seeds, labels, patches, shell_pieces

    return partitioner.working_mesh, seeds, labels, patches, None


# ---------------------------------------------------------------------------
# Phase 3
# ---------------------------------------------------------------------------

def run_phase3(
    patches: list[trimesh.Trimesh],
    pieces: list[trimesh.Trimesh] | None,
    labels: np.ndarray,
    mesh: trimesh.Trimesh,
    config: Config,
) -> tuple[list[trimesh.Trimesh], list[trimesh.Trimesh] | None]:
    """Apply interlocking joinery to patch boundaries."""
    print(f"[Phase 3] Generating joinery (mode={config.mode}) …")
    result_patches, result_pieces = apply_joinery(
        patches, pieces, labels, mesh, config
    )
    print("[Phase 3] Joinery complete.")
    return result_patches, result_pieces


# ---------------------------------------------------------------------------
# Phase 4
# ---------------------------------------------------------------------------

def run_phase4(
    mesh: trimesh.Trimesh,
    patches: list[trimesh.Trimesh],
    pieces: list[trimesh.Trimesh] | None,
    labels: np.ndarray,
    config: Config,
) -> list[trimesh.Trimesh]:
    """Boolean cutting, hole capping, triplanar UV assignment (or planar BSP slicing)."""
    if config.mode == "planar":
        print("[Phase 4] Planar BSP slicing pipeline …")
        return cut_pieces_planar(mesh, config.pieces, seed=config.seed)

    if config.mode == "shell":
        target = pieces if pieces else patches
        return cut_pieces_shell(target, mesh, config)

    print("[Phase 4] Full-3D boolean cutting pipeline …")
    return cut_pieces_full_3d(mesh, patches, labels, config)


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def export_results(
    config: Config,
    mesh: trimesh.Trimesh,
    seeds: np.ndarray,
    labels: np.ndarray,
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

    checkpoint = {
        "source": os.path.basename(config.input_path),
        "piece_count": config.pieces,
        "mode": config.mode,
        "gap": config.gap,
        "peg_clearance": config.peg_clearance,
        "shell_thickness": config.shell_thickness,
        "tab_density": config.tab_density,
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

    if config.mode == "planar":
        print("[Main] Planar mode selected — skipping Phases 2 (Voronoi) and 3 (joinery).")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)
        seeds = np.zeros(config.pieces, dtype=np.int64)
        # assign each original-mesh face to the piece with the nearest centroid
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
        working_mesh, seeds, labels, patches, pieces = run_phase2(mesh, config)
        patches, pieces = run_phase3(patches, pieces, labels, working_mesh, config)
        final_pieces = run_phase4(mesh, patches, pieces, labels, config)

    export_results(config, mesh, seeds, labels, final_pieces)

    print(f"\n[Done] Output directory: {config.output_path}")
    print(f"[Done] {len(final_pieces)} pieces exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
