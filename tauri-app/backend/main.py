import sys
from pathlib import Path

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles

toolspath = Path(__file__).resolve().parent.parent.parent / "tools" / "jigsaw_generator"
if str(toolspath) not in sys.path:
    sys.path.insert(0, str(toolspath))

from routes import router

DIST_DIR = Path(__file__).resolve().parent.parent / "dist"

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(router)

if DIST_DIR.exists():
    app.mount("/", StaticFiles(directory=str(DIST_DIR), html=True), name="static")
