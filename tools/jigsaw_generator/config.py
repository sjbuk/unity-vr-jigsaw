"""
Parameter defaults, validation, and CLI argument parsing.

CLI params (from jigsaw_tool_plan.md):
  --input <path>            Source GLB model file
  --output <path>           Output directory for generated assets
  --pieces <int>            Target count of puzzle pieces (default 24)
  --mode shell|full_3d      Partitioning mode (default full_3d)
  --shell_thickness <f>     Inward extrusion distance in shell mode (default 0.02)
  --gap <f>                 Micro-bevel gap between adjacent boundaries (default 0.001)
  --peg_clearance <f>       Radial air-gap for alignment dowels (default 0.003)
  --tab_density <f>         Fraction 0.0-1.0 controlling interlocking frequency (default 0.3)
  --snap_radius_min <f>     Hard snap locking distance (default 0.02)
  --snap_radius_max <f>     Loose attraction boundary (default 0.08)
  --snap_angle_tolerance <f> Max angular deviation for snap (default 25.0)
  --seed <int>              Reproducibility seed
"""

import argparse
from dataclasses import dataclass
from typing import Literal

Mode = Literal["shell", "full_3d"]


@dataclass
class Config:
    input_path: str
    output_path: str
    pieces: int = 24
    mode: Mode = "full_3d"
    shell_thickness: float = 0.02
    gap: float = 0.001
    peg_clearance: float = 0.003
    tab_density: float = 0.3
    snap_radius_min: float = 0.02
    snap_radius_max: float = 0.08
    snap_angle_tolerance: float = 25.0
    seed: int | None = None

    def validate(self) -> None:
        if self.pieces < 2:
            raise ValueError("pieces must be >= 2")
        if self.mode not in ("shell", "full_3d"):
            raise ValueError("mode must be 'shell' or 'full_3d'")
        if self.tab_density < 0.0 or self.tab_density > 1.0:
            raise ValueError("tab_density must be between 0 and 1")
        if self.gap < 0.0:
            raise ValueError("gap must be >= 0")
        if self.peg_clearance < 0.0:
            raise ValueError("peg_clearance must be >= 0")
        if self.snap_radius_min < 0.0:
            raise ValueError("snap_radius_min must be >= 0")
        if self.snap_radius_max <= self.snap_radius_min:
            raise ValueError("snap_radius_max must be > snap_radius_min")
        if self.snap_angle_tolerance < 0.0 or self.snap_angle_tolerance > 180.0:
            raise ValueError("snap_angle_tolerance must be between 0 and 180")
        if self.mode == "shell" and self.shell_thickness <= 0.0:
            raise ValueError("shell_thickness must be > 0 for shell mode")

    @classmethod
    def from_args(cls, args: argparse.Namespace) -> "Config":
        return cls(
            input_path=args.input,
            output_path=args.output,
            pieces=args.pieces,
            mode=args.mode,
            shell_thickness=args.shell_thickness,
            gap=args.gap,
            peg_clearance=args.peg_clearance,
            tab_density=args.tab_density,
            snap_radius_min=args.snap_radius_min,
            snap_radius_max=args.snap_radius_max,
            snap_angle_tolerance=args.snap_angle_tolerance,
            seed=args.seed,
        )


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="3D Jigsaw Piece Generator — decompose a GLB model into interlocking puzzle pieces.",
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
        "--mode",
        choices=["shell", "full_3d"],
        default="full_3d",
        help="Partitioning mode: full_3d (volumetric) or shell (thin-walled) (default: full_3d)",
    )
    parser.add_argument(
        "--shell_thickness",
        type=float,
        default=0.02,
        help="Inward extrusion distance for shell mode (default: 0.02)",
    )
    parser.add_argument(
        "--gap",
        type=float,
        default=0.001,
        help="Micro-bevel gap between adjacent boundaries (default: 0.001)",
    )
    parser.add_argument(
        "--peg_clearance",
        type=float,
        default=0.003,
        help="Radial air-gap for alignment dowels (default: 0.003)",
    )
    parser.add_argument(
        "--tab_density",
        type=float,
        default=0.3,
        help="Interlocking mechanism frequency 0.0-1.0 (default: 0.3)",
    )
    parser.add_argument(
        "--snap_radius_min",
        type=float,
        default=0.02,
        help="Hard snap locking distance threshold (default: 0.02)",
    )
    parser.add_argument(
        "--snap_radius_max",
        type=float,
        default=0.08,
        help="Loose attraction boundary radius (default: 0.08)",
    )
    parser.add_argument(
        "--snap_angle_tolerance",
        type=float,
        default=25.0,
        help="Maximum angular deviation for snap attraction in degrees (default: 25.0)",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=None,
        help="Integer seed for reproducible partitioning",
    )
    return parser
