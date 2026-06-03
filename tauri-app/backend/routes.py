import asyncio
import json
import logging
import os
import shutil
import threading
import time
from pathlib import Path

import numpy as np

from fastapi import APIRouter, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from starlette.responses import Response
from starlette.background import BackgroundTask

from planar_lib import Config
from planar_phase_010 import load_model, normalize_mesh
from planar_phase_040 import generate_lowpoly_preview

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api")

OUTPUTS_DIR = Path("/app/data/outputs")
SCRATCH_DIR = Path("/app/data/scratch")
MAX_UPLOAD_BYTES = 100 * 1024 * 1024  # 100 MB

_slice_lock = threading.Lock()
_progress_queues: dict[str, asyncio.Queue] = {}


def _emit(loop, job_id, msg):
    loop.call_soon_threadsafe(_progress_queues[job_id].put_nowait, msg)


def _done_cleanup(lock, loop, job_id):
    lock.release()
    loop.call_soon_threadsafe(lambda: _progress_queues.pop(job_id, None))


def _load_pieces(job_dir: Path) -> list:
    pieces_dir = job_dir / "pieces"
    pieces = []
    i = 0
    while True:
        path = pieces_dir / f"piece_{i:04d}.glb"
        if not path.exists():
            break
        m = load_model(str(path))
        pieces.append(m)
        i += 1
    return pieces


def _clean_outputs(job_dir: Path):
    pieces_dir = job_dir / "pieces"
    if pieces_dir.exists():
        shutil.rmtree(str(pieces_dir))
    for name in ("pieces.glb", "preview.png", "colour_atlas.png", "lowpoly_preview.glb"):
        p = job_dir / name
        if p.exists():
            p.unlink()


def _resolve_job_dir(job_id: str) -> Path:
    """Return the scratch dir for *job_id* if it exists, else the output dir.

    Scratch always takes precedence so that in-progress work is visible
    before it has been saved to the permanent output directory.
    """
    scratch = SCRATCH_DIR / job_id
    if scratch.exists():
        return scratch
    return OUTPUTS_DIR / job_id


def _resolve_scratch(job_id: str) -> Path:
    """Return the scratch dir for *job_id*, creating it if necessary."""
    path = SCRATCH_DIR / job_id
    path.mkdir(parents=True, exist_ok=True)
    return path


def _sync_upload(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock):
    logger.info("Job %s: upload started (input=%s)", job_id, config.input_path)
    try:
        _emit(loop, job_id, "[Phase 1] Loading and normalizing model...")
        mesh = load_model(config.input_path)
        logger.info("Job %s: loaded %d verts, %d faces", job_id, len(mesh.vertices), len(mesh.faces))
        mesh = normalize_mesh(mesh)

        _emit(loop, job_id, "[Export] Writing normalized mesh...")
        norm_path = os.path.join(config.output_path, "normalized.glb")
        mesh.export(norm_path)

        bb = mesh.bounding_box
        checkpoint = {
            "source": os.path.basename(config.input_path),
            "total_bounds": {
                "center": bb.centroid.tolist(),
                "extents": bb.extents.tolist(),
            },
        }
        with open(os.path.join(config.output_path, "checkpoint.json"), "w") as f:
            json.dump(checkpoint, f, indent=2)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: upload complete", job_id)
    except Exception as exc:
        logger.exception("Job %s: upload failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_slice(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock, old_meta: dict):
    from planar_main import export_results
    from planar_phase_021 import cut_pieces_planar
    from planar_phase_022 import reassign_orphans
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours

    logger.info("Job %s: slice started (pieces=%d, gap=%s, seed=%s, orphans=%s)",
                job_id, config.pieces, config.gap, config.seed, config.reassign_orphans)
    try:
        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(config.input_path)
        logger.info("Job %s: loaded normalized mesh (%d verts, %d faces)",
                    job_id, len(mesh.vertices), len(mesh.faces))

        _emit(loop, job_id, "[Phase 2] Planar BSP slicing...")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)
        logger.info("Job %s: slicing produced %d pieces", job_id, len(final_pieces))

        if config.reassign_orphans:
            _emit(loop, job_id, "[Phase 2] Reassigning orphan fragments...")
            final_pieces = reassign_orphans(final_pieces, max_iter=1)
            logger.info("Job %s: orphan reassignment done (%d pieces)", job_id, len(final_pieces))

        if config.smooth_edges:
            _emit(loop, job_id, "[Phase 2d] Smoothing cut edges...")
            smooth_piece_boundaries(
                final_pieces,
                gap=config.gap,
                smooth_iterations=config.smooth_iterations,
                smooth_lambda=config.smooth_lambda,
                smooth_nu=config.smooth_nu,
            )

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(final_pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(Path(config.output_path))
        export_results(config, mesh, final_pieces, back_pieces)

        # Ensure normalized.glb is present in the output dir so subsequent
        # operations (e.g. orphans ↔ reassign) can find it via _resolve_job_dir.
        _copy_normalized_to_output(config.input_path, config.output_path)

        _patch_checkpoint_meta(config.output_path, old_meta)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: slice complete (%d pieces)", job_id, len(final_pieces))
    except Exception as exc:
        logger.exception("Job %s: slice failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _copy_normalized_to_output(input_path: str, output_path: str):
    """Copy normalized.glb from *input_path* dir to *output_path* dir if not already there."""
    src = os.path.join(os.path.dirname(input_path), "normalized.glb")
    dst = os.path.join(output_path, "normalized.glb")
    if os.path.exists(src) and not os.path.exists(dst):
        shutil.copy2(src, dst)


def _sync_orphans(job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock, old_meta: dict):
    from planar_main import export_results
    from planar_phase_022 import reassign_orphans
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours

    logger.info("Job %s: orphan reassignment started", job_id)
    try:
        src_dir = _resolve_job_dir(job_id)
        out_dir = _resolve_scratch(job_id)

        norm_glb = src_dir / "normalized.glb"
        if not norm_glb.exists():
            raise FileNotFoundError("normalized.glb not found — upload model first")
        ck_path = src_dir / "checkpoint.json"
        if not ck_path.exists():
            raise FileNotFoundError("checkpoint.json not found — slice model first")

        with open(ck_path) as f:
            ck = json.load(f)

        config = Config(
            input_path=str(norm_glb),
            output_path=str(out_dir),
            pieces=ck.get("piece_count", 24),
            gap=ck.get("gap", 0.001),
            seed=ck.get("seed", None),
            adjacency_threshold=ck.get("adjacency_threshold", 0.01),
            preview_faces=ck.get("preview_faces", 2000),
            smooth_edges=ck.get("smooth_edges", False),
            smooth_iterations=ck.get("smooth_iterations", 5),
            smooth_lambda=ck.get("smooth_lambda", 0.5),
            smooth_nu=ck.get("smooth_nu", 0.5),
        )

        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(str(norm_glb))

        _emit(loop, job_id, "[Load] Loading current pieces...")
        pieces = _load_pieces(src_dir)
        if not pieces:
            raise RuntimeError("No pieces found — slice model first")
        logger.info("Job %s: loaded %d existing pieces from %s", job_id, len(pieces), src_dir)

        _emit(loop, job_id, f"[Phase 2] Reassigning orphan fragments ({len(pieces)} pieces)...")
        pieces = reassign_orphans(pieces, max_iter=1)
        logger.info("Job %s: orphan reassignment produced %d pieces", job_id, len(pieces))

        if config.smooth_edges:
            _emit(loop, job_id, "[Phase 2d] Smoothing cut edges...")
            smooth_piece_boundaries(
                pieces,
                gap=config.gap,
                smooth_iterations=config.smooth_iterations,
                smooth_lambda=config.smooth_lambda,
                smooth_nu=config.smooth_nu,
            )

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(out_dir)
        export_results(config, mesh, pieces, back_pieces)

        _copy_normalized_to_output(str(norm_glb), str(out_dir))

        _patch_checkpoint_meta(str(out_dir), old_meta)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: orphan reassignment complete (%d pieces)", job_id, len(pieces))
    except Exception as exc:
        logger.exception("Job %s: orphan reassignment failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_fix_orphans(
    job_id: str,
    loop: asyncio.AbstractEventLoop,
    lock: threading.Lock,
    old_meta: dict,
    payload: dict,
):
    from planar_main import export_results
    from planar_phase_022 import _extract_submesh, _merge_mesh_into
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours
    from planar_phase_010 import load_model

    logger.info("Job %s: manual fix-orphans started", job_id)
    try:
        src_dir = _resolve_job_dir(job_id)
        out_dir = _resolve_scratch(job_id)

        norm_glb = src_dir / "normalized.glb"
        if not norm_glb.exists():
            raise FileNotFoundError("normalized.glb not found — upload model first")
        ck_path = src_dir / "checkpoint.json"
        if not ck_path.exists():
            raise FileNotFoundError("checkpoint.json not found — slice model first")

        with open(ck_path) as f:
            ck = json.load(f)

        config = Config(
            input_path=str(norm_glb),
            output_path=str(out_dir),
            pieces=ck.get("piece_count", 24),
            gap=ck.get("gap", 0.001),
            seed=ck.get("seed", None),
            adjacency_threshold=ck.get("adjacency_threshold", 0.01),
            preview_faces=ck.get("preview_faces", 2000),
            smooth_edges=ck.get("smooth_edges", False),
            smooth_iterations=ck.get("smooth_iterations", 5),
            smooth_lambda=ck.get("smooth_lambda", 0.5),
            smooth_nu=ck.get("smooth_nu", 0.5),
        )

        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(str(norm_glb))

        _emit(loop, job_id, "[Load] Loading current pieces...")
        pieces = _load_pieces(src_dir)
        if not pieces:
            raise RuntimeError("No pieces found — slice model first")

        dest_idx = payload.get("destination_piece", -1)
        assignments = payload.get("assignments", [])

        if dest_idx < 0 or dest_idx >= len(pieces):
            raise ValueError(f"Invalid destination piece index: {dest_idx}")

        _emit(loop, job_id, f"[Fix] Applying {len(assignments)} reassignment(s)...")

        for assignment in assignments:
            src_idx = assignment.get("source_piece", -1)
            face_indices = assignment.get("face_indices", [])

            if src_idx < 0 or src_idx >= len(pieces):
                logger.warning("Job %s: skipping invalid source piece %d", job_id, src_idx)
                continue
            if src_idx == dest_idx:
                logger.warning("Job %s: skipping self-assignment piece %d", job_id, src_idx)
                continue
            if not face_indices:
                continue

            src_mesh = pieces[src_idx]
            dst_mesh = pieces[dest_idx]

            valid_indices = [i for i in face_indices if i < len(src_mesh.faces)]
            if not valid_indices:
                continue

            mask = np.zeros(len(src_mesh.faces), dtype=bool)
            mask[valid_indices] = True

            if np.all(mask):
                logger.warning(
                    "Job %s: skipping reassignment that would empty source piece %d",
                    job_id, src_idx,
                )
                continue

            orphan = _extract_submesh(src_mesh, mask)
            remaining = _extract_submesh(src_mesh, ~mask)
            remaining.merge_vertices()

            # Strip stale material textures from all involved meshes
            for m in (src_mesh, dst_mesh, orphan, remaining):
                mat = m.visual.material if hasattr(m.visual, "material") else None
                if mat is not None:
                    for attr_name in ("baseColorTexture", "metallicRoughnessTexture",
                                      "normalTexture", "occlusionTexture", "emissiveTexture"):
                        if hasattr(mat, attr_name):
                            setattr(mat, attr_name, None)

            merged = _merge_mesh_into(dst_mesh, orphan)
            merged.merge_vertices()

            pieces[src_idx] = remaining
            pieces[dest_idx] = merged

            logger.info(
                "Job %s: moved %d faces from piece %d to piece %d",
                job_id, len(valid_indices), src_idx, dest_idx,
            )

        if config.smooth_edges:
            _emit(loop, job_id, "[Phase 2d] Smoothing cut edges...")
            smooth_piece_boundaries(
                pieces,
                gap=config.gap,
                smooth_iterations=config.smooth_iterations,
                smooth_lambda=config.smooth_lambda,
                smooth_nu=config.smooth_nu,
            )

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(out_dir)
        export_results(config, mesh, pieces, back_pieces)

        _copy_normalized_to_output(str(norm_glb), str(out_dir))
        _patch_checkpoint_meta(str(out_dir), old_meta)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: manual fix-orphans complete (%d pieces)", job_id, len(pieces))
    except Exception as exc:
        logger.exception("Job %s: manual fix-orphans failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_remove_islands(
    job_id: str,
    loop: asyncio.AbstractEventLoop,
    lock: threading.Lock,
    old_meta: dict,
    payload: dict,
):
    from planar_main import export_results
    from planar_phase_022 import _extract_submesh, _merge_mesh_into, _discover_one, _find_best_parent
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours
    from planar_phase_010 import load_model

    logger.info("Job %s: remove islands started", job_id)
    try:
        src_dir = _resolve_job_dir(job_id)
        out_dir = _resolve_scratch(job_id)

        norm_glb = src_dir / "normalized.glb"
        if not norm_glb.exists():
            raise FileNotFoundError("normalized.glb not found — upload model first")
        ck_path = src_dir / "checkpoint.json"
        if not ck_path.exists():
            raise FileNotFoundError("checkpoint.json not found — slice model first")

        with open(ck_path) as f:
            ck = json.load(f)

        config = Config(
            input_path=str(norm_glb),
            output_path=str(out_dir),
            pieces=ck.get("piece_count", 24),
            gap=ck.get("gap", 0.001),
            seed=ck.get("seed", None),
            adjacency_threshold=ck.get("adjacency_threshold", 0.01),
            preview_faces=ck.get("preview_faces", 2000),
            smooth_edges=ck.get("smooth_edges", False),
            smooth_iterations=ck.get("smooth_iterations", 5),
            smooth_lambda=ck.get("smooth_lambda", 0.5),
            smooth_nu=ck.get("smooth_nu", 0.5),
        )

        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(str(norm_glb))

        _emit(loop, job_id, "[Load] Loading current pieces...")
        pieces = _load_pieces(src_dir)
        if not pieces:
            raise RuntimeError("No pieces found — slice model first")

        source_idx = payload.get("source_piece", -1)
        min_island_size = payload.get("min_island_size", 100)

        if source_idx < 0 or source_idx >= len(pieces):
            raise ValueError(f"Invalid source piece index: {source_idx}")

        _emit(loop, job_id, f"[Islands] Discovering components in piece {source_idx}...")

        src_mesh = pieces[source_idx]
        parent, orphans = _discover_one(src_mesh)

        if not orphans:
            _emit(loop, job_id, "[Islands] No disconnected components found in piece.")
            _emit(loop, job_id, "[DONE]")
            return

        small_orphans = []
        for face_count, mask_or_mesh in orphans:
            if face_count < min_island_size:
                small_orphans.append((face_count, mask_or_mesh))

        if not small_orphans:
            _emit(loop, job_id, f"[Islands] No components smaller than {min_island_size} faces found.")
            _emit(loop, job_id, "[DONE]")
            return

        _emit(loop, job_id, f"[Islands] Found {len(small_orphans)} small island(s) to reassign (threshold: {min_island_size} faces).")

        parent_aabbs = np.array([p.bounds for p in pieces])
        parent_centroids = np.array([p.centroid for p in pieces])

        origin_mesh = src_mesh
        origin_n_faces = len(origin_mesh.faces)

        assignments = []
        kept_mask = np.ones(origin_n_faces, dtype=bool)
        has_maskless = False
        islands_flipped = 0
        faces_changed = 0

        for face_count, mask_or_mesh in small_orphans:
            if isinstance(mask_or_mesh, np.ndarray):
                island = _extract_submesh(origin_mesh, mask_or_mesh)
                face_mask = mask_or_mesh
            else:
                island = mask_or_mesh
                face_mask = None
                has_maskless = True

            dest_idx = _find_best_parent(island, pieces, parent_aabbs, parent_centroids)
            if dest_idx == source_idx:
                logger.info("Job %s: island best parent is itself, skipping", job_id)
                continue

            if face_mask is not None:
                kept_mask[face_mask] = False

            assignments.append((island, dest_idx, face_count))
            islands_flipped += 1
            faces_changed += face_count

        if islands_flipped == 0:
            _emit(loop, job_id, "[Islands] No islands were reassigned.")
            _emit(loop, job_id, "[DONE]")
            return

        if has_maskless:
            remaining = parent
            remaining.merge_vertices()
        else:
            remaining = _extract_submesh(origin_mesh, kept_mask)
            remaining.merge_vertices()

        for m in (origin_mesh, remaining):
            mat = m.visual.material if hasattr(m.visual, "material") else None
            if mat is not None:
                for attr_name in ("baseColorTexture", "metallicRoughnessTexture",
                                "normalTexture", "occlusionTexture", "emissiveTexture"):
                    if hasattr(mat, attr_name):
                        setattr(mat, attr_name, None)

        pieces[source_idx] = remaining

        for island, dest_idx, face_count in assignments:
            for m in (pieces[dest_idx], island):
                mat = m.visual.material if hasattr(m.visual, "material") else None
                if mat is not None:
                    for attr_name in ("baseColorTexture", "metallicRoughnessTexture",
                                    "normalTexture", "occlusionTexture", "emissiveTexture"):
                        if hasattr(mat, attr_name):
                            setattr(mat, attr_name, None)

            merged = _merge_mesh_into(pieces[dest_idx], island)
            merged.merge_vertices()
            pieces[dest_idx] = merged

            logger.info(
                "Job %s: moved island of %d faces from piece %d to piece %d",
                job_id, face_count, source_idx, dest_idx,
            )

        _emit(loop, job_id, f"[Islands] Reassigned {islands_flipped} islands ({faces_changed} faces total).")

        if config.smooth_edges:
            _emit(loop, job_id, "[Phase 2d] Smoothing cut edges...")
            smooth_piece_boundaries(
                pieces,
                gap=config.gap,
                smooth_iterations=config.smooth_iterations,
                smooth_lambda=config.smooth_lambda,
                smooth_nu=config.smooth_nu,
            )

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(out_dir)
        export_results(config, mesh, pieces, back_pieces)

        _copy_normalized_to_output(str(norm_glb), str(out_dir))
        _patch_checkpoint_meta(str(out_dir), old_meta)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: remove islands complete (%d pieces)", job_id, len(pieces))
    except Exception as exc:
        logger.exception("Job %s: remove islands failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_smooth(
    job_id: str,
    loop: asyncio.AbstractEventLoop,
    lock: threading.Lock,
    old_meta: dict,
    payload: dict,
):
    from planar_main import export_results
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours
    from planar_phase_010 import load_model

    logger.info("Job %s: smooth edges started", job_id)
    try:
        src_dir = _resolve_job_dir(job_id)
        out_dir = _resolve_scratch(job_id)

        norm_glb = src_dir / "normalized.glb"
        if not norm_glb.exists():
            raise FileNotFoundError("normalized.glb not found -- upload model first")
        ck_path = src_dir / "checkpoint.json"
        if not ck_path.exists():
            raise FileNotFoundError("checkpoint.json not found -- slice model first")

        with open(ck_path) as f:
            ck = json.load(f)

        gap = payload.get("gap", ck.get("gap", 0.001))
        smooth_iterations = payload.get("smooth_iterations", ck.get("smooth_iterations", 1))
        smooth_lambda = payload.get("smooth_lambda", ck.get("smooth_lambda", 0.5))
        smooth_nu = payload.get("smooth_nu", ck.get("smooth_nu", 0.5))

        config = Config(
            input_path=str(norm_glb),
            output_path=str(out_dir),
            pieces=ck.get("piece_count", 24),
            gap=gap,
            seed=ck.get("seed", None),
            adjacency_threshold=ck.get("adjacency_threshold", 0.01),
            preview_faces=ck.get("preview_faces", 2000),
            smooth_edges=True,
            smooth_iterations=smooth_iterations,
            smooth_lambda=smooth_lambda,
            smooth_nu=smooth_nu,
        )

        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(str(norm_glb))

        _emit(loop, job_id, "[Load] Loading current pieces...")
        pieces = _load_pieces(src_dir)
        if not pieces:
            raise RuntimeError("No pieces found -- slice model first")
        logger.info("Job %s: loaded %d existing pieces from %s", job_id, len(pieces), src_dir)

        _emit(loop, job_id, f"[Phase 2d] Smoothing cut edges ({len(pieces)} pieces)...")
        smooth_piece_boundaries(
            pieces,
            gap=config.gap,
            smooth_iterations=config.smooth_iterations,
            smooth_lambda=config.smooth_lambda,
            smooth_nu=config.smooth_nu,
        )
        logger.info("Job %s: smooth edges complete (%d pieces)", job_id, len(pieces))

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(out_dir)
        export_results(config, mesh, pieces, back_pieces)

        _copy_normalized_to_output(str(norm_glb), str(out_dir))

        ck_path_out = out_dir / "checkpoint.json"
        if ck_path_out.exists():
            with open(ck_path_out) as f:
                ck_out = json.load(f)
            ck_out["smooth_edges"] = True
            ck_out["smooth_iterations"] = smooth_iterations
            ck_out["smooth_lambda"] = smooth_lambda
            ck_out["smooth_nu"] = smooth_nu
            with open(ck_path_out, "w") as f:
                json.dump(ck_out, f, indent=2)

        _patch_checkpoint_meta(str(out_dir), old_meta)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: smooth edges complete (%d pieces)", job_id, len(pieces))
    except Exception as exc:
        logger.exception("Job %s: smooth edges failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _patch_checkpoint_meta(output_path: str, old_meta: dict):
    """Merge preserved name/orientation into the freshly-written checkpoint."""
    if not old_meta:
        return
    ck_path = os.path.join(output_path, "checkpoint.json")
    if not os.path.exists(ck_path):
        return
    with open(ck_path) as f:
        ck = json.load(f)
    changed = False
    for key in ("name", "orientation"):
        if old_meta.get(key, None) is not None:
            ck[key] = old_meta[key]
            changed = True
    if changed:
        with open(ck_path, "w") as f:
            json.dump(ck, f, indent=2)


def _read_old_meta(job_dir: Path) -> dict:
    ck_path = job_dir / "checkpoint.json"
    if not ck_path.exists():
        return {}
    with open(ck_path) as f:
        ck = json.load(f)
    meta = {}
    for key in ("name", "orientation"):
        if key in ck:
            meta[key] = ck[key]
    return meta


def _save_upload(config_data: dict, file: UploadFile) -> tuple[str, Config, str]:
    filename = file.filename or "model.glb"
    if not filename.lower().endswith((".glb", ".gltf")):
        raise HTTPException(status_code=400, detail="Only .glb and .gltf files are supported")

    cfg = Config(
        input_path="",
        output_path="",
        **{k: v for k, v in config_data.items() if k in (
            "pieces", "gap", "seed", "reassign_orphans", "adjacency_threshold",
            "preview_resolution", "preview_height", "preview_faces",
            "smooth_edges", "smooth_iterations", "smooth_lambda", "smooth_nu",
        )},
    )

    ts = str(int(time.time()))
    job_id = f"{Path(filename).stem}_{ts}"
    job_dir = _resolve_scratch(job_id)

    input_path = str(job_dir / filename)
    cfg.input_path = input_path
    cfg.output_path = str(job_dir)

    return job_id, cfg, filename


async def _read_upload(file: UploadFile) -> tuple[bytearray, str]:
    size = 0
    contents = bytearray()
    chunk_size = 64 * 1024
    while True:
        chunk = await file.read(chunk_size)
        if not chunk:
            break
        size += len(chunk)
        if size > MAX_UPLOAD_BYTES:
            raise HTTPException(status_code=413, detail="File exceeds 100 MB limit")
        contents.extend(chunk)
    return contents, (file.filename or "model.glb")


def _start_job(job_id: str, target, *args):
    _progress_queues[job_id] = asyncio.Queue()
    _progress_queues[job_id].put_nowait("Starting job...")
    loop = asyncio.get_running_loop()
    loop.run_in_executor(None, target, *args)
    return {"job_id": job_id}


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@router.post("/slice")
async def slice_model(
    file: UploadFile = File(...),
    config: str = Form(...),
):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another slicing job is already running")

    try:
        config_data = json.loads(config)
        job_id, cfg, filename = _save_upload(config_data, file)
        contents, _ = await _read_upload(file)

        with open(cfg.input_path, "wb") as f:
            f.write(contents)

        try:
            cfg.validate()
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))

        logger.info("POST /slice: job=%s file=%s pieces=%d", job_id, filename, cfg.pieces)
        return _start_job(job_id, _sync_pipeline, cfg, job_id, asyncio.get_running_loop(), _slice_lock)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /slice: unexpected error")
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


def _sync_pipeline(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock):
    from planar_main import run_ingest, export_results
    from planar_phase_021 import cut_pieces_planar
    from planar_phase_022 import reassign_orphans
    from planar_phase_025 import smooth_piece_boundaries
    from planar_phase_030 import bake_backface_colours

    logger.info("Job %s: pipeline started (pieces=%d, gap=%s, seed=%s)",
                job_id, config.pieces, config.gap, config.seed)
    try:
        _emit(loop, job_id, "[Phase 1] Loading and normalizing model...")
        mesh = run_ingest(config)
        logger.info("Job %s: ingest complete (%d verts, %d faces)",
                    job_id, len(mesh.vertices), len(mesh.faces))

        _emit(loop, job_id, "[Phase 2] Planar BSP slicing...")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)
        logger.info("Job %s: slicing produced %d pieces", job_id, len(final_pieces))

        if config.reassign_orphans:
            _emit(loop, job_id, "[Phase 2] Reassigning orphan fragments...")
            final_pieces = reassign_orphans(final_pieces, max_iter=1)

        if config.smooth_edges:
            _emit(loop, job_id, "[Phase 2d] Smoothing cut edges...")
            smooth_piece_boundaries(
                final_pieces,
                gap=config.gap,
                smooth_iterations=config.smooth_iterations,
                smooth_lambda=config.smooth_lambda,
                smooth_nu=config.smooth_nu,
            )

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(final_pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        export_results(config, mesh, final_pieces, back_pieces)

        _emit(loop, job_id, "[DONE]")
        logger.info("Job %s: pipeline complete (%d pieces)", job_id, len(final_pieces))
    except Exception as exc:
        logger.exception("Job %s: pipeline failed", job_id)
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


@router.post("/upload")
async def upload_model(file: UploadFile = File(...)):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        contents, filename = await _read_upload(file)
        job_id, cfg, _ = _save_upload({}, file)

        with open(cfg.input_path, "wb") as f:
            f.write(contents)

        logger.info("POST /upload: job=%s file=%s (%d bytes)", job_id, filename, len(contents))
        return _start_job(job_id, _sync_upload, cfg, job_id, asyncio.get_running_loop(), _slice_lock)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /upload: unexpected error")
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/slice/{job_id}")
async def slice_job(job_id: str, payload: dict = {}):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another slicing job is already running")

    try:
        src_dir = _resolve_job_dir(job_id)
        norm_glb = src_dir / "normalized.glb"
        if not norm_glb.exists():
            raise HTTPException(status_code=400, detail="No uploaded model found for this job. Upload first via POST /api/upload")

        out_dir = _resolve_scratch(job_id)

        cfg = Config(
            input_path=str(norm_glb),
            output_path=str(out_dir),
            pieces=payload.get("pieces", 24),
            gap=payload.get("gap", 0.001),
            seed=payload.get("seed", None),
            reassign_orphans=payload.get("reassign_orphans", False),
            preview_faces=payload.get("preview_faces", 2000),
            smooth_edges=payload.get("smooth_edges", False),
            smooth_iterations=payload.get("smooth_iterations", 5),
            smooth_lambda=payload.get("smooth_lambda", 0.5),
            smooth_nu=payload.get("smooth_nu", 0.5),
        )
        try:
            cfg.validate()
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))

        old_meta = _read_old_meta(src_dir)
        logger.info("POST /slice/%s: re-slicing (pieces=%d, gap=%s)", job_id, cfg.pieces, cfg.gap)
        return _start_job(job_id, _sync_slice, cfg, job_id, asyncio.get_running_loop(), _slice_lock, old_meta)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /slice/%s: unexpected error", job_id)
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/orphans/{job_id}")
async def reassign_orphans_endpoint(job_id: str):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        job_dir = _resolve_job_dir(job_id)
        if not job_dir.exists():
            raise HTTPException(status_code=404, detail="Job not found")

        old_meta = _read_old_meta(job_dir)
        logger.info("POST /orphans/%s: reassigning orphans", job_id)
        return _start_job(job_id, _sync_orphans, job_id, asyncio.get_running_loop(), _slice_lock, old_meta)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /orphans/%s: unexpected error", job_id)
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/fix-orphans/{job_id}")
async def manual_fix_orphans(job_id: str, payload: dict = {}):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        job_dir = _resolve_job_dir(job_id)
        if not job_dir.exists():
            raise HTTPException(status_code=404, detail="Job not found")

        old_meta = _read_old_meta(job_dir)
        logger.info("POST /fix-orphans/%s: manual fix orphan reassignment", job_id)
        return _start_job(job_id, _sync_fix_orphans, job_id, asyncio.get_running_loop(), _slice_lock, old_meta, payload)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /fix-orphans/%s: unexpected error", job_id)
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/remove-islands/{job_id}")
async def remove_islands(job_id: str, payload: dict = {}):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        job_dir = _resolve_job_dir(job_id)
        if not job_dir.exists():
            raise HTTPException(status_code=404, detail="Job not found")

        old_meta = _read_old_meta(job_dir)
        logger.info("POST /remove-islands/%s: removing small islands", job_id)
        return _start_job(job_id, _sync_remove_islands, job_id, asyncio.get_running_loop(), _slice_lock, old_meta, payload)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /remove-islands/%s: unexpected error", job_id)
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/smooth/{job_id}")
async def smooth_edges(job_id: str, payload: dict = {}):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        job_dir = _resolve_job_dir(job_id)
        if not job_dir.exists():
            raise HTTPException(status_code=404, detail="Job not found")

        old_meta = _read_old_meta(job_dir)
        logger.info("POST /smooth/%s: smoothing edges", job_id)
        return _start_job(job_id, _sync_smooth, job_id, asyncio.get_running_loop(), _slice_lock, old_meta, payload)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        logger.exception("POST /smooth/%s: unexpected error", job_id)
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/save/{job_id}")
async def save_job(job_id: str, payload: dict = {}):
    """Persist scratch results to the permanent output directory (or update name if already saved)."""
    scratch_dir = SCRATCH_DIR / job_id
    output_dir = OUTPUTS_DIR / job_id
    name = (payload.get("name") or "").strip()

    if scratch_dir.exists():
        output_dir.mkdir(parents=True, exist_ok=True)
        for item in scratch_dir.iterdir():
            dst = output_dir / item.name
            if item.is_dir():
                if dst.exists():
                    shutil.rmtree(str(dst))
                shutil.copytree(str(item), str(dst))
            else:
                shutil.copy2(str(item), str(dst))
        logger.info("POST /save/%s: copied scratch → output", job_id)
    elif not output_dir.exists():
        raise HTTPException(status_code=404, detail="No data found for this job.")

    # Write name to checkpoint
    ck_path = output_dir / "checkpoint.json"
    if ck_path.exists():
        with open(ck_path) as f:
            ck = json.load(f)
        ck["name"] = name
        with open(ck_path, "w") as f:
            json.dump(ck, f, indent=2)
    elif name:
        with open(ck_path, "w") as f:
            json.dump({"name": name}, f, indent=2)

    # Build response matching the [DONE] result format
    pieces_info = []
    pieces_dir = output_dir / "pieces"
    if pieces_dir.exists():
        i = 0
        while (pieces_dir / f"piece_{i:04d}.glb").exists():
            pieces_info.append({
                "index": i,
                "path": f"pieces/piece_{i:04d}.glb",
                "vertices": 0,
                "back_path": f"pieces/piece_{i:04d}_back.glb",
                "back_vertices": 0,
            })
            i += 1

    result = {
        "job_id": job_id,
        "piece_count": len(pieces_info),
        "output_dir": str(output_dir),
        "pieces": pieces_info,
        "name": name,
    }
    if (output_dir / "pieces.glb").exists():
        result["consolidated"] = "pieces.glb"
    if (output_dir / "normalized.glb").exists():
        result["normalized_glb"] = "normalized.glb"
    if (output_dir / "lowpoly_preview.glb").exists():
        result["preview_glb"] = "lowpoly_preview.glb"
    if (output_dir / "checkpoint.json").exists():
        result["checkpoint"] = "checkpoint.json"
    if (output_dir / "colour_atlas.png").exists():
        result["colour_atlas"] = "colour_atlas.png"

    if ck_path.exists():
        with open(ck_path) as f:
            ck = json.load(f)
        result["orientation"] = ck.get("orientation")

    logger.info("POST /save/%s: saved (%d pieces, name='%s')", job_id, len(pieces_info), name)
    return result


@router.post("/preview/{job_id}")
async def regenerate_preview(job_id: str, payload: dict = {}):
    job_dir = _resolve_job_dir(job_id)
    norm_glb = job_dir / "normalized.glb"
    if not norm_glb.exists():
        raise HTTPException(status_code=400, detail="No normalized model found — upload first")

    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    preview_faces = payload.get("preview_faces", 2000)

    try:
        from planar_phase_010 import load_model

        logger.info("POST /preview/%s: regenerating (%d faces)", job_id, preview_faces)
        mesh = load_model(str(norm_glb))
        lowpoly_path = job_dir / "lowpoly_preview.glb"

        lowpoly_verts, lowpoly_faces = generate_lowpoly_preview(
            mesh, str(lowpoly_path), target_faces=preview_faces,
        )

        if lowpoly_verts is None or lowpoly_faces is None:
            raise RuntimeError("Preview generation failed — ensure pymeshlab is installed")

        ck_path = job_dir / "checkpoint.json"
        if ck_path.exists():
            with open(ck_path) as f:
                ck = json.load(f)
            ck["lowpoly_vertices"] = lowpoly_verts
            ck["lowpoly_faces"] = lowpoly_faces
            ck["preview_faces"] = preview_faces
            with open(ck_path, "w") as f:
                json.dump(ck, f, indent=2)

        return {
            "status": "ok",
            "preview_glb": "lowpoly_preview.glb",
            "lowpoly_vertices": lowpoly_verts,
            "lowpoly_faces": lowpoly_faces,
        }

    except Exception as e:
        logger.exception("POST /preview/%s: failed", job_id)
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        _slice_lock.release()


@router.get("/progress/{job_id}")
async def progress_stream(job_id: str):
    queue = _progress_queues.get(job_id)
    if queue is None:
        raise HTTPException(status_code=404, detail="Job not found or already completed")

    async def generate():
        try:
            while True:
                try:
                    msg = await asyncio.wait_for(queue.get(), timeout=30)
                except asyncio.TimeoutError:
                    yield "data: keepalive\n\n"
                    continue

                if msg == "[DONE]":
                    job_dir = _resolve_job_dir(job_id)
                    checkpoint_path = job_dir / "checkpoint.json"
                    norm_path = job_dir / "normalized.glb"
                    result = {}
                    if checkpoint_path.exists():
                        import json
                        with open(checkpoint_path) as f:
                            ck = json.load(f)
                        pieces_info = []
                        for i in range(ck.get("piece_count", 0)):
                            pieces_info.append({
                                "index": i,
                                "path": f"pieces/piece_{i:04d}.glb",
                                "vertices": ck.get("piece_vertex_counts", [])[i] if i < len(ck.get("piece_vertex_counts", [])) else 0,
                                "back_path": f"pieces/piece_{i:04d}_back.glb",
                                "back_vertices": 0,
                            })
                        result = {
                            "job_id": job_id,
                            "piece_count": ck.get("piece_count", 0),
                            "output_dir": str(job_dir),
                            "consolidated": "pieces.glb",
                            "checkpoint": "checkpoint.json",
                            "colour_atlas": "colour_atlas.png",
                            "pieces": pieces_info,
                            "name": ck.get("name", ""),
                            "orientation": ck.get("orientation", None),
                        }
                        if norm_path.exists():
                            result["normalized_glb"] = "normalized.glb"
                        if (job_dir / "lowpoly_preview.glb").exists():
                            result["preview_glb"] = "lowpoly_preview.glb"
                    yield f"data: [DONE]\ndata: {json.dumps(result)}\n\n"
                    return
                elif msg.startswith("[ERROR]"):
                    yield f"data: {msg}\n\n"
                    return
                else:
                    yield f"data: {msg}\n\n"
        except asyncio.CancelledError:
            pass

    return StreamingResponse(
        generate(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )


@router.get("/outputs/{job_id}/{file_path:path}")
async def serve_output(job_id: str, file_path: str):
    # Check scratch first, fall back to output
    for base in (SCRATCH_DIR, OUTPUTS_DIR):
        job_dir = base / job_id
        full_path = (job_dir / file_path).resolve()
        if not str(full_path).startswith(str(job_dir.resolve())):
            continue  # path traversal — skip this base
        if full_path.exists() and full_path.is_file():
            ext = full_path.suffix.lower()
            media_type_map = {
                ".glb": "model/gltf-binary",
                ".gltf": "model/gltf+json",
                ".png": "image/png",
                ".json": "application/json",
                ".bin": "application/octet-stream",
            }
            media_type = media_type_map.get(ext, "application/octet-stream")

            return FileResponse(
                full_path,
                media_type=media_type,
                headers={
                    "Access-Control-Allow-Origin": "*",
                    "Cache-Control": "no-cache, no-store, must-revalidate",
                },
            )

    raise HTTPException(status_code=404, detail="File not found")


@router.get("/jobs")
async def list_jobs():
    if not OUTPUTS_DIR.exists():
        return []
    jobs = []
    for entry in sorted(OUTPUTS_DIR.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
        if not entry.is_dir():
            continue
        checkpoint = entry / "checkpoint.json"
        job_info = {
            "job_id": entry.name,
            "piece_count": 0,
            "source_model": "",
            "name": "",
            "created_at": "",
        }
        if checkpoint.exists():
            try:
                with open(checkpoint) as f:
                    ck = json.load(f)
                job_info["piece_count"] = ck.get("piece_count", 0)
                job_info["source_model"] = ck.get("source", "")
                job_info["name"] = ck.get("name", "")
            except Exception:
                pass
        job_info["created_at"] = time.strftime(
            "%Y-%m-%d %H:%M", time.localtime(entry.stat().st_mtime)
        )
        jobs.append(job_info)
    return jobs


@router.get("/jobs/{job_id}")
async def get_job(job_id: str):
    job_dir = _resolve_job_dir(job_id)
    if not job_dir.exists():
        raise HTTPException(status_code=404, detail="Job not found")

    checkpoint_path = job_dir / "checkpoint.json"
    if not checkpoint_path.exists():
        raise HTTPException(status_code=404, detail="Checkpoint not found for this job")

    with open(checkpoint_path) as f:
        ck = json.load(f)

    pieces_info = []
    for i in range(ck.get("piece_count", 0)):
        idx = ck.get("piece_vertex_counts", [])
        pieces_info.append({
            "index": i,
            "path": f"pieces/piece_{i:04d}.glb",
            "vertices": idx[i] if i < len(idx) else 0,
            "back_path": f"pieces/piece_{i:04d}_back.glb",
            "back_vertices": 0,
        })

    result = {
        "job_id": job_id,
        "piece_count": ck.get("piece_count", 0),
        "output_dir": str(job_dir),
        "consolidated": "pieces.glb",
        "checkpoint": "checkpoint.json",
        "colour_atlas": "colour_atlas.png",
        "pieces": pieces_info,
        "name": ck.get("name", ""),
        "orientation": ck.get("orientation", None),
    }
    if (job_dir / "normalized.glb").exists():
        result["normalized_glb"] = "normalized.glb"
    if (job_dir / "lowpoly_preview.glb").exists():
        result["preview_glb"] = "lowpoly_preview.glb"
    return result


@router.patch("/jobs/{job_id}")
async def update_job_meta(job_id: str, payload: dict):
    job_dir = _resolve_job_dir(job_id)
    checkpoint_path = job_dir / "checkpoint.json"
    if not checkpoint_path.exists():
        raise HTTPException(status_code=404, detail="Job not found")

    with open(checkpoint_path) as f:
        ck = json.load(f)

    if "name" in payload:
        ck["name"] = payload["name"]
    if "orientation" in payload:
        ck["orientation"] = payload["orientation"]

    with open(checkpoint_path, "w") as f:
        json.dump(ck, f, indent=2)

    return {"status": "ok"}
