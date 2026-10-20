#!/usr/bin/env python3
"""Compare protected original package/save files against the pre-work baseline."""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "output/runtime/merchanttravel-testing"


def fingerprint(path):
    stat = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return {"size": stat.st_size, "mtime_ns": stat.st_mtime_ns, "sha256": digest.hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("name", help="New report filename within the private QA evidence folder")
    parser.add_argument("--saves-only", action="store_true")
    args = parser.parse_args()
    if Path(args.name).name != args.name or not args.name.endswith(".json"):
        raise SystemExit("Report must be a simple JSON filename")
    baseline = json.loads((ROOT / "protected-before.json").read_text())
    results = []
    for root, before in baseline["roots"].items():
        directory = Path(root)
        if args.saves_only and directory.name == "Contents":
            continue
        files = {
            str(path.relative_to(directory)): fingerprint(path)
            for path in sorted(directory.rglob("*"))
            if path.is_file() and not any(part.startswith("._") or part == ".DS_Store" for part in path.relative_to(directory).parts)
        } if directory.exists() else {}
        expected = before["files"]
        changed = [name for name in sorted(files.keys() | expected.keys()) if files.get(name) != expected.get(name)]
        results.append({"root": root, "beforeCount": len(expected), "afterCount": len(files),
                        "unchanged": directory.exists() == before["exists"] and not changed, "changed": changed})
    report = {"utc": dt.datetime.now(dt.timezone.utc).isoformat(), "scope": "File set, size, SHA-256, modification time; metadata files excluded as in baseline", "roots": results,
              "unchanged": all(result["unchanged"] for result in results)}
    with (ROOT / args.name).open("x") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["unchanged"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
