#!/usr/bin/env python3
"""Build a new isolated native-painter app with the complete voyage profile.

Never launches the game, migrates saves, or overwrites an earlier app.
"""
import argparse
import json
import subprocess
import sys

import build_merchant_travel as builder
import merchant_travel_guard as guard


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", default="DREDGE 油漆匠整合版.app")
    args = parser.parse_args(argv)
    try:
        print(json.dumps(builder.build(args.name, "voyage-painter"), ensure_ascii=False, indent=2))
        return 0
    except (guard.Refusal, OSError, ValueError, KeyError, ImportError, subprocess.CalledProcessError) as error:
        print("REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
