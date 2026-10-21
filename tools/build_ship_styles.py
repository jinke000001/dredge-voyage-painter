#!/usr/bin/env python3
"""Build both Boat5 styles into a new, separately isolated app.

Uses the existing voyage-painter seventeen-hook profile and version lock.
Never launches the game, copies saves, or replaces an earlier app.
"""
import argparse
import json
import subprocess
import sys

import build_merchant_travel as builder
import merchant_travel_guard as guard


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", default="DREDGE 双船型外观预览 2.3.0.app")
    args = parser.parse_args(argv)
    try:
        print(json.dumps(builder.build(args.name, "voyage-painter", identity_variant="ship-styles"), ensure_ascii=False, indent=2))
        return 0
    except (guard.Refusal, OSError, ValueError, KeyError, ImportError, subprocess.CalledProcessError) as error:
        print("REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
