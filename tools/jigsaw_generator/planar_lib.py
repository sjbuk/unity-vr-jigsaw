"""
planar_lib — Shared configuration and CLI argument parsing.
"""

import argparse
from dataclasses import dataclass


@dataclass
class Config:
    input_path: str
    output_path: str
    pieces: int = 24
    gap: float = 0.001
    seed: int | None = None
    reassign_orphans: bool = True
    adjacency_threshold: float = 0.01
    preview_resolution: int = 512

    def validate(self) -> None:
        if self.pieces < 2:
            raise ValueError("pieces must be >= 2")
        if self.gap < 0.0:
            raise ValueError("gap must be >= 0")
        if self.adjacency_threshold < 0.0:
            raise ValueError("adjacency_threshold must be >= 0")

    @classmethod
    def from_args(cls, args: argparse.Namespace) -> "Config":
        return cls(
            input_path=args.input,
            output_path=args.output,
            pieces=args.pieces,
            gap=args.gap,
            seed=args.seed,
            reassign_orphans=not args.no_reassign_orphans,
            adjacency_threshold=args.adjacency_threshold,
            preview_resolution=args.preview_resolution,
        )


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="3D Jigsaw — decompose a GLB model via planar BSP slicing.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--input", required=True, help="Path to source GLB model file")
    parser.add_argument("--output", required=True, help="Output directory for generated assets")
    parser.add_argument("--pieces", type=int, default=24, help="Target number of pieces (default: 24)")
    parser.add_argument("--gap", type=float, default=0.001, help="Micro-bevel gap between boundaries (default: 0.001)")
    parser.add_argument("--seed", type=int, default=None, help="Integer seed for reproducible slicing")
    parser.add_argument("--no-reassign-orphans", action="store_true",
                        help="Skip orphan fragment reassignment (default: on)")
    parser.add_argument("--adjacency-threshold", type=float, default=0.01,
                        help="AABB expansion for neighbour detection (default: 0.01)")
    parser.add_argument("--preview-resolution", type=int, default=512,
                        help="Preview thumbnail resolution in pixels (default: 512)")
    return parser
