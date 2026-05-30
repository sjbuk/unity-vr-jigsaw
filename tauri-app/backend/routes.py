import asyncio
import json
import os
import threading
import time
from pathlib import Path

from fastapi import APIRouter, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from starlette.responses import Response
from starlette.background import BackgroundTask

from planar_lib import Config

router = APIRouter(prefix="/api")

OUTPUTS_DIR = Path("/app/data/outputs")
MAX_UPLOAD_BYTES = 100 * 1024 * 1024  # 100 MB

_slice_lock = threading.Lock()
_progress_queues: dict[str, asyncio.Queue] = {}


def _sync_pipeline(config: Config, job_id: str, loop: asyncio.AbstractEventLoop, lock: threading.Lock):
    from planar_main import run_ingest, export_results
    from planar_phase_021 import cut_pieces_planar
    from planar_phase_022 import reassign_orphans
    from planar_phase_030 import bake_backface_colours

    def emit(msg: str):
        loop.call_soon_threadsafe(
            _progress_queues[job_id].put_nowait, msg
        )

    try:
        emit("[Phase 1] Loading and normalizing model...")
        mesh = run_ingest(config)

        emit("[Phase 2] Planar BSP slicing...")
        final_pieces = cut_pieces_planar(mesh, config.pieces, seed=config.seed)

        if config.reassign_orphans:
            emit("[Phase 2] Reassigning orphan fragments...")
            final_pieces = reassign_orphans(final_pieces)

        emit("[Phase 3] Baking back-face colours...")
        back_pieces = bake_backface_colours(final_pieces, config.output_path)

        emit("[Export] Writing output files...")
        export_results(config, mesh, final_pieces, back_pieces)

        emit("[DONE]")
    except Exception as exc:
        emit(f"[ERROR] {exc}")
        raise
    finally:
        lock.release()
        loop.call_soon_threadsafe(
            lambda: _progress_queues.pop(job_id, None)
        )


@router.post("/slice")
async def slice_model(
    file: UploadFile = File(...),
    config: str = Form(...),
):
    if not _slice_lock.acquire(blocking=False):
        raise HTTPException(status_code=409, detail="Another slicing job is already running")

    try:
        config_data = json.loads(config)
        cfg = Config(**config_data)

        filename = file.filename or "model.glb"
        if not filename.lower().endswith((".glb", ".gltf")):
            raise HTTPException(status_code=400, detail="Only .glb and .gltf files are supported")

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

        ts = str(int(time.time()))
        job_id = f"{Path(filename).stem}_{ts}"
        job_dir = OUTPUTS_DIR / job_id
        job_dir.mkdir(parents=True, exist_ok=True)

        input_path = str(job_dir / filename)
        with open(input_path, "wb") as f:
            f.write(contents)

        cfg.input_path = input_path
        cfg.output_path = str(job_dir)

        try:
            cfg.validate()
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))

        _progress_queues[job_id] = asyncio.Queue()
        _progress_queues[job_id].put_nowait("Starting slicing job...")

        loop = asyncio.get_running_loop()
        loop.run_in_executor(None, _sync_pipeline, cfg, job_id, loop, _slice_lock)

        return {"job_id": job_id}

    except HTTPException:
        _slice_lock.release()
        raise
    except Exception as e:
        _slice_lock.release()
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        pass


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
        headers={"Access-Control-Allow-Origin": "*"},
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

    return {
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
