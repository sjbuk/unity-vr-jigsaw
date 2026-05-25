"""
jigsaw_generator — Decompose a 3D model into interlocking jigsaw pieces.

Usage:
    python -m jigsaw_generator.planar_step_08_main --input model.glb --output out/ --pieces 24
"""

from .planar_step_01_config import Config, build_arg_parser

__all__ = ["Config", "build_arg_parser"]
