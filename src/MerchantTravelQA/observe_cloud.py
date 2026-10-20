#!/usr/bin/env python3
"""Observe the fixed Steam Cloud log after the unchanged private QA baseline.

Usage: python3 src/MerchantTravelQA/observe_cloud.py cloud-after-exit06.json
Optional: --previous cloud-after-exit05.json to distinguish the new log interval.
Only reads protected-before.json, that optional prior report, and cloud_log.txt.
Writes one new report under output/runtime/merchanttravel-testing; never reads a
save body, remotecache.vdf, account configuration or credentials, or changes Steam.
An observed exit sync is not proof of universal Steam Cloud isolation.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import stat
import sys


ROOT = Path(__file__).resolve().parents[2] / "output/runtime/merchanttravel-testing"
APP_ID = "1562430"
IDENTIFIER = "local.jinke.dredge.merchanttravel.qa.20261018"
FORMAL_RULE = "Black Salt Games/DREDGE/saves"
APP_PATTERN = re.compile(r"\[AppID\s+" + APP_ID + r"\]")
MAX_TAIL_BYTES = 16 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def no_links(path):
    path = Path(os.path.abspath(path))
    for part in (path, *path.parents):
        try:
            info = part.lstat()
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(info.st_mode), "Symlinks are not allowed: " + str(part))
    return path


def read_file(path):
    path = no_links(path)
    info = path.stat()
    require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1, "Expected a regular, unlinked file: " + str(path))
    return path


def report_path(name):
    require(re.fullmatch(r"cloud-[A-Za-z0-9][A-Za-z0-9._-]*\.json", name) is not None,
            "Report name must be a simple cloud-*.json filename.")
    return no_links(ROOT / name)


def json_file(path):
    result = json.loads(read_file(path).read_text(encoding="utf-8"))
    require(isinstance(result, dict), "Expected a JSON object: " + str(path))
    return result


def parse_events(raw, start_offset):
    events = []
    offset = start_offset
    lines = raw.splitlines(keepends=True)
    for index, line in enumerate(lines, 1):
        text = line.decode("utf-8", errors="replace").rstrip("\r\n")
        # A writer may still be appending the last line at this byte snapshot.
        if line.endswith(b"\n") and APP_PATTERN.search(text):
            events.append({"lineSinceBaseline": index, "byteOffset": offset, "text": text})
        offset += len(line)
    return events, len(lines)


def summarize(events, raw):
    texts = [event["text"] for event in events]
    rules, skipped, persisted, changes, cache_writes = [], [], [], [], []
    for text in texts:
        rule = re.search(r'Evaluating rule .*?root="([^"]*)" path="([^"]*)" pattern="([^"]*)"', text)
        if rule:
            rules.append({"root": rule[1], "path": rule[2], "pattern": rule[3]})
        match = re.search(r"Skipping un-modified file (.+)$", text)
        if match:
            skipped.append(match[1])
        match = re.search(r"Persisting file (.+) to the cloud$", text)
        if match:
            persisted.append(match[1])
        match = re.search(r"Attempting to sync from local change number '(\d+)' to global change number '(\d+)'", text)
        if match:
            changes.append({"local": int(match[1]), "global": int(match[2])})
        match = re.search(r"YldWriteCacheDirectoryToFile - saved to '([^']+)'", text)
        if match:
            cache_writes.append(match[1])
    decoded = raw.decode("utf-8", errors="replace")
    test_mentions = any(marker in decoded for marker in (IDENTIFIER, "DREDGE Merchant Travel QA 20261018", "Jinke Local Tests"))
    successful_changes = [int(match[1]) for text in texts if (match := re.search(r"Successfully synced to ChangeNumber (\d+)", text))]
    already_synced = [int(match[1]) for text in texts if (match := re.search(r"Currently already synced to global change number '(\d+)'", text))]
    return {
        "appEventCount": len(events),
        "firstEvent": texts[0] if texts else None,
        "lastEvent": texts[-1] if texts else None,
        "autoCloudExitRan": any("Running AutoCloud on exit" in text for text in texts),
        "autoCloudComplete": any("AutoCloud complete" in text for text in texts),
        "rulesEvaluated": rules,
        "matchedFileCounts": [int(match[1]) for text in texts if (match := re.search(r"Found (\d+) files that match", text))],
        "formalSaveReportedUnmodified": any(FORMAL_RULE + "/" in path for path in skipped),
        "skippedUnmodifiedFiles": skipped,
        "persistingCandidateMessages": persisted,
        "testIdentityMentioned": test_mentions,
        "noLaunchRecordMessage": any("No launch record found" in text for text in texts),
        "cloudMetadata": {
            "attemptedChangeNumberSyncs": changes,
            "successfulChangeNumbers": successful_changes,
            "alreadySyncedGlobalChangeNumbers": already_synced,
            "cacheWriteRecorded": bool(cache_writes),
            "cachePathsReported": cache_writes,
            "uploadCompleteInBuildListMarker": any("Upload complete in build list" in text for text in texts),
        },
        "interpretation": {
            "formalSave": "Skipped as unmodified in this interval" if any(FORMAL_RULE + "/" in path for path in skipped) else "No unmodified formal-save skip was observed in this interval",
            "testProfile": "Test identity appeared in this interval; inspect event lines" if test_mentions else "Test identity was not referenced in this observed log interval",
            "contentUpload": "Persisting and Upload complete in build list are not by themselves evidence of save-content transmission; unmodified skip messages must be considered",
            "cloudMetadata": "Cache-write/change-number messages describe Cloud metadata activity, not a changed game-save body",
        },
    }


def observe(name, previous_name=None):
    destination = report_path(name)
    require(not destination.exists(), "Refusing to overwrite an existing observation.")
    require(no_links(ROOT).is_dir(), "The existing private QA directory is required.")
    baseline_path = ROOT / "protected-before.json"
    baseline = json_file(baseline_path)
    observation = baseline.get("steamCloudObservation")
    require(isinstance(observation, dict), "The baseline has no Steam Cloud observation offset.")
    log = Path(pwd.getpwuid(os.getuid()).pw_dir) / "Library/Application Support/Steam/logs/cloud_log.txt"
    require(observation.get("log") == str(log), "Baseline log path differs from this user's fixed Steam Cloud log.")
    offset = observation.get("offsetBefore")
    require(type(offset) is int and offset >= 0, "Invalid baseline offset.")
    read_file(log)
    descriptor = os.open(log, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1, "Unsafe Steam log file.")
        end = before.st_size
        require(end >= offset, "Cloud log shrank/rotated below the baseline offset; continuity is not established.")
        require(end - offset <= MAX_TAIL_BYTES, "Cloud log tail exceeds the bounded observation size.")
        stream.seek(offset)
        raw = stream.read(end - offset)
        after = os.fstat(stream.fileno())
        require(len(raw) == end - offset and after.st_size >= end, "Cloud log was truncated while being read.")
    events, line_count = parse_events(raw, offset)
    since_baseline = summarize(events, raw)
    report = {
        "schemaVersion": 1, "utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "appId": APP_ID, "log": str(log), "baselineOffset": offset, "currentSize": end,
        "baselineRecordedUtc": observation.get("recordedUtc"),
        "baselineSha256": hashlib.sha256(read_file(baseline_path).read_bytes()).hexdigest(),
        "newBytes": len(raw), "newLines": line_count, "observedTailSha256": hashlib.sha256(raw).hexdigest(),
        "logDevice": before.st_dev, "logInode": before.st_ino, "logGrewDuringRead": after.st_size > end,
        "tailEndsWithNewline": not raw or raw.endswith(b"\n"),
        "appIdMentioned": bool(events), "testIdentityMentioned": since_baseline["testIdentityMentioned"],
        "scope": "Local Steam Cloud log from the unchanged initial byte offset to this snapshot; only AppID 1562430 event text is retained",
        "sinceBaseline": since_baseline, "eventLines": events,
        "limitations": [
            "No Steam settings, save bodies, remotecache contents, credentials or remote Cloud state were read or changed",
            "Absence of the test profile in this log is scoped to this interval, not a universal no-access or no-upload guarantee",
            "Formal save bytes and modification times require the separate protected-file report; this tool does not verify them",
            "The old baseline has no log inode/prefix hash; shrink is rejected, but a rotation replaced by a larger log cannot be excluded from that baseline alone",
            "A latest exit cycle is a log observation; it is not automatically attributed to a particular native session",
        ],
    }
    starts = [index for index, event in enumerate(events) if "Starting sync (up,AC Exit," in event["text"]]
    if starts:
        latest = events[starts[-1]:]
        latest_offset = latest[0]["byteOffset"]
        report["latestObservedExitCycle"] = {"startOffset": latest_offset, "endOffset": end,
                                             "observations": summarize(latest, raw[latest_offset - offset:])}
    if previous_name:
        previous_path = report_path(previous_name)
        require(previous_path != destination, "Previous and new report filenames must differ.")
        previous = json_file(previous_path)
        previous_end = previous.get("currentSize")
        require(previous.get("appId") == APP_ID and previous.get("baselineOffset") == offset
                and type(previous_end) is int and offset <= previous_end <= end,
                "Previous observation is not compatible with this baseline/log size.")
        prior_inode = previous.get("logInode")
        if prior_inode is not None:
            require((previous.get("logDevice"), prior_inode) == (before.st_dev, before.st_ino), "Log identity changed after the previous observation.")
        prior_hash = previous.get("observedTailSha256")
        if prior_hash is not None:
            require(hashlib.sha256(raw[:previous_end - offset]).hexdigest() == prior_hash, "Previously observed log bytes changed.")
        delta = raw[previous_end - offset:]
        selected = [event for event in events if event["byteOffset"] >= previous_end]
        report["sincePreviousObservation"] = {"report": previous_name, "startOffset": previous_end, "endOffset": end,
                                               "newBytes": len(delta), "newLines": len(delta.splitlines()),
                                               "observations": summarize(selected, delta)}
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    destination.chmod(0o444)
    selected = report.get("sincePreviousObservation", {}).get("observations", since_baseline)
    print(json.dumps({"report": str(destination), "newBytesSinceBaseline": len(raw),
                      "comparison": "previous observation" if previous_name else "initial baseline",
                      "observations": selected}, ensure_ascii=False, indent=2))
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("name", help="Fresh cloud-*.json filename in the private QA evidence directory")
    parser.add_argument("--previous", help="Optional earlier cloud-*.json report to identify the subsequent log interval")
    args = parser.parse_args(argv)
    try:
        return observe(args.name, args.previous)
    except (OSError, ValueError, KeyError) as error:
        print("CLOUD OBSERVATION REFUSED: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
