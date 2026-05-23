"""
jigsaw_generator — Decompose a 3D model into interlocking jigsaw pieces.

Usage:
    python -m jigsaw_generator.main --input model.glb --output out/ --pieces 24
"""

from .config import Config, build_arg_parser

__all__ = ["Config", "build_arg_parser"]
