#!/usr/bin/env python3
"""Build a fresh isolated voyage app with the fixed voyage and merchant hooks.

Never launches the game, overwrites an earlier app, or reads/writes save bodies.
The immutable delivered merchant app remains usable through its existing launcher.
"""
import argparse
import json
import subprocess
import sys

import build_merchant_travel as builder
import merchant_travel_guard as guard


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", default="DREDGE 航行体验版 2026-10-19.app")
    args = parser.parse_args(argv)
    try:
        print(json.dumps(builder.build(args.name, "voyage-experience"), ensure_ascii=False, indent=2))
        return 0
    except (guard.Refusal, OSError, ValueError, KeyError, ImportError, subprocess.CalledProcessError) as error:
        print("REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
