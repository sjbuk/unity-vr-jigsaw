"""
jigsaw_generator — Decompose a 3D model into jigsaw pieces via planar BSP slicing.

Usage:
    python -m jigsaw_generator.planar_step_08_main --input model.glb --output out/ --pieces 24
"""

from .planar_step_01_config import Config, build_arg_parser

__all__ = ["Config", "build_arg_parser"]
