"""
Configuration and CLI argument parsing for planar jigsaw slicing.
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

    def validate(self) -> None:
        if self.pieces < 2:
            raise ValueError("pieces must be >= 2")
        if self.gap < 0.0:
            raise ValueError("gap must be >= 0")

    @classmethod
    def from_args(cls, args: argparse.Namespace) -> "Config":
        return cls(
            input_path=args.input,
            output_path=args.output,
            pieces=args.pieces,
            gap=args.gap,
            seed=args.seed,
        )


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="3D Jigsaw Piece Generator — decompose a GLB model via planar BSP slicing.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--input", required=True, help="Path to source GLB model file")
    parser.add_argument(
        "--output",
        required=True,
        help="Path to output directory for generated assets",
    )
    parser.add_argument(
        "--pieces",
        type=int,
        default=24,
        help="Target number of puzzle pieces (default: 24)",
    )
    parser.add_argument(
        "--gap",
        type=float,
        default=0.001,
        help="Micro-bevel gap between adjacent boundaries (default: 0.001)",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=None,
        help="Integer seed for reproducible slicing",
    )
    return parser
