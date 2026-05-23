"""Standalone profiling script for the jigsaw generation pipeline."""
import cProfile
import pstats
import io
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "jigsaw_generator"))

from jigsaw_generator.config import Config
from jigsaw_generator.main import main


INPUT_MODEL = os.path.join(os.path.dirname(__file__), "assets", "CarConcept.glb")
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "profile_output")


def run_profile():
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    argv = [
        "--input", INPUT_MODEL,
        "--output", OUTPUT_DIR,
        "--pieces", "24",
        "--mode", "full_3d",
        "--gap", "0.001",
        "--seed", "42",
    ]

    profiler = cProfile.Profile()
    wall_start = time.perf_counter()
    profiler.enable()
    try:
        exit_code = main(argv)
    finally:
        profiler.disable()
    wall_end = time.perf_counter()

    wall_elapsed = wall_end - wall_start

    s = io.StringIO()
    sort_by = "cumtime"
    ps = pstats.Stats(profiler, stream=s).sort_stats(sort_by)
    ps.print_stats(40)

    print("\n" + "=" * 70)
    print(f"WALL-CLOCK TOTAL: {wall_elapsed:.2f} seconds")
    print("=" * 70)
    print("\nTop 40 functions by cumulative time:")
    print(s.getvalue())

    s2 = io.StringIO()
    ps2 = pstats.Stats(profiler, stream=s2).sort_stats("tottime")
    ps2.print_stats(20)
    print("\nTop 20 functions by total (own) time:")
    print(s2.getvalue())

    prof_path = os.path.join(OUTPUT_DIR, "profile.prof")
    profiler.dump_stats(prof_path)
    print(f"\nRaw profile data saved to: {prof_path}")

    return exit_code


if __name__ == "__main__":
    rc = run_profile()
    sys.exit(rc if rc is not None else 0)
