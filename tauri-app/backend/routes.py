import asyncio
import json
import os
import shutil
import threading
import time
from pathlib import Path

from fastapi import APIRouter, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from starlette.responses import Response
from starlette.background import BackgroundTask

from planar_lib import Config
from planar_phase_010 import load_model, normalize_mesh
from planar_phase_040 import generate_lowpoly_preview

router = APIRouter(prefix="/api")

OUTPUTS_DIR = Path("/app/data/outputs")
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


def _sync_upload(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock):
    try:
        _emit(loop, job_id, "[Phase 1] Loading and normalizing model...")
        mesh = load_model(config.input_path)
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
    except Exception as exc:
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_slice(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock, old_meta: dict):
    from planar_main import export_results
    from planar_phase_021 import cut_pieces_planar
    from planar_phase_022 import reassign_orphans
    from planar_phase_030 import bake_backface_colours

    try:
        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(config.input_path)

        _emit(loop, job_id, "[Phase 2] Planar BSP slicing...")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

        if config.reassign_orphans:
            _emit(loop, job_id, "[Phase 2] Reassigning orphan fragments...")
            final_pieces = reassign_orphans(final_pieces)

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(final_pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(Path(config.output_path))
        export_results(config, mesh, final_pieces, back_pieces)

        _patch_checkpoint_meta(config.output_path, old_meta)

        _emit(loop, job_id, "[DONE]")
    except Exception as exc:
        _emit(loop, job_id, f"[ERROR] {exc}")
        raise
    finally:
        _done_cleanup(lock, loop, job_id)


def _sync_orphans(job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock, old_meta: dict):
    from planar_main import export_results
    from planar_phase_022 import reassign_orphans
    from planar_phase_030 import bake_backface_colours

    try:
        job_dir = OUTPUTS_DIR / job_id
        norm_glb = job_dir / "normalized.glb"
        if not norm_glb.exists():
            raise FileNotFoundError("normalized.glb not found — upload model first")
        ck_path = job_dir / "checkpoint.json"
        if not ck_path.exists():
            raise FileNotFoundError("checkpoint.json not found — slice model first")

        with open(ck_path) as f:
            ck = json.load(f)

        config = Config(
            input_path=str(norm_glb),
            output_path=str(job_dir),
            pieces=ck.get("piece_count", 24),
            gap=ck.get("gap", 0.001),
            seed=ck.get("seed", None),
            adjacency_threshold=ck.get("adjacency_threshold", 0.01),
            preview_faces=ck.get("preview_faces", 2000),
        )

        _emit(loop, job_id, "[Phase 1] Loading normalized mesh...")
        mesh = load_model(str(norm_glb))

        _emit(loop, job_id, "[Load] Loading current pieces...")
        pieces = _load_pieces(job_dir)
        if not pieces:
            raise RuntimeError("No pieces found — slice model first")

        _emit(loop, job_id, f"[Phase 2] Reassigning orphan fragments ({len(pieces)} pieces)...")
        pieces = reassign_orphans(pieces)

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        _clean_outputs(job_dir)
        export_results(config, mesh, pieces, back_pieces)

        _patch_checkpoint_meta(str(job_dir), old_meta)

        _emit(loop, job_id, "[DONE]")
    except Exception as exc:
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
        )},
    )

    ts = str(int(time.time()))
    job_id = f"{Path(filename).stem}_{ts}"
    job_dir = OUTPUTS_DIR / job_id
    job_dir.mkdir(parents=True, exist_ok=True)

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

        return _start_job(job_id, _sync_pipeline, cfg, job_id, asyncio.get_running_loop(), _slice_lock)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


def _sync_pipeline(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock):
    from planar_main import run_ingest, export_results
    from planar_phase_021 import cut_pieces_planar
    from planar_phase_022 import reassign_orphans
    from planar_phase_030 import bake_backface_colours

    try:
        _emit(loop, job_id, "[Phase 1] Loading and normalizing model...")
        mesh = run_ingest(config)

        _emit(loop, job_id, "[Phase 2] Planar BSP slicing...")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

        if config.reassign_orphans:
            _emit(loop, job_id, "[Phase 2] Reassigning orphan fragments...")
            final_pieces = reassign_orphans(final_pieces)

        _emit(loop, job_id, "[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(final_pieces, config.output_path)

        _emit(loop, job_id, "[Export] Writing output files...")
        export_results(config, mesh, final_pieces, back_pieces)

        _emit(loop, job_id, "[DONE]")
    except Exception as exc:
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

        return _start_job(job_id, _sync_upload, cfg, job_id, asyncio.get_running_loop(), _slice_lock)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/slice/{job_id}")
async def slice_job(job_id: str, payload: dict = {}):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another slicing job is already running")

    try:
        job_dir = OUTPUTS_DIR / job_id
        norm_glb = job_dir / "normalized.glb"
        if not norm_glb.exists():
            raise HTTPException(status_code=400, detail="No uploaded model found for this job. Upload first via POST /api/upload")

        cfg = Config(
            input_path=str(norm_glb),
            output_path=str(job_dir),
            pieces=payload.get("pieces", 24),
            gap=payload.get("gap", 0.001),
            seed=payload.get("seed", None),
            reassign_orphans=payload.get("reassign_orphans", False),
            preview_faces=payload.get("preview_faces", 2000),
        )
        try:
            cfg.validate()
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))

        old_meta = _read_old_meta(job_dir)
        return _start_job(job_id, _sync_slice, cfg, job_id, asyncio.get_running_loop(), _slice_lock, old_meta)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/orphans/{job_id}")
async def reassign_orphans_endpoint(job_id: str):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    try:
        job_dir = OUTPUTS_DIR / job_id
        if not job_dir.exists():
            raise HTTPException(status_code=404, detail="Job not found")

        old_meta = _read_old_meta(job_dir)
        return _start_job(job_id, _sync_orphans, job_id, asyncio.get_running_loop(), _slice_lock, old_meta)

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/preview/{job_id}")
async def regenerate_preview(job_id: str, payload: dict = {}):
    job_dir = OUTPUTS_DIR / job_id
    norm_glb = job_dir / "normalized.glb"
    if not norm_glb.exists():
        raise HTTPException(status_code=400, detail="No normalized model found — upload first")

    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another job is already running")

    preview_faces = payload.get("preview_faces", 2000)

    try:
        from planar_phase_010 import load_model

        mesh = load_model(str(norm_glb))
        lowpoly_path = job_dir / "lowpoly_preview.glb"

        lowpoly_verts, lowpoly_faces = generate_lowpoly_preview(
            mesh, str(lowpoly_path), target_faces=preview_faces,
        )

        if lowpoly_verts is None or lowpoly_faces is None:
            raise RuntimeError("Preview generation failed — ensure fast_simplification is installed")

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
                    job_dir = OUTPUTS_DIR / job_id
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
    job_dir = OUTPUTS_DIR / job_id
    full_path = job_dir / file_path
    resolved = full_path.resolve()
    if not str(resolved).startswith(str(job_dir.resolve())):
        raise HTTPException(status_code=403, detail="Path traversal not allowed")
    if not resolved.exists() or not resolved.is_file():
        raise HTTPException(status_code=404, detail="File not found")

    ext = resolved.suffix.lower()
    media_type_map = {
        ".glb": "model/gltf-binary",
        ".gltf": "model/gltf+json",
        ".png": "image/png",
        ".json": "application/json",
        ".bin": "application/octet-stream",
    }
    media_type = media_type_map.get(ext, "application/octet-stream")

    return FileResponse(
        resolved,
        media_type=media_type,
        headers={
            "Access-Control-Allow-Origin": "*",
            "Cache-Control": "no-cache, no-store, must-revalidate",
        },
    )


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
    job_dir = OUTPUTS_DIR / job_id
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
    job_dir = OUTPUTS_DIR / job_id
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
