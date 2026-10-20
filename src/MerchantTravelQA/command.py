#!/usr/bin/env python3
"""Send one explicit fixture/API command to an already running isolated QA probe."""
import argparse
import json
import os
from pathlib import Path
import re
import time

ROOT = Path(__file__).resolve().parents[2] / "output/runtime/merchanttravel-testing"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session", help="Existing session directory name below the QA evidence root")
    parser.add_argument("command", help="JSON object with a new id and an action")
    args = parser.parse_args()
    if not re.fullmatch(r"session-[a-zA-Z0-9-]+", args.session):
        raise SystemExit("Invalid QA session name")
    session = ROOT / args.session
    if not session.is_dir() or session.is_symlink() or not (session / "started.json").is_file():
        raise SystemExit("A running, identity-verified QA session is required")
    command = json.loads(args.command)
    identifier = command.get("id", "")
    if not re.fullmatch(r"[A-Za-z0-9-]{1,80}", identifier):
        raise SystemExit("A fresh alphanumeric command id is required")
    response = session / (identifier + ".json")
    if response.exists():
        raise SystemExit("Command id already used")
    temporary = session / (identifier + ".command-tmp")
    with temporary.open("x") as stream:
        json.dump(command, stream, ensure_ascii=False)
    os.replace(temporary, session / "command.json")
    end = time.monotonic() + 15
    while time.monotonic() < end:
        if response.is_file():
            try:
                result = json.loads(response.read_text())
            except json.JSONDecodeError:
                time.sleep(0.05)
                continue
            state = result.get("after", result.get("snapshot", {}))
            diagnostics = state.get("diagnostics")
            if isinstance(diagnostics, dict):
                diagnostics = dict(diagnostics)
                diagnostics["targets"] = [
                    {key: target.get(key) for key in ("id", "unavailable", "selected")}
                    for target in diagnostics.get("targets", [])
                ]
            print(json.dumps({"report": str(response), "result": result.get("result"), "error": result.get("error"),
                              "playing": state.get("playing"), "player": state.get("player"),
                              "paused": state.get("paused"), "actionLayer": state.get("actionLayer"),
                              "teleportBegins": state.get("teleportBegins"), "teleportCompletes": state.get("teleportCompletes"),
                              "diagnostics": diagnostics}, ensure_ascii=False, indent=2))
            return 1 if result.get("error") else 0
        time.sleep(0.1)
    raise SystemExit("QA probe did not answer; no success is inferred")


if __name__ == "__main__":
    raise SystemExit(main())
